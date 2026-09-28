using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI <c>ZUI.Envelope(rect, points, curveColor, def, runtime)</c> (added for the Zounds UI
    /// Toolkit port, T-0468), driven by the very same <see cref="ZUIEnvelopeDef"/> (look) and <see cref="ZUIEnvelopeRuntime"/>
    /// (domain, permissions, callbacks) objects, with the IMGUI control's rules one for one:
    ///
    /// - drawn inside the def's padding: background and border on the outer rect, then the optional grid, the curve
    ///   (sampled every 3 px, bent segments), the Shift-hover segment highlight, the point handles (per-edit-state
    ///   radius and colours, hover grow + selection ring, value tag), the add-point ghost, and the selection box;
    /// - press on a point drags it (double-click removes it when allowed; Shift+right-press bends its segment); press on
    ///   the line adds a point there and drags it; Shift+press on the line drags the segment, Shift+right-press bends it;
    ///   press on empty plot starts a box select; dragging with several selected moves them all; Delete removes the
    ///   selection; a one-point envelope can grow back from a press on its flat line;
    /// - the runtime's onDragStarted / onDragUpdated / onMutated fire exactly where the IMGUI control fires them.
    ///
    /// Why this exists beside <see cref="ZuiEnvelope"/>: that control is the Toolkit-native envelope with its own
    /// established gestures (double-click to insert, right-click to remove) and fixed look, used by other tools. This one
    /// is the exact twin of the sheet-styled IMGUI control, for ports that must behave and look like the IMGUI original.
    ///
    /// Several envelopes stacked over one area (the Klip waveform draws volume and pitch over the same rect) must be offered
    /// each event in turn, as IMGUI offers it to each in draw order; a host does that by setting <c>pickingMode</c> to
    /// Ignore and calling <see cref="PointerDown"/> / <see cref="PointerMove"/> / <see cref="PointerUp"/> /
    /// <see cref="KeyDown"/> itself, which report whether they used the event. Standalone, the element wires them itself.
    ///
    /// Not ported: markers (loop/trim markers), which no Toolkit user needs yet.
    public class ZuiSkinEnvelope : VisualElement
    {
        public List<ZUIEnvelopePoint> points;
        public Color curveColor;
        public ZUIEnvelopeDef def;
        public ZUIEnvelopeRuntime rt;
        /// <summary>
        /// Right-click on a point (without Shift, which bends a segment): the host's settings for that point, such as a
        /// random point's ellipse (Zounds, T-0483). Given the point's index and its centre in world space. Null: ignored.
        /// </summary>
        public System.Action<int, Vector2> onPointContext;
        /// <summary>
        /// Curves drawn dotted over the authored one, each a time-to-value function: what the plays under way are actually
        /// hearing (Zounds, T-0484: a curve with random points is drawn afresh for every play). Null or empty: nothing extra.
        /// </summary>
        public List<System.Func<float, float>> liveCurves;

        int _dragPoint = -1, _dragLine = -1, _dragExponent = -1, _hoverPoint = -1, _hoverLine = -1;
        bool _boxSelecting, _pressed, _shift;
        Vector2 _boxStart, _pointer = new Vector2(float.NaN, float.NaN);
        readonly List<int> _selected = new List<int>();
        readonly Label _tag;

        public ZuiSkinEnvelope(List<ZUIEnvelopePoint> points, Color curveColor, ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt, bool standalone = true)
        {
            this.points = points; this.curveColor = curveColor; this.def = def; this.rt = rt;
            AddToClassList("zui-skinenvelope");
            focusable = true;
            generateVisualContent += Paint;
            _tag = new Label { pickingMode = PickingMode.Ignore };
            _tag.AddToClassList("zui-skinenvelope__tag");
            _tag.style.position = Position.Absolute;
            _tag.style.fontSize = 10;
            _tag.style.color = Color.white;
            _tag.style.backgroundColor = new Color(0f, 0f, 0f, 0.78f);
            _tag.style.paddingLeft = 4; _tag.style.paddingRight = 4; _tag.style.paddingTop = 1; _tag.style.paddingBottom = 1;
            _tag.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tag.style.display = DisplayStyle.None;
            Add(_tag);
            if (standalone)
            {
                RegisterCallback<PointerDownEvent>(e => { if (PointerDown(e.localPosition, e.button, e.clickCount, e.shiftKey)) { this.CapturePointer(e.pointerId); e.StopPropagation(); } });
                RegisterCallback<PointerMoveEvent>(e => { if (PointerMove(e.localPosition, e.deltaPosition, e.shiftKey, e.pressedButtons)) e.StopPropagation(); });
                RegisterCallback<PointerUpEvent>(e => { PointerUp(); if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); });
                RegisterCallback<PointerLeaveEvent>(_ => { if (!_pressed) { _hoverPoint = _hoverLine = -1; _pointer = new Vector2(float.NaN, float.NaN); Repaint(); } });
                RegisterCallback<KeyDownEvent>(e => { if (KeyDown(e.keyCode)) e.StopPropagation(); });
            }
        }

        /// <summary>Redraw after the points or the look changed elsewhere.</summary>
        public void Repaint() { MarkDirtyRepaint(); UpdateTag(); }

        // ─────────────────────────── geometry ───────────────────────────

        Rect Plot
        {
            get
            {
                var r = contentRect;
                var d = def;
                return new Rect(r.x + d.paddingLeft, r.y + d.paddingTop,
                                Mathf.Max(0f, r.width - d.paddingLeft - d.paddingRight),
                                Mathf.Max(0f, r.height - d.paddingTop - d.paddingBottom));
            }
        }

        float TimeToX(float t, Rect r) => r.x + (t - rt.xMin) / (rt.xMax - rt.xMin) * r.width;
        float XToTime(float x, Rect r) => rt.xMin + (x - r.x) / r.width * (rt.xMax - rt.xMin);
        float ValueToY(float v, Rect r) => r.y + r.height - (v - rt.yMin) / (rt.yMax - rt.yMin) * r.height;

        ZUIEnvelopeEditState State(int i)
        {
            if (rt.anchorsLocked && points.Count > 1 && (i == 0 || i == points.Count - 1)) return ZUIEnvelopeEditState.NotEditable;
            return points[i].editState;
        }
        static bool CanMoveX(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.XEditable;
        static bool CanMoveY(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.YEditable;
        static bool CanRemove(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable;

        float MaxHandleRadius => Mathf.Max(def.editable.radius, Mathf.Max(def.xEditable.radius, Mathf.Max(def.yEditable.radius, def.notEditable.radius)));

        static float Evaluate(List<ZUIEnvelopePoint> pts, float time, float fallback)
        {
            if (pts.Count == 0) return fallback;
            if (pts.Count == 1) return pts[0].value;
            int idx = pts.Count;
            for (int i = 0; i < pts.Count; i++) if (pts[i].time > time) { idx = i; break; }
            if (idx <= 0) return pts[0].value;
            if (idx >= pts.Count) return pts[pts.Count - 1].value;
            var a = pts[idx - 1]; var b = pts[idx];
            float range = b.time - a.time;
            if (range <= 0f) return b.value;
            float e = b.exponent <= 0f ? 0.000001f : b.exponent;
            return Mathf.Lerp(a.value, b.value, Mathf.Pow((time - a.time) / range, e));
        }

        int HitPoint(Rect r, Vector2 m)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var h = def.GetHandle(State(i));
                if (Vector2.Distance(m, new Vector2(TimeToX(points[i].time, r), ValueToY(points[i].value, r))) <= h.radius + def.hitRadiusExtra) return i;
            }
            return -1;
        }

        int HitLine(Rect r, Vector2 m)
        {
            if (points.Count < 2) return -1;
            float t = XToTime(m.x, r);
            if (Mathf.Abs(m.y - ValueToY(Evaluate(points, t, rt.yMax), r)) > 6f) return -1;
            for (int i = 1; i < points.Count; i++) if (points[i].time >= t) return i;
            return -1;
        }

        void UpdateHover(Vector2 m)
        {
            _hoverPoint = _hoverLine = -1;
            var r = Plot;
            float slop = MaxHandleRadius + def.hitRadiusExtra;
            if (!new Rect(r.x - slop, r.y - slop, r.width + slop * 2f, r.height + slop * 2f).Contains(m)) return;
            int p = HitPoint(r, m);
            if (p >= 0) { if (State(p) != ZUIEnvelopeEditState.NotEditable) _hoverPoint = p; return; }
            if (!r.Contains(m)) return;
            int l = HitLine(r, m);
            if (l > 0) _hoverLine = l;
        }

        // ─────────────────────────── input (host-callable) ───────────────────────────

        /// <summary>The IMGUI MouseDown path. Returns true when this envelope took the press (the host should capture).</summary>
        public bool PointerDown(Vector2 m, int button, int clickCount, bool shift)
        {
            if (points == null || rt == null || def == null || !rt.editable) return false;
            _shift = shift; _pointer = m;
            UpdateHover(m);
            var r = Plot;
            if (r.width <= 0f || r.height <= 0f || rt.xMax <= rt.xMin || rt.yMax <= rt.yMin) return false;
            float slop = MaxHandleRadius + def.hitRadiusExtra;
            if (!new Rect(r.x - slop, r.y - slop, r.width + slop * 2f, r.height + slop * 2f).Contains(m)) return false;
            bool inside = r.Contains(m);

            int hit = HitPoint(r, m);
            if (hit >= 0)
            {
                var es = State(hit);
                if (es == ZUIEnvelopeEditState.NotEditable) return false;
                if (button == 0)
                {
                    if (clickCount == 2 && rt.allowRemovePoints && CanRemove(es) && points.Count > 1)
                    {
                        rt.onDragStarted?.Invoke();
                        points.RemoveAt(hit);
                        _selected.Clear();
                        rt.onMutated?.Invoke();
                        Repaint();
                        return true;
                    }
                    rt.onDragStarted?.Invoke();
                    _dragPoint = hit;
                    if (!_selected.Contains(hit)) { _selected.Clear(); _selected.Add(hit); }
                    Focus();
                    _pressed = true; Repaint();
                    return true;
                }
                if (button == 1 && shift && rt.allowExponentEdit && hit > 0)
                {
                    rt.onDragStarted?.Invoke();
                    _dragExponent = hit;
                    _pressed = true; Repaint();
                    return true;
                }
                if (button == 1 && !shift && onPointContext != null)
                {
                    onPointContext(hit, this.LocalToWorld(new Vector2(TimeToX(points[hit].time, r), ValueToY(points[hit].value, r))));
                    return true;
                }
                return false;
            }
            if (!inside) return false;

            if (points.Count == 1 && button == 0 && rt.allowAddPoints)
            {
                float t = XToTime(m.x, r), v = Evaluate(points, t, rt.yMax);
                if (Mathf.Abs(m.y - ValueToY(v, r)) <= 6f)
                {
                    rt.onDragStarted?.Invoke();
                    int ins = Insert(new ZUIEnvelopePoint(t, v, 1f));
                    _dragPoint = ins; _selected.Clear(); _selected.Add(ins);
                    rt.onMutated?.Invoke();
                    _pressed = true; Repaint();
                    return true;
                }
            }
            int line = HitLine(r, m);
            if (line > 0)
            {
                if (shift && button == 0 && rt.allowSegmentDrag) { rt.onDragStarted?.Invoke(); _dragLine = line; _selected.Clear(); _pressed = true; Repaint(); return true; }
                if (shift && button == 1 && rt.allowExponentEdit) { rt.onDragStarted?.Invoke(); _dragExponent = line; _selected.Clear(); _pressed = true; Repaint(); return true; }
                if (button == 0 && rt.allowAddPoints)
                {
                    float t = XToTime(m.x, r), v = Evaluate(points, t, rt.yMax);
                    rt.onDragStarted?.Invoke();
                    int ins = Insert(new ZUIEnvelopePoint(t, v, 1f));
                    _dragPoint = ins; _selected.Clear(); _selected.Add(ins);
                    rt.onMutated?.Invoke();
                    _pressed = true; Repaint();
                    return true;
                }
            }
            if (button == 0 && rt.allowBoxSelect)
            {
                _boxSelecting = true; _boxStart = m; _selected.Clear();
                Focus();
                _pressed = true; Repaint();
                return true;
            }
            return false;
        }

        /// <summary>The IMGUI MouseMove/MouseDrag path. <paramref name="pressedButtons"/> as UI Toolkit reports it (bit 0 = left).</summary>
        public bool PointerMove(Vector2 m, Vector2 delta, bool shift, int pressedButtons)
        {
            if (points == null || rt == null || def == null) return false;
            _shift = shift; _pointer = m;
            var r = Plot;
            bool used = false;
            if (_pressed && r.width > 0f && r.height > 0f)
            {
                float xRange = rt.xMax - rt.xMin, yRange = rt.yMax - rt.yMin;
                if (_dragPoint >= 0 && _dragPoint < points.Count)
                {
                    var p = points[_dragPoint]; var es = State(_dragPoint);
                    float t = Mathf.Clamp(XToTime(m.x, r), rt.xMin, rt.xMax);
                    float v = Mathf.Clamp(rt.yMin + yRange * (1f - (m.y - r.y) / r.height), rt.yMin, rt.yMax);
                    if (_dragPoint > 0) t = Mathf.Max(t, points[_dragPoint - 1].time);
                    if (_dragPoint < points.Count - 1) t = Mathf.Min(t, points[_dragPoint + 1].time);
                    if (CanMoveX(es)) p.time = t;
                    if (CanMoveY(es)) p.value = v;
                    rt.onDragUpdated?.Invoke(); used = true;
                }
                else if (_dragLine > 0 && _dragLine < points.Count)
                {
                    if (!shift) _dragLine = -1;
                    else { MoveSegment(_dragLine, xRange, yRange, r, delta); rt.onDragUpdated?.Invoke(); used = true; }
                }
                else if (_dragExponent > 0 && _dragExponent < points.Count)
                {
                    if (!shift) _dragExponent = -1;
                    else
                    {
                        var p = points[_dragExponent]; var prev = points[_dragExponent - 1];
                        float mul = p.exponent <= 0f ? 0.000001f : Mathf.Sqrt(p.exponent);
                        float dy = delta.y; if (p.value < prev.value) dy = -dy;
                        p.exponent = Mathf.Max(0f, p.exponent + dy / r.height * mul * 8f);
                        rt.onDragUpdated?.Invoke(); used = true;
                    }
                }
                else if (_boxSelecting)
                {
                    BoxUpdate(r, m); used = true;
                }
                else if (_selected.Count > 1 && (pressedButtons & 1) != 0)
                {
                    rt.onDragStarted?.Invoke();
                    float dt = xRange * delta.x / r.width, dv = -yRange * delta.y / r.height;
                    foreach (int i in _selected)
                    {
                        if (i < 0 || i >= points.Count) continue;
                        var p = points[i]; var es = State(i);
                        if (CanMoveX(es)) p.time = Mathf.Clamp(p.time + dt, rt.xMin, rt.xMax);
                        if (CanMoveY(es)) p.value = Mathf.Clamp(p.value + dv, rt.yMin, rt.yMax);
                    }
                    points.Sort((x, y) => x.time.CompareTo(y.time));
                    rt.onDragUpdated?.Invoke(); used = true;
                }
            }
            else UpdateHover(m);
            Repaint();
            return used;
        }

        public void PointerUp()
        {
            _pressed = false; _boxSelecting = false;
            _dragPoint = _dragLine = _dragExponent = -1;
            Repaint();
        }

        /// <summary>Clears the pointer (it left the area), so no hover or ghost stays drawn.</summary>
        public void PointerLeft()
        {
            if (_pressed) return;
            _hoverPoint = _hoverLine = -1; _pointer = new Vector2(float.NaN, float.NaN);
            Repaint();
        }

        public bool KeyDown(KeyCode key)
        {
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift) { _shift = true; Repaint(); return false; }
            if (key != KeyCode.Delete || rt == null || !rt.allowRemovePoints || _selected.Count == 0) return false;
            rt.onDragStarted?.Invoke();
            _selected.Sort();
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                if (points.Count <= 1) break;
                int idx = _selected[i];
                if (idx < 0 || idx >= points.Count || !CanRemove(State(idx))) continue;
                points.RemoveAt(idx);
            }
            _selected.Clear();
            rt.onMutated?.Invoke();
            Repaint();
            return true;
        }

        public void ResetState() { _dragPoint = _dragLine = _dragExponent = -1; _boxSelecting = false; _pressed = false; _selected.Clear(); _hoverPoint = _hoverLine = -1; Repaint(); }

        int Insert(ZUIEnvelopePoint p)
        {
            int idx = points.Count;
            for (int i = 0; i < points.Count; i++) if (p.time < points[i].time) { idx = i; break; }
            points.Insert(idx, p);
            return idx;
        }

        void MoveSegment(int endIdx, float xRange, float yRange, Rect r, Vector2 delta)
        {
            var a = points[endIdx - 1]; var b = points[endIdx];
            var aS = State(endIdx - 1); var bS = State(endIdx);
            float dt = xRange * delta.x / r.width, dv = -yRange * delta.y / r.height;
            if (CanMoveX(aS) && CanMoveX(bS))
            {
                float aT = a.time + dt, bT = b.time + dt;
                if (endIdx - 1 > 0) aT = Mathf.Max(aT, points[endIdx - 2].time);
                if (endIdx + 1 < points.Count) bT = Mathf.Min(bT, points[endIdx + 1].time);
                a.time = Mathf.Clamp(aT, rt.xMin, rt.xMax);
                b.time = Mathf.Clamp(bT, rt.xMin, rt.xMax);
            }
            if (CanMoveY(aS)) a.value = Mathf.Clamp(a.value + dv, rt.yMin, rt.yMax);
            if (CanMoveY(bS)) b.value = Mathf.Clamp(b.value + dv, rt.yMin, rt.yMax);
        }

        void BoxUpdate(Rect r, Vector2 m)
        {
            var cur = new Vector2(Mathf.Clamp(m.x, r.x, r.xMax), Mathf.Clamp(m.y, r.y, r.yMax));
            float minX = Mathf.Min(_boxStart.x, cur.x), maxX = Mathf.Max(_boxStart.x, cur.x);
            float minY = Mathf.Min(_boxStart.y, cur.y), maxY = Mathf.Max(_boxStart.y, cur.y);
            _selected.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                if (State(i) == ZUIEnvelopeEditState.NotEditable) continue;
                float px = TimeToX(points[i].time, r), py = ValueToY(points[i].value, r);
                if (px >= minX && px <= maxX && py >= minY && py <= maxY) _selected.Add(i);
            }
        }

        // ─────────────────────────── drawing ───────────────────────────

        static float Px(float devicePixels) => devicePixels / Mathf.Max(1f, UnityEditor.EditorGUIUtility.pixelsPerPoint);

        /// <summary>A dotted polyline: every other short piece of it (about 3 px on, 3 px off), so it reads as distinct from
        /// the solid curve it lies on (also used by other Toolkit curve editors).</summary>
        public static void Dotted(Painter2D p2, Vector2[] pts, Color c, float width)
        {
            p2.strokeColor = c; p2.lineWidth = width; p2.lineCap = LineCap.Round;
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                p2.BeginPath(); p2.MoveTo(pts[i]); p2.LineTo(pts[i + 1]); p2.Stroke();
            }
        }

        /// <summary>An axis-aligned ellipse, filled and outlined (also used by other Toolkit curve editors).</summary>
        public static void Ellipse(Painter2D p2, Vector2 c, float rx, float ry, Color fill, Color stroke, float width)
        {
            const int n = 40;
            p2.BeginPath();
            for (int k = 0; k <= n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                var q = new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry);
                if (k == 0) p2.MoveTo(q); else p2.LineTo(q);
            }
            p2.ClosePath();
            if (fill.a > 0f) { p2.fillColor = fill; p2.Fill(); }
            if (stroke.a > 0f) { p2.strokeColor = stroke; p2.lineWidth = width; p2.Stroke(); }
        }

        void Paint(MeshGenerationContext ctx)
        {
            if (points == null || rt == null || def == null) return;
            var sheet = def.ownerSheet;
            var outer = contentRect;
            var p2 = ctx.painter2D;

            // Background (a flat colour; the Zounds overlay's is fully transparent) and border.
            var bg = def.background != null ? def.background.GetColorA(sheet) : Color.clear;
            if (bg.a > 0f) FillRect(p2, outer, bg);
            if (def.border != null)
            {
                var ew = def.border.edgeWidth; var bc = def.border.color.GetColorA(sheet);
                if (bc.a > 0f)
                {
                    if (ew.Top > 0f) FillRect(p2, new Rect(outer.x, outer.y, outer.width, ew.Top), bc);
                    if (ew.Bottom > 0f) FillRect(p2, new Rect(outer.x, outer.yMax - ew.Bottom, outer.width, ew.Bottom), bc);
                    if (ew.Left > 0f) FillRect(p2, new Rect(outer.x, outer.y, ew.Left, outer.height), bc);
                    if (ew.Right > 0f) FillRect(p2, new Rect(outer.xMax - ew.Right, outer.y, ew.Right, outer.height), bc);
                }
            }
            if (rt.xMax - rt.xMin <= 0f || rt.yMax - rt.yMin <= 0f) return;
            var r = Plot;
            if (r.width <= 0f || r.height <= 0f) return;

            if (rt.showGrid && def.gridRows > 0)
            {
                var g = def.gridColor.Resolve(sheet);
                for (int i = 1; i < def.gridRows; i++) FillRect(p2, new Rect(r.x, r.y + r.height * i / def.gridRows, r.width, 1f), g);
            }

            // Curve.
            if (points.Count > 0)
            {
                int n = Mathf.Max(2, (int)(r.width / 3f));
                p2.strokeColor = curveColor; p2.lineWidth = Px(def.curveThickness);
                p2.lineJoin = LineJoin.Round; p2.lineCap = LineCap.Round;
                p2.BeginPath();
                for (int i = 0; i <= n; i++)
                {
                    float t = rt.xMin + (rt.xMax - rt.xMin) * i / n;
                    var pt = new Vector2(TimeToX(t, r), ValueToY(Evaluate(points, t, rt.yMax), r));
                    if (i == 0) p2.MoveTo(pt); else p2.LineTo(pt);
                }
                p2.Stroke();
                if (_hoverLine > 0 && _hoverLine < points.Count && _shift)
                {
                    var a = points[_hoverLine - 1]; var b = points[_hoverLine];
                    int segPx = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(TimeToX(b.time, r) - TimeToX(a.time, r)) / 2f));
                    Color.RGBToHSV(curveColor, out float h, out float s, out float vC);
                    p2.strokeColor = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.8f), Mathf.Min(vC * 1.4f, 1f));
                    p2.lineWidth = Px(def.curveHoverThickness);
                    p2.BeginPath();
                    for (int i = 0; i <= segPx; i++)
                    {
                        float t = Mathf.Lerp(a.time, b.time, (float)i / segPx);
                        var pt = new Vector2(TimeToX(t, r), ValueToY(Evaluate(points, t, rt.yMax), r));
                        if (i == 0) p2.MoveTo(pt); else p2.LineTo(pt);
                    }
                    p2.Stroke();
                }
            }

            // What the plays under way actually hear (T-0484), dotted over the authored curve.
            if (liveCurves != null && liveCurves.Count > 0)
            {
                int n = Mathf.Max(2, (int)(r.width / 3f));
                var dotted = new Vector2[n + 1];
                foreach (var f in liveCurves)
                {
                    if (f == null) continue;
                    for (int i = 0; i <= n; i++)
                    {
                        float t = rt.xMin + (rt.xMax - rt.xMin) * i / n;
                        dotted[i] = new Vector2(TimeToX(t, r), ValueToY(f(t), r));
                    }
                    Dotted(p2, dotted, Color.Lerp(curveColor, Color.white, 0.45f), Px(Mathf.Max(def.curveThickness, 1.5f)));
                }
            }

            // Random points' ellipses (T-0483): where each play may move the point, faint fill and a thin outline.
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                if (pt.randomX <= 0f && pt.randomY <= 0f) continue;
                var c = new Vector2(TimeToX(pt.time, r), ValueToY(pt.value, r));
                float ex = pt.randomX / (rt.xMax - rt.xMin) * r.width, ey = pt.randomY / (rt.yMax - rt.yMin) * r.height;
                Ellipse(p2, c, Mathf.Max(ex, 1f), Mathf.Max(ey, 1f), new Color(curveColor.r, curveColor.g, curveColor.b, 0.14f),
                        new Color(curveColor.r, curveColor.g, curveColor.b, 0.75f), Px(1f));
            }

            // Points.
            var sel = def.selectedColor.Resolve(sheet);
            for (int i = 0; i < points.Count; i++)
            {
                var es = State(i); var h = def.GetHandle(es);
                var c = new Vector2(TimeToX(points[i].time, r), ValueToY(points[i].value, r));
                bool hover = _hoverPoint == i || _dragPoint == i;
                bool isSel = _selected.Contains(i) || _dragPoint == i;
                float vr = hover && es != ZUIEnvelopeEditState.NotEditable ? h.hoverRadius : h.radius;
                var fill = hover ? h.hoverFillColor.Resolve(sheet) : h.fillColor.Resolve(sheet);
                if (isSel) fill = sel;
                if (h.borderWidth > 0f) Disc(p2, c, vr + h.borderWidth, h.borderColor.Resolve(sheet));
                Disc(p2, c, vr, fill);
                if (hover && !isSel && es != ZUIEnvelopeEditState.NotEditable)
                {
                    p2.strokeColor = sel; p2.lineWidth = Px(1f);
                    p2.BeginPath(); p2.Arc(c, vr + 1.5f, 0f, 360f); p2.Stroke();
                }
            }

            // Add-point ghost.
            if (rt.allowAddPoints && !_shift && _hoverLine > 0 && _dragPoint < 0 && _dragLine < 0 && !float.IsNaN(_pointer.x))
            {
                float t = Mathf.Clamp(XToTime(_pointer.x, r), rt.xMin, rt.xMax);
                var hd = def.GetHandle(ZUIEnvelopeEditState.Editable);
                var f = hd.fillColor.Resolve(sheet); f.a *= 0.5f;
                Disc(p2, new Vector2(TimeToX(t, r), ValueToY(Evaluate(points, t, rt.yMax), r)), hd.radius, f);
            }

            // Selection box.
            if (_boxSelecting && !float.IsNaN(_pointer.x))
            {
                var cur = new Vector2(Mathf.Clamp(_pointer.x, r.x, r.xMax), Mathf.Clamp(_pointer.y, r.y, r.yMax));
                var box = Rect.MinMaxRect(Mathf.Min(_boxStart.x, cur.x), Mathf.Min(_boxStart.y, cur.y), Mathf.Max(_boxStart.x, cur.x), Mathf.Max(_boxStart.y, cur.y));
                FillRect(p2, box, new Color(1f, 1f, 1f, 0.1f));
                p2.strokeColor = sel; p2.lineWidth = Px(1f);
                p2.BeginPath(); p2.MoveTo(box.min); p2.LineTo(new Vector2(box.xMax, box.yMin)); p2.LineTo(box.max); p2.LineTo(new Vector2(box.xMin, box.yMax)); p2.ClosePath(); p2.Stroke();
            }
        }

        /// <summary>The value tag beside a hovered or selected point (the IMGUI DrawPointValueTag), as a positioned label.</summary>
        void UpdateTag()
        {
            if (points == null || rt == null || def == null) { _tag.style.display = DisplayStyle.None; return; }
            int i = _dragPoint >= 0 ? _dragPoint : _hoverPoint >= 0 ? _hoverPoint : _selected.Count > 0 ? _selected[_selected.Count - 1] : -1;
            if (i < 0 || i >= points.Count || contentRect.width <= 0f) { _tag.style.display = DisplayStyle.None; return; }
            var r = Plot;
            var es = State(i); var h = def.GetHandle(es);
            bool hover = _hoverPoint == i || _dragPoint == i;
            float vr = hover && es != ZUIEnvelopeEditState.NotEditable ? h.hoverRadius : h.radius;
            var p = points[i];
            var c = new Vector2(TimeToX(p.time, r), ValueToY(p.value, r));
            bool showTime = !(Mathf.Approximately(rt.xMin, 0f) && Mathf.Approximately(rt.xMax, 1f));
            _tag.text = showTime ? Fmt(p.time) + ", " + Fmt(p.value) : Fmt(p.value);
            var sz = _tag.MeasureTextSize(_tag.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined) + new Vector2(8f, 2f);
            float lx = c.x + vr + 5f, ly = c.y - sz.y - 3f;
            if (lx + sz.x > r.xMax + 3f) lx = c.x - vr - 5f - sz.x;
            if (ly < r.y - 1f) ly = c.y + vr + 3f;
            _tag.style.left = lx; _tag.style.top = ly;
            _tag.style.display = DisplayStyle.Flex;
        }

        static string Fmt(float v)
        {
            float a = Mathf.Abs(v);
            return a >= 100f ? v.ToString("0") : a >= 10f ? v.ToString("0.#") : v.ToString("0.##");
        }

        static void FillRect(Painter2D p2, Rect r, Color c)
        {
            p2.fillColor = c;
            p2.BeginPath(); p2.MoveTo(r.min); p2.LineTo(new Vector2(r.xMax, r.yMin)); p2.LineTo(r.max); p2.LineTo(new Vector2(r.xMin, r.yMax)); p2.ClosePath(); p2.Fill();
        }

        static void Disc(Painter2D p2, Vector2 c, float radius, Color col)
        {
            p2.fillColor = col;
            p2.BeginPath(); p2.Arc(c, radius, 0f, 360f); p2.ClosePath(); p2.Fill();
        }
    }
}
