// PyrePlusWindow.FrameCache — the ONE preview frame cache shared by the animated preview and the Strip view.
// Every frame of the spec is rendered at most once per edit: the frame on screen renders immediately (so a
// scrub or a dial drag stays responsive), the rest render on every other core (PlusFrameFill) and are uploaded
// from the editor tick as they land, in playback order from the current frame. Playback never calls the renderer —
// it only advances onto frames that are already in the cache, so the first loop plays as fast as the fill allows
// and every loop after it costs nothing. Invalidated by the shared previewDirty flag (every authored edit routes
// through MarkDirty) or by a frameCount / canvas change. Specs the renderer cannot run off the main thread
// (stateful sims, Text, Sprite — PyrePlusRenderer.IsParallelSafe) and specs too cheap to be worth the threads keep
// the serial per-tick fill.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // Per-tick render budget of the SERIAL fill. A single heavy frame can blow well past this — the budget only
        // decides whether a SECOND frame starts in the same tick, so cheap specs fill in a few ticks while heavy ones
        // yield to the editor between every frame.
        const double FillBudgetMs = 12.0;
        // Below this much remaining serial work a fill stays on the main thread: spinning up the worker threads and
        // their spec clones costs a few ms and their first results only land on the NEXT tick, so a spec the serial
        // loop finishes in a handful of ticks gains nothing (measured: a 48 px × 6-frame Inferno, 19 ms of rendering,
        // took 87 ms wall through 5 workers; a 64 px Disc clip is 2 ms).
        const double ParallelWorthMs = 100.0;

        // One RGBA32 point-filtered texture per frame, allocated once per (frameCount, canvasSize) and reused
        // across edits — an edit clears the ready flags and re-renders INTO the same textures, so a dial drag
        // never churns GPU memory. Destroyed on disable / asset change / structural change.
        Texture2D[] frameCache;
        bool[] frameReady;
        int frameReadyCount;
        int frameCacheCanvas = -1;
        int fillCursor;            // the next frame the background fill tries (cycles forward from the current frame)
        double fillStartedAt;      // EditorApplication.timeSinceStartup when the current fill began
        double lastFillMs = -1;    // wall time of the last COMPLETE fill (-1 = none yet) — shown in the readout tooltip
        double lastFillSlowestMs;  // the slowest single frame of that fill
        double lastFillRenderMs;   // the sum of that fill's per-frame render times (its serial-equivalent cost)
        int lastFillWorkers;       // 1 = it ran serially
        double fillSlowestMs;      // the slowest frame of the fill in progress
        double fillRenderMs;       // per-frame render time summed so far for the fill in progress
        double fillProbeMs = -1;   // the last frame this fill rendered on the MAIN thread — the cost sample that decides serial vs parallel
        int cacheRenderCount;      // every renderer call this cache has ever made (main thread or worker) — the number a probe watches to prove playback renders nothing

        // The parallel fill in flight (null = none: idle, serial, or waiting for a cancelled one to wind down). An
        // invalidation does not dispose it — its workers may be mid-frame — it is cancelled and parked in `retired`
        // until every worker has left, then disposed from the tick. Its queue is never drained again, so a stale
        // frame can never land in the new cache: the fill object IS the generation id.
        PlusFrameFill parallelFill;
        readonly List<PlusFrameFill> retiredFills = new List<PlusFrameFill>();
        bool fillIsSerial;         // the fill in progress chose the serial path (readout tooltip says why)
        string fillSerialReason;

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
        }

        // Cancel the fill in flight (if any) and park it until its workers are idle. Called from every invalidation;
        // the actual Dispose happens in ReapRetiredFills once the threads have exited, so the main thread never
        // blocks on a worker mid-frame.
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
                if (retiredFills[i].WorkersIdle) { retiredFills[i].Dispose(); retiredFills.RemoveAt(i); }
        }

        // The window is going away (close, asset switch handled separately, domain reload): wait for any worker
        // still mid-frame so the clones can be destroyed — bounded by one frame's render time.
        void DisposeAllFills()
        {
            RetireParallelFill();
            foreach (var f in retiredFills) f.Dispose();
            retiredFills.Clear();
        }

        // Bring the cache in line with the spec's render inputs. Consumes previewDirty: a dirty cache keeps its
        // textures but forgets every frame, and the fill restarts from the frame on screen. A frameCount / canvas
        // change reallocates. Cheap when nothing changed — every consumer calls it first.
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
                System.Array.Clear(frameReady, 0, frameReady.Length);
            }
            frameReadyCount = 0;
            fillCursor = Mathf.Clamp(frame, 0, n - 1);
            fillStartedAt = EditorApplication.timeSinceStartup;
            fillSlowestMs = 0;
            fillRenderMs = 0;
            fillProbeMs = -1;
            fillIsSerial = false;
            fillSerialReason = null;
            previewDirty = false;
            RefreshFillReadout();
        }

        bool IsFrameReady(int i) => frameReady != null && i >= 0 && i < frameReady.Length && frameReady[i];

        // The texture for frame i, rendering it NOW if the cache doesn't have it yet — this is the "current frame
        // first" rule: whatever the preview is about to show is never deferred to the background fill.
        Texture2D CachedFrame(PyrePlusSpec s, int i)
        {
            EnsureFrameCache(s);
            i = Mathf.Clamp(i, 0, frameCache.Length - 1);
            if (!frameReady[i]) RenderIntoCache(s, i);
            return frameCache[i];
        }

        // Render frame i on the main thread and upload it. Returns its render time.
        double RenderIntoCache(PyrePlusSpec s, int i)
        {
            double t0 = EditorApplication.timeSinceStartup;
            var px = PyrePlusRenderer.RenderFrame(s, i);
            double ms = (EditorApplication.timeSinceStartup - t0) * 1000.0;
            fillProbeMs = ms;
            StoreFrame(i, px, ms);
            return ms;
        }

        // Upload one rendered frame (from the main thread or a worker) into its texture and account for it.
        void StoreFrame(int i, Color32[] px, double renderMs)
        {
            frameCache[i].SetPixels32(px);
            frameCache[i].Apply();
            frameReady[i] = true;
            frameReadyCount++;
            cacheRenderCount++;
            fillRenderMs += renderMs;
            if (renderMs > fillSlowestMs) fillSlowestMs = renderMs;
            if (frameReadyCount == frameCache.Length)
            {
                lastFillMs = (EditorApplication.timeSinceStartup - fillStartedAt) * 1000.0;
                lastFillSlowestMs = fillSlowestMs;
                lastFillRenderMs = fillRenderMs;
                lastFillWorkers = parallelFill != null ? parallelFill.Workers : 1;
                RetireParallelFill();
            }
            RefreshFillReadout();
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
            // render synchronously anyway. (Usually the repaint already did, through CachedFrame — either way every
            // fill starts with one main-thread render, whose time is the cost sample that picks serial vs parallel.)
            if (frame >= 0 && frame < n && !frameReady[frame]) { RenderIntoCache(spec, frame); fillCursor = frame; landed = true; }
            if (!FillInProgress) { if (landed) preview.MarkDirtyRepaint(); return; }

            if (parallelFill != null)
            {
                // Drain what the workers finished since the last tick. Uploads are cheap (a 128 px RGBA32 is ~64 KB),
                // so everything pending goes in this tick; a frame the main thread rendered meanwhile (a cherry
                // pick, a scrub) is simply skipped. A worker that threw leaves null pixels — that frame renders
                // serially below, once, with the error surfaced in the console. (Local: storing the last frame
                // completes the fill and retires the field mid-loop.)
                var fill = parallelFill;
                while (fill.TryTake(out var r))
                {
                    if (r.pixels == null)
                    {
                        if (fill.FirstError != null) Debug.LogException(fill.FirstError);
                        continue;
                    }
                    if (r.frameIndex >= 0 && r.frameIndex < n && !frameReady[r.frameIndex]) { StoreFrame(r.frameIndex, r.pixels, r.renderMs); landed = true; }
                }
                // Workers all gone but frames still missing (a worker threw): finish serially.
                if (parallelFill == fill && fill.WorkersIdle && FillInProgress) { RetireParallelFill(); fillIsSerial = true; fillSerialReason = "a worker thread failed"; }
            }
            else if (!fillIsSerial && retiredFills.Count == 0)
            {
                // Nothing running and no cancelled fill still winding down (a dial drag retires one per edit; starting
                // another while the old workers are mid-frame would only oversubscribe the cores). Decide the path
                // once per fill: the parallel-safety predicate, then the cost (ParallelWorthMs).
                if (!PyrePlusRenderer.IsParallelSafe(spec, out var why)) { fillIsSerial = true; fillSerialReason = why; }
                else if (fillProbeMs >= 0 && fillProbeMs * (n - frameReadyCount) < ParallelWorthMs) { fillIsSerial = true; fillSerialReason = "cheap spec"; }
                else
                {
                    var order = new List<int>(n);
                    for (int k = 0; k < n; k++) { int f = (fillCursor + k) % n; if (!frameReady[f]) order.Add(f); }
                    if (order.Count > 0) parallelFill = new PlusFrameFill(spec, order);
                }
            }

            if (fillIsSerial)
            {
                // The serial fill: render missing frames forward from fillCursor (wrapping) until the budget is spent —
                // at least one frame per tick so a heavy spec always makes progress.
                double t0 = EditorApplication.timeSinceStartup;
                do
                {
                    int tries = 0;
                    while (frameReady[fillCursor] && tries++ < n) fillCursor = (fillCursor + 1) % n;
                    if (frameReady[fillCursor]) break;   // nothing left
                    RenderIntoCache(spec, fillCursor);
                    landed = true;
                    fillCursor = (fillCursor + 1) % n;
                }
                while (FillInProgress && (EditorApplication.timeSinceStartup - t0) * 1000.0 < FillBudgetMs);
            }

            if (landed) preview.MarkDirtyRepaint();
        }

        // The transport's "rendering n/N" readout: visible only while a fill is in progress (visibility, not
        // display, so the row never reflows). Its tooltip carries the timing of the last complete fill, which is
        // the cheapest honest answer to "how heavy is this spec".
        void RefreshFillReadout()
        {
            if (fillReadout == null) return;
            bool filling = FillInProgress;
            fillReadout.style.visibility = filling ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
            if (filling)
            {
                fillReadout.text = $"rendering {frameReadyCount}/{frameCache.Length}";
                string path = parallelFill != null ? $" Rendering on {parallelFill.Workers} worker threads."
                            : fillIsSerial && fillSerialReason != null ? $" Rendering on the main thread ({fillSerialReason})." : "";
                string last = lastFillMs < 0 ? ""
                            : lastFillWorkers > 1
                                ? $" Last complete fill: {lastFillMs:F0} ms wall on {lastFillWorkers} threads for {lastFillRenderMs:F0} ms of rendering (slowest frame {lastFillSlowestMs:F0} ms)."
                                : $" Last complete fill: {lastFillMs:F0} ms (slowest frame {lastFillSlowestMs:F0} ms).";
                fillReadout.tooltip = "Frames rendered into the preview cache so far. Playback only steps onto rendered "
                    + "frames, so the first loop waits at the render front; after that every loop plays from the cache "
                    + "with no rendering at all. Any edit restarts the fill from the frame on screen." + path + last;
            }
        }
    }
}
