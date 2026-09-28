using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Point = ZUIEnvelopePoint;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of the Zounds envelope editor (EnvelopeGUI), drawn with UI Toolkit's vector painter and driven by
    /// pointer events, with the old editor's rules one for one:
    /// - a point under the pointer wins: press to drag it (and select it), double-click to delete it;
    /// - on the line (within 4 px): press adds a point there and drags it, unless an existing point is within two handle
    ///   radii (then that point is dragged); with Shift, press-drag moves the whole segment and right-drag bends it
    ///   (the segment's exponent), the segment drawn in the "selected line" colour;
    /// - elsewhere: double-click adds a point, press-drag draws a selection box; dragging with more than one point selected
    ///   moves them all (the first and a required last point move up and down only);
    /// - Delete removes the selected points.
    /// Point moves go through the old editor's own <c>EnvelopeGUI.MovePoints</c>, so limits and ordering are identical.
    /// </summary>
    public class EnvelopeTK : VisualElement {

        public Envelope envelope;
        public Color mainColor;
        public float thickness = 1.5f;
        /// <summary>Before the first change of a gesture (open an Undo step here).</summary>
        public Action onBegin;
        /// <summary>After every change.</summary>
        public Action onChanged;
        /// <summary>Right-click on a point: its settings (a random point's ellipse, T-0483), given the point and its centre in
        /// world space. Null: ignored.</summary>
        public Action<int, Vector2> onPointContext;
        /// <summary>Curves drawn dotted over this one: what the plays under way actually hear (T-0484). Null: nothing extra.</summary>
        public List<Func<float, float>> liveCurves;

        int draggedPoint = -1, draggedLine = -1, draggedExponent = -1;
        bool boxSelecting, pressed, multiMoveStarted;
        Vector2 boxStart, pointer = new Vector2(-1f, -1f);
        bool shiftHeld, hovering;
        readonly List<int> selected = new List<int>();

        static ZoundsProject.ProjectSettings.EditorStyle Style => ZoundsProject.Instance.projectSettings.editorStyle;

        public EnvelopeTK(Envelope envelope, Color mainColor) {
            this.envelope = envelope; this.mainColor = mainColor;
            focusable = true;
            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => { hovering = false; MarkDirtyRepaint(); });
            RegisterCallback<PointerEnterEvent>(_ => { hovering = true; });
            RegisterCallback<KeyDownEvent>(OnKey);
        }

        float XRange => envelope.xMax - envelope.xMin;
        float YRange => envelope.yMax - envelope.yMin;
        Vector2 Size => contentRect.size;

        Vector2 ToLocal(Point p) => new Vector2((p.time - envelope.xMin) / XRange * Size.x, Size.y - (p.value - envelope.yMin) / YRange * Size.y);
        float TimeAt(float x) => envelope.xMin + XRange * x / Size.x;
        float LineY(float x) => Size.y - (envelope.Evaluate(TimeAt(x)) - envelope.yMin) / YRange * Size.y;

        int PointAt(Vector2 local) {
            float r = Style.envelopeHandleSize;
            for (int i = 0; i < envelope.Count; i++) {
                var c = ToLocal(envelope.GetPoint(i));
                if (new Rect(c.x - r, c.y - r, r * 2f, r * 2f).Contains(local)) return i;
            }
            return -1;
        }

        bool Inside(Vector2 l) => l.x > 0f && l.y > 0f && l.x <= Size.x && l.y <= Size.y;
        bool OnLine(Vector2 l) => Inside(l) && Mathf.Abs(l.y - LineY(l.x)) <= 4f;

        void Changed() { onChanged?.Invoke(); MarkDirtyRepaint(); }

        // ─────────────────────────── input ───────────────────────────

        void OnDown(PointerDownEvent e) {
            if (envelope == null) return;
            var l = (Vector2)e.localPosition;
            shiftHeld = e.shiftKey;
            Focus();
            int hit = PointAt(l);
            if (hit >= 0 && e.button == 1 && !e.shiftKey && onPointContext != null) {
                onPointContext(hit, this.LocalToWorld(ToLocal(envelope.GetPoint(hit))));
                e.StopPropagation();
                return;
            }
            if (hit >= 0 && e.button == 0) {
                if (e.clickCount == 2) {
                    onBegin?.Invoke();
                    selected.Clear();
                    envelope.RemovePoint(hit);
                    Changed();
                }
                else {
                    onBegin?.Invoke();
                    draggedPoint = hit;
                    if (!selected.Contains(hit)) { selected.Clear(); selected.Add(hit); }
                    Capture(e);
                }
                e.StopPropagation();
                return;
            }
            if (OnLine(l)) {
                float time = TimeAt(l.x);
                if (e.shiftKey) {
                    int ci = envelope.GetClosestIndexCeil(time);
                    if (ci > 0 && ci < envelope.Count) {
                        onBegin?.Invoke();
                        if (e.button == 0) draggedLine = ci; else if (e.button == 1) draggedExponent = ci;
                        selected.Clear();
                        Capture(e);
                    }
                }
                else if (e.button == 0) {
                    float r = Style.envelopeHandleSize;
                    int near = -1;
                    for (int i = 0; i < envelope.Count && near < 0; i++)
                        if (Vector2.Distance(l, ToLocal(envelope.GetPoint(i))) <= r * 2f) near = i;
                    if (near >= 0) {
                        if (e.clickCount == 1) {
                            onBegin?.Invoke();
                            draggedPoint = near;
                            if (!selected.Contains(near)) { selected.Clear(); selected.Add(near); }
                            Capture(e);
                        }
                    }
                    else {
                        onBegin?.Invoke();
                        var p = envelope.AddPoint(time, envelope.Evaluate(time));
                        draggedPoint = envelope.IndexOf(p);
                        selected.Clear(); selected.Add(draggedPoint);
                        Changed();
                        Capture(e);
                    }
                }
                e.StopPropagation();
                return;
            }
            if (e.button == 0 && Inside(l)) {
                if (e.clickCount == 2) {
                    onBegin?.Invoke();
                    envelope.AddPoint(TimeAt(l.x), YRange - YRange * l.y / Size.y);
                    Changed();
                }
                else if (selected.Count > 1) {
                    // Pressing away from the points with several selected starts the old editor's multi-move on drag;
                    // a click without dragging clears the selection, as the old box select does.
                    pressed = true; multiMoveStarted = false; boxStart = l;
                    Capture(e);
                }
                else {
                    boxSelecting = true; boxStart = l; selected.Clear();
                    Capture(e);
                }
                e.StopPropagation();
            }
        }

        void Capture(PointerDownEvent e) { pressed = true; this.CapturePointer(e.pointerId); MarkDirtyRepaint(); }

        void OnMove(PointerMoveEvent e) {
            if (envelope == null) return;
            var l = (Vector2)e.localPosition;
            pointer = l; shiftHeld = e.shiftKey; hovering = true;
            Vector2 d = e.deltaPosition;
            if (pressed && this.HasPointerCapture(e.pointerId)) {
                if (boxSelecting) {
                    UpdateBox(l);
                }
                else if (draggedLine >= 0) {
                    if (!e.shiftKey) { draggedLine = -1; }
                    else { EnvelopeGUI.MovePoints(envelope, XRange, YRange, Size, d, new[] { draggedLine - 1, draggedLine }, new int[0]); Changed(); }
                }
                else if (draggedExponent >= 0) {
                    if (!e.shiftKey) { draggedExponent = -1; }
                    else {
                        var p = envelope.GetPoint(draggedExponent);
                        float mult = p.exponent == 0f ? 0.000001f : Mathf.Sqrt(p.exponent);
                        float dy = d.y;
                        if (p.value < envelope.GetPoint(draggedExponent - 1).value) dy *= -1f;
                        p.exponent = Mathf.Max(0f, p.exponent + dy / Size.y * mult * 8f);
                        Changed();
                    }
                }
                else if (selected.Count > 1 && (draggedPoint >= 0 || draggedPoint < 0)) {
                    if (draggedPoint < 0 && !multiMoveStarted && d == Vector2.zero) { }
                    else {
                        if (!multiMoveStarted) { multiMoveStarted = true; onBegin?.Invoke(); }
                        var moved = new List<int>(selected);
                        var yOnly = new List<int>();
                        if (moved.Remove(0)) yOnly.Add(0);
                        if (Envelope.requiresEndPoint && moved.Remove(envelope.Count - 1)) yOnly.Add(envelope.Count - 1);
                        if (moved.Count > 0 || yOnly.Count > 0) {
                            if (moved.Count > 0) EnvelopeGUI.MovePoints(envelope, XRange, YRange, Size, d, moved.ToArray(), yOnly.ToArray());
                            else MoveYOnly(yOnly, d);
                            Changed();
                        }
                    }
                }
                else if (draggedPoint >= 0) {
                    DragPointTo(l);
                    Changed();
                }
            }
            MarkDirtyRepaint();
        }

        void MoveYOnly(List<int> idx, Vector2 d) {
            float dv = YRange * -d.y / Size.y;
            foreach (var i in idx) { var p = envelope.GetPoint(i); p.value = Mathf.Clamp(p.value + dv, envelope.yMin, envelope.yMax); }
        }

        void DragPointTo(Vector2 l) {
            int i = draggedPoint;
            float time = XRange * l.x / Size.x + envelope.xMin;
            float val = YRange - YRange * l.y / Size.y + envelope.yMin;
            if (i > 0) { if (time < envelope.GetPoint(i - 1).time) time = envelope.GetPoint(i - 1).time; }
            else time = envelope.xMin;
            if (i < envelope.Count - 1) { if (time > envelope.GetPoint(i + 1).time) time = envelope.GetPoint(i + 1).time; }
            else time = Envelope.requiresEndPoint ? envelope.xMax : Mathf.Min(time, envelope.xMax);
            val = Mathf.Clamp(val, envelope.yMin, envelope.yMax);
            var p = envelope.GetPoint(i);
            p.time = time; p.value = val;
        }

        void UpdateBox(Vector2 l) {
            float cx = Mathf.Clamp(l.x, 0f, Size.x), cy = Mathf.Clamp(l.y, 0f, Size.y);
            float t0 = TimeAt(boxStart.x), v0 = YRange - YRange * boxStart.y / Size.y;
            float t1 = TimeAt(cx), v1 = YRange - YRange * cy / Size.y;
            float minT = Mathf.Min(t0, t1), maxT = Mathf.Max(t0, t1), minV = Mathf.Min(v0, v1), maxV = Mathf.Max(v0, v1);
            selected.Clear();
            envelope.ForEach((i, p) => { if (p.time >= minT && p.time <= maxT && p.value >= minV && p.value <= maxV) selected.Add(i); });
        }

        void OnUp(PointerUpEvent e) {
            if (pressed && selected.Count > 1 && !multiMoveStarted && draggedPoint < 0 && !boxSelecting && draggedLine < 0 && draggedExponent < 0)
                selected.Clear();
            pressed = false; boxSelecting = false; multiMoveStarted = false;
            draggedPoint = draggedLine = draggedExponent = -1;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            MarkDirtyRepaint();
        }

        void OnKey(KeyDownEvent e) {
            if (e.keyCode != KeyCode.Delete || selected.Count == 0 || envelope == null) return;
            onBegin?.Invoke();
            var pts = new List<Point>();
            foreach (var i in selected) if (i < envelope.Count) pts.Add(envelope.GetPoint(i));
            foreach (var p in pts) envelope.RemovePoint(p);
            selected.Clear();
            Changed();
            e.StopPropagation();
        }

        // ─────────────────────────── drawing ───────────────────────────

        void Paint(MeshGenerationContext ctx) {
            if (envelope == null || Size.x <= 0f || Size.y <= 0f) return;
            var p2 = ctx.painter2D;
            p2.lineJoin = LineJoin.Round; p2.lineCap = LineCap.Round;

            // The main line, sampled every 4 px as the old editor samples it.
            Stroke(p2, envelope.xMin, XRange, mainColor, thickness);

            // A segment picked for Shift-moving or bending, in the "selected line" colour.
            int seg = draggedLine >= 0 ? draggedLine : draggedExponent >= 0 ? draggedExponent : -1;
            if (seg < 0 && hovering && shiftHeld && OnLine(pointer)) {
                int ci = envelope.GetClosestIndexCeil(TimeAt(pointer.x));
                if (ci > 0 && ci < envelope.Count) seg = ci;
            }
            if (seg > 0 && seg < envelope.Count) {
                float a = envelope.GetPoint(seg - 1).time, b = envelope.GetPoint(seg).time;
                Stroke(p2, a, b - a, Style.selectedEnvelopeLineColor, 1.5f);
            }

            // What the plays under way actually hear (T-0484), dotted over the authored curve.
            if (liveCurves != null && liveCurves.Count > 0) {
                int n = Mathf.Max(2, (int)(Size.x / 3f));
                var dotted = new Vector2[n + 1];
                foreach (var f in liveCurves) {
                    if (f == null) continue;
                    for (int i = 0; i <= n; i++) {
                        float t = envelope.xMin + XRange * i / n;
                        dotted[i] = new Vector2((t - envelope.xMin) / XRange * Size.x, Size.y - (f(t) - envelope.yMin) / YRange * Size.y);
                    }
                    Laubrary.Zui.ZuiSkinEnvelope.Dotted(p2, dotted, Color.Lerp(mainColor, Color.white, 0.45f), thickness / Mathf.Max(1f, UnityEditor.EditorGUIUtility.pixelsPerPoint) * 1.2f);
                }
            }

            // Random points' ellipses (T-0483): where each play may move the point.
            float px = 1f / Mathf.Max(1f, UnityEditor.EditorGUIUtility.pixelsPerPoint);
            for (int i = 0; i < envelope.Count; i++) {
                var pt = envelope.GetPoint(i);
                if (pt.randomX <= 0f && pt.randomY <= 0f) continue;
                Laubrary.Zui.ZuiSkinEnvelope.Ellipse(p2, ToLocal(pt), Mathf.Max(pt.randomX / XRange * Size.x, 1f), Mathf.Max(pt.randomY / YRange * Size.y, 1f),
                    new Color(mainColor.r, mainColor.g, mainColor.b, 0.14f), new Color(mainColor.r, mainColor.g, mainColor.b, 0.75f), px);
            }

            // Points, brighter when under the pointer or dragged; then the selected ones in the selection colour.
            float r = Style.envelopeHandleSize;
            for (int i = 0; i < envelope.Count; i++) {
                var c = ToLocal(envelope.GetPoint(i));
                bool hi = i == draggedPoint || (hovering && new Rect(c.x - r, c.y - r, r * 2f, r * 2f).Contains(pointer));
                var col = mainColor;
                if (hi) { Color.RGBToHSV(col, out float h, out float s, out float v); col = Color.HSVToRGB(h, s * 0.8f, Mathf.Min(v * 1.5f, 1f)); }
                if (!enabledInHierarchy) col.a /= 2f;
                Disc(p2, c, r, col);
            }
            foreach (var i in selected) if (i < envelope.Count) Disc(p2, ToLocal(envelope.GetPoint(i)), r, Style.selectedEnvelopeHandleColor);

            // The "a point goes here" disc while hovering the line away from any point.
            if (hovering && !pressed && !shiftHeld && PointAt(pointer) < 0 && OnLine(pointer)) {
                bool near = false;
                for (int i = 0; i < envelope.Count && !near; i++) near = Vector2.Distance(pointer, ToLocal(envelope.GetPoint(i))) <= r * 2f;
                if (!near) Disc(p2, new Vector2(pointer.x, LineY(pointer.x)), r, new Color(1f, 1f, 1f, 0.5f));
            }

            // The selection box.
            if (boxSelecting) {
                float cx = Mathf.Clamp(pointer.x, 0f, Size.x), cy = Mathf.Clamp(pointer.y, 0f, Size.y);
                var rect = Rect.MinMaxRect(Mathf.Min(boxStart.x, cx), Mathf.Min(boxStart.y, cy), Mathf.Max(boxStart.x, cx), Mathf.Max(boxStart.y, cy));
                p2.fillColor = new Color(1f, 1f, 1f, 0.1f);
                p2.BeginPath(); p2.MoveTo(rect.min); p2.LineTo(new Vector2(rect.xMax, rect.yMin)); p2.LineTo(rect.max); p2.LineTo(new Vector2(rect.xMin, rect.yMax)); p2.ClosePath(); p2.Fill();
                p2.strokeColor = Color.white; p2.lineWidth = 1f / Mathf.Max(1f, UnityEditor.EditorGUIUtility.pixelsPerPoint); p2.Stroke();
            }
        }

        void Stroke(Painter2D p2, float startTime, float range, Color color, float width) {
            float total = XRange;
            int n = Mathf.Max(2, (int)(range / total * Size.x / 4f));
            float step = range / n, t = startTime, end = startTime + range;
            // IMGUI's anti-aliased polyline width is in device pixels, the painter's in points: convert, or the line is
            // drawn 2.25 times as wide at a 225 % display (measured against the old window).
            p2.strokeColor = color; p2.lineWidth = width / Mathf.Max(1f, UnityEditor.EditorGUIUtility.pixelsPerPoint);
            p2.BeginPath();
            for (int it = 0; it <= n; it++) {
                var pt = new Vector2((t - envelope.xMin) / total * Size.x, Size.y - (envelope.Evaluate(t) - envelope.yMin) / YRange * Size.y);
                if (it == 0) p2.MoveTo(pt); else p2.LineTo(pt);
                if (it < n) { t += step; if (t > end) t = end; }
            }
            p2.Stroke();
        }

        static void Disc(Painter2D p2, Vector2 c, float r, Color col) {
            p2.fillColor = col;
            p2.BeginPath(); p2.Arc(c, r, 0f, 360f); p2.ClosePath(); p2.Fill();
        }

        /// <summary>Redraws after the envelope was changed elsewhere (the old window, an undo).</summary>
        public void Refresh() => MarkDirtyRepaint();
    }
}
