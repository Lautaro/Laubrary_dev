using System;
using System.Collections.Generic;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- design B9's future-proofing requirement, built as a SEAM only: no tilted (non-fixed) camera
    /// resolve exists anywhere in Shaper today (2.3's own doc, <c>D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\...\
    /// P3 - Shaper internals...md</c> §2.3, calls arbitrary 3-D rotation "not possible in this representation --
    /// not 'hard', not 'expensive', but structurally excluded" for the CURRENT height-field resolve). This file
    /// exists so that WHEN a tilted resolve is eventually added, it has a separately-keyed, separately-budgeted
    /// cache to live in from day one, rather than being wedged into <see cref="ShaperNodeCache"/> where it would
    /// silently share that cache's budget and eviction policy with every ordinary, cheap, fixed-camera node.
    ///
    /// <b>Why a tilted resolve genuinely needs its own cache rather than reusing the flat one.</b> A fixed
    /// camera's cost is bounded by the SHAPE's own area (one evaluation pass per node per frame, over the
    /// node's support box -- <see cref="ShaperCachedEvaluator"/>'s whole design). A tilted resolve's cost is a
    /// per-PIXEL SEARCH (walking along a ray/line-of-sight per output pixel to find where it first hits solid
    /// content), which is bounded by the CANVAS's area regardless of how small the shape is, and is measured
    /// elsewhere (SHAPER_THE_DESIGN.md B9's own citation) at seven to eleven times the cost of a flat resolve
    /// for the SAME shape. Lumping that into the same key/budget space as eleven untilted layers would mean one
    /// tilted layer's cache churn (a much more expensive re-key, evicting far more often under a shared entry
    /// cap) degrades the OTHER eleven layers' responsiveness guarantee, even though nothing about them changed
    /// -- exactly the failure this file's separation is required to prevent.
    /// </summary>
    public readonly struct ShaperTiltedCacheKey : IEquatable<ShaperTiltedCacheKey>
    {
        // A DIFFERENT struct type from ShaperCacheKey on purpose -- not a type alias, not implicitly
        // convertible -- so a tilted-resolve key can never be looked up in (or accidentally stored into)
        // ShaperNodeCache's dictionary; the two key spaces are incompatible at COMPILE time, not just by
        // convention.
        public readonly ulong lane0, lane1;
        public ShaperTiltedCacheKey(ulong lane0, ulong lane1) { this.lane0 = lane0; this.lane1 = lane1; }

        public bool Equals(ShaperTiltedCacheKey other) => lane0 == other.lane0 && lane1 == other.lane1;
        public override bool Equals(object obj) => obj is ShaperTiltedCacheKey k && Equals(k);
        public override int GetHashCode() => unchecked((int)(lane0 ^ (lane1 * 0x9E3779B97F4A7C15UL)));
    }

    /// <summary>
    /// The capability a future tilted-camera-aware node/effect would implement to be resolved via the
    /// per-pixel-search path and cached here instead of in <see cref="ShaperNodeCache"/>. No implementation
    /// exists today -- this is the hook a future task wires a real resolver into, mirroring the "declared
    /// capability, never assumed from a base interface" posture <see cref="IShaperSwarmNativeSource"/> and
    /// <see cref="IShaperCacheableSource"/> already take.
    /// </summary>
    public interface IShaperTiltedResolveSource
    {
        /// <summary>A deterministic key covering everything that changes this source's tilted-resolve result --
        /// same contract as <see cref="IShaperCacheableSource.ContentHash"/>, in the SEPARATE key space.</summary>
        ShaperTiltedCacheKey TiltedContentHash();
    }

    /// <summary>
    /// The separately-budgeted store. Deliberately a much smaller default budget than
    /// <see cref="ShaperNodeCache.MaxEntries"/> -- each entry is expected to cost several times more to produce
    /// (the 7-11x figure above) and to occupy more memory (a per-pixel search result is not reducible to a
    /// small support-box buffer the way a flat SDF sample is), so holding as many entries as the flat cache
    /// would be a strictly worse trade at this cache's own cost-per-entry.
    /// </summary>
    public sealed class ShaperTiltedResolveCache
    {
        public int MaxEntries = 256;

        sealed class Entry { public object payload; public long lastTouch; }
        readonly Dictionary<ShaperTiltedCacheKey, Entry> map = new Dictionary<ShaperTiltedCacheKey, Entry>();
        long tick;

        public long ComputeCount, HitCount, EvictCount;
        public int Count => map.Count;

        /// <summary><paramref name="payload"/> is deliberately <c>object</c> -- no tilted-resolve result type
        /// exists yet (no resolver exists yet to produce one). A real implementation would replace this with a
        /// concrete result type the same way <see cref="ShaperNodeCache"/> uses <see cref="ShaperFieldBuffer"/>.</summary>
        public bool TryGet(ShaperTiltedCacheKey key, out object payload)
        {
            if (map.TryGetValue(key, out var e)) { e.lastTouch = ++tick; payload = e.payload; HitCount++; return true; }
            payload = null;
            return false;
        }

        public void Store(ShaperTiltedCacheKey key, object payload)
        {
            if (!map.ContainsKey(key) && map.Count >= MaxEntries) EvictOldest();
            map[key] = new Entry { payload = payload, lastTouch = ++tick };
            ComputeCount++;
        }

        void EvictOldest()
        {
            ShaperTiltedCacheKey victim = default;
            long oldest = long.MaxValue;
            bool found = false;
            foreach (var kv in map)
                if (kv.Value.lastTouch < oldest) { oldest = kv.Value.lastTouch; victim = kv.Key; found = true; }
            if (found) { map.Remove(victim); EvictCount++; }
        }

        public void Clear() => map.Clear();
    }
}
