// ShaperPreviewFrameCache — the per-frame PIXEL cache actually wired into the preview stage, the transport
// and the background pre-baker (T-0165).
//
// ── Why this is a NEW class rather than wiring the existing ShaperFrameCache/ShaperNodeCache/
// ShaperCachedEvaluator (Runtime/Shaper) as T-0165's brief literally names ──────────────────────────────────
// Those three cache only a NODE's own distance FIELD (ShaperFieldBuffer) -- ShaperCachedEvaluator's own class
// doc says so explicitly ("this evaluator produces a node's DISTANCE FIELD for the node cache", .cs:56-58).
// The measured ~30ms/frame cost T-0115/T-0158 both cite comes from ShaperFillResolver.Resolve + PaintTile
// (fill, light, composite) and the effects pass, called from ShaperDocumentRenderer.RenderPhaseInto
// (ShaperDocumentRenderer.cs:187-195) -- and RenderPhaseInto never touches ShaperNodeCache or
// ShaperCachedEvaluator at all (grep across Runtime/Shaper for those two names outside their own file and
// ShaperCacheAudit.cs finds nothing). So wiring the geometry cache into the window exactly as built would
// leave every real millisecond of that cost completely unchanged -- a "cached" tick that lied about being
// fast. This class instead memoizes the thing the preview and transport actually need: the FINISHED
// Color32[] ShaperDocumentRenderer.RenderFrame produces per frame index, which is where the real cost lives.
// It mirrors ShaperFrameCache's own API shape (IsFrameCached/CountCachedFrames/ComputeFrame/NextUncachedFrame)
// on purpose, and ShaperFramePrebaker's own tick/budget/event shape (see ShaperPreviewFramePrebaker below) on
// purpose, so a future pass that closes the geometry-cache gap (making ShaperFillResolver itself
// content-cache-aware) can retarget these call sites at the Runtime classes without touching ShaperWindow.
//
// Editor-only by construction: it exists to serve one interactive window's live preview, matching where
// ShaperFramePrebaker itself already lives (this file's own sibling) and for the identical reason stated in
// ShaperFrameCache.cs's own header -- a runtime consumer would drive the same idea through a coroutine, not
// this class.
using System.Collections.Generic;
using Laubrary.PyreShaper;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// A per-document, per-frame-index cache of <see cref="ShaperDocumentRenderer.RenderFrame"/>'s finished
    /// pixels. Invalidated WHOLESALE by <see cref="Invalidate"/> -- Shaper has no fine-grained content hash
    /// reaching from a document edit down to "which frames does this actually touch" (that is exactly the
    /// unbuilt geometry-cache wiring described in this file's header), so the honest, always-correct choice
    /// is "any authored edit drops every frame's pixels", not a partial invalidation that risks showing a
    /// stale frame. Over-invalidation only costs a recompute; under-invalidation would show wrong pixels.
    /// </summary>
    internal sealed class ShaperPreviewFrameCache
    {
        readonly Dictionary<int, Color32[]> pixels = new Dictionary<int, Color32[]>();
        readonly ShaperRenderBufferPool bufferPool = new ShaperRenderBufferPool();

        public int frameCount = 1;
        public int width = 1, height = 1;

        /// <summary>Re-point this cache at a (possibly new) canvas shape/frame count, dropping everything if
        /// any of the three actually changed. Cheap to call every Refresh -- the common case (nothing
        /// changed) is three int compares.</summary>
        public void EnsureShape(int newFrameCount, int newWidth, int newHeight)
        {
            newFrameCount = Mathf.Max(1, newFrameCount);
            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);
            if (newFrameCount == frameCount && newWidth == width && newHeight == height) return;
            frameCount = newFrameCount;
            width = newWidth;
            height = newHeight;
            Invalidate();
        }

        /// <summary>Drop every cached frame's pixels (but keep the layer buffer pool -- its own exact-match
        /// check means a shape change simply reallocates the layers that actually changed size, which is no
        /// worse than before this pool existed). Call on any authored edit.</summary>
        public void Invalidate()
        {
            pixels.Clear();
            bufferPool.Clear();
        }

        public bool IsFrameCached(int frameIndex) => pixels.ContainsKey(Wrap(frameIndex));

        public int CountCachedFrames() => pixels.Count;

        /// <summary>Compute (or, if resident, return) one frame's pixels. Synchronous -- the caller (a direct
        /// scrub, or the background prebaker below) decides when this is acceptable to block on.</summary>
        public Color32[] ComputeFrame(int frameIndex, ShaperDocument doc)
        {
            int idx = Wrap(frameIndex);
            if (pixels.TryGetValue(idx, out var hit)) return hit;
            if (doc == null) return null;

            var px = ShaperDocumentRenderer.RenderFrame(doc, idx, ShaperEffectApplier.Instance, bufferPool);
            if (px != null && px.Length == width * height) pixels[idx] = px;
            return px;
        }

        /// <summary>The first frame index at or after <paramref name="fromInclusive"/> that has no cached
        /// pixels yet, wrapping once, or -1 when every frame is resident -- what the background pre-baker
        /// walks in order, mirroring <see cref="ShaperFrameCache.NextUncachedFrame"/>.</summary>
        public int NextUncachedFrame(int fromInclusive)
        {
            for (int i = 0; i < frameCount; i++)
            {
                int idx = Wrap(fromInclusive + i);
                if (!IsFrameCached(idx)) return idx;
            }
            return -1;
        }

        int Wrap(int i) => frameCount <= 1 ? 0 : ((i % frameCount) + frameCount) % frameCount;
    }

    /// <summary>
    /// The background pre-baker for <see cref="ShaperPreviewFrameCache"/> -- same non-blocking shape as
    /// <see cref="ShaperFramePrebaker"/> (bounded per-tick wall-clock budget, resumable next tick,
    /// Progressed/Completed events), driving this file's pixel cache instead of the Runtime geometry one.
    /// </summary>
    internal sealed class ShaperPreviewFramePrebaker
    {
        readonly ShaperPreviewFrameCache cache;
        readonly System.Func<ShaperDocument> doc;
        int cursor;
        bool running;

        /// <summary>Same budget as ShaperFramePrebaker -- one document's per-frame cost is comparable either
        /// way, so there is no reason for the two to disagree.</summary>
        public float MaxMillisecondsPerTick = 8f;

        public bool IsRunning => running;

        public event System.Action<ShaperPreviewFramePrebaker> Progressed;
        public event System.Action<ShaperPreviewFramePrebaker> Completed;

        public ShaperPreviewFramePrebaker(ShaperPreviewFrameCache cache, System.Func<ShaperDocument> doc)
        {
            this.cache = cache;
            this.doc = doc;
        }

        public void Start()
        {
            if (running) return;
            running = true;
            cursor = 0;
            UnityEditor.EditorApplication.update += Tick;
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            UnityEditor.EditorApplication.update -= Tick;
        }

        void Tick()
        {
            var d = doc?.Invoke();
            if (d == null || d.frameCount <= 1) { Stop(); return; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool progressed = false;
            while (sw.Elapsed.TotalMilliseconds < MaxMillisecondsPerTick)
            {
                int next = cache.NextUncachedFrame(cursor);
                if (next < 0)
                {
                    Stop();
                    Completed?.Invoke(this);
                    if (progressed) Progressed?.Invoke(this);
                    return;
                }
                cache.ComputeFrame(next, d);
                cursor = (next + 1) % Mathf.Max(1, cache.frameCount);
                progressed = true;
            }
            if (progressed) Progressed?.Invoke(this);
        }
    }
}
