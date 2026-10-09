using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// Shared retained-mode envelope canvas. Configuration controls gestures, permissions and decorations; adapters
    /// retain the established entry points without maintaining separate painters or editing algorithms.
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
        public readonly ZuiEnvelopeConfiguration configuration = new ZuiEnvelopeConfiguration();
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
        /// <summary>
        /// Only a press on a point or on the line is this envelope's (Zounds non-destructive editing, T-0566): a press anywhere
        /// else is not consumed and passes through to whatever is underneath, so a curve drawn over a Zequence track leaves
        /// selecting, moving and trimming the track to the track.
        /// </summary>
        public bool pointsAndLineOnly;
        /// <summary>Drawn dashed: a curve that belongs to the timeline (a track's or the Zequence's own), which stays where it is
        /// when a piece moves, unlike the sound's own curves that travel with its audio (T-0566).</summary>
        public bool dashed;
        /// <summary>
        /// Drawn as a backdrop (Zounds, owner's request 2026-10-08): a curve that is shown while another one over the same
        /// area is being edited. Half transparent and twice as wide, with no handles, hover or ghost, and it takes no
        /// input, so it reads as context behind the curve under the pointer rather than competing with it.
        /// </summary>
        public bool backdrop;
        /// <summary>How a backdrop is drawn (a host's setting): dotted, half transparent, and how many pixels wider than the
        /// curve's own line. The defaults are solid, half transparent, twice as wide (a negative bonus).</summary>
        public bool backdropDotted, backdropTransparent = true;
        public float backdropWidthBonus = -1f;
        /// <summary>Whether the points are drawn at all. Off: just the line (no handles, no ghost, no value tag), as a curve
        /// that is not selected for editing is shown. Input is still refused when the runtime says the curve is not editable.</summary>
        public bool showHandles = true;

        int _dragPoint = -1, _dragLine = -1, _dragExponent = -1, _hoverPoint = -1, _hoverLine = -1;
        bool _boxSelecting, _pressed, _shift;
        Vector2 _boxStart, _pointer = new Vector2(float.NaN, float.NaN);
        readonly List<int> _selected = new List<int>();
        bool _multiDrag;
        bool _gestureRecorded, _pendingMultiDrag;
        readonly ZuiEnvelopePresentation _presentation = new ZuiEnvelopePresentation();

        /// <summary>The selected points, as indices (a host's point menu acts on them when the clicked point is one).</summary>
        public IReadOnlyList<int> SelectedPoints => _selected;
        readonly Label _tag;

        public ZuiSkinEnvelope(List<ZUIEnvelopePoint> points, Color curveColor, ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt, bool standalone = true)
        {
            this.points = points; this.curveColor = curveColor; this.def = def; this.rt = rt;
            AddToClassList("zui-skinenvelope");
            AddToClassList("zui-envelope");
            focusable = true;
            generateVisualContent += Paint;
            RegisterCallback<CustomStyleResolvedEvent>(e => { _presentation.Resolve(e, def, curveColor); Repaint(); });
            _tag = new Label { pickingMode = PickingMode.Ignore };
            _tag.AddToClassList("zui-skinenvelope__tag");
            _tag.AddToClassList("zui-envelope__readout");
            _tag.style.display = DisplayStyle.None;
            Add(_tag);
            if (standalone)
            {
                RegisterCallback<PointerDownEvent>(e => { if (PointerDown(e.localPosition, e.button, e.clickCount, e.shiftKey)) { this.CapturePointer(e.pointerId); e.StopPropagation(); } });
                RegisterCallback<PointerMoveEvent>(e => { if (PointerMove(e.localPosition, e.deltaPosition, e.shiftKey, e.pressedButtons)) e.StopPropagation(); });
                RegisterCallback<PointerUpEvent>(e => { PointerUp(); if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); });
                RegisterCallback<PointerLeaveEvent>(_ => { if (!_pressed) { _hoverPoint = _hoverLine = -1; _pointer = new Vector2(float.NaN, float.NaN); Repaint(); } });
                RegisterCallback<KeyDownEvent>(e => { if (KeyDown(e.keyCode)) e.StopPropagation(); });
                RegisterCallback<PointerCaptureOutEvent>(_ => PointerUp());
                RegisterCallback<DetachFromPanelEvent>(_ => PointerUp());
            }
        }

        /// <summary>Redraw after the points or the look changed elsewhere.</summary>
        public void Repaint() { Prepare(); MarkDirtyRepaint(); UpdateTag(); }

        /// <summary>Adapters refresh mutable domain/configuration before drawing and editing.</summary>
        protected virtual void Prepare() { }

        void BeginGesture()
        {
            if (_gestureRecorded) return;
            _gestureRecorded = true;
            rt.onDragStarted?.Invoke();
        }

        void SelectionChanged() => configuration.onSelectionChanged?.Invoke();
        bool ValidPlot(Rect r) => Finite(r.min) && Finite(r.size) && r.width > 0f && r.height > 0f
            && rt != null && Finite(new Vector2(rt.xMin, rt.xMax)) && Finite(new Vector2(rt.yMin, rt.yMax))
            && rt.xMax > rt.xMin && rt.yMax > rt.yMin;

        ZuiEnvelopePresentation.Values Presentation
        {
            get { _presentation.RefreshFallbacks(def, curveColor); return _presentation.Current; }
        }

        // ─────────────────────────── geometry ───────────────────────────

        Rect Plot
        {
            get
            {
                var r = contentRect;
                var s = Presentation;
                return new Rect(r.x + s.paddingLeft, r.y + s.paddingTop,
                                Mathf.Max(0f, r.width - s.paddingLeft - s.paddingRight),
                                Mathf.Max(0f, r.height - s.paddingTop - s.paddingBottom));
            }
        }

        /// The plotting area in this element's local space — where xMin..xMax actually lands. A host that lines
        /// another control up with this envelope's time axis (a scrubber above it) aligns to THIS, not to the
        /// element's edges, because the style sheet pads the plot inside the element.
        public Rect PlotRect => Plot;

        /// <summary>
        /// An optional time axis that is not a straight line (Zounds, 2026-10-09: a curve drawn over a Zequence track, where
        /// a time curve or a pitch change stretches the sound unevenly along the timeline): a curve time to an x in this
        /// element's local space, and back. Both must be set, monotonic, and inverses of each other; null is the usual
        /// linear axis from <c>rt.xMin..xMax</c> across the plot. Hit-testing, drawing and dragging all go through it.
        /// </summary>
        public System.Func<float, float> timeToLocalX, localXToTime;

        float TimeToX(float t, Rect r) => timeToLocalX != null && localXToTime != null ? timeToLocalX(t) : r.x + (t - rt.xMin) / (rt.xMax - rt.xMin) * r.width;
        float XToTime(float x, Rect r) => timeToLocalX != null && localXToTime != null ? localXToTime(x) : rt.xMin + (x - r.x) / r.width * (rt.xMax - rt.xMin);
        /// <summary>A pointer move of <paramref name="dx"/> pixels ending at <paramref name="x"/>, in curve time.</summary>
        float TimeDelta(float x, float dx, Rect r) => XToTime(x, r) - XToTime(x - dx, r);
        float ValueToY(float v, Rect r) => r.y + r.height - (v - rt.yMin) / (rt.yMax - rt.yMin) * r.height;

        ZUIEnvelopeEditState State(int i)
        {
            if (rt.anchorsLocked && points.Count > 1 && (i == 0 || i == points.Count - 1)) return ZUIEnvelopeEditState.NotEditable;
            if (configuration.pointState != null) return configuration.pointState(i);
            return points[i].editState;
        }
        static bool CanMoveX(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.XEditable;
        static bool CanMoveY(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.YEditable;
        static bool CanRemove(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable;

        float MaxHandleRadius => Presentation.MaxHandleRadius;

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
            int best = -1; float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                var h = Presentation.GetHandle(State(i));
                float distance = Vector2.Distance(m, new Vector2(TimeToX(points[i].time, r), ValueToY(points[i].value, r)));
                if (distance > h.radius + Presentation.hitRadiusExtra) continue;
                if (!configuration.nearestPointHit) return i;
                if (distance <= bestDistance) { best = i; bestDistance = distance; }
            }
            return best;
        }

        int HitLine(Rect r, Vector2 m)
        {
            if (points.Count < 2) return -1;
            if (configuration.geometricSegmentHit)
            {
                for (int i = 1; i < points.Count; i++)
                {
                    var a = points[i - 1]; var b = points[i];
                    Vector2 previous = new Vector2(TimeToX(a.time, r), ValueToY(a.value, r));
                    for (int s = 1; s <= 16; s++)
                    {
                        float f = s / 16f;
                        var current = new Vector2(TimeToX(Mathf.Lerp(a.time, b.time, f), r), ValueToY(ZUIEnvelopeEvaluator.Bend(a.value, b.value, f, b.exponent), r));
                        Vector2 d = current - previous;
                        float tOnLine = d.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(m - previous, d) / d.sqrMagnitude) : 0f;
                        if (Vector2.Distance(m, previous + d * tOnLine) <= configuration.lineHitDistance) return i;
                        previous = current;
                    }
                }
                return -1;
            }
            float t = XToTime(m.x, r);
            if (t < points[0].time || t > points[points.Count - 1].time) return -1;
            if (Mathf.Abs(m.y - ValueToY(Evaluate(points, t, rt.yMax), r)) > configuration.lineHitDistance) return -1;
            for (int i = 1; i < points.Count; i++) if (points[i].time >= t) return i;
            return -1;
        }

        void UpdateHover(Vector2 m)
        {
            _hoverPoint = _hoverLine = -1;
            var r = Plot;
            float slop = MaxHandleRadius + Presentation.hitRadiusExtra;
            if (!new Rect(r.x - slop, r.y - slop, r.width + slop * 2f, r.height + slop * 2f).Contains(m)) return;
            int p = HitPoint(r, m);
            if (p >= 0) { if (State(p) != ZUIEnvelopeEditState.NotEditable) _hoverPoint = p; return; }
            if (!r.Contains(m)) return;
            int l = HitLine(r, m);
            if (l > 0) _hoverLine = l;
        }

        // ─────────────────────────── input (host-callable) ───────────────────────────

        /// <summary>
        /// Whether an editable point is under <paramref name="m"/> (local position). A host that overlaps this envelope with
        /// controls of its own (a waveform's trim handles sit exactly on the first and last points) asks this first, so a press
        /// on a point reaches the point.
        /// </summary>
        public bool IsOverPoint(Vector2 m)
        {
            Prepare();
            if (points == null || rt == null || def == null || !rt.editable || backdrop || !showHandles) return false;
            var r = Plot;
            if (!ValidPlot(r)) return false;
            int hit = HitPoint(r, m);
            return hit >= 0 && State(hit) != ZUIEnvelopeEditState.NotEditable;
        }

        /// <summary>Returns true when the envelope consumed the press; overlapping hosts then capture the pointer.</summary>
        public bool PointerDown(Vector2 m, int button, int clickCount, bool shift)
        {
            Prepare();
            if (!CanEdit || !ValidPlot(Plot) || !Finite(m)) return false;
            _shift = shift; _pointer = m;
            UpdateHover(m);
            var r = Plot;
            float slop = MaxHandleRadius + Presentation.hitRadiusExtra;
            if (!new Rect(r.x - slop, r.y - slop, r.width + slop * 2f, r.height + slop * 2f).Contains(m)) return false;
            int hit = HitPoint(r, m);
            if (button == 1 && shift && rt.allowExponentEdit)
            {
                int segment = hit > 0 ? hit : HitLine(r, m);
                if (segment > 0)
                {
                    BeginGesture(); _dragExponent = segment; _pressed = true; Focus(); Repaint(); return true;
                }
            }
            if (hit >= 0)
            {
                if (State(hit) == ZUIEnvelopeEditState.NotEditable) return false;
                if (button == 1 && !shift)
                {
                    if (onPointContext != null) { onPointContext(hit, this.LocalToWorld(PointToLocal(points[hit].time, points[hit].value))); return true; }
                    if (configuration.rightClickRemovesPoint) return RemovePoint(hit);
                    return false;
                }
                if (button != 0) return false;
                if (clickCount == 2) return RemovePoint(hit);
                BeginDrag(hit); Focus(); Repaint(); return true;
            }
            if (!r.Contains(m)) return false;
            int line = HitLine(r, m);
            if (line > 0 && shift && button == 0 && rt.allowSegmentDrag)
            {
                BeginGesture(); _dragLine = line; _selected.Clear(); SelectionChanged();
                _pressed = true; Focus(); Repaint(); return true;
            }
            bool onFlatLine = points.Count == 1 && Mathf.Abs(m.y - ValueToY(points[0].value, r)) <= configuration.lineHitDistance;
            if (pointsAndLineOnly && line <= 0 && !onFlatLine) return false;
            if (button == 0 && !shift && rt.allowAddPoints && configuration.addOnLinePress && (line > 0 || onFlatLine))
            {
                InsertAt(m, true, true); return true;
            }
            if (button == 0 && clickCount == 2 && rt.allowAddPoints)
            {
                InsertAt(m, false, configuration.dragAfterEmptyInsert); return true;
            }
            if (button == 0 && configuration.moveSelectionFromEmpty && _selected.Count > 1)
            {
                _pendingMultiDrag = true; _pressed = true; Focus(); Repaint(); return true;
            }
            if (button == 0 && rt.allowBoxSelect)
            {
                _boxSelecting = true; _boxStart = m; _selected.Clear(); SelectionChanged();
                _pressed = true; Focus(); Repaint(); return true;
            }
            return false;
        }

        bool CanEdit => points != null && rt != null && def != null && rt.editable && enabledInHierarchy && !backdrop;
        static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
        float Coordinate(float v) => configuration.roundCoordinates ? (float)System.Math.Round(v, 5) : v;

        public Vector2 PointToLocal(float time, float value)
        {
            Prepare(); var r = Plot;
            return ValidPlot(r) ? new Vector2(TimeToX(time, r), ValueToY(value, r)) : Vector2.zero;
        }
        public int FindPointIndexNear(Vector2 local) { Prepare(); return points != null && ValidPlot(Plot) ? HitPoint(Plot, local) : -1; }
        /// <summary>The LEFT endpoint index of the hit segment, or -1.</summary>
        public int FindSegmentIndexNear(Vector2 local) { Prepare(); int i = points != null && ValidPlot(Plot) ? HitLine(Plot, local) : -1; return i > 0 ? i - 1 : -1; }

        int InsertAt(Vector2 local, bool onLine, bool drag)
        {
            Prepare();
            if (!CanEdit || !rt.allowAddPoints || !ValidPlot(Plot) || !Finite(local)) return -1;
            var r = Plot;
            float t = Coordinate(Mathf.Clamp(XToTime(local.x, r), rt.xMin, rt.xMax));
            float v = onLine ? Evaluate(points, t, rt.yMax) : rt.yMin + (rt.yMax - rt.yMin) * (1f - (local.y - r.y) / r.height);
            v = Coordinate(Mathf.Clamp(v, rt.yMin, rt.yMax));
            BeginGesture();
            int index = Insert(new ZUIEnvelopePoint(t, v, 1f));
            _selected.Clear(); _selected.Add(index); SelectionChanged();
            _dragPoint = drag ? index : -1; _pressed = drag;
            rt.onMutated?.Invoke();
            if (!drag) _gestureRecorded = false;
            Focus(); Repaint(); return index;
        }
        public int InsertPoint(Vector2 local) => InsertAt(local, false, false);

        public bool RemovePoint(int index)
        {
            Prepare();
            if (!CanEdit || !rt.allowRemovePoints || index < 0 || index >= points.Count
                || points.Count <= Mathf.Max(0, configuration.minimumPoints) || !CanRemove(State(index))) return false;
            BeginGesture(); points.RemoveAt(index);
            _selected.Clear(); _hoverPoint = _hoverLine = -1; SelectionChanged();
            rt.onMutated?.Invoke(); _gestureRecorded = false; Repaint(); return true;
        }

        public void SelectInBox(Vector2 fromLocal, Vector2 toLocal)
        {
            Prepare();
            if (points == null || rt == null || !ValidPlot(Plot)) return;
            _boxStart = fromLocal; BoxUpdate(Plot, toLocal); Repaint();
        }

        public void BeginDrag(int index)
        {
            Prepare();
            if (!CanEdit || index < 0 || index >= points.Count || State(index) == ZUIEnvelopeEditState.NotEditable) return;
            BeginGesture();
            _pointer = PointToLocal(points[index].time, points[index].value);
            if (_selected.Count > 1 && _selected.Contains(index)) { _multiDrag = true; _dragPoint = -1; }
            else { _dragPoint = index; _selected.Clear(); _selected.Add(index); SelectionChanged(); }
            _pressed = true;
        }
        public void DragToLocal(Vector2 local)
        {
            if (!CanEdit || !ValidPlot(Plot)) return;
            Vector2 delta = local - _pointer;
            if (!Finite(_pointer)) delta = Vector2.zero;
            PointerMove(local, delta, _shift, 1);
        }
        public void EndDrag() => PointerUp();

        /// <summary>Moves the captured gesture or refreshes hover; hosts may forward their own pointer stream.</summary>
        public bool PointerMove(Vector2 m, Vector2 delta, bool shift, int pressedButtons)
        {
            Prepare();
            if (points == null || rt == null || def == null || !ValidPlot(Plot) || !Finite(m) || !Finite(delta)) return false;
            _shift = shift; _pointer = m;
            var r = Plot;
            bool used = false;
            if (_pressed && CanEdit)
            {
                float xRange = rt.xMax - rt.xMin, yRange = rt.yMax - rt.yMin;
                if (_pendingMultiDrag && delta != Vector2.zero)
                {
                    _pendingMultiDrag = false; _multiDrag = true; BeginGesture();
                }
                if (_dragPoint >= 0 && _dragPoint < points.Count)
                {
                    var p = points[_dragPoint]; var es = State(_dragPoint);
                    float t = Coordinate(Mathf.Clamp(XToTime(m.x, r), rt.xMin, rt.xMax));
                    float v = Coordinate(Mathf.Clamp(rt.yMin + yRange * (1f - (m.y - r.y) / r.height), rt.yMin, rt.yMax));
                    if (_dragPoint > 0) t = Mathf.Max(t, points[_dragPoint - 1].time);
                    if (_dragPoint < points.Count - 1) t = Mathf.Min(t, points[_dragPoint + 1].time);
                    if (CanMoveX(es)) p.time = t;
                    if (CanMoveY(es)) p.value = v;
                    rt.onDragUpdated?.Invoke(); used = true;
                }
                else if (_dragLine > 0 && _dragLine < points.Count)
                {
                    if (!shift && configuration.requireShiftDuringDrag) _dragLine = -1;
                    else { MoveSegment(_dragLine, TimeDelta(m.x, delta.x, r), yRange, r, delta); rt.onDragUpdated?.Invoke(); used = true; }
                }
                else if (_dragExponent > 0 && _dragExponent < points.Count)
                {
                    if (!shift && configuration.requireShiftDuringDrag) _dragExponent = -1;
                    else
                    {
                        var p = points[_dragExponent]; var prev = points[_dragExponent - 1];
                        float dy = p.value < prev.value ? -delta.y : delta.y;
                        p.exponent = configuration.usePixelBend ? Mathf.Clamp(p.exponent * Mathf.Pow(1.02f, dy), 0.05f, 20f) : BendStep(p.exponent, dy / r.height);
                        rt.onDragUpdated?.Invoke(); used = true;
                    }
                }
                else if (_boxSelecting) { BoxUpdate(r, m); used = true; }
                else if (_multiDrag && _selected.Count > 1 && (pressedButtons & 1) != 0)
                {
                    MoveSelection(TimeDelta(m.x, delta.x, r), -yRange * delta.y / r.height);
                    rt.onDragUpdated?.Invoke(); used = true;
                }
            }
            else UpdateHover(m);
            Repaint(); return used;
        }

        public void PointerUp()
        {
            if (_pendingMultiDrag) { _selected.Clear(); SelectionChanged(); }
            _pressed = _boxSelecting = _multiDrag = _pendingMultiDrag = _gestureRecorded = false;
            _dragPoint = _dragLine = _dragExponent = -1;
            Repaint();
        }

        public void PointerLeft()
        {
            if (_pressed) return;
            _hoverPoint = _hoverLine = -1; _pointer = new Vector2(float.NaN, float.NaN); Repaint();
        }

        public bool KeyDown(KeyCode key)
        {
            Prepare();
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift) { _shift = true; Repaint(); return false; }
            if (key != KeyCode.Delete || !CanEdit || !rt.allowRemovePoints || _selected.Count == 0) return false;
            _selected.Sort();
            bool changed = false;
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                if (points.Count <= Mathf.Max(0, configuration.minimumPoints)) break;
                int idx = _selected[i];
                if (idx < 0 || idx >= points.Count || !CanRemove(State(idx))) continue;
                BeginGesture(); points.RemoveAt(idx); changed = true;
            }
            _selected.Clear(); SelectionChanged();
            if (changed) rt.onMutated?.Invoke();
            _gestureRecorded = false; Repaint(); return true;
        }

        public void ResetState()
        {
            _dragPoint = _dragLine = _dragExponent = _hoverPoint = _hoverLine = -1;
            _boxSelecting = _pressed = _multiDrag = _pendingMultiDrag = _gestureRecorded = false;
            _selected.Clear(); SelectionChanged(); Repaint();
        }

        /// <summary>
        /// One step of a Shift-drag bend, in log space: <paramref name="step"/> (a share of the curve's height) multiplies the
        /// exponent by the same factor whichever way it goes, so dragging down by some distance gives exactly 1 / what dragging
        /// up by that distance gives. The segment is a + (b - a) t^e, and t^e and t^(1/e) are inverse functions, so the two
        /// shapes are mirror images across the straight segment (T-0513). The old additive step slowed towards nought and hit
        /// it, which made bending down look uneven. Shared with the Zounds chain-card curves.
        /// </summary>
        public static float BendStep(float exponent, float step)
        {
            float k = Mathf.Log(Mathf.Max(exponent, 1e-3f)) + step * 8f;
            return Mathf.Exp(Mathf.Clamp(k, Mathf.Log(1e-3f), Mathf.Log(1e3f)));
        }

        /// <summary>
        /// Moves the selected points together by (dt, dv), as a group: the group's time move is limited so that no point passes
        /// a neighbour that is not moving along with it (or leaves the curve), and its value move so that none leaves the range,
        /// so the selection keeps its shape (T-0512). A point that may only move one way (an end point) moves only that way.
        /// </summary>
        void MoveSelection(float dt, float dv)
        {
            bool Moves(int i) => _selected.Contains(i) && CanMoveX(State(i));
            float lo = float.NegativeInfinity, hi = float.PositiveInfinity, vlo = float.NegativeInfinity, vhi = float.PositiveInfinity;
            foreach (int i in _selected)
            {
                if (i < 0 || i >= points.Count) continue;
                var p = points[i]; var es = State(i);
                if (CanMoveX(es))
                {
                    float left = i > 0 && !Moves(i - 1) ? points[i - 1].time : rt.xMin;
                    float right = i < points.Count - 1 && !Moves(i + 1) ? points[i + 1].time : rt.xMax;
                    lo = Mathf.Max(lo, left - p.time); hi = Mathf.Min(hi, right - p.time);
                }
                if (CanMoveY(es)) { vlo = Mathf.Max(vlo, rt.yMin - p.value); vhi = Mathf.Min(vhi, rt.yMax - p.value); }
            }
            dt = Mathf.Clamp(dt, Mathf.Min(lo, 0f), Mathf.Max(hi, 0f));
            dv = Mathf.Clamp(dv, Mathf.Min(vlo, 0f), Mathf.Max(vhi, 0f));
            foreach (int i in _selected)
            {
                if (i < 0 || i >= points.Count) continue;
                var p = points[i]; var es = State(i);
                if (CanMoveX(es)) p.time += dt;
                if (CanMoveY(es)) p.value += dv;
            }
        }

        int Insert(ZUIEnvelopePoint p)
        {
            int idx = points.Count;
            for (int i = 0; i < points.Count; i++) if (p.time < points[i].time) { idx = i; break; }
            points.Insert(idx, p);
            return idx;
        }

        void MoveSegment(int endIdx, float timeDelta, float yRange, Rect r, Vector2 delta)
        {
            var a = points[endIdx - 1]; var b = points[endIdx];
            var aS = State(endIdx - 1); var bS = State(endIdx);
            float dt = configuration.segmentVerticalOnly ? 0f : timeDelta, dv = -yRange * delta.y / r.height;
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
            SelectionChanged();
        }

        // ─────────────────────────── drawing ───────────────────────────

        float StrokeWidth(float width) => configuration.logicalStrokeWidths ? width : Px(width);

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
            Prepare();
            if (points == null || rt == null || def == null) return;
            _selected.RemoveAll(i => i < 0 || i >= points.Count);
            var style = Presentation;
            var outer = contentRect;
            var p2 = ctx.painter2D;

            // Background (a flat colour; the Zounds overlay's is fully transparent) and border.
            var bg = style.background;
            if (bg.a > 0f) FillRect(p2, outer, bg);
            if (style.borderColor.a > 0f)
            {
                if (style.borderTop > 0f) FillRect(p2, new Rect(outer.x, outer.y, outer.width, style.borderTop), style.borderColor);
                if (style.borderBottom > 0f) FillRect(p2, new Rect(outer.x, outer.yMax - style.borderBottom, outer.width, style.borderBottom), style.borderColor);
                if (style.borderLeft > 0f) FillRect(p2, new Rect(outer.x, outer.y, style.borderLeft, outer.height), style.borderColor);
                if (style.borderRight > 0f) FillRect(p2, new Rect(outer.xMax - style.borderRight, outer.y, style.borderRight, outer.height), style.borderColor);
            }
            if (rt.xMax - rt.xMin <= 0f || rt.yMax - rt.yMin <= 0f) return;
            var r = Plot;
            if (!ValidPlot(r)) return;

            if (rt.showGrid && style.gridRows > 0)
            {
                for (int i = 1; i < style.gridRows; i++) FillRect(p2, new Rect(r.x, r.y + r.height * i / style.gridRows, r.width, style.gridThickness), style.gridColor);
            }
            DrawDecorations(ctx, p2, r, style);

            // Curve.
            if (points.Count > 0)
            {
                int n = Mathf.Max(2, (int)(r.width / (backdrop && backdropDotted ? 1f : 3f)));
                var lineColor = style.curveColor;
                if (backdrop && backdropTransparent) lineColor.a *= 0.5f;
                float lineW = !backdrop ? style.curveThickness : backdropWidthBonus < 0f ? style.curveThickness * 2f : style.curveThickness + backdropWidthBonus;
                p2.strokeColor = lineColor; p2.lineWidth = StrokeWidth(lineW);
                // A dotted backdrop: short pieces with gaps a little longer than the line is wide, so it reads as dots.
                bool broken = dashed || (backdrop && backdropDotted);
                float on = dashed ? 6f : Mathf.Max(1.5f, lineW * 0.6f), off = dashed ? 4f : Mathf.Max(3f, lineW + 2f);
                if (backdrop && backdropDotted) p2.lineCap = LineCap.Butt;
                p2.lineJoin = LineJoin.Round; if (!(backdrop && backdropDotted)) p2.lineCap = LineCap.Round;
                p2.BeginPath();
                Vector2 prevPt = default; float dashRun = 0f; bool dashOn = true;
                for (int i = 0; i <= n; i++)
                {
                    float t = rt.xMin + (rt.xMax - rt.xMin) * i / n;
                    var pt = new Vector2(TimeToX(t, r), ValueToY(Evaluate(points, t, rt.yMax), r));
                    if (i == 0) p2.MoveTo(pt);
                    else if (broken)
                    {
                        // Dashed: 6 px drawn, 4 px gap; dotted: short pieces, along the curve.
                        dashRun += Vector2.Distance(prevPt, pt);
                        if (dashOn) p2.LineTo(pt); else p2.MoveTo(pt);
                        if (dashRun >= (dashOn ? on : off)) { dashRun = 0f; dashOn = !dashOn; }
                    }
                    else p2.LineTo(pt);
                    prevPt = pt;
                }
                p2.Stroke();
                if (_hoverLine > 0 && _hoverLine < points.Count && _shift && !backdrop)
                {
                    var a = points[_hoverLine - 1]; var b = points[_hoverLine];
                    int segPx = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(TimeToX(b.time, r) - TimeToX(a.time, r)) / 2f));
                    Color.RGBToHSV(style.curveColor, out float h, out float s, out float vC);
                    p2.strokeColor = Color.HSVToRGB(h, Mathf.Clamp01(s * style.hoverSaturationScale), Mathf.Min(vC * style.hoverValueScale, 1f));
                    p2.lineWidth = StrokeWidth(style.curveHoverThickness);
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
                    Dotted(p2, dotted, Color.Lerp(style.curveColor, Color.white, style.dottedWhiteMix), StrokeWidth(Mathf.Max(style.curveThickness, style.dottedMinWidth)));
                }
            }

            // A backdrop or an unselected curve is just its line: no ellipses, points, ghost, selection box.
            if (backdrop || !showHandles) return;

            // Random points' ellipses (T-0483): where each play may move the point, faint fill and a thin outline.
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                if (pt.randomX <= 0f && pt.randomY <= 0f) continue;
                var c = new Vector2(TimeToX(pt.time, r), ValueToY(pt.value, r));
                float ex = Mathf.Abs(TimeToX(pt.time + pt.randomX, r) - c.x), ey = pt.randomY / (rt.yMax - rt.yMin) * r.height;
                Ellipse(p2, c, Mathf.Max(ex, 1f), Mathf.Max(ey, 1f), new Color(style.curveColor.r, style.curveColor.g, style.curveColor.b, style.uncertaintyFillAlpha),
                        new Color(style.curveColor.r, style.curveColor.g, style.curveColor.b, style.uncertaintyStrokeAlpha), StrokeWidth(style.uncertaintyStrokeThickness));
            }

            // Points.
            var sel = style.selectedColor;
            for (int i = 0; i < points.Count; i++)
            {
                var es = State(i); var h = style.GetHandle(es);
                var c = new Vector2(TimeToX(points[i].time, r), ValueToY(points[i].value, r));
                bool hover = _hoverPoint == i || _dragPoint == i;
                bool isSel = _selected.Contains(i) || _dragPoint == i;
                float vr = hover && es != ZUIEnvelopeEditState.NotEditable ? h.hoverRadius : h.radius;
                var fill = hover ? h.hoverFillColor : h.fillColor;
                if (isSel) fill = sel;
                if (configuration.yColorFor != null)
                {
                    var tint = configuration.yColorFor(points[i].value);
                    fill = new Color(tint.r, tint.g, tint.b, 1f);
                }
                if (!enabledInHierarchy) fill.a *= 0.5f;
                if (h.borderWidth > 0f) Disc(p2, c, vr + h.borderWidth, h.borderColor);
                Disc(p2, c, vr, fill);
                if (configuration.yColorFor != null && (hover || isSel))
                {
                    p2.strokeColor = isSel ? sel : h.hoverFillColor; p2.lineWidth = StrokeWidth(style.selectedStrokeThickness);
                    p2.BeginPath(); p2.Arc(c, vr + 0.5f, 0f, 360f); p2.Stroke();
                }
                if (rt.showValueLabels)
                {
                    string text = Fmt(points[i].value);
                    float x = c.x + 7f;
                    if (x + text.Length * 6.5f > r.xMax) x = c.x - 8f - text.Length * 6.5f;
                    ctx.DrawText(text, new Vector2(x, c.y - 15f), style.valueLabelSize, style.valueLabelColor);
                }
                if (hover && !isSel && es != ZUIEnvelopeEditState.NotEditable)
                {
                    p2.strokeColor = sel; p2.lineWidth = StrokeWidth(style.selectedStrokeThickness);
                    p2.BeginPath(); p2.Arc(c, vr + 1.5f, 0f, 360f); p2.Stroke();
                }
            }

            // Add-point ghost.
            if (rt.allowAddPoints && !_shift && _hoverLine > 0 && _dragPoint < 0 && _dragLine < 0 && !float.IsNaN(_pointer.x))
            {
                float t = Mathf.Clamp(XToTime(_pointer.x, r), rt.xMin, rt.xMax);
                var hd = style.GetHandle(ZUIEnvelopeEditState.Editable);
                var f = hd.fillColor; f.a *= style.ghostOpacity;
                Disc(p2, new Vector2(TimeToX(t, r), ValueToY(Evaluate(points, t, rt.yMax), r)), hd.radius, f);
            }

            // Selection box.
            if (_boxSelecting && !float.IsNaN(_pointer.x))
            {
                var cur = new Vector2(Mathf.Clamp(_pointer.x, r.x, r.xMax), Mathf.Clamp(_pointer.y, r.y, r.yMax));
                var box = Rect.MinMaxRect(Mathf.Min(_boxStart.x, cur.x), Mathf.Min(_boxStart.y, cur.y), Mathf.Max(_boxStart.x, cur.x), Mathf.Max(_boxStart.y, cur.y));
                FillRect(p2, box, new Color(1f, 1f, 1f, style.selectionBoxFillAlpha));
                p2.strokeColor = sel; p2.lineWidth = StrokeWidth(style.selectedStrokeThickness);
                p2.BeginPath(); p2.MoveTo(box.min); p2.LineTo(new Vector2(box.xMax, box.yMin)); p2.LineTo(box.max); p2.LineTo(new Vector2(box.xMin, box.yMax)); p2.ClosePath(); p2.Stroke();
            }
        }

        void DrawDecorations(MeshGenerationContext ctx, Painter2D painter, Rect plot, ZuiEnvelopePresentation.Values style)
        {
            if (configuration.yColorFor != null)
            {
                float x0 = contentRect.x + 1f, x1 = Mathf.Max(x0 + 2f, plot.x - 1f);
                int slices = Mathf.Clamp(Mathf.RoundToInt(plot.height / 2f), 8, 64);
                for (int s = 0; s < slices; s++)
                {
                    float f0 = s / (float)slices, f1 = (s + 1) / (float)slices;
                    var color = configuration.yColorFor(Mathf.Lerp(rt.yMax, rt.yMin, (f0 + f1) * 0.5f));
                    FillRect(painter, new Rect(x0, plot.y + plot.height * f0, x1 - x0, plot.height * (f1 - f0)), new Color(color.r, color.g, color.b, 1f));
                }
                painter.strokeColor = style.legendBorderColor; painter.lineWidth = 1f;
                painter.BeginPath(); painter.MoveTo(new Vector2(x0, plot.y)); painter.LineTo(new Vector2(x1, plot.y));
                painter.LineTo(new Vector2(x1, plot.yMax)); painter.LineTo(new Vector2(x0, plot.yMax)); painter.ClosePath(); painter.Stroke();
            }
            if (configuration.showFrameLines && configuration.frameStarts01 != null && configuration.frameStarts01.Length > 0)
            {
                // Uneven frames: a line at every frame start and one at the end; labels thin out like the even case.
                var starts = configuration.frameStarts01;
                float lastLabelX = float.NegativeInfinity;
                painter.lineWidth = 1f;
                for (int i = 0; i <= starts.Length; i++)
                {
                    float f = i < starts.Length ? Mathf.Clamp01(starts[i]) : 1f;
                    float x = plot.x + plot.width * f;
                    painter.strokeColor = i == 0 || i == starts.Length ? style.frameEdgeColor : style.frameLineColor;
                    painter.BeginPath(); painter.MoveTo(new Vector2(x, plot.y)); painter.LineTo(new Vector2(x, plot.yMax)); painter.Stroke();
                    if (i == starts.Length || x - lastLabelX < style.markerLabelSpacing) continue;
                    string text = (i + 1).ToString(); float w = text.Length * style.markerLabelSize * 0.61f + 3f;
                    ctx.DrawText(text, new Vector2(x + w > plot.xMax ? x - w : x + 2f, plot.y + 1f), style.markerLabelSize, style.frameLabelColor);
                    lastLabelX = x;
                }
            }
            else if (configuration.showFrameLines && configuration.frameCount > 1)
            {
                int count = configuration.frameCount;
                float spacing = plot.width / (count - 1);
                int labelStep = Mathf.Max(1, Mathf.CeilToInt(style.markerLabelSpacing / Mathf.Max(1f, spacing)));
                painter.lineWidth = 1f;
                for (int i = 0; i < count; i++)
                {
                    float x = plot.x + plot.width * i / (count - 1);
                    painter.strokeColor = i == 0 || i == count - 1 ? style.frameEdgeColor : style.frameLineColor;
                    painter.BeginPath(); painter.MoveTo(new Vector2(x, plot.y)); painter.LineTo(new Vector2(x, plot.yMax)); painter.Stroke();
                }
                for (int i = 0; i < count; i += labelStep)
                {
                    float x = plot.x + plot.width * i / (count - 1);
                    string text = i.ToString(); float w = text.Length * style.markerLabelSize * 0.61f + 3f;
                    ctx.DrawText(text, new Vector2(x + w > plot.xMax ? x - w : x + 2f, plot.y + 1f), style.markerLabelSize, style.frameLabelColor);
                }
            }
            if (!float.IsNaN(configuration.playhead01))
            {
                float x = plot.x + plot.width * Mathf.Clamp01(configuration.playhead01);
                painter.strokeColor = new Color(1f, 1f, 1f, 0.85f); painter.lineWidth = 1.5f;
                painter.BeginPath(); painter.MoveTo(new Vector2(x, plot.y)); painter.LineTo(new Vector2(x, plot.yMax)); painter.Stroke();
            }
            if (!string.IsNullOrEmpty(configuration.xAxisLabel))
                ctx.DrawText(configuration.xAxisLabel, new Vector2(plot.center.x - configuration.xAxisLabel.Length * style.axisLabelSize * 0.305f, plot.yMax - style.axisLabelSize - 2f), style.axisLabelSize, style.axisLabelColor);
            if (!string.IsNullOrEmpty(configuration.yAxisLabel))
                ctx.DrawText(configuration.yAxisLabel, new Vector2(plot.x + 3f, plot.center.y - style.axisLabelSize * 0.67f), style.axisLabelSize, style.axisLabelColor);
        }

        /// <summary>A positioned value tag beside the hovered or selected point.</summary>
        void UpdateTag()
        {
            if (!configuration.showReadout || points == null || rt == null || def == null || backdrop || !showHandles) { _tag.style.display = DisplayStyle.None; return; }
            int i = _dragPoint >= 0 ? _dragPoint : _hoverPoint >= 0 ? _hoverPoint : _selected.Count > 0 ? _selected[_selected.Count - 1] : -1;
            if (i < 0 || i >= points.Count || contentRect.width <= 0f) { _tag.style.display = DisplayStyle.None; return; }
            var r = Plot;
            var es = State(i); var h = Presentation.GetHandle(es);
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
