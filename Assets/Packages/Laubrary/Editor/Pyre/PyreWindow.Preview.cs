// PyreWindow.Preview — the right-hand IMGUI preview island (the ONE sanctioned IMGUI surface) plus the
// Swarm authoring overlay (SLICE 2 / T5). The overlay draws the CURRENT authored shape, a dot at every
// particle's ACTUAL computed spawn point (straight from PyreRenderer.ComputeSpawns — the placement
// truth), and drag handles for the shape's position and the Custom path's points. Gizmo-interaction style
// mirrors Pyre's own Editor/Pyre/PyreWindow.Preview.cs: plain Rect hit tests, hot-drag with mouse capture,
// mutate on mouse-up wrapped in Undo (via the Dirty helper), repaint on change. Interaction state is
// non-serialized. See PYREPLUS_DESIGN.md → SLICE 2 "Preview authoring overlay".
using System.Collections.Generic;
using UnityEditor;
using Laubrary.BackSplash.Editor;
using UnityEngine;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        // ── overlay tuning (every magic distance named) ───────────────────────────────
        const int OutlineCircleSegments = 48;   // polyline segments approximating a Circle outline
        const int OutlineCustomSegments = 64;    // samples along a Custom envelope-pair path
        const float SpawnDotRadius = 2.2f;        // base spawn-dot radius (px), scaled by depth
        const float CustomDotRadius = 4f;         // authored Custom-path control-point marker radius (px)
        const float HandleHalf = 5f;              // half-size of the shape-position square (px)
        const float HitRadius = 8f;               // click hit radius for the handle and Custom dots (px)

        static readonly Color OutlineColor = new Color(0.35f, 0.9f, 1f, 0.5f);       // ~50% cyan
        static readonly Color SpawnDotColor = new Color(1f, 0.85f, 0.45f, 0.95f);    // warm placement dots
        static readonly Color CustomDotColor = new Color(0.45f, 1f, 0.7f, 0.95f);    // editable path point
        static readonly Color CustomDotHot = new Color(1f, 0.7f, 0.2f, 1f);          // the point being dragged
        static readonly Color CustomDotDim = new Color(0.45f, 1f, 0.7f, 0.3f);       // non-draggable under tilt
        static readonly Color HandleColor = new Color(0.4f, 0.85f, 1f, 0.9f);        // draggable position square
        static readonly Color HandleHotColor = new Color(1f, 0.7f, 0.2f, 1f);        // while dragging
        static readonly Color HandleDisabled = new Color(0.6f, 0.6f, 0.6f, 0.5f);    // hollow (animated offset)
        const float TraceWidth = 2f;                                                 // spawner-trace polyline width (px)
        static readonly Color TraceColor = new Color(1f, 0.6f, 0.15f, 0.6f);         // warm amber spine, distinct from the cyan outline

        // The rect + zoom the current DrawPreview computed for the frame-texture blit. CanvasToScreen /
        // ScreenToCanvas are the SINGLE mapping used for every overlay draw and hit test, so a mapping fix is
        // one place; they read these two fields, refreshed every DrawPreview call (all event types).
        Rect swarmRect;
        float swarmZoom = 1f;

        // Non-serialized interaction state (never persisted).
        readonly List<PyreRenderer.SpawnPoint> swarmSpawns = new List<PyreRenderer.SpawnPoint>();
        // The spawner-trace spine (Part B) — canonical absolute canvas points, refilled every repaint exactly like
        // swarmSpawns above (per-repaint, no dirty flag: at ≈4×frameCount samples clamped 64..512 the recompute is
        // trivial). `traceScreen` is the reused screen-space run buffer for the polyline (broken at the view edge).
        readonly List<Vector2> swarmTrace = new List<Vector2>();
        readonly List<Vector3> traceScreen = new List<Vector3>();
        readonly List<Vector3> outlineScratch = new List<Vector3>();
        bool draggingShapeHandle;
        Vector2 shapeHandleDragOff;   // live (uncommitted) shape-centre offset while dragging the handle
        bool draggingCustomPoint;
        int hotCustomPoint = -1;
        Vector2 customDragLocal;      // live (uncommitted) local offset of the Custom point being dragged

        // Canvas pixels are y-UP with origin at buffer (0,0); IMGUI screen points are y-DOWN. GUI.DrawTexture
        // blits the frame texture so its top row (highest buffer y = highest canvas y) lands at swarmRect.yMin,
        // hence canvas y-up already displays up: worldY maps via swarmRect.yMax - worldY*zoom. THIS is the flip.
        Vector2 CanvasToScreen(Vector2 world)
            => new Vector2(swarmRect.x + world.x * swarmZoom, swarmRect.yMax - world.y * swarmZoom);
        Vector2 ScreenToCanvas(Vector2 gui)
            => new Vector2((gui.x - swarmRect.x) / swarmZoom, (swarmRect.yMax - gui.y) / swarmZoom);

        // The last IMGUI rect DrawPreview computed for the frame blit, refreshed on every Repaint. The backdrop
        // panel's auto-fit hook (fired when the user picks a new image, BEFORE the preview repaints at the new
        // size) reads this to compute a sensible fit-zoom instead of guessing.
        Rect lastPreviewView;

        void DrawPreview(Pyre s)
        {
            var view = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (s == null) return;
            if (Event.current.type == EventType.Repaint) lastPreviewView = view;

            // CherryFraming's blank-during-delay sentinel (frame == -1, see PyreWindow.CherryAdvanceOneBeat):
            // just the backdrop, no frame texture, no overlay — the preview holds visually empty for the gap.
            if (s.cherryEnabled && frame < 0)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    DrawBackdrop(view);
                    GUI.Label(new Rect(view.x + 6, view.yMax - 20, 200, 18), "…", EditorStyles.whiteMiniLabel);
                }
                return;
            }

            // Playback 3D (PROOF OF CONCEPT) — when the SELECTED layer is this form, the preview island shows the
            // live PreviewRenderUtility 3D render (or its pixelated downsample) INSTEAD of the normal composited
            // 2D canvas: this form has no bake path into that canvas at all yet (see PyreRenderer's stub), so
            // compositing it with the other layers would be a lie about what's actually happening. Filmstrip mode
            // isn't meaningful for a live 3D sim either (there's no discrete authored frame to tile), so it's
            // skipped for this form too — the 3D preview always shows the single live view.
            var selForPlayback = SelLayer;
            if (selForPlayback != null && selForPlayback.shapeForm == ShapeForm.Playback3D)
            {
                DrawPlayback3DPreview(view, selForPlayback);
                return;
            }

            // Filmstrip mode: the whole animation as a contact sheet instead of the single zoomed frame + overlay.
            if (s.previewStrip) { DrawFilmstrip(view, s); return; }

            float zoom = Mathf.Max(1f, s.previewZoom);
            float w = s.Width * zoom, h = s.Height * zoom;
            var rct = new Rect(view.center.x - w * 0.5f, view.center.y - h * 0.5f, w, h);
            swarmRect = rct; swarmZoom = zoom;   // feed the one mapping (used by drawing AND hit-testing below)

            // The frame currently on screen, and its normalized life — computed with the SAME formula the renderer
            // uses (frameIndex → life), so the overlay's outline/handle transform snapshots at the same life the
            // dots' particles were spawned/evaluated against. Hoisted out of the Repaint block because the overlay
            // interacts (and re-evaluates the transform) on every event, not only on repaint.
            int cur = Mathf.Clamp(frame, 0, Mathf.Max(0, s.frameCount - 1));
            float life = s.frameCount > 1 ? cur / (float)(s.frameCount - 1) : 0f;

            if (Event.current.type == EventType.Repaint)
            {
                DrawBackdrop(view);
                // The shared frame cache (PyreWindow.FrameCache.cs): a cache hit costs nothing, a miss renders
                // just this frame now — playback only ever lands on cached frames, so a miss here is a scrub or an edit.
                GUI.DrawTexture(rct, CachedFrame(s, cur), ScaleMode.StretchToFill, true);
                if (s.previewShowFrame)
                {
                    // A thin border around the canvas edge (cosmetic — never baked). Toggled by the transport's Frame.
                    var frameCol = new Color(1f, 1f, 1f, 0.55f);
                    EditorGUI.DrawRect(new Rect(rct.x, rct.y, rct.width, 1f), frameCol);
                    EditorGUI.DrawRect(new Rect(rct.x, rct.yMax - 1f, rct.width, 1f), frameCol);
                    EditorGUI.DrawRect(new Rect(rct.x, rct.y, 1f, rct.height), frameCol);
                    EditorGUI.DrawRect(new Rect(rct.xMax - 1f, rct.y, 1f, rct.height), frameCol);
                }
                GUI.Label(new Rect(view.x + 6, view.yMax - 20, 200, 18),
                    $"frame {cur + 1}/{s.frameCount}", EditorStyles.whiteMiniLabel);
            }

            // The overlay draws + interacts on every event when the SELECTED layer's swarm is on; it never fights
            // playback (it only paints over the already-blitted frame texture). It authors that one layer's swarm.
            var sel = SelLayer;
            if (sel != null && sel.swarmEnabled) DrawSwarmOverlay(view, s, sel, life);
        }

        // ── Playback 3D preview (PROOF OF CONCEPT) ──────────────────────────────────────
        // Renders the assigned prefab's live ParticleSystem(s) via PyrePlayback3DPreview into the SAME view
        // rect the normal canvas preview uses, backdrop first (so it reads consistently with every other shape),
        // then either the raw 3D frame or (Pixelated preview on) the point-filtered downsampled grid, scaled to
        // fill the view with point sampling so the pixelation is legible rather than blurred back out.
        void DrawPlayback3DPreview(Rect view, PyreLayer sel)
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawBackdrop(view);
            if (sel.playbackPrefab == null)
            {
                GUI.Label(new Rect(view.x + 6, view.yMax - 20, 320, 18),
                    "Playback 3D — assign a Prefab to preview.", EditorStyles.whiteMiniLabel);
                return;
            }

            playback3DPreview ??= new PyrePlayback3DPreview();
            var tex = playback3DPreview.Render(sel, view, sel.playbackPixelated, sel.playbackPixelGrid);
            if (tex == null) return;

            var filterModeScope = tex.filterMode;   // remember so we can restore it (shared RT reused elsewhere)
            if (sel.playbackPixelated)
            {
                tex.filterMode = FilterMode.Point;
                // Draw the pixel grid at a WHOLE-NUMBER scale, centred. ScaleToFit picks a fractional scale, and a
                // point-filtered texture drawn at (say) 7.6x gives some cells 7 screen pixels and some 8 — the grid
                // visibly wobbles and stops reading as a pixel-art canvas. Falls back to ScaleToFit if the grid is
                // somehow larger than the island (scale < 1).
                float scale = Mathf.Floor(Mathf.Min(view.width / tex.width, view.height / tex.height));
                if (scale >= 1f)
                {
                    float dw = tex.width * scale, dh = tex.height * scale;
                    GUI.DrawTexture(new Rect(Mathf.Round(view.center.x - dw * 0.5f),
                                             Mathf.Round(view.center.y - dh * 0.5f), dw, dh),
                                    tex, ScaleMode.StretchToFill, true);
                }
                else GUI.DrawTexture(view, tex, ScaleMode.ScaleToFit, true);
            }
            else GUI.DrawTexture(view, tex, ScaleMode.ScaleToFit, true);
            tex.filterMode = filterModeScope;

            GUI.Label(new Rect(view.x + 6, view.yMax - 20, 320, 18),
                sel.playbackPixelated
                    // The ACTUAL grid, not grid×grid: only the long edge is the requested size, the short edge
                    // follows the preview's aspect. Printing a square here would contradict what is on screen.
                    ? $"Playback 3D (POC) — pixelated {playback3DPreview.LastPixelWidth}×{playback3DPreview.LastPixelHeight}"
                    : "Playback 3D (POC) — live 3D",
                EditorStyles.whiteMiniLabel);

            // The live sim advances only when the transport is playing, exactly like every other form's frame
            // stepping — Tick() already calls preview?.MarkDirtyRepaint() on each advanced beat, so no extra
            // scheduling is needed here; a static Scrub position (paused) simply re-simulates to the same result.
        }

        // ── filmstrip / contact sheet (Part A) ────────────────────────────────────────
        // Draws EVERY frame as a point-filtered tile of previewStripSize px (aspect = the canvas), laid
        // left-to-right and WRAPPING to a new row when the next tile would overflow the view width. The block of
        // rows is centred vertically; a block taller than the view just clips (no scrolling this round — the tile
        // drawing runs inside a GUI.BeginClip(view)). The backdrop paints once behind the whole sheet; the Frame
        // toggle outlines every tile; the current transport frame gets a bright highlight; clicking a tile jumps
        // the transport to it. Tiles come from the shared frame cache — the loop never renders: a frame the
        // background fill hasn't reached yet is simply an empty tile until it lands (the fill repaints as it goes).
        // The layout (cols/rows/tile rects) is computed the SAME way for the click hit test and the draw, so a
        // click always lands on the tile under the cursor.
        void DrawFilmstrip(Rect view, Pyre s)
        {
            int n = Mathf.Max(1, s.frameCount);
            float tileW = Mathf.Clamp(s.previewStripSize, 32f, 256f);
            float tileH = tileW * s.Height / Mathf.Max(1, s.Width);   // aspect = canvas (square here → square tiles)
            const float gap = 4f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((view.width + gap) / (tileW + gap)));
            int rows = Mathf.CeilToInt(n / (float)cols);
            int usedCols = Mathf.Min(n, cols);
            float blockW = usedCols * tileW + (usedCols - 1) * gap;
            float blockH = rows * tileH + (rows - 1) * gap;
            float startX = view.x + (view.width - blockW) * 0.5f;
            float startY = view.y + (view.height - blockH) * 0.5f;   // < view.y when the block overflows → top rows clip

            // Click a tile → jump the transport there (playback pause state is left as-is). Absolute-rect hit test,
            // gated to the visible view so clicks on clipped-away tiles don't register. Repaint only (no re-render).
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                for (int i = 0; i < n; i++)
                {
                    var tr = new Rect(startX + (i % cols) * (tileW + gap), startY + (i / cols) * (tileH + gap), tileW, tileH);
                    if (tr.Contains(e.mousePosition)) { frame = i; preview?.MarkDirtyRepaint(); RefreshTransportReadout(); e.Use(); break; }
                }
            }

            if (Event.current.type != EventType.Repaint) return;

            DrawBackdrop(view);            // one backdrop behind the whole sheet, exactly as the single-frame path
            EnsureFrameCache(s);           // consume a pending invalidation so the tiles below read the right state

            var frameCol = new Color(1f, 1f, 1f, 0.55f);        // the Frame toggle's tile border
            var hotCol = new Color(1f, 0.85f, 0.2f, 1f);        // the current transport frame's highlight
            int curFrame = Mathf.Clamp(frame, 0, n - 1);

            // Clip to the view so an overflowing block (or the tile edges) can't spill past the preview island.
            // Inside the clip, coordinates are relative to view's top-left, so subtract view.x/view.y.
            GUI.BeginClip(view);
            for (int i = 0; i < n; i++)
            {
                var tr = new Rect(startX - view.x + (i % cols) * (tileW + gap),
                                  startY - view.y + (i / cols) * (tileH + gap), tileW, tileH);
                if (IsFrameReady(i)) GUI.DrawTexture(tr, frameCache[i], ScaleMode.StretchToFill, true);
                if (s.previewShowFrame) DrawRectOutline(tr, frameCol, 1f);
                if (i == curFrame) DrawRectOutline(tr, hotCol, 2f);
            }
            GUI.EndClip();

            GUI.Label(new Rect(view.x + 6, view.yMax - 20, 280, 18),
                $"strip — {n} frames · frame {curFrame + 1}", EditorStyles.whiteMiniLabel);
        }

        // A 1-or-2-px rectangle outline via four EditorGUI.DrawRect edges (same idiom as the single-frame Frame
        // border). `wd` is the edge thickness in px.
        static void DrawRectOutline(Rect r, Color col, float wd)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, wd), col);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - wd, r.width, wd), col);
            EditorGUI.DrawRect(new Rect(r.x, r.y, wd, r.height), col);
            EditorGUI.DrawRect(new Rect(r.xMax - wd, r.y, wd, r.height), col);
        }

        // The BackSplash backdrop behind the frame texture — a flat camera-colour fill plus one optional image,
        // drawn exactly like PyreWindow.DrawBackdrop (blit order: fill → image → the frame texture on top). backSplash
        // is the window-held instance; null-safe with the old dark fill as a fallback.
        // Shared painter — see BackSplashPainter; this used to be a byte-for-byte copy of Pyre's.
        void DrawBackdrop(Rect view)
            => BackSplashPainter.Draw(view, backSplash, new Color(0.1f, 0.1f, 0.12f));

        // ── swarm authoring overlay ───────────────────────────────────────────────────
        // `life` is the CURRENT preview frame's normalized life. The outline/handle transform is snapshotted at
        // THIS life (not a fixed life 0), so as playback runs the drawn shape visibly rotates / grows / slides
        // along its authored transform curves — matching the user's feedback that Rotation should make the spawn
        // path rotate on screen. The spawn dots still come straight from ComputeSpawns (each particle's own
        // spawn-frame snapshot), so they keep marking real placements while the outline animates over them.
        void DrawSwarmOverlay(Rect view, Pyre s, PyreLayer sel, float life)
        {
            // Two independent visualisations (Part B), each spec-gated:
            //   Show shape  → the authored spawn shape: its outline, the drag handle, the Custom path points, AND
            //                 the numbered spawn dots. This whole cluster is the only INTERACTIVE chrome.
            //   Show trace  → the objective spawner-trace spine (a read-only amber polyline), plus the same dots.
            // The spawn dots belong to BOTH (they mark the real placements), so they draw when EITHER is on. Neither
            // on ⇒ nothing draws (handle included) and no interaction runs.
            bool showShape = s.previewShowShape;
            bool showTrace = s.previewShowTrace;
            if (!showShape && !showTrace) return;

            // The shape the CURRENT frame presents: the whole transform snapshotted at `life`. Mirrors the
            // renderer's per-particle transform (ComputeSpawns) so the outline/handle geometry reads the same
            // curves the dots do. For a Static transform field EvalField(·, life) == EvalField(·, 0) (a constant),
            // so a Static offset's handle is unaffected and drag stays correct; only animated fields move. Canvas
            // dimensions come from the spec; every shape/swarm field comes from the SELECTED layer `sel`.
            float cx = s.Width * 0.5f, cy = s.Height * 0.5f;
            float half = Mathf.Max(1f, s.canvasSize * 0.5f);
            float r = Mathf.Max(0f, EvalField(sel.shapeScale, life));
            if (sel.shapeScaleSnap > 0f) r = Mathf.Round(r / sel.shapeScaleSnap) * sel.shapeScaleSnap;
            float rot = EvalField(sel.shapeRotation, life);
            float yaw = EvalField(sel.shapeYaw, life);
            float pitch = EvalField(sel.shapePitch, life);
            Vector2 baseOff = new Vector2(EvalField(sel.shapeOffsetX, life), EvalField(sel.shapeOffsetY, life));
            // The position handle only drags when BOTH offset channels are plain Static numbers; an animated
            // (Curve/MinMax) offset has no single value a drag could set, so it draws hollow + inert.
            bool offsetDraggable = sel.shapeOffsetX.mode == ZUIValue.Mode.Static
                                && sel.shapeOffsetY.mode == ZUIValue.Mode.Static;

            // Interaction (hit priority): the shape-position handle first (it sits at the centre), then the
            // Custom path points / click-to-add. Each Use()s the event it consumes, which turns e.type to Used
            // and short-circuits the later handlers — the same chaining Pyre's viewport relies on. ONLY the shape
            // chrome is interactive; when Show shape is off there is nothing to drag, so the handlers are skipped.
            if (showShape)
            {
                HandlePositionHandle(view, sel, baseOff, offsetDraggable, cx, cy, half);
                Vector2 drawOffI = draggingShapeHandle ? shapeHandleDragOff : baseOff;
                HandleCustomPoints(view, sel, r, rot, yaw, pitch, drawOffI, cx, cy, half);
            }

            if (Event.current.type != EventType.Repaint) return;

            Vector2 drawOff = draggingShapeHandle ? shapeHandleDragOff : baseOff;
            Vector2 delta = drawOff - baseOff;   // live-shifts the spawn dots while the handle is dragged

            Handles.BeginGUI();
            var prevC = Handles.color;
            // Trace first, so it reads as a spine UNDER the outline + dots.
            if (showTrace) DrawSpawnTrace(view, s, sel, life);
            if (showShape) DrawShapeOutline(sel, r, rot, yaw, pitch, drawOff, cx, cy);
            DrawSpawnDots(view, s, sel, delta, life);   // the dots belong to both visualisations (life → live swarm spin)
            if (showShape)
            {
                DrawCustomPoints(view, sel, r, rot, yaw, pitch, drawOff, cx, cy);
                DrawPositionHandle(view, drawOff, offsetDraggable, cx, cy);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        // The objective spawner-trace spine (Part B): the canonical, index-free path the spawn POINT sweeps over
        // the whole timeline, from PyreRenderer.ComputeSpawnTrace — a warm-amber semi-transparent polyline
        // beneath the cyan outline and the spawn dots. With a MinMax / per-index effect the real dots scatter
        // around this spine; with a plain Static/Curve transform they sit right on it. Recomputed every repaint
        // into swarmTrace (just like DrawSpawnDots recomputes swarmSpawns). Absolute canvas points → CanvasToScreen;
        // the polyline BREAKS wherever it leaves the view so a wrapped (progress > 1) or off-canvas span doesn't
        // draw a stray chord across the viewport (the same view-gating DrawSpawnDots applies to each dot).
        void DrawSpawnTrace(Rect view, Pyre s, PyreLayer sel, float life)
        {
            int samples = Mathf.Clamp(s.frameCount * 4, 64, 512);
            PyreRenderer.ComputeSpawnTrace(s, sel, samples, swarmTrace);
            if (swarmTrace.Count < 2) return;

            // ComputeSpawnTrace bakes a PER-t swarmScale into each spine point (centre + s_t·spun-offset), but the
            // RENDER and the spawn DOTS apply ONE live swarmScale at the current frame (ApplySwarmScale at `life`)
            // to the whole placed cloud — so an animated swarmScale makes the spine diverge from what actually
            // renders. Re-normalise every point to that single live scale, PREVIEW-SIDE only (the render path is
            // untouched): strip the per-t factor s_t (the SAME canonical eval ComputeSpawnTrace used, via the
            // preview's EvalField) to recover the pre-scale/post-spin point, then re-apply the live scale through
            // ApplySwarmScale — the exact helper the dots use — so the spine now matches the dots and the bake.
            // The intentional per-t SPIN sweep is preserved (it's baked into the offset and untouched). s_t≈0
            // collapsed the point onto the centre (its offset is unrecoverable), so that sample is dropped as a
            // polyline break — harmless: it's one of 64+ samples and only where the cloud is scale-0 (invisible).
            float cx = s.Width * 0.5f, cy = s.Height * 0.5f;
            Vector2 centre = new Vector2(cx, cy);
            int count = swarmTrace.Count;

            Handles.color = TraceColor;
            traceScreen.Clear();
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float sT = EvalField(sel.swarmScale, t);   // the per-t scale ComputeSpawnTrace applied (canonical)
                if (Mathf.Abs(sT) <= 1e-4f)
                {
                    if (traceScreen.Count >= 2) Handles.DrawAAPolyLine(TraceWidth, traceScreen.ToArray());
                    traceScreen.Clear();
                    continue;
                }
                Vector2 unscaled = centre + (swarmTrace[i] - centre) / sT;                 // undo per-t scale
                Vector2 live = PyreRenderer.ApplySwarmScale(s, sel, unscaled, life);    // re-apply single live scale
                Vector2 sc = CanvasToScreen(live);
                if (view.Contains(sc))
                    traceScreen.Add(new Vector3(sc.x, sc.y, 0f));
                else
                {
                    if (traceScreen.Count >= 2) Handles.DrawAAPolyLine(TraceWidth, traceScreen.ToArray());
                    traceScreen.Clear();
                }
            }
            if (traceScreen.Count >= 2) Handles.DrawAAPolyLine(TraceWidth, traceScreen.ToArray());
        }

        // 1) The current authored shape outline, transformed exactly like the renderer transforms placements.
        void DrawShapeOutline(PyreLayer s, float r, float rot, float yaw, float pitch,
                              Vector2 off, float cx, float cy)
        {
            Handles.color = OutlineColor;
            var kind = s.swarmShapeKind;
            var pts = outlineScratch; pts.Clear();

            if (s.swarmSpawnMode == SwarmSpawnMode.Path && kind == SwarmShapeKind.Custom)
            {
                // The two envelopes ARE the path — sample x(t)/y(t) as canvas-pixel local offsets (open, not
                // closed). Reads committed values, so mid-drag it snaps consistent on mouse-up.
                for (int i = 0; i <= OutlineCustomSegments; i++)
                {
                    float t = i / (float)OutlineCustomSegments;
                    Vector2 local = new Vector2(EvalField(s.swarmCustomX, t), EvalField(s.swarmCustomY, t));
                    pts.Add(ToScreen3(ApplyXform(local, r, rot, yaw, pitch, off.x, off.y, cx, cy)));
                }
            }
            else if (kind == SwarmShapeKind.Circle)
            {
                for (int i = 0; i <= OutlineCircleSegments; i++)
                {
                    float a = i / (float)OutlineCircleSegments * Mathf.PI * 2f;
                    Vector2 local = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
                    pts.Add(ToScreen3(ApplyXform(local, r, rot, yaw, pitch, off.x, off.y, cx, cy)));
                }
            }
            else
            {
                int n = SideCount(kind);
                if (n < 3) return;
                // Vertex 0 top (+90°), counter-clockwise, closed — mirrors the renderer's PolyVertex convention.
                for (int k = 0; k <= n; k++)
                {
                    float a = Mathf.PI / 2f + 2f * Mathf.PI * k / n;
                    Vector2 local = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
                    pts.Add(ToScreen3(ApplyXform(local, r, rot, yaw, pitch, off.x, off.y, cx, cy)));
                }
            }
            if (pts.Count >= 2) Handles.DrawAAPolyLine(1f, pts.ToArray());
        }

        // 2) A dot at every particle's ACTUAL computed spawn point — the placement truth, not the outline. `life`
        //    is the current preview frame's normalized life: the render applies the LIVE whole-cloud swarm spin
        //    (swarmTurn/Tilt/Roll at this life) to every placed particle, so each dot must carry that same spin to
        //    keep marking the real rendered position when the swarm spins.
        void DrawSpawnDots(Rect view, Pyre s, PyreLayer sel, Vector2 delta, float life)
        {
            PyreRenderer.ComputeSpawns(s, sel, swarmSpawns);
            // Each dot is annotated with the FRAME it spawns on (1-based, matching the transport's "frame N/M"
            // readout). Clutter guard: past 40 dots the labels overlap into an unreadable smear, so only the dots
            // draw. (frameCount clamped so a 1-frame spec still maps every dot to frame 1 without dividing by zero.)
            int frameCount = Mathf.Max(1, s.frameCount);
            bool showLabels = swarmSpawns.Count <= 40;
            Color prevContent = GUI.contentColor;
            for (int i = 0; i < swarmSpawns.Count; i++)
            {
                var sp = swarmSpawns[i];
                // FIX 3: spin the placement (plus the live handle-drag `delta`) by the whole-cloud swarm spin at the
                // frame's life, EXACTLY as RenderSwarm does, via the shared renderer helper — so the dot lands on the
                // particle's ACTUAL rendered position. All-zero spin ⇒ ApplySwarmSpin returns the point unchanged,
                // so a non-spinning swarm's dots are byte-identical to before. Depth coding below still keys off
                // sp.zNorm (the spawn-time tilt), which the render also discards the spin's z of — unchanged.
                // Slice 0: then apply the live whole-cloud SCALE, AFTER the spin (centre + scale·spin(offset)), via
                // the sibling ApplySwarmScale helper — the SAME order + math RenderSwarm uses — so the dots stay ON
                // the scaled particles. Scale 1 (the default) is an exact no-op, byte-identical to before.
                Vector2 spun = PyreRenderer.ApplySwarmSpin(s, sel, sp.pos + delta, life);
                Vector2 screen = CanvasToScreen(PyreRenderer.ApplySwarmScale(s, sel, spun, life));
                if (!view.Contains(screen)) continue;
                // Depth-code loosely like the renderer's 0.35 / 0.30 factors: nearer (zNorm>0) = bigger, brighter.
                float sizeMul = Mathf.Clamp(1f + 0.35f * sp.zNorm, 0.5f, 1.6f);
                float brightMul = Mathf.Clamp(1f + 0.30f * sp.zNorm, 0.55f, 1.45f);
                Handles.color = new Color(Mathf.Clamp01(SpawnDotColor.r * brightMul),
                                          Mathf.Clamp01(SpawnDotColor.g * brightMul),
                                          Mathf.Clamp01(SpawnDotColor.b * brightMul), SpawnDotColor.a);
                Handles.DrawSolidDisc(new Vector3(screen.x, screen.y, 0f), Vector3.forward, SpawnDotRadius * sizeMul);

                if (!showLabels) continue;
                // The frame this particle spawns on — the SAME life→frame rounding the transport uses, +1 for the
                // 1-based display. Drawn a few px up-right of the dot; a 1px dark shadow copy is drawn first so the
                // white mini-label stays readable over a bright backdrop image.
                int spawnFrame = Mathf.RoundToInt(sp.spawnLife * (frameCount - 1)) + 1;
                string txt = spawnFrame.ToString();
                var labelRect = new Rect(screen.x + 4f, screen.y - 7f, 30f, 14f);
                GUI.contentColor = new Color(0f, 0f, 0f, 0.9f);
                GUI.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), txt, EditorStyles.whiteMiniLabel);
                GUI.contentColor = Color.white;
                GUI.Label(labelRect, txt, EditorStyles.whiteMiniLabel);
            }
            GUI.contentColor = prevContent;
        }

        // 4) The authored Custom-path control points (Path + Custom only), each a draggable marker.
        void DrawCustomPoints(Rect view, PyreLayer s, float r, float rot, float yaw, float pitch,
                              Vector2 off, float cx, float cy)
        {
            if (s.swarmSpawnMode != SwarmSpawnMode.Path || s.swarmShapeKind != SwarmShapeKind.Custom) return;
            var xv = s.swarmCustomX; var yv = s.swarmCustomY;
            if (xv == null || yv == null) return;
            bool tilted = yaw != 0f || pitch != 0f;   // dim + non-draggable while tilted (exact inverse overkill)
            int count = Mathf.Min(xv.points.Count, yv.points.Count);
            for (int i = 0; i < count; i++)
            {
                bool hot = draggingCustomPoint && hotCustomPoint == i;
                Vector2 local = hot ? customDragLocal : new Vector2(xv.points[i].value, yv.points[i].value);
                Vector2 screen = CanvasToScreen(ApplyXform(local, r, rot, yaw, pitch, off.x, off.y, cx, cy));
                if (!view.Contains(screen)) continue;
                Handles.color = tilted ? CustomDotDim : (hot ? CustomDotHot : CustomDotColor);
                Handles.DrawSolidDisc(new Vector3(screen.x, screen.y, 0f), Vector3.forward, CustomDotRadius);
                GUI.Label(new Rect(screen.x + 5f, screen.y - 9f, 26f, 14f), (i + 1).ToString(), EditorStyles.miniLabel);
            }
        }

        // 3) The shape-position square: filled + interactive when draggable, hollow + grey when the offset is
        //    animated (no single value to drag).
        void DrawPositionHandle(Rect view, Vector2 off, bool draggable, float cx, float cy)
        {
            Vector2 screen = CanvasToScreen(new Vector2(cx + off.x, cy + off.y));
            if (!view.Contains(screen)) return;
            Rect sq = new Rect(screen.x - HandleHalf, screen.y - HandleHalf, HandleHalf * 2f, HandleHalf * 2f);
            if (draggable)
            {
                EditorGUI.DrawRect(sq, draggingShapeHandle ? HandleHotColor : HandleColor);
            }
            else
            {
                Handles.color = HandleDisabled;
                Handles.DrawAAPolyLine(1f,
                    new Vector3(sq.x, sq.y), new Vector3(sq.xMax, sq.y),
                    new Vector3(sq.xMax, sq.yMax), new Vector3(sq.x, sq.yMax), new Vector3(sq.x, sq.y));
            }
        }

        // ── handlers ──────────────────────────────────────────────────────────────────
        void HandlePositionHandle(Rect view, PyreLayer s, Vector2 baseOff, bool draggable,
                                  float cx, float cy, float half)
        {
            var e = Event.current;
            if (draggingShapeHandle)
            {
                // Live feedback only: track the dragged offset, DON'T touch the asset until mouse-up (so the one
                // Undo covers exactly the whole drag with the correct before/after).
                if (e.type == EventType.MouseDrag)
                {
                    Vector2 c = ScreenToCanvas(e.mousePosition);
                    shapeHandleDragOff = new Vector2(Mathf.Clamp(c.x - cx, -half, half), Mathf.Clamp(c.y - cy, -half, half));
                    preview?.MarkDirtyRepaint(); e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    Vector2 fin = shapeHandleDragOff;
                    Dirty(() => { s.shapeOffsetX.staticValue = fin.x; s.shapeOffsetY.staticValue = fin.y; });
                    draggingShapeHandle = false; e.Use();
                }
                return;
            }
            if (!draggable) return;
            Vector2 screen = CanvasToScreen(new Vector2(cx + baseOff.x, cy + baseOff.y));
            Rect zone = new Rect(screen.x - HitRadius, screen.y - HitRadius, HitRadius * 2f, HitRadius * 2f);
            if (view.Contains(screen)) EditorGUIUtility.AddCursorRect(zone, MouseCursor.MoveArrow);
            if (e.type == EventType.MouseDown && e.button == 0 && zone.Contains(e.mousePosition) && view.Contains(e.mousePosition))
            {
                draggingShapeHandle = true;
                shapeHandleDragOff = baseOff;
                e.Use();
            }
        }

        void HandleCustomPoints(Rect view, PyreLayer s, float r, float rot, float yaw, float pitch,
                                Vector2 drawOff, float cx, float cy, float half)
        {
            if (s.swarmSpawnMode != SwarmSpawnMode.Path || s.swarmShapeKind != SwarmShapeKind.Custom) return;
            var xv = s.swarmCustomX; var yv = s.swarmCustomY;
            if (xv == null || yv == null) return;
            var e = Event.current;
            bool tilted = yaw != 0f || pitch != 0f;
            int count = Mathf.Min(xv.points.Count, yv.points.Count);

            // Continue an in-flight point drag (mouse capture). Values are canvas-pixel offsets from the shape
            // centre, so we inverse-transform the cursor by the same life-0 transform. Exact inversion under a
            // pseudo-3D tilt is overkill, so tilt disables dragging entirely (handled below) — here we're only
            // ever un-rotating + un-offsetting (no perspective) so the inverse is exact.
            if (draggingCustomPoint)
            {
                if (e.type == EventType.MouseDrag && hotCustomPoint >= 0 && hotCustomPoint < count)
                {
                    customDragLocal = InverseXform(ScreenToCanvas(e.mousePosition), rot, drawOff, cx, cy, half);
                    preview?.MarkDirtyRepaint(); e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    int idx = hotCustomPoint; Vector2 fin = customDragLocal;
                    if (idx >= 0 && idx < count)
                        Dirty(() => { xv.points[idx].value = fin.x; yv.points[idx].value = fin.y; });
                    draggingCustomPoint = false; hotCustomPoint = -1; e.Use();
                }
                return;
            }

            if (e.type != EventType.MouseDown || (e.button != 0 && e.button != 1) || !view.Contains(e.mousePosition))
                return;

            // Hit-test existing dots topmost-first: left = grab-drag, right = remove the pair.
            for (int i = count - 1; i >= 0; i--)
            {
                Vector2 local = new Vector2(xv.points[i].value, yv.points[i].value);
                Vector2 screen = CanvasToScreen(ApplyXform(local, r, rot, yaw, pitch, drawOff.x, drawOff.y, cx, cy));
                if ((screen - e.mousePosition).sqrMagnitude > HitRadius * HitRadius) continue;

                if (e.button == 1)   // right-click removes; refuse below 2 points
                {
                    if (count > 2)
                        Dirty(() => { xv.points.RemoveAt(i); yv.points.RemoveAt(i); RenormalizeTimes(xv); RenormalizeTimes(yv); });
                    e.Use(); return;
                }
                if (tilted) { e.Use(); return; }   // grabbed a dot, but tilt disables editing it
                hotCustomPoint = i; customDragLocal = local; draggingCustomPoint = true; e.Use();
                return;
            }

            // Left-click on empty canvas appends a new pair AT THE END (point order = progress), then renormalizes
            // every point's time to even spacing i/(n-1). Disabled under tilt (the inverse-transform is only exact
            // without perspective).
            if (e.button == 0 && !tilted)
            {
                Vector2 local = InverseXform(ScreenToCanvas(e.mousePosition), rot, drawOff, cx, cy, half);
                Dirty(() =>
                {
                    xv.points.Add(new ZUIEnvelopePoint(1f, local.x));
                    yv.points.Add(new ZUIEnvelopePoint(1f, local.y));
                    RenormalizeTimes(xv); RenormalizeTimes(yv);
                });
                e.Use();
            }
        }

        // ── shared geometry / helpers ─────────────────────────────────────────────────

        Vector3 ToScreen3(Vector2 canvas)
        {
            Vector2 s = CanvasToScreen(canvas);
            return new Vector3(s.x, s.y, 0f);
        }

        // Local reimplementation of PyreRenderer's per-particle transform (ComputeSpawns steps 2–6). Kept
        // here — NOT by changing the renderer — so the overlay's outline + Custom markers land where the dots do.
        // `local` = shape-local canvas-pixel offset (y-up); `r` = snapshot radius (only normalizes tilt depth).
        static Vector2 ApplyXform(Vector2 local, float r, float rot, float yaw, float pitch,
                                  float offX, float offY, float cx, float cy)
        {
            float x = local.x, y = local.y, z = 0f;
            if (rot != 0f)   // 2D rotation, counter-clockwise (y-up)
            {
                float a = rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - y * s, ny = x * s + y * c; x = nx; y = ny;
            }
            if (yaw != 0f)   // yaw about vertical y axis (x,z plane)
            {
                float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - z * s, nz = x * s + z * c; x = nx; z = nz;
            }
            if (pitch != 0f) // pitch about horizontal x axis (y,z plane)
            {
                float a = pitch * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float ny = y * c - z * s, nz = y * s + z * c; y = ny; z = nz;
            }
            float zNorm = Mathf.Clamp(z / Mathf.Max(1e-3f, r), -1f, 1f);
            float persp = 1f + 0.25f * zNorm;
            return new Vector2(cx + offX + x * persp, cy + offY + y * persp);
        }

        // Inverse of ApplyXform WITHOUT the tilt (perspective) — un-offset then un-rotate. Valid only when
        // yaw/pitch are 0, which is exactly when the caller allows a drag/add. Result clamped to the canvas half.
        static Vector2 InverseXform(Vector2 canvasPos, float rot, Vector2 off, float cx, float cy, float half)
        {
            float x = canvasPos.x - cx - off.x;
            float y = canvasPos.y - cy - off.y;
            if (rot != 0f)
            {
                float a = -rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - y * s, ny = x * s + y * c; x = nx; y = ny;
            }
            return new Vector2(Mathf.Clamp(x, -half, half), Mathf.Clamp(y, -half, half));
        }

        // Representative evaluation of a transform ZUIValue for the OUTLINE/handle guide only (the dots use the
        // renderer directly). Static → the value; Curve → the envelope at `life`; MinMax → the midpoint — a
        // MinMax field has no single boundary, so the midpoint is the honest guide while the per-particle spread
        // shows in the dots. Mirrors PyreRenderer.Eval's Static/Curve branches (Curve reads the normalized
        // points at life, ignoring duration/warmup — the frame-baked timeline).
        static float EvalField(ZUIValue v, float life)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax: return (v.min + v.max) * 0.5f;
                case ZUIValue.Mode.Curve: return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
                default: return v.staticValue;
            }
        }

        // Side count for a regular-polygon kind (0 for Circle/Custom). Mirrors the renderer's private SideCount.
        static int SideCount(SwarmShapeKind kind)
        {
            switch (kind)
            {
                case SwarmShapeKind.Triangle: return 3;
                case SwarmShapeKind.Square:   return 4;
                case SwarmShapeKind.Pentagon: return 5;
                case SwarmShapeKind.Hexagon:  return 6;
                default:                      return 0;
            }
        }

        // Even-space every point's time to i/(n-1) so "point order is progress" holds after an add/remove.
        static void RenormalizeTimes(ZUIValue v)
        {
            int n = v.points.Count;
            for (int i = 0; i < n; i++)
                v.points[i].time = n > 1 ? i / (float)(n - 1) : 0f;
        }
    }
}
