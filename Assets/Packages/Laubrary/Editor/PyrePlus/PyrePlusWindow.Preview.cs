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
                EditorGUI.DrawRect(view, new Color(0.1f, 0.1f, 0.12f));
                if (previewDirty || cur != lastRenderedFrame || previewTex == null)
                {
                    if (previewTex != null) DestroyImmediate(previewTex);
                    previewTex = PyrePlusRenderer.RenderFrameTexture(s, cur);
                    lastRenderedFrame = cur;
                    previewDirty = false;
                }
                GUI.DrawTexture(rct, previewTex, ScaleMode.StretchToFill, true);
                GUI.Label(new Rect(view.x + 6, view.yMax - 20, 200, 18),
                    $"frame {cur + 1}/{s.frameCount}", EditorStyles.whiteMiniLabel);
            }

            // The overlay draws + interacts on every event when the swarm is on; it never fights playback (it
            // only paints over the already-blitted frame texture).
            if (s.swarmEnabled) DrawSwarmOverlay(view, s, life);
        }

        // ── swarm authoring overlay ───────────────────────────────────────────────────
        // `life` is the CURRENT preview frame's normalized life. The outline/handle transform is snapshotted at
        // THIS life (not a fixed life 0), so as playback runs the drawn shape visibly rotates / grows / slides
        // along its authored transform curves — matching the user's feedback that Rotation should make the spawn
        // path rotate on screen. The spawn dots still come straight from ComputeSpawns (each particle's own
        // spawn-frame snapshot), so they keep marking real placements while the outline animates over them.
        void DrawSwarmOverlay(Rect view, PyrePlusSpec s, float life)
        {
            // The shape the CURRENT frame presents: the whole transform snapshotted at `life`. Mirrors the
            // renderer's per-particle transform (ComputeSpawns) so the outline/handle geometry reads the same
            // curves the dots do. For a Static transform field EvalField(·, life) == EvalField(·, 0) (a constant),
            // so a Static offset's handle is unaffected and drag stays correct; only animated fields move.
            float cx = s.Width * 0.5f, cy = s.Height * 0.5f;
            float half = Mathf.Max(1f, s.canvasSize * 0.5f);
            float r = Mathf.Max(0f, EvalField(s.shapeScale, life));
            if (s.shapeScaleSnap > 0f) r = Mathf.Round(r / s.shapeScaleSnap) * s.shapeScaleSnap;
            float rot = EvalField(s.shapeRotation, life);
            float yaw = EvalField(s.shapeYaw, life);
            float pitch = EvalField(s.shapePitch, life);
            Vector2 baseOff = new Vector2(EvalField(s.shapeOffsetX, life), EvalField(s.shapeOffsetY, life));
            // The position handle only drags when BOTH offset channels are plain Static numbers; an animated
            // (Curve/MinMax) offset has no single value a drag could set, so it draws hollow + inert.
            bool offsetDraggable = s.shapeOffsetX.mode == ZUIValue.Mode.Static
                                && s.shapeOffsetY.mode == ZUIValue.Mode.Static;

            // Interaction (hit priority): the shape-position handle first (it sits at the centre), then the
            // Custom path points / click-to-add. Each Use()s the event it consumes, which turns e.type to Used
            // and short-circuits the later handlers — the same chaining Pyre's viewport relies on.
            HandlePositionHandle(view, s, baseOff, offsetDraggable, cx, cy, half);
            Vector2 drawOff = draggingShapeHandle ? shapeHandleDragOff : baseOff;
            HandleCustomPoints(view, s, r, rot, yaw, pitch, drawOff, cx, cy, half);

            if (Event.current.type != EventType.Repaint) return;

            drawOff = draggingShapeHandle ? shapeHandleDragOff : baseOff;
            Vector2 delta = drawOff - baseOff;   // live-shifts the spawn dots while the handle is dragged

            Handles.BeginGUI();
            var prevC = Handles.color;
            DrawShapeOutline(s, r, rot, yaw, pitch, drawOff, cx, cy);
            DrawSpawnDots(view, s, delta);
            DrawCustomPoints(view, s, r, rot, yaw, pitch, drawOff, cx, cy);
            DrawPositionHandle(view, drawOff, offsetDraggable, cx, cy);
            Handles.color = prevC;
            Handles.EndGUI();
        }

        // 1) The current authored shape outline, transformed exactly like the renderer transforms placements.
        void DrawShapeOutline(PyrePlusSpec s, float r, float rot, float yaw, float pitch,
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
        void DrawSpawnDots(Rect view, PyrePlusSpec s, Vector2 delta)
        {
            PyrePlusRenderer.ComputeSpawns(s, swarmSpawns);
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
            }
        }

        // 4) The authored Custom-path control points (Path + Custom only), each a draggable marker.
        void DrawCustomPoints(Rect view, PyrePlusSpec s, float r, float rot, float yaw, float pitch,
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
        void HandlePositionHandle(Rect view, PyrePlusSpec s, Vector2 baseOff, bool draggable,
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

        void HandleCustomPoints(Rect view, PyrePlusSpec s, float r, float rot, float yaw, float pitch,
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
