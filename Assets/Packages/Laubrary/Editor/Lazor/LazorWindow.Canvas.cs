// LazorWindow.Canvas.cs — the drawing surface: a centered grid you can zoom (scroll wheel, toward the
// cursor) and pan (middle-drag or space-drag), with the selected layer's mirror symmetry shown as guide
// spokes and the whole shape previewed live through LazorGeometry (so the canvas shows exactly what the
// game will draw). Pen adds/closes strokes, Edit drags vertices, Erase removes them.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        Texture2D _white;
        int hoverVertex = -1, hoverVertexPath = -1;

        static readonly Color GridMinor = new Color(1, 1, 1, 0.06f);
        static readonly Color GridAxis = new Color(0.4f, 0.8f, 1f, 0.35f);
        static readonly Color GridFrame = new Color(1, 1, 1, 0.18f);
        static readonly Color CanvasBg = new Color(0.06f, 0.07f, 0.09f, 1f);
        static readonly Color GuideColor = new Color(1f, 0.5f, 0.2f, 0.5f);

        void EnsureWhiteTex()
        {
            if (_white != null) return;
            _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        // ---- Coordinate transforms (clip-local: coordinates relative to the canvas rect origin) ----

        Vector2 _canvasCenter;   // clip-local center, set each frame

        Vector2 GridToLocal(Vector2 g) => _canvasCenter + new Vector2(g.x * zoom, -g.y * zoom) + pan;

        Vector2 LocalToGrid(Vector2 local)
        {
            Vector2 d = local - _canvasCenter - pan;
            return new Vector2(d.x / zoom, -d.y / zoom);
        }

        Vector2 SnapGrid(Vector2 g)
        {
            bool s = snap ^ Event.current.control;   // Ctrl temporarily inverts the snap setting
            return s ? new Vector2(Mathf.Round(g.x), Mathf.Round(g.y)) : g;
        }

        float PxThickness(LazorLayer l) => Mathf.Max(1f, l.thickness * shape.gridResolution * zoom);

        // ---- Main entry ----

        void DrawCanvas(Rect canvasRect)
        {
            EnsureWhiteTex();
            _canvasCenter = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);

            if (!zoomInitialized) { FitView(canvasRect); zoomInitialized = true; }

            EditorGUI.DrawRect(canvasRect, CanvasBg);

            var e = Event.current;
            Vector2 mouseLocal = e.mousePosition - canvasRect.position;
            bool inside = canvasRect.Contains(e.mousePosition);

            if (e.type == EventType.Repaint)
            {
                GUI.BeginClip(canvasRect);
                if (showGrid) DrawGrid(canvasRect);
                DrawSymmetryGuides();
                DrawAllStrokes();
                DrawEditOverlay(mouseLocal, inside);
                GUI.EndClip();
            }
            else
            {
                HandleCanvasInput(canvasRect, mouseLocal, inside, e);
            }

            DrawCanvasHud(canvasRect);
        }

        void FitView(Rect canvasRect)
        {
            // Frame the ACTUAL resolved content (mirror/symmetry included), unioned with the design grid, so nothing
            // is ever clipped — however far off-centre the drawing is or the mirror spreads it. Honour BOTH canvas
            // dimensions (not just the smaller one): a small/narrow window used to cut off strokes that fell outside
            // the fixed grid frame.
            float half = shape != null ? shape.gridResolution * 0.5f : 8f;
            float minX = -half, minY = -half, maxX = half, maxY = half;
            if (shape != null && shape.layers != null)
            {
                var buf = new List<ResolvedPolyline>();
                foreach (var layer in shape.layers)
                {
                    if (layer == null || !layer.enabled) continue;
                    buf.Clear();
                    LazorGeometry.ResolveLayer(layer, 1f, buf);   // grid units, same as the canvas draws
                    foreach (var poly in buf)
                        if (poly.points != null)
                            foreach (var p in poly.points)
                            {
                                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                            }
                }
            }
            float w = (maxX - minX) + 2f;   // ~1-cell margin each side
            float h = (maxY - minY) + 2f;
            zoom = Mathf.Clamp(Mathf.Min(canvasRect.width / w, canvasRect.height / h), 2f, 200f);
            Vector2 c = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            pan = new Vector2(-c.x * zoom, c.y * zoom);   // centre the content in the canvas
        }

        // ---- Rendering ----

        void DrawGrid(Rect canvasRect)
        {
            float half = shape.gridResolution * 0.5f;
            int lo = Mathf.CeilToInt(-half), hi = Mathf.FloorToInt(half);

            for (int gx = lo; gx <= hi; gx++)
            {
                float x = GridToLocal(new Vector2(gx, 0)).x;
                Color col = gx == 0 ? GridAxis : GridMinor;
                GuiLine(new Vector2(x, 0), new Vector2(x, canvasRect.height), col, gx == 0 ? 1.5f : 1f);
            }
            for (int gy = lo; gy <= hi; gy++)
            {
                float y = GridToLocal(new Vector2(0, gy)).y;
                Color col = gy == 0 ? GridAxis : GridMinor;
                GuiLine(new Vector2(0, y), new Vector2(canvasRect.width, y), col, gy == 0 ? 1.5f : 1f);
            }

            // Design frame (the authored grid bounds).
            Vector2 tl = GridToLocal(new Vector2(-half, half));
            Vector2 br = GridToLocal(new Vector2(half, -half));
            GuiRectOutline(Rect.MinMaxRect(tl.x, tl.y, br.x, br.y), GridFrame, 1.5f);
        }

        void DrawSymmetryGuides()
        {
            if (shape.layers.Count == 0) return;
            var layer = shape.layers[layerSel];
            if (!layer.symmetryEnabled) return;

            Vector2 c = GridToLocal(Vector2.zero);
            int n = Mathf.Clamp(layer.symmetryCount, 2, 8);
            float step = 360f / n;
            float len = shape.gridResolution * zoom * 0.8f;
            for (int k = 0; k < n; k++)
            {
                float a = (layer.symmetryAngle + k * step) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)); // local y is down
                GuiLine(c - dir * len, c + dir * len, GuideColor, 1f);
            }
        }

        void DrawAllStrokes()
        {
            var buf = new List<ResolvedPolyline>();
            for (int li = 0; li < shape.layers.Count; li++)
            {
                var layer = shape.layers[li];
                if (!layer.enabled) continue;
                buf.Clear();
                LazorGeometry.ResolveLayer(layer, 1f, buf); // inv=1 -> points stay in grid units
                bool sel = li == layerSel;
                Color col = layer.color;
                if (!sel) col.a *= 0.4f;
                float th = PxThickness(layer);
                foreach (var poly in buf)
                    DrawPolylineGrid(poly.points, poly.closed, col, th);
            }

            // Active pen stroke preview (rubber-band to the cursor).
            if (tool == Tool.Pen && activePath >= 0 && shape.layers.Count > 0)
            {
                var l = shape.layers[layerSel];
                if (activePath < l.paths.Count)
                {
                    var p = l.paths[activePath];
                    if (p.points.Count > 0)
                    {
                        Vector2 lastLocal = GridToLocal(p.points[p.points.Count - 1]);
                        Vector2 curLocal = GridToLocal(SnapGrid(LocalToGrid(_lastMouseLocal)));
                        GuiLine(lastLocal, curLocal, new Color(1, 1, 1, 0.4f), 1f);
                    }
                }
            }
        }

        void DrawEditOverlay(Vector2 mouseLocal, bool inside)
        {
            if (shape.layers.Count == 0) return;
            DrawVertexHandles();
        }

        void DrawVertexHandles()
        {
            if (shape.layers.Count == 0) return;
            var layer = shape.layers[layerSel];
            for (int pi = 0; pi < layer.paths.Count; pi++)
            {
                var path = layer.paths[pi];
                for (int i = 0; i < path.points.Count; i++)
                {
                    Vector2 lp = GridToLocal(path.points[i]);
                    bool hovered = pi == hoverVertexPath && i == hoverVertex;
                    bool first = pi == activePath && i == 0;
                    float s = hovered ? 9f : 6f;
                    Color c = first ? new Color(0.3f, 1f, 0.4f, 1f)
                            : hovered ? Color.white
                            : new Color(0.7f, 0.9f, 1f, 0.9f);
                    GuiFillRect(new Rect(lp.x - s * 0.5f, lp.y - s * 0.5f, s, s), c);
                }
            }
        }

        void DrawCanvasHud(Rect canvasRect)
        {
            var hud = new Rect(canvasRect.xMax - 168, canvasRect.y + 6, 162, 22);
            using (new GUILayout.AreaScope(hud))
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label($"{Mathf.RoundToInt(zoom)} px/cell", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Fit", "Frame the whole grid in the canvas."), EditorStyles.miniButton, GUILayout.Width(34)))
                    FitView(canvasRect);
            }
        }

        // ---- Input ----

        Vector2 _lastMouseLocal;

        void HandleCanvasInput(Rect canvasRect, Vector2 mouseLocal, bool inside, Event e)
        {
            _lastMouseLocal = mouseLocal;

            // Zoom toward cursor.
            if (e.type == EventType.ScrollWheel && inside)
            {
                Vector2 gridUnder = LocalToGrid(mouseLocal);
                zoom = Mathf.Clamp(zoom * (e.delta.y < 0 ? 1.1f : 1f / 1.1f), 2f, 200f);
                pan = mouseLocal - _canvasCenter - new Vector2(gridUnder.x * zoom, -gridUnder.y * zoom);
                e.Use(); Repaint(); return;
            }

            // Pan: middle mouse, or left mouse while holding space/alt.
            bool panButton = e.button == 2 || (e.button == 0 && (e.alt || _spaceHeld));
            if (e.type == EventType.MouseDown && inside && panButton) { draggingPan = true; e.Use(); return; }
            if (draggingPan)
            {
                if (e.type == EventType.MouseDrag) { pan += e.delta; Repaint(); e.Use(); }
                if (e.type == EventType.MouseUp) { draggingPan = false; e.Use(); }
                return;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space) _spaceHeld = true;
            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space) _spaceHeld = false;

            UpdateHover(mouseLocal, inside);

            if (shape.layers.Count == 0) return;

            switch (tool)
            {
                case Tool.Pen: HandlePen(mouseLocal, inside, e); break;
                case Tool.Edit: HandleEdit(mouseLocal, inside, e); break;
                case Tool.Erase: HandleErase(mouseLocal, inside, e); break;
            }
        }

        bool _spaceHeld;

        void UpdateHover(Vector2 mouseLocal, bool inside)
        {
            hoverVertex = hoverVertexPath = -1;
            if (!inside || shape.layers.Count == 0) return;
            var layer = shape.layers[layerSel];
            float best = 10f;
            for (int pi = 0; pi < layer.paths.Count; pi++)
            {
                var path = layer.paths[pi];
                for (int i = 0; i < path.points.Count; i++)
                {
                    float d = Vector2.Distance(mouseLocal, GridToLocal(path.points[i]));
                    if (d < best) { best = d; hoverVertex = i; hoverVertexPath = pi; }
                }
            }
            Repaint();
        }

        void HandlePen(Vector2 mouseLocal, bool inside, Event e)
        {
            var layer = shape.layers[layerSel];

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                Vector2 g = SnapGrid(LocalToGrid(mouseLocal));

                // Click near the active stroke's first point closes it.
                if (activePath >= 0 && activePath < layer.paths.Count)
                {
                    var ap = layer.paths[activePath];
                    if (ap.points.Count >= 3 && Vector2.Distance(mouseLocal, GridToLocal(ap.points[0])) < 10f)
                    {
                        RecordShape("Close Lazor stroke");
                        ap.closed = true; activePath = -1; MarkDirty(); e.Use(); Repaint(); return;
                    }
                }

                RecordShape("Draw Lazor point");
                if (activePath < 0 || activePath >= layer.paths.Count)
                {
                    var np = new LazorPath();
                    layer.paths.Add(np);
                    activePath = layer.paths.Count - 1;
                }
                layer.paths[activePath].points.Add(g);
                MarkDirty(); e.Use(); Repaint(); return;
            }

            if (e.type == EventType.MouseDown && e.button == 1) { FinishStroke(); e.Use(); return; } // right-click finishes
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape) { FinishStroke(); e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.Backspace && activePath >= 0 && activePath < layer.paths.Count)
                {
                    RecordShape("Undo Lazor point");
                    var ap = layer.paths[activePath];
                    if (ap.points.Count > 0) ap.points.RemoveAt(ap.points.Count - 1);
                    if (ap.points.Count == 0) { layer.paths.RemoveAt(activePath); activePath = -1; }
                    MarkDirty(); e.Use(); Repaint();
                }
            }
            if (e.type == EventType.MouseMove) Repaint();
        }

        void FinishStroke()
        {
            if (activePath >= 0 && shape.layers.Count > 0 && layerSel < shape.layers.Count)
            {
                var layer = shape.layers[layerSel];
                if (activePath < layer.paths.Count && layer.paths[activePath].points.Count < 2)
                {
                    RecordShape("Discard Lazor stroke");
                    layer.paths.RemoveAt(activePath);
                    MarkDirty();
                }
            }
            activePath = -1;
        }

        void HandleEdit(Vector2 mouseLocal, bool inside, Event e)
        {
            var layer = shape.layers[layerSel];

            if (e.type == EventType.MouseDown && e.button == 0 && hoverVertex >= 0)
            {
                RecordShape("Move Lazor vertex");
                dragVertex = hoverVertex; dragVertexPath = hoverVertexPath; e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragVertex >= 0)
            {
                layer.paths[dragVertexPath].points[dragVertex] = SnapGrid(LocalToGrid(mouseLocal));
                MarkDirty(); Repaint(); e.Use();
            }
            else if (e.type == EventType.MouseUp && dragVertex >= 0)
            {
                dragVertex = dragVertexPath = -1; e.Use();
            }
        }

        void HandleErase(Vector2 mouseLocal, bool inside, Event e)
        {
            if (e.type != EventType.MouseDown || e.button != 0 || hoverVertex < 0) return;
            var layer = shape.layers[layerSel];
            RecordShape("Erase Lazor vertex");
            if (e.alt) layer.paths.RemoveAt(hoverVertexPath); // Alt+click removes the whole stroke
            else
            {
                var path = layer.paths[hoverVertexPath];
                path.points.RemoveAt(hoverVertex);
                if (path.points.Count < 2) layer.paths.RemoveAt(hoverVertexPath);
            }
            hoverVertex = hoverVertexPath = -1;
            MarkDirty(); e.Use(); Repaint();
        }

        // ---- GUI primitives (clip-local; rotated-quad lines, no camera needed) ----

        void GuiFillRect(Rect r, Color c)
        {
            Color save = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white); GUI.color = save;
        }

        void GuiLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 save = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            Color cs = GUI.color; GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), _white);
            GUI.color = cs; GUI.matrix = save;
        }

        void GuiRectOutline(Rect r, Color c, float t)
        {
            GuiFillRect(new Rect(r.x, r.y, r.width, t), c);
            GuiFillRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            GuiFillRect(new Rect(r.x, r.y, t, r.height), c);
            GuiFillRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void DrawPolylineGrid(Vector2[] gridPts, bool closed, Color color, float thickness)
        {
            if (gridPts == null || gridPts.Length < 2) return;
            // A soft dark halo underneath so bright strokes read against the grid, then the bright core.
            Color halo = new Color(0, 0, 0, color.a * 0.5f);
            int last = closed ? gridPts.Length : gridPts.Length - 1;
            for (int pass = 0; pass < 2; pass++)
            {
                Color c = pass == 0 ? halo : color;
                float t = pass == 0 ? thickness + 2f : thickness;
                for (int i = 0; i < last; i++)
                {
                    Vector2 a = GridToLocal(gridPts[i]);
                    Vector2 b = GridToLocal(gridPts[(i + 1) % gridPts.Length]);
                    GuiLine(a, b, c, t);
                }
            }
        }
    }
}
