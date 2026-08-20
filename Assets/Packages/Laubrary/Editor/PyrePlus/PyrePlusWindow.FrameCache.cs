// PyrePlusWindow.FrameCache — the ONE preview frame cache shared by the animated preview and the Strip view,
// built from a cache of LAYER buffers.
//
// Every layer of every frame is rendered at most once per CONTENT: a layer's isolated buffer is stored under its
// content key (PlusLayerKey — the layer's dials + its index + canvas/frames/seed + the global geometry/pixel
// modifiers), so hiding, showing or reordering layers, editing a spec-level post modifier, undoing a dial or
// switching back to an earlier state all find their buffers again and only COMPOSITE (PyrePlusRenderer.ComposeFrame:
// mattes, clips, Over, deferred borders, global posts). Editing a dial on one layer changes that layer's key alone,
// so only that layer's frames render; the rest of the stack composites from the store. The store is LRU-capped
// (LayerStoreBytes) and never evicts the layers of the spec being filled.
//
// Work is scheduled as jobs — "render layer L of frame F" and "compose frame F" — on every other core
// (PlusLayerFill, one spec clone per worker, the same cancel/generation discipline as T-0060) or, for specs the
// renderer cannot run off the main thread (PyrePlusRenderer.IsParallelSafe) and specs too cheap to be worth the
// threads, on the main thread within a per-tick budget. The frame on screen always comes first, synchronously,
// so a scrub or a dial drag stays responsive. Playback never renders — it only steps onto composited frames.
// Invalidated by the shared previewDirty flag (every authored edit routes through MarkDirty) or by a frameCount /
// canvas change; an invalidation recomputes the keys and schedules only what is missing.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // Per-tick budget of main-thread work (the serial fill, inline composes). A single heavy layer can blow
        // past it — it only decides whether ANOTHER job starts in the same tick.
        const double FillBudgetMs = 12.0;
        // Below this much remaining serial work a fill stays on the main thread: the worker threads and their spec
        // clones cost a few ms to spin up and their first results only land on the NEXT tick (measured in T-0060: a
        // 48 px × 6 Inferno, 19 ms of work, took 87 ms wall through 5 workers; a 64 px Disc clip is 2 ms).
        const double ParallelWorthMs = 100.0;
        // How much rendered layer data the store keeps. 64 MB = ~1000 layer-frames at 128 px.
        const long LayerStoreBytes = 64L << 20;

        // One RGBA32 point-filtered texture per frame, allocated once per (frameCount, canvasSize) and reused
        // across edits — a composite is uploaded INTO the same textures, so a dial drag never churns GPU memory.
        Texture2D[] frameCache;
        bool[] frameReady;
        int frameReadyCount;
        int frameCacheCanvas = -1;

        // The layer buffers, by content key. Lives as long as the window; the textures above are just the
        // composited view of it for the current spec state.
        PlusLayerCache layerStore;

        // The current fill's plan: what every frame needs (plans), under which keys (layerKeys, variants), what the
        // store already has (layerDone) and what has been handed to a worker (layerQueued / composeQueued).
        PyrePlusRenderer.LayerPlan[][] plans;         // [frame][layer]
        ulong[] layerKeys;           // [layer]
        ulong[][] variants;          // [frame][layer]
        bool[][] layerDone;          // [frame][layer] buffer present in the store under the current key + variant
        bool[][] layerQueued;        // [frame][layer] render job enqueued on the parallel fill
        int[] layerMissing;          // [frame] active layers still missing
        bool[] composeQueued;        // [frame] compose job enqueued on the parallel fill
        int fillGeneration;          // stamps every job; a compose from an older generation is dropped

        int fillCursor;              // the frame the fill starts from (the one on screen when it began)
        double fillStartedAt;
        double fillProbeMs = -1;     // the last layer-frame rendered on the MAIN thread — the cost sample that decides serial vs parallel
        int fillRenders, fillHits;   // layer-frames rendered vs found in the store, this fill
        int fillLayerFrames;         // active layer-frames this fill needs in total
        double fillRenderMs;         // render time summed so far (serial-equivalent)
        double lastFillMs = -1;      // wall time of the last COMPLETE fill (-1 = none yet) — in the readout tooltip
        int lastFillRenders, lastFillHits, lastFillWorkers;
        double lastFillRenderMs;
        int cacheRenderCount;        // every layer-frame render this cache has ever made — what a probe watches
        int cacheComposeCount;       // every frame composite

        PlusLayerFill parallelFill;
        readonly List<PlusLayerFill> retiredFills = new List<PlusLayerFill>();
        bool fillIsSerial;
        string fillSerialReason;

        // What a render job hands back.
        sealed class LayerFrameResult { public Color32[] pixels, border; }
        // A job's identity on the fill's queue.
        readonly struct JobTag
        {
            public readonly bool compose; public readonly int frame, frames, layer, generation; public readonly ulong key, variant;
            // `frames` is the frame count of the spec the job rendered for: a retired fill's render may land after
            // an asset switch, and its buffer is still valid — under its own key, sized for its own spec.
            public JobTag(bool compose, int frame, int frames, int layer, int generation, ulong key, ulong variant)
            { this.compose = compose; this.frame = frame; this.frames = frames; this.layer = layer; this.generation = generation; this.key = key; this.variant = variant; }
        }

        bool FillInProgress => frameCache != null && frameReadyCount < frameCache.Length;

        void DestroyFrameCache()
        {
            RetireParallelFill();
            if (frameCache != null)
                for (int i = 0; i < frameCache.Length; i++)
                    if (frameCache[i] != null) DestroyImmediate(frameCache[i]);
            frameCache = null;
            frameReady = null;
            frameReadyCount = 0;
            frameCacheCanvas = -1;
            plans = null;
        }

        // Forget the layer buffers too (window close). An asset switch keeps them: a different asset has different
        // keys, and switching back finds its layers again.
        void DestroyLayerStore() { layerStore = null; }

        // Cancel the fill in flight (if any) and park it until its workers are idle; its finished RENDER results are
        // still absorbed (they are content-addressed, so they are valid whatever the spec did since), its composes
        // are not. Disposed from the tick once the threads have exited, so the main thread never blocks mid-job.
        void RetireParallelFill()
        {
            if (parallelFill == null) return;
            parallelFill.Cancel();
            retiredFills.Add(parallelFill);
            parallelFill = null;
        }

        void ReapRetiredFills()
        {
            for (int i = retiredFills.Count - 1; i >= 0; i--)
            {
                var f = retiredFills[i];
                while (f.TryTake(out var r)) if (!((JobTag)r.tag).compose) Absorb(r, fromWorker: true);
                if (f.WorkersIdle) { f.Dispose(); retiredFills.RemoveAt(i); }
            }
        }

        void DisposeAllFills()
        {
            RetireParallelFill();
            foreach (var f in retiredFills) f.Dispose();
            retiredFills.Clear();
        }

        // Bring the cache in line with the spec. Consumes previewDirty: the keys and plans are recomputed, every
        // frame is marked not-composited, and only the layer-frames the store lacks get scheduled. A frameCount /
        // canvas change reallocates the textures. Cheap when nothing changed — every consumer calls it first.
        void EnsureFrameCache(PyrePlusSpec s)
        {
            int n = Mathf.Max(1, s.frameCount);
            bool structural = frameCache == null || frameCache.Length != n || frameCacheCanvas != s.canvasSize;
            if (!structural && !previewDirty) return;

            if (structural)
            {
                DestroyFrameCache();
                frameCache = new Texture2D[n];
                frameReady = new bool[n];
                for (int i = 0; i < n; i++)
                    frameCache[i] = new Texture2D(s.Width, s.Height, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                frameCacheCanvas = s.canvasSize;
            }
            else
            {
                RetireParallelFill();
                Array.Clear(frameReady, 0, frameReady.Length);
            }
            layerStore ??= new PlusLayerCache(LayerStoreBytes);
            layerStore.BeginGeneration();
            fillGeneration++;

            int L = s.layers != null ? s.layers.Count : 0;
            layerKeys = new ulong[L];
            for (int li = 0; li < L; li++) layerKeys[li] = PlusLayerKey.LayerKey(s, li);
            plans = new PyrePlusRenderer.LayerPlan[n][];
            variants = new ulong[n][];
            layerDone = new bool[n][];
            layerQueued = new bool[n][];
            layerMissing = new int[n];
            composeQueued = new bool[n];
            fillLayerFrames = 0; fillRenders = 0; fillHits = 0;
            for (int f = 0; f < n; f++)
            {
                var pf = plans[f] = PyrePlusRenderer.PlanFrame(s, f);
                variants[f] = new ulong[L];
                layerDone[f] = new bool[L];
                layerQueued[f] = new bool[L];
                for (int li = 0; li < L; li++)
                {
                    if (!pf[li].active) continue;
                    fillLayerFrames++;
                    variants[f][li] = PlusLayerKey.FrameVariant(s, pf, layerKeys, li);
                    if (layerStore.TryGet(layerKeys[li], f, variants[f][li], out _)) { layerDone[f][li] = true; fillHits++; }
                    else layerMissing[f]++;
                }
            }
            frameReadyCount = 0;
            fillCursor = Mathf.Clamp(frame, 0, n - 1);
            fillStartedAt = EditorApplication.timeSinceStartup;
            fillRenderMs = 0;
            fillProbeMs = -1;
            fillIsSerial = false;
            fillSerialReason = null;
            previewDirty = false;
            RefreshFillReadout();
        }

        bool IsFrameReady(int i) => frameReady != null && i >= 0 && i < frameReady.Length && frameReady[i];

        // The texture for frame i, composited NOW if the cache doesn't have it yet — the "current frame first"
        // rule: whatever the preview is about to show is never deferred to the background fill.
        Texture2D CachedFrame(PyrePlusSpec s, int i)
        {
            EnsureFrameCache(s);
            i = Mathf.Clamp(i, 0, frameCache.Length - 1);
            if (!frameReady[i]) ComposeNow(s, i);
            return frameCache[i];
        }

        // ── jobs ─────────────────────────────────────────────────────────────────────────────────────────────
        // The two job bodies. Both take the spec they run against (the real one on the main thread, a worker's
        // clone on a thread) and read only the captured plan + buffers — the plan is identical for a clone.

        Func<PyrePlusSpec, object> RenderJob(int f, int li)
        {
            var pf = plans[f];
            var plan = pf[li];
            // A heightmap consumer renders from the accumulated channel of the writers below it — captured from the
            // store now (ScheduleReady only schedules a consumer once its writers are present).
            Color32[][] writerPixels = plan.isHeightConsumer ? SlotPixels(f, pf, li) : null;
            return s =>
            {
                float[] hf = plan.isHeightConsumer
                    ? PyrePlusRenderer.AccumulateChannel(s, pf, writerPixels, li, s.layers[li].heightFromChannel) : null;
                var px = PyrePlusRenderer.RenderLayerFrame(s, li, f, plan, hf, out var border);
                return new LayerFrameResult { pixels = px, border = border };
            };
        }

        Func<PyrePlusSpec, object> ComposeJob(int f)
        {
            var pf = plans[f];
            var px = SlotPixels(f, pf, pf.Length);
            var borders = new Color32[pf.Length][];
            for (int li = 0; li < pf.Length; li++)
                if (pf[li].active && layerStore.TryGet(layerKeys[li], f, variants[f][li], out var slot)) borders[li] = slot.border;
            return s => PyrePlusRenderer.ComposeFrame(s, f, pf, px, borders);
        }

        // The stored pixel buffers of the active layers of frame f below `uptoLi`, indexed like spec.layers.
        Color32[][] SlotPixels(int f, PyrePlusRenderer.LayerPlan[] pf, int uptoLi)
        {
            var px = new Color32[pf.Length][];
            for (int li = 0; li < uptoLi && li < pf.Length; li++)
                if (pf[li].active && layerStore.TryGet(layerKeys[li], f, variants[f][li], out var slot)) px[li] = slot.pixels;
            return px;
        }

        // A heightmap consumer may only render once every writer it reads is in the store.
        bool DepsReady(int f, int li)
        {
            var pf = plans[f];
            if (!pf[li].isHeightConsumer) return true;
            int channel = spec.layers[li].heightFromChannel;
            for (int w = 0; w < li; w++)
                if (pf[w].active && pf[w].isMatte && Mathf.Clamp(spec.layers[w].matteChannel, 0, 3) == channel && !layerDone[f][w]) return false;
            return true;
        }

        // Store one finished render (from a worker or the main thread) and account for it against the current
        // fill when it is what the fill is waiting for.
        void Absorb(PlusLayerFill.Result r, bool fromWorker)
        {
            var tag = (JobTag)r.tag;
            if (!(r.payload is LayerFrameResult lf) || lf.pixels == null) return;
            if (layerStore == null || tag.frame < 0 || tag.frame >= tag.frames) return;
            layerStore.Put(tag.key, tag.frames, tag.frame, tag.variant, lf.pixels, lf.border);
            cacheRenderCount++;
            if (frameCache != null && tag.frames == frameCache.Length && tag.layer < layerKeys.Length && layerKeys[tag.layer] == tag.key && variants[tag.frame][tag.layer] == tag.variant
                && plans[tag.frame][tag.layer].active && !layerDone[tag.frame][tag.layer])
            {
                layerDone[tag.frame][tag.layer] = true;
                layerMissing[tag.frame]--;
                fillRenders++;
                fillRenderMs += r.ms;
                if (fromWorker && parallelFill != null) ScheduleReady(tag.frame);
            }
        }

        // Render one layer-frame on the main thread, against the real spec. Its time is the cost sample.
        void RenderInline(int f, int li)
        {
            double t0 = EditorApplication.timeSinceStartup;
            var payload = RenderJob(f, li)(spec);
            double ms = (EditorApplication.timeSinceStartup - t0) * 1000.0;
            fillProbeMs = ms;
            Absorb(new PlusLayerFill.Result(new JobTag(false, f, frameCache.Length, li, fillGeneration, layerKeys[li], variants[f][li]), payload, ms), fromWorker: false);
        }

        // Composite one frame on the main thread (every layer present) and upload it.
        void ComposeInline(int f)
        {
            var px = (Color32[])ComposeJob(f)(spec);
            Upload(f, px);
        }

        void Upload(int f, Color32[] px)
        {
            if (px == null || frameReady[f]) return;
            frameCache[f].SetPixels32(px);
            frameCache[f].Apply();
            frameReady[f] = true;
            frameReadyCount++;
            cacheComposeCount++;
            if (frameReadyCount == frameCache.Length)
            {
                lastFillMs = (EditorApplication.timeSinceStartup - fillStartedAt) * 1000.0;
                lastFillRenders = fillRenders; lastFillHits = fillHits; lastFillRenderMs = fillRenderMs;
                lastFillWorkers = parallelFill != null ? parallelFill.Workers : 1;
                RetireParallelFill();
            }
            RefreshFillReadout();
        }

        // Everything frame i needs, now: the missing layers (writers before consumers — list order), then the
        // composite. This is the synchronous path a repaint takes for the frame on screen.
        void ComposeNow(PyrePlusSpec s, int i)
        {
            var pf = plans[i];
            for (int li = 0; li < pf.Length; li++)
                if (pf[li].active && !layerDone[i][li]) RenderInline(i, li);
            ComposeInline(i);
        }

        // Hand the parallel fill every job that can run now for frame f: a compose when the frame is complete (at
        // the front of the queue — it is cheap and makes the frame visible), else the heightmap consumers whose
        // writers just landed.
        void ScheduleReady(int f)
        {
            if (parallelFill == null || frameReady[f]) return;
            if (layerMissing[f] == 0)
            {
                if (!composeQueued[f])
                {
                    composeQueued[f] = true;
                    parallelFill.Enqueue(new JobTag(true, f, frameCache.Length, -1, fillGeneration, 0, 0), ComposeJob(f), urgent: true);
                }
                return;
            }
            var pf = plans[f];
            for (int li = 0; li < pf.Length; li++)
                if (pf[li].active && !layerDone[f][li] && !layerQueued[f][li] && DepsReady(f, li))
                {
                    layerQueued[f][li] = true;
                    parallelFill.Enqueue(new JobTag(false, f, frameCache.Length, li, fillGeneration, layerKeys[li], variants[f][li]), RenderJob(f, li));
                }
        }

        // One editor tick of background fill. Repaints the preview and the readout as frames land. Runs whether or
        // not playback is on (a paused preview still wants its strip and its scrub targets ready).
        void FillFrameCacheTick()
        {
            ReapRetiredFills();
            if (spec == null || preview == null) return;
            EnsureFrameCache(spec);
            if (!FillInProgress) return;

            int n = frameCache.Length;
            bool landed = false;

            // The frame on screen always goes first, on the main thread, wherever the fill was — playback resets and
            // cherry picks move `frame` between ticks, and that frame is the one the next repaint would otherwise
            // composite synchronously anyway.
            if (frame >= 0 && frame < n && !frameReady[frame]) { ComposeNow(spec, frame); fillCursor = frame; landed = true; }
            if (!FillInProgress) { if (landed) preview.MarkDirtyRepaint(); return; }

            if (parallelFill != null)
            {
                // Drain what the workers finished since the last tick: renders into the store (which schedules the
                // consumers / composes they unblock), composites into the textures. A job that threw leaves a null
                // payload — the fill falls back to the main thread, once, with the error in the console.
                var fill = parallelFill;
                while (fill.TryTake(out var r))
                {
                    var tag = (JobTag)r.tag;
                    if (r.payload == null) continue;
                    if (tag.compose)
                    {
                        if (tag.generation == fillGeneration && tag.frame >= 0 && tag.frame < n && !frameReady[tag.frame]) { Upload(tag.frame, (Color32[])r.payload); landed = true; }
                    }
                    else Absorb(r, fromWorker: true);
                }
                if (parallelFill == fill && fill.FirstError != null)
                {
                    Debug.LogException(fill.FirstError);
                    RetireParallelFill(); fillIsSerial = true; fillSerialReason = "a worker thread failed";
                }
            }
            else if (!fillIsSerial && retiredFills.Count == 0)
            {
                // Nothing running and no cancelled fill still winding down (a dial drag retires one per edit; starting
                // another while the old workers are mid-job would only oversubscribe the cores). Decide the path
                // once per fill: the parallel-safety predicate, then the cost (ParallelWorthMs).
                int missing = 0;
                for (int f = 0; f < n; f++) missing += layerMissing[f];
                if (!PyrePlusRenderer.IsParallelSafe(spec, out var why)) { fillIsSerial = true; fillSerialReason = why; }
                else if (fillProbeMs >= 0 && fillProbeMs * missing < ParallelWorthMs) { fillIsSerial = true; fillSerialReason = "cheap spec"; }
                else
                {
                    parallelFill = new PlusLayerFill(spec);
                    for (int k = 0; k < n; k++) ScheduleReady((fillCursor + k) % n);
                }
            }

            if (fillIsSerial)
            {
                // The serial fill: frames forward from fillCursor (wrapping), each frame's missing layers in list
                // order then its composite, until the budget is spent — at least one job per tick so a heavy spec
                // always makes progress.
                double t0 = EditorApplication.timeSinceStartup;
                bool within() => (EditorApplication.timeSinceStartup - t0) * 1000.0 < FillBudgetMs;
                bool first = true;
                for (int k = 0; k < n && (first || within()); k++)
                {
                    int f = (fillCursor + k) % n;
                    if (frameReady[f]) continue;
                    var pf = plans[f];
                    for (int li = 0; li < pf.Length && (first || within()); li++)
                        if (pf[li].active && !layerDone[f][li]) { RenderInline(f, li); first = false; }
                    if (layerMissing[f] == 0 && (first || within())) { ComposeInline(f); landed = true; first = false; }
                }
            }

            if (landed) preview.MarkDirtyRepaint();
        }

        // The transport's "rendering n/N" readout: visible only while a fill is in progress (visibility, not
        // display, so the row never reflows). Its tooltip carries what the fill is doing and the timing of the last
        // complete one — the cheapest honest answer to "how heavy is this spec".
        void RefreshFillReadout()
        {
            if (fillReadout == null) return;
            bool filling = FillInProgress;
            fillReadout.style.visibility = filling ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            if (filling)
            {
                fillReadout.text = $"rendering {frameReadyCount}/{frameCache.Length}";
                string work = $" This fill: {fillLayerFrames} layer-frames, {fillHits} already cached, {fillRenders} rendered so far.";
                string path = parallelFill != null ? $" Rendering on {parallelFill.Workers} worker threads."
                            : fillIsSerial && fillSerialReason != null ? $" Rendering on the main thread ({fillSerialReason})." : "";
                string last = lastFillMs < 0 ? ""
                            : $" Last complete fill: {lastFillMs:F0} ms wall{(lastFillWorkers > 1 ? $" on {lastFillWorkers} threads" : "")}, {lastFillRenders} layer-frames rendered ({lastFillRenderMs:F0} ms), {lastFillHits} from the cache.";
                fillReadout.tooltip = "Frames composited into the preview cache so far. Each layer is cached on its own, so hiding, "
                    + "showing or reordering layers and editing spec-level post modifiers only recomposite, and a dial on one "
                    + "layer re-renders that layer alone. Playback only steps onto composited frames; after the first loop "
                    + "every loop plays from the cache. Any edit restarts the fill from the frame on screen." + work + path + last;
            }
        }
    }
}
