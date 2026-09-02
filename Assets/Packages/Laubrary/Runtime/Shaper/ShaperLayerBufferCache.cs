// ShaperLayerBufferCache — the resolved-buffer store the per-layer preview cache is built on (T-0194).
//
// One entry is ONE layer's finished contribution at one phase: the premultiplied-linear destination it would
// have composited (4 floats per sample, post-mask, post its own pre-composite effects) and its accumulated
// height field (1 float per sample, which the depth composite reads). Those two arrays are exactly what
// ShaperDocumentRenderer's layer walk hands to CompositeDepth, and nothing downstream of the walk reads
// anything else from the layer, which is why they are the whole of what needs storing.
//
// Keyed by ShaperLayerKey.PaintKey — content, never a layer index or an object reference — so a layer that is
// hidden and shown again, moved and moved back, or edited and undone hits the same entry it had before.
//
// ── Eviction ────────────────────────────────────────────────────────────────────────────────────────────────
// Least-recently-used against a FLOAT budget rather than an entry count, because an entry's size is the
// canvas: 96×64 costs 123 KB and 256×256 costs 1.3 MB, so a fixed entry count would either starve a small
// document or blow up a large one. Evicting is always safe — a miss costs one re-resolve.
using System.Collections.Generic;

namespace Laubrary.Shaper
{
    /// <summary>A per-layer, per-phase store of resolved layer buffers, keyed by content.</summary>
    public sealed class ShaperLayerBufferCache
    {
        sealed class Entry
        {
            public float[] dst;         // 4 floats per sample, premultiplied linear
            public float[] height;      // 1 float per sample
            public int sampleCount;
            public long stamp;          // last touched, for LRU
        }

        readonly Dictionary<ShaperCacheKey, Entry> entries = new Dictionary<ShaperCacheKey, Entry>();
        long clock;
        long floats;

        /// <summary>
        /// The memoized layer content keys every user of this cache shares. It lives here rather than on each
        /// caller because the key and the buffer it addresses must be built from the same walk of the document:
        /// two independently-memoized key sets could disagree about what an edit meant, and the disagreement
        /// would surface as a stale buffer rather than as an error.
        /// </summary>
        public ShaperLayerKeys Keys { get; } = new ShaperLayerKeys();

        /// <summary>Re-walk the document's layers for a pass that will ASK about keys without rendering — the
        /// preview cache's invalidation scan, which re-keys every resident frame to decide what it can keep.
        /// A render never relies on this: <see cref="ShaperDocumentRenderer.RenderPhaseInto"/> rebuilds the memo
        /// itself, every call, so no render can read a key built before an edit.</summary>
        public void BeginPass(ShaperDocument doc) => Keys.Rebuild(doc);

        /// <summary>
        /// Total floats the cache may hold before it starts evicting. 8M floats is 32 MB — comfortably more
        /// than a 16-frame, 6-layer document at any canvas an editor preview is legible at, and small enough
        /// that an accidental 512² document cannot quietly eat a gigabyte.
        /// </summary>
        public long MaxFloats = 8L * 1024L * 1024L;

        public int Count => entries.Count;
        public long FloatsHeld => floats;

        /// <summary>Hits and misses since the last <see cref="ResetStats"/> — what a performance probe reads to
        /// tell "the picture did not change" apart from "the picture was recomputed cheaply".</summary>
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        public void ResetStats() { Hits = 0; Misses = 0; }

        public void Clear()
        {
            entries.Clear();
            floats = 0;
        }

        /// <summary>True when this exact layer content at this exact sample count is already resolved. Does NOT
        /// count as a hit — it is the question the preview cache asks before deciding whether a frame can be
        /// re-composited in place or has to be dropped and re-resolved.</summary>
        public bool Contains(ShaperCacheKey key, int sampleCount)
            => entries.TryGetValue(key, out var e) && e.sampleCount == sampleCount;

        public bool TryGet(ShaperCacheKey key, int sampleCount, out float[] dst, out float[] height)
        {
            if (entries.TryGetValue(key, out var e) && e.sampleCount == sampleCount)
            {
                e.stamp = ++clock;
                dst = e.dst; height = e.height;
                Hits++;
                return true;
            }
            dst = null; height = null;
            Misses++;
            return false;
        }

        /// <summary>
        /// Take a private copy of a layer's finished buffers. Copying rather than adopting the caller's arrays
        /// is not caution: <c>ShaperRenderBufferPool</c> hands the SAME <see cref="ShaperFillBuffers"/> back for
        /// the same layer index on the next frame, and the per-layer effect path writes into one shared scratch
        /// reused by every later layer in the pass — adopting either would leave every stored entry pointing at
        /// a buffer the next layer is about to overwrite.
        /// </summary>
        public void Store(ShaperCacheKey key, float[] dst, float[] height, int sampleCount)
        {
            if (dst == null || sampleCount <= 0) return;
            int need = sampleCount * ShaperDocumentRenderer.FloatsPerSample;
            if (dst.Length < need) return;

            var e = new Entry
            {
                dst = new float[need],
                height = new float[sampleCount],
                sampleCount = sampleCount,
                stamp = ++clock,
            };
            System.Array.Copy(dst, e.dst, need);
            // A layer with no height stage publishes none: a null height field is a flat layer at its own base
            // plane, and CompositeDepth already reads it that way. Storing zeros keeps the entry one shape.
            if (height != null && height.Length >= sampleCount)
                System.Array.Copy(height, e.height, sampleCount);

            if (entries.TryGetValue(key, out var old)) floats -= EntryFloats(old);
            entries[key] = e;
            floats += EntryFloats(e);
            EvictIfOver();
        }

        static long EntryFloats(Entry e) => (long)e.sampleCount * (ShaperDocumentRenderer.FloatsPerSample + 1);

        void EvictIfOver()
        {
            if (floats <= MaxFloats || entries.Count <= 1) return;
            // A linear scan for the oldest entry per eviction, not a maintained order list: evictions happen
            // only when the budget is actually exceeded, and the map holds hundreds of entries at most, so the
            // scan is cheaper than keeping a second structure correct across every hit.
            var victims = new List<ShaperCacheKey>();
            while (floats > MaxFloats && entries.Count - victims.Count > 1)
            {
                ShaperCacheKey oldest = default;
                long best = long.MaxValue;
                bool found = false;
                foreach (var kv in entries)
                {
                    if (victims.Contains(kv.Key)) continue;
                    if (kv.Value.stamp >= best) continue;
                    best = kv.Value.stamp; oldest = kv.Key; found = true;
                }
                if (!found) break;
                victims.Add(oldest);
                floats -= EntryFloats(entries[oldest]);
            }
            for (int i = 0; i < victims.Count; i++) entries.Remove(victims[i]);
        }
    }
}
