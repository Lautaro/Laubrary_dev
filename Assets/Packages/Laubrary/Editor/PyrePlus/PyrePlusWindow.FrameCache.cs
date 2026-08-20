// PyrePlusWindow.FrameCache — the ONE preview frame cache shared by the animated preview and the Strip view.
// Every frame of the spec is rendered at most once per edit: the frame on screen renders immediately (so a
// scrub or a dial drag stays responsive), the rest fill in from the editor tick under a time budget, in playback
// order from the current frame, repainting as they land. Playback never calls the renderer — it only advances
// onto frames that are already in the cache, so the first loop plays as fast as the fill allows and every loop
// after it costs nothing. Invalidated by the shared previewDirty flag (every authored edit routes through
// MarkDirty) or by a frameCount / canvas change.
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // Per-tick render budget. A single heavy frame can blow well past this (Arc Burst ≈ 350 ms at 128 px) —
        // the budget only decides whether a SECOND frame starts in the same tick, so cheap specs fill in a few
        // ticks while heavy ones yield to the editor between every frame.
        const double FillBudgetMs = 12.0;

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
        double fillSlowestMs;      // the slowest frame of the fill in progress
        int cacheRenderCount;      // every renderer call this cache has ever made — the number a probe watches to prove playback renders nothing

        bool FillInProgress => frameCache != null && frameReadyCount < frameCache.Length;

        void DestroyFrameCache()
        {
            if (frameCache != null)
                for (int i = 0; i < frameCache.Length; i++)
                    if (frameCache[i] != null) DestroyImmediate(frameCache[i]);
            frameCache = null;
            frameReady = null;
            frameReadyCount = 0;
            frameCacheCanvas = -1;
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
                System.Array.Clear(frameReady, 0, frameReady.Length);
            }
            frameReadyCount = 0;
            fillCursor = Mathf.Clamp(frame, 0, n - 1);
            fillStartedAt = EditorApplication.timeSinceStartup;
            fillSlowestMs = 0;
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

        void RenderIntoCache(PyrePlusSpec s, int i)
        {
            double t0 = EditorApplication.timeSinceStartup;
            frameCache[i].SetPixels32(PyrePlusRenderer.RenderFrame(s, i));
            frameCache[i].Apply();
            frameReady[i] = true;
            frameReadyCount++;
            cacheRenderCount++;
            double ms = (EditorApplication.timeSinceStartup - t0) * 1000.0;
            if (ms > fillSlowestMs) fillSlowestMs = ms;
            if (frameReadyCount == frameCache.Length)
            {
                lastFillMs = (EditorApplication.timeSinceStartup - fillStartedAt) * 1000.0;
                lastFillSlowestMs = fillSlowestMs;
            }
            RefreshFillReadout();
        }

        // One editor tick of background fill: render missing frames forward from fillCursor (wrapping) until the
        // budget is spent — at least one frame per tick so a heavy spec always makes progress. Repaints the
        // preview and the readout as frames land. Runs whether or not playback is on (a paused preview still
        // wants its strip and its scrub targets ready).
        void FillFrameCacheTick()
        {
            if (spec == null || preview == null) return;
            EnsureFrameCache(spec);
            if (!FillInProgress) return;

            int n = frameCache.Length;
            // The frame on screen always goes first, wherever the cursor was left — playback resets and cherry
            // picks move `frame` between ticks, and that frame is the one the next repaint would otherwise
            // render synchronously.
            if (frame >= 0 && frame < n && !frameReady[frame]) fillCursor = frame;
            double t0 = EditorApplication.timeSinceStartup;
            bool rendered = false;
            do
            {
                int tries = 0;
                while (frameReady[fillCursor] && tries++ < n) fillCursor = (fillCursor + 1) % n;
                if (frameReady[fillCursor]) break;   // nothing left
                RenderIntoCache(spec, fillCursor);
                rendered = true;
                fillCursor = (fillCursor + 1) % n;
            }
            while (FillInProgress && (EditorApplication.timeSinceStartup - t0) * 1000.0 < FillBudgetMs);

            if (rendered) preview.MarkDirtyRepaint();
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
                fillReadout.tooltip = "Frames rendered into the preview cache so far. Playback only steps onto rendered "
                    + "frames, so the first loop waits at the render front; after that every loop plays from the cache "
                    + "with no rendering at all. Any edit restarts the fill from the frame on screen."
                    + (lastFillMs >= 0 ? $" Last complete fill: {lastFillMs:F0} ms (slowest frame {lastFillSlowestMs:F0} ms)." : "");
            }
        }
    }
}
