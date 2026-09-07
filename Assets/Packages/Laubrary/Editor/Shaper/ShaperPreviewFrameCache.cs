// ShaperPreviewFrameCache — the per-frame PIXEL cache actually wired into the preview stage, the transport
// and the background pre-baker (T-0165).
//
// ── Why this is a NEW class rather than wiring the T-0115 node cache (ShaperFrameCache/ShaperNodeCache/
// ShaperCachedEvaluator, Runtime/Shaper) as T-0165's brief literally names ────────────────────────────────
// Those three cached only a NODE's own distance FIELD -- ShaperCachedEvaluator's own class doc said so
// explicitly ("this evaluator produces a node's DISTANCE FIELD for the node cache", .cs:56-58).
// The measured ~30ms/frame cost T-0115/T-0158 both cite comes from ShaperFillResolver.Resolve + PaintTile
// (fill, light, composite) and the effects pass, called from ShaperDocumentRenderer.RenderPhaseInto
// (ShaperDocumentRenderer.cs:187-195) -- and RenderPhaseInto never touched that node cache at all (grep
// across Runtime/Shaper for those names outside their own file and ShaperCacheAudit.cs found nothing). So
// wiring the geometry cache into the window exactly as built would have left every real millisecond of that
// cost completely unchanged -- a "cached" tick that lied about being fast. This class instead memoizes the
// thing the preview and transport actually need: the FINISHED Color32[] ShaperDocumentRenderer.RenderFrame
// produces per frame index, which is where the real cost lives. It mirrors that old cache's own API shape
// (IsFrameCached/CountCachedFrames/ComputeFrame/NextUncachedFrame) on purpose, and the old ShaperFramePrebaker's
// own tick/budget/event shape (see ShaperPreviewFramePrebaker below) on purpose.
//
// The T-0115 node cache and its prebaker were never wired to anything real and were deleted outright
// (T-0253, 2026-09-07) -- there is nothing left to retarget these call sites at.
//
// Editor-only by construction: it exists to serve one interactive window's live preview -- a runtime
// consumer would drive the same idea through a coroutine, not this class.
using System.Collections.Generic;
using Laubrary.PyreShaper;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// A per-document, per-frame-index cache of <see cref="ShaperDocumentRenderer.RenderFrame"/>'s finished
    /// pixels, rendered through a per-LAYER resolved-buffer cache.
    ///
    /// <b>T-0194 changed what an authored edit costs here.</b> This used to invalidate wholesale, because
    /// Shaper had no content hash reaching from a document edit down to "which frames does this actually
    /// touch" -- so every edit dropped every frame and every frame re-resolved every layer. It now has one
    /// (<see cref="ShaperLayerKey"/>), and <see cref="Invalidate(ShaperDocument)"/> uses it to sort resident
    /// frames into unchanged / re-composite-in-place / genuinely-recomputing. Over-invalidation still only
    /// costs a recompute; under-invalidation would show wrong pixels, and the key is built to err the first way.
    /// </summary>
    internal sealed class ShaperPreviewFrameCache
    {
        readonly Dictionary<int, Color32[]> pixels = new Dictionary<int, Color32[]>();
        readonly Dictionary<int, ShaperCacheKey> signatures = new Dictionary<int, ShaperCacheKey>();
        readonly ShaperRenderBufferPool bufferPool = new ShaperRenderBufferPool();

        /// <summary>
        /// T-0194 — the per-LAYER resolved-buffer cache this frame cache renders through. It is what makes an
        /// edit cost one layer instead of a document: <see cref="ShaperDocumentRenderer.RenderPhaseInto"/>
        /// re-resolves only the layers whose content key changed and composites the rest from here.
        /// </summary>
        readonly ShaperLayerBufferCache layerCache = new ShaperLayerBufferCache();

        readonly List<int> scratchLayers = new List<int>();

        /// <summary>Wall-clock ceiling on the in-place re-composite an <see cref="Invalidate"/> may do before it
        /// gives up and drops the rest for the background pre-baker. Sized so the re-composite still fits inside
        /// one editor frame at 60 Hz on a document with a dozen resident frames; anything past that is better
        /// spent letting the user keep dragging.</summary>
        public float MaxRecompositeMilliseconds = 12f;

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

        /// <summary>Drop everything, layer buffers included -- the unconditional reset, for a document swap or
        /// a canvas/frame-count change where no stored buffer can be reused at all.</summary>
        public void Invalidate()
        {
            pixels.Clear();
            signatures.Clear();
            bufferPool.Clear();
            layerCache.Clear();
        }

        /// <summary>
        /// <b>T-0194 -- the invalidation an authored edit actually wants.</b> Each resident frame is re-keyed
        /// (<see cref="ShaperLayerKey.FrameSignature"/>) and lands in one of three states:
        ///
        /// <list type="number">
        /// <item><b>Signature unchanged</b> -- the edit did not touch this frame's picture at all (a dial on a
        /// layer that is outside its lifetime window here, an edit undone). Kept, untouched.</item>
        /// <item><b>Changed, but every contributing layer is already resolved</b> -- a COMPOSITE-ONLY change: a
        /// layer toggled, a Z offset nudged, the background recoloured, a document effect edited. Re-rendered
        /// IN PLACE, which with the layer cache warm is a composite and an encode rather than a resolve, so the
        /// frame never leaves the cache and the tick strip never flashes for it.</item>
        /// <item><b>Changed and some layer must be re-resolved</b> -- dropped, for the pre-baker to refill. The
        /// strip goes yellow for exactly these frames, which is the honest signal: they really are recomputing.
        /// </item>
        /// </list>
        ///
        /// Over-invalidation still only costs a recompute and under-invalidation would show wrong pixels, so
        /// the signature is deliberately a superset of what the renderer reads (<see cref="ShaperLayerKey"/>'s
        /// header states that asymmetry).
        /// </summary>
        public void Invalidate(ShaperDocument doc)
        {
            if (doc == null) { Invalidate(); return; }

            bool effectsEnabled = ShaperEffectApplier.Instance != null;
            // ONE reflection walk of the document's layers for the whole invalidation. Re-walking per frame
            // measured 3.3 ms/frame on a 4-layer document -- more than the re-composite this is deciding about.
            layerCache.BeginPass(doc);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // A snapshot, because the loop writes to both dictionaries.
            var resident = new List<int>(pixels.Keys);
            for (int i = 0; i < resident.Count; i++)
            {
                int idx = resident[i];
                var sig = layerCache.Keys.FrameSignature(doc, idx, effectsEnabled);
                if (signatures.TryGetValue(idx, out var was) && was == sig) continue;

                if (CanRecomposite(doc, idx, effectsEnabled) && sw.Elapsed.TotalMilliseconds < MaxRecompositeMilliseconds)
                {
                    var px = ShaperDocumentRenderer.RenderFrame(doc, idx, ShaperEffectApplier.Instance,
                                                                bufferPool, layerCache);
                    if (px != null && px.Length == width * height)
                    {
                        pixels[idx] = px;
                        signatures[idx] = sig;
                        continue;
                    }
                }

                pixels.Remove(idx);
                signatures.Remove(idx);
            }
        }

        /// <summary>True when every layer that paints into <paramref name="frameIndex"/> already has its
        /// resolved buffer in hand, so re-rendering the frame is a composite rather than a resolve. This is the
        /// one question that separates "the strip may stay green" from "this frame is genuinely recomputing".</summary>
        bool CanRecomposite(ShaperDocument doc, int frameIndex, bool effectsEnabled)
        {
            int n = ShaperDocumentRenderer.SampleCount(doc);
            if (n == 0) return false;

            float phase01 = doc.PhaseOfFrame(frameIndex);
            ShaperLayerKey.ContributingLayers(doc, frameIndex, scratchLayers);
            for (int i = 0; i < scratchLayers.Count; i++)
            {
                int li = scratchLayers[i];
                float baseZ = ShaperHeightCompiler.LayerBase(doc, li, phase01, doc.seed);
                var key = layerCache.Keys.PaintKey(doc, li, phase01, baseZ, frameIndex, effectsEnabled);
                if (!layerCache.Contains(key, n)) return false;
            }
            return true;
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

            var px = ShaperDocumentRenderer.RenderFrame(doc, idx, ShaperEffectApplier.Instance, bufferPool,
                                                       layerCache);
            if (px != null && px.Length == width * height)
            {
                pixels[idx] = px;
                signatures[idx] = layerCache.Keys.FrameSignature(doc, idx, ShaperEffectApplier.Instance != null);
            }
            return px;
        }

        /// <summary>Layer-cache hits and misses since the last reset -- what a performance probe reads to tell a
        /// composite-only refill apart from a real re-resolve.</summary>
        public ShaperLayerBufferCache LayerCache => layerCache;

        /// <summary>The first frame index at or after <paramref name="fromInclusive"/> that has no cached
        /// pixels yet, wrapping once, or -1 when every frame is resident -- what the background pre-baker
        /// walks in order, mirroring the old T-0115 node cache's own <c>NextUncachedFrame</c> (since deleted,
        /// T-0253).</summary>
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
    /// The background pre-baker for <see cref="ShaperPreviewFrameCache"/> -- same non-blocking shape as the
    /// old T-0115 <c>ShaperFramePrebaker</c> (since deleted, T-0253; bounded per-tick wall-clock budget,
    /// resumable next tick, Progressed/Completed events), driving this file's pixel cache instead of the
    /// Runtime geometry one.
    /// </summary>
    internal sealed class ShaperPreviewFramePrebaker
    {
        readonly ShaperPreviewFrameCache cache;
        readonly System.Func<ShaperDocument> doc;
        int cursor;
        bool running;

        /// <summary>Same budget the old (deleted) ShaperFramePrebaker used -- one document's per-frame cost
        /// is comparable either way, so there is no reason to disagree.</summary>
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
