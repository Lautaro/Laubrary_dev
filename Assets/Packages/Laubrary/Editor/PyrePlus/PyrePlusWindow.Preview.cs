// PyrePlusWindow.Preview — the right-hand IMGUI preview island (the ONE sanctioned IMGUI surface) plus the
// Swarm authoring overlay (SLICE 2 / T5). The overlay draws the CURRENT authored shape, a dot at every
// particle's ACTUAL computed spawn point (straight from PyrePlusRenderer.ComputeSpawns — the placement
// truth), and drag handles for the shape's position and the Custom path's points. Gizmo-interaction style
// mirrors Pyre's own Editor/Pyre/PyreWindow.Preview.cs: plain Rect hit tests, hot-drag with mouse capture,
// mutate on mouse-up wrapped in Undo (via the Dirty helper), repaint on change. Interaction state is
// non-serialized. See PYREPLUS_DESIGN.md → SLICE 2 "Preview authoring overlay".
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
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

        // The rect + zoom the current DrawPreview computed for the frame-texture blit. CanvasToScreen /
        // ScreenToCanvas are the SINGLE mapping used for every overlay draw and hit test, so a mapping fix is
        // one place; they read these two fields, refreshed every DrawPreview call (all event types).
        Rect swarmRect;
        float swarmZoom = 1f;

        // ── filmstrip (contact-sheet) cache (Part A) ──────────────────────────────────
        // One point-filtered Texture2D per frame, rendered ONCE and reused every repaint. Rebuilt only when dirty:
        // the shared previewDirty flag (any authored edit routes through it) OR a frameCount/canvas change (tracked
        // by the two shadow fields). Mirrors the single-frame previewTex lifecycle — created lazily, destroyed on
        // disable / asset change (see PyrePlusWindow.OnDisable / OnAssetChanged) and before every rebuild.
        Texture2D[] stripCache;
        int stripCacheCanvas = -1;   // the canvasSize the cache was built for (frameCount is tracked by stripCache.Length)

        void DestroyStripCache()
        {
            if (stripCache == null) return;
            for (int i = 0; i < stripCache.Length; i++)
                if (stripCache[i] != null) DestroyImmediate(stripCache[i]);
            stripCache = null;
            stripCacheCanvas = -1;
        }

        // Non-serialized interaction state (never persisted).
        readonly List<PyrePlusRenderer.SpawnPoint> swarmSpawns = new List<PyrePlusRenderer.SpawnPoint>();
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

        void DrawPreview(PyrePlusSpec s)
        {
            var view = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (s == null) return;

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
                if (previewDirty || cur != lastRenderedFrame || previewTex == null)
                {
                    if (previewTex != null) DestroyImmediate(previewTex);
                    previewTex = PyrePlusRenderer.RenderFrameTexture(s, cur);
                    lastRenderedFrame = cur;
                    previewDirty = false;
                }
                GUI.DrawTexture(rct, previewTex, ScaleMode.StretchToFill, true);
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

        // ── filmstrip / contact sheet (Part A) ────────────────────────────────────────
        // Draws EVERY frame as a point-filtered tile of previewStripSize px (aspect = the canvas), laid
        // left-to-right and WRAPPING to a new row when the next tile would overflow the view width. The block of
        // rows is centred vertically; a block taller than the view just clips (no scrolling this round — the tile
        // drawing runs inside a GUI.BeginClip(view)). The backdrop paints once behind the whole sheet; the Frame
        // toggle outlines every tile; the current transport frame gets a bright highlight; clicking a tile jumps
        // the transport to it. Tiles come from stripCache, rebuilt only when dirty (EnsureStripCache) — the loop
        // never re-renders frames. The layout (cols/rows/tile rects) is computed the SAME way for the click hit
        // test and the draw, so a click always lands on the tile under the cursor.
        void DrawFilmstrip(Rect view, PyrePlusSpec s)
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
                    if (tr.Contains(e.mousePosition)) { frame = i; preview?.MarkDirtyRepaint(); e.Use(); break; }
                }
            }

            if (Event.current.type != EventType.Repaint) return;

            DrawBackdrop(view);            // one backdrop behind the whole sheet, exactly as the single-frame path
            EnsureStripCache(s, n);        // (re)render the per-frame tiles only when dirty

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
                var tex = (stripCache != null && i < stripCache.Length) ? stripCache[i] : null;
                if (tex != null) GUI.DrawTexture(tr, tex, ScaleMode.StretchToFill, true);
                if (s.previewShowFrame) DrawRectOutline(tr, frameCol, 1f);
                if (i == curFrame) DrawRectOutline(tr, hotCol, 2f);
            }
            GUI.EndClip();

            GUI.Label(new Rect(view.x + 6, view.yMax - 20, 280, 18),
                $"strip — {n} frames · frame {curFrame + 1}", EditorStyles.whiteMiniLabel);
        }

        // Rebuild the per-frame tile textures ONLY when the render inputs changed: the shared previewDirty flag, or
        // a frameCount change (the array Length) / canvas-size change (the stripCacheCanvas shadow). Otherwise reused —
        // the whole point of the cache. Consumes previewDirty (like the single-frame path); the Strip toggle
        // re-dirties on a mode switch so the newly-active path always rebuilds. Destroys the old textures first.
        void EnsureStripCache(PyrePlusSpec s, int n)
        {
            bool structural = stripCache == null || stripCache.Length != n || stripCacheCanvas != s.canvasSize;
            if (!previewDirty && !structural) return;

            DestroyStripCache();
            stripCache = new Texture2D[n];
            for (int i = 0; i < n; i++) stripCache[i] = PyrePlusRenderer.RenderFrameTexture(s, i);
            stripCacheCanvas = s.canvasSize;
            previewDirty = false;
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
        void DrawBackdrop(Rect view)
        {
            var bs = backSplash;
            if (bs == null) { EditorGUI.DrawRect(view, new Color(0.1f, 0.1f, 0.12f)); return; }

            EditorGUI.DrawRect(view, bs.cameraColor);
            if (bs.image == null || bs.image.texture == null) return;

            // Draw the sprite's own sub-rect out of its (possibly atlas'd) source texture, not the whole page.
            var tex = bs.image.texture;
            var r = bs.image.textureRect;
            var tc = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);

            var prevCol = GUI.color;
            GUI.color = bs.imageTint;
            GUI.BeginClip(view);
            float w = view.width * bs.imageZoom, h = view.height * bs.imageZoom;
            var imgRect = new Rect((view.width - w) * 0.5f + bs.imagePos.x, (view.height - h) * 0.5f - bs.imagePos.y, w, h);
            GUI.DrawTextureWithTexCoords(imgRect, tex, tc, true);
            GUI.EndClip();
            GUI.color = prevCol;
        }

        // ── swarm authoring overlay ───────────────────────────────────────────────────
        // `life` is the CURRENT preview frame's normalized life. The outline/handle transform is snapshotted at
        // THIS life (not a fixed life 0), so as playback runs the drawn shape visibly rotates / grows / slides
        // along its authored transform curves — matching the user's feedback that Rotation should make the spawn
        // path rotate on screen. The spawn dots still come straight from ComputeSpawns (each particle's own
        // spawn-frame snapshot), so they keep marking real placements while the outline animates over them.
        void DrawSwarmOverlay(Rect view, PyrePlusSpec s, PyrePlusLayer sel, float life)
        {
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
            // and short-circuits the later handlers — the same chaining Pyre's viewport relies on.
            HandlePositionHandle(view, sel, baseOff, offsetDraggable, cx, cy, half);
            Vector2 drawOff = draggingShapeHandle ? shapeHandleDragOff : baseOff;
            HandleCustomPoints(view, sel, r, rot, yaw, pitch, drawOff, cx, cy, half);

            if (Event.current.type != EventType.Repaint) return;

            drawOff = draggingShapeHandle ? shapeHandleDragOff : baseOff;
            Vector2 delta = drawOff - baseOff;   // live-shifts the spawn dots while the handle is dragged

            Handles.BeginGUI();
            var prevC = Handles.color;
            DrawShapeOutline(sel, r, rot, yaw, pitch, drawOff, cx, cy);
            DrawSpawnDots(view, s, sel, delta);
            DrawCustomPoints(view, sel, r, rot, yaw, pitch, drawOff, cx, cy);
            DrawPositionHandle(view, drawOff, offsetDraggable, cx, cy);
            Handles.color = prevC;
            Handles.EndGUI();
        }

        // 1) The current authored shape outline, transformed exactly like the renderer transforms placements.
        void DrawShapeOutline(PyrePlusLayer s, float r, float rot, float yaw, float pitch,
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

        // 2) A dot at every particle's ACTUAL computed spawn point — the placement truth, not the outline.
        void DrawSpawnDots(Rect view, PyrePlusSpec s, PyrePlusLayer sel, Vector2 delta)
        {
            PyrePlusRenderer.ComputeSpawns(s, sel, swarmSpawns);
            // Each dot is annotated with the FRAME it spawns on (1-based, matching the transport's "frame N/M"
            // readout). Clutter guard: past 40 dots the labels overlap into an unreadable smear, so only the dots
            // draw. (frameCount clamped so a 1-frame spec still maps every dot to frame 1 without dividing by zero.)
            int frameCount = Mathf.Max(1, s.frameCount);
            bool showLabels = swarmSpawns.Count <= 40;
            Color prevContent = GUI.contentColor;
            for (int i = 0; i < swarmSpawns.Count; i++)
            {
                var sp = swarmSpawns[i];
                Vector2 screen = CanvasToScreen(sp.pos + delta);
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
        void DrawCustomPoints(Rect view, PyrePlusLayer s, float r, float rot, float yaw, float pitch,
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
        void HandlePositionHandle(Rect view, PyrePlusLayer s, Vector2 baseOff, bool draggable,
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

        void HandleCustomPoints(Rect view, PyrePlusLayer s, float r, float rot, float yaw, float pitch,
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

        // Local reimplementation of PyrePlusRenderer's per-particle transform (ComputeSpawns steps 2–6). Kept
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
        // shows in the dots. Mirrors PyrePlusRenderer.Eval's Static/Curve branches (Curve reads the normalized
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
