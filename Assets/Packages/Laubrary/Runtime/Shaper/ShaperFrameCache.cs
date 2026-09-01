using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- design B9 requirement 2: "each node also caches per frame, so the first pass through an
    /// animation may compute and every later loop replays." This is NOT a second cache alongside the per-node
    /// one (<see cref="ShaperNodeCache"/>) -- it is a thin bookkeeping layer OVER it. <see cref="ShaperNodeIdentity"/>
    /// already folds <c>phase01</c> into every node's own key (see that file's class doc for why), so a
    /// document's frame 7 and frame 12 already live at DIFFERENT keys in the SAME <see cref="ShaperNodeCache"/>
    /// used for live dial-drag preview. What this file adds is purely about ANIMATION PLAYBACK, not a new
    /// storage mechanism:
    /// <list type="bullet">
    /// <item>a frame INDEX to <c>phase01</c> mapping (<see cref="PhaseOfFrame"/>), so "frame 7 of 24" has one
    /// agreed meaning;</item>
    /// <item><see cref="IsFrameCached"/> -- a read-only presence check (never a compute, never a hit-count
    /// bump) for a UI to show which frames are already resident, so a slow first loop reads as "working,
    /// filling in" rather than "broken";</item>
    /// <item><see cref="ComputeFrame"/> -- do the actual work for one frame, called either synchronously
    /// (scrubbing straight to an uncached frame) or by a background driver one bounded slice at a time (see
    /// <c>Editor/Shaper/ShaperFramePrebaker.cs</c> -- EDITOR-ONLY, because a non-blocking "spend at most N
    /// milliseconds per editor tick" driver needs <c>EditorApplication.update</c>, which this RUNTIME asmdef
    /// may not reference; this file has zero UnityEditor dependency and could equally be driven by a runtime
    /// coroutine if the underlying node cache is ever consumed at play time).</item>
    /// </list>
    /// </summary>
    public sealed class ShaperFrameCache
    {
        public readonly ShaperNode root;
        public readonly ShaperNodeCache nodeCache;
        public readonly ShaperSampleGrid grid;
        public readonly int width, height;
        public readonly int frameCount;
        public readonly uint seed;

        /// <summary>Cached per frame index so <see cref="IsFrameCached"/> never has to walk the tree just to
        /// answer "is this frame resident" -- computed once when a frame is actually asked for by
        /// <see cref="PhaseOfFrame"/>/<see cref="RootKeyOfFrame"/> below and reused.</summary>
        readonly ShaperCacheKey[] rootKeyCache;
        readonly bool[] rootKeyKnown;

        public ShaperFrameCache(ShaperNode root, ShaperNodeCache nodeCache, ShaperSampleGrid grid,
                                int width, int height, int frameCount, uint seed = 0u)
        {
            this.root = root;
            this.nodeCache = nodeCache;
            this.grid = grid;
            this.width = width;
            this.height = height;
            this.frameCount = Mathf.Max(1, frameCount);
            this.seed = seed;
            rootKeyCache = new ShaperCacheKey[this.frameCount];
            rootKeyKnown = new bool[this.frameCount];
        }

        /// <summary>Frame 0 is phase 0, the LAST frame is phase 1 -- a single-frame document (frameCount == 1)
        /// is phase 0 throughout, the same convention <c>ShaperCompiler.Compile</c>'s own default parameter
        /// (<c>phase01 = 0f</c>) already uses for a non-animated document.
        ///
        /// T-0144 moved the arithmetic itself to <see cref="ShaperClock.PhaseOfFrame"/> without changing it,
        /// so this cache and <see cref="ShaperDocument.PhaseOfFrame"/> cannot drift into two answers for one
        /// question. This convention was set here first; see <see cref="ShaperClock"/> for why it stands.</summary>
        public float PhaseOfFrame(int frameIndex) => ShaperClock.PhaseOfFrame(frameIndex, frameCount);

        public ShaperCacheKey RootKeyOfFrame(int frameIndex)
        {
            frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);
            if (rootKeyKnown[frameIndex]) return rootKeyCache[frameIndex];
            var key = ShaperNodeIdentity.FullSubtreeStructuralKey(root, PhaseOfFrame(frameIndex), seed);
            rootKeyCache[frameIndex] = key;
            rootKeyKnown[frameIndex] = true;
            return key;
        }

        /// <summary>True when this frame's ROOT result is already resident in <see cref="nodeCache"/> -- the
        /// data a future window's per-frame cached/not-cached strip would read. Does NOT touch the cache's
        /// hit/miss counters (see <see cref="ShaperNodeCache.ContainsKey"/>'s own doc).</summary>
        public bool IsFrameCached(int frameIndex) => nodeCache.ContainsKey(RootKeyOfFrame(frameIndex));

        /// <summary>How many of the <see cref="frameCount"/> frames are currently resident -- the single number
        /// a progress readout needs.</summary>
        public int CountCachedFrames()
        {
            int c = 0;
            for (int i = 0; i < frameCount; i++) if (IsFrameCached(i)) c++;
            return c;
        }

        /// <summary>Compute (or, if already resident, cheaply confirm) one frame. Returns the root buffer.
        /// This is where the actual per-pixel work happens -- called synchronously when a user scrubs straight
        /// to an uncached frame, or by a background driver one frame (or a few, time-budgeted) per tick.</summary>
        public ShaperFieldBuffer ComputeFrame(int frameIndex)
        {
            float phase = PhaseOfFrame(frameIndex);
            var result = ShaperCachedEvaluator.Evaluate(root, ShaperMatrix.Identity, phase, seed, grid, width, height, nodeCache);
            return result.buffer;
        }

        /// <summary>The first frame index at or after <paramref name="fromInclusive"/> that is NOT yet cached,
        /// wrapping once back to 0 -- what a background pre-baker walks in order. Returns -1 when every frame
        /// is already resident (nothing left to bake).</summary>
        public int NextUncachedFrame(int fromInclusive)
        {
            for (int i = 0; i < frameCount; i++)
            {
                int idx = (fromInclusive + i) % frameCount;
                if (!IsFrameCached(idx)) return idx;
            }
            return -1;
        }
    }
}
