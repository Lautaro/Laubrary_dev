using System.Collections.Generic;

namespace Laubrary.Shaper
{
    /// <summary>T-0115 -- one node's (or one bag-fold-prefix's) evaluated output, in canvas units, over the
    /// FULL sample grid (see SPEC.md Part 4 for why this task did not restrict buffers to a node's own
    /// support box -- a real, named scope cut, not an oversight). Immutable once stored: a cache entry is
    /// never mutated in place, only replaced wholesale on a miss, so a buffer handed out by
    /// <see cref="ShaperNodeCache.TryGet"/> can be read freely without defensive copying.</summary>
    public sealed class ShaperFieldBuffer
    {
        public readonly int width, height;
        public readonly float[] distance;

        /// <summary>The node's own canvas-space support box, carried alongside the buffer so a cache HIT never
        /// has to recompile just to learn it. This matters concretely for a Composite node: recompiling on a
        /// hit would call <see cref="IShaperCompositeSource.Render"/> again -- exactly the expensive bake T-0112
        /// caches once at compile time -- silently defeating the whole point of caching that node. Storing the
        /// box here is what keeps a hit a true zero-recompute, zero-rerender hit.</summary>
        public readonly float boxCx, boxCy, boxHalfW, boxHalfH;
        public readonly bool boxValid;

        public ShaperFieldBuffer(int width, int height, float[] distance,
                                 float boxCx = 0f, float boxCy = 0f, float boxHalfW = 0f, float boxHalfH = 0f, bool boxValid = false)
        {
            this.width = width; this.height = height; this.distance = distance;
            this.boxCx = boxCx; this.boxCy = boxCy; this.boxHalfW = boxHalfW; this.boxHalfH = boxHalfH; this.boxValid = boxValid;
        }
    }

    /// <summary>
    /// T-0115 -- the keyed store backing <see cref="ShaperCachedEvaluator"/>: <see cref="ShaperCacheKey"/> to
    /// <see cref="ShaperFieldBuffer"/>, with hit/miss/evict counters kept for real measurement (design B9's own
    /// "measured, not asserted" standard -- <c>ShaperCacheAudit</c> reads these counters directly rather than
    /// inferring cache behaviour from timing alone).
    ///
    /// A plain <c>Dictionary</c> plus an O(entries) linear-scan eviction when <see cref="MaxEntries"/> is
    /// exceeded -- correct and simple at the scale this task's own budget names (a realistic document's total
    /// live key count across a whole 24-frame animation is in the low thousands, not millions; see SPEC.md
    /// Part 6 for the measured count). A real LRU (doubly-linked list + dictionary, O(1) touch/evict) is the
    /// obvious next step if a document ever needs more entries than this scan stays cheap for -- named as a
    /// real, valuable follow-up this task did not build, not a hidden gap.
    /// </summary>
    public sealed class ShaperNodeCache
    {
        public int MaxEntries = 8192;

        sealed class Entry
        {
            public ShaperFieldBuffer buffer;
            public long lastTouch;
        }

        readonly Dictionary<ShaperCacheKey, Entry> map = new Dictionary<ShaperCacheKey, Entry>();
        long tick;

        /// <summary>Incremented every time <see cref="ShaperCachedEvaluator"/> actually did per-pixel work for
        /// a key (a cache MISS that was then computed and stored).</summary>
        public long ComputeCount;
        /// <summary>Incremented every time <see cref="TryGet"/> found an existing entry.</summary>
        public long HitCount;
        /// <summary>Incremented every time a node was skipped by the cache on purpose
        /// (<see cref="ShaperNodeIdentity.IsCacheable"/> false -- a MinMax-mode dial, see that method's doc).</summary>
        public long BypassCount;
        /// <summary>Incremented every time <see cref="MaxEntries"/> forced an eviction.</summary>
        public long EvictCount;

        public int Count => map.Count;

        /// <summary>Read-only presence check that does NOT count as a hit and does NOT touch the entry's LRU
        /// timestamp -- for a UI indicator ("is this frame already cached?") that must not itself distort the
        /// hit/miss counters it exists to help interpret, and must not keep a frame artificially "warm" against
        /// eviction just by being displayed.</summary>
        public bool ContainsKey(ShaperCacheKey key) => map.ContainsKey(key);

        public bool TryGet(ShaperCacheKey key, out ShaperFieldBuffer buffer)
        {
            if (map.TryGetValue(key, out var e))
            {
                e.lastTouch = ++tick;
                buffer = e.buffer;
                HitCount++;
                return true;
            }
            buffer = null;
            return false;
        }

        public void Store(ShaperCacheKey key, ShaperFieldBuffer buffer)
        {
            if (!map.ContainsKey(key) && map.Count >= MaxEntries) EvictOldest();
            map[key] = new Entry { buffer = buffer, lastTouch = ++tick };
        }

        void EvictOldest()
        {
            ShaperCacheKey victim = default;
            long oldest = long.MaxValue;
            bool found = false;
            foreach (var kv in map)
            {
                if (kv.Value.lastTouch < oldest) { oldest = kv.Value.lastTouch; victim = kv.Key; found = true; }
            }
            if (found) { map.Remove(victim); EvictCount++; }
        }

        public void Clear() => map.Clear();

        public void ResetCounters()
        {
            ComputeCount = 0; HitCount = 0; BypassCount = 0; EvictCount = 0;
        }
    }
}
