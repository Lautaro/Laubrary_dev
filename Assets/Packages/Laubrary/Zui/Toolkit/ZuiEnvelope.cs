// ZuiEnvelope — the UI Toolkit counterpart of ZUI.Envelope: a DAW-style multi-point envelope
// editor over the SAME runtime data the IMGUI control edits (List<ZUIEnvelopePoint>, sampled by
// ZUIEnvelopeEvaluator). Drawing is Painter2D (the Trial-2 pattern), interaction is pointer-capture
// based and mirrors the IMGUI control's model (2026-07-23 parity pass, per user direction):
//   • DOUBLE-click empty space / the line  → insert a point there (drag continues immediately);
//   • single click + drag on empty space   → box-select marquee;
//   • drag a point                          → move it (or the whole selection if it's selected);
//   • double-click a point / right-click    → remove it;
//   • Shift+RIGHT-drag on a segment         → bend that segment's exponent (same gesture as IMGUI);
//   • Shift+LEFT-drag on a segment          → drag both endpoint values vertically;
//   • Delete key                            → remove every selected (removable) point.
// Still NOT ported (tracked in CHANGELOG/zui.md): loop/trim markers.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// Per-instance envelope configuration — the UI Toolkit analog of ZUIEnvelopeRuntime.
    public class ZuiEnvelopeOptions
    {
        public float xMin = 0f, xMax = 1f;
        public float yMin = 0f, yMax = 1f;
        /// Force first + last points to NotEditable regardless of their own editState.
        public bool anchorsLocked = false;
        public bool editable = true;
        public bool allowAddPoints = true;
        public bool allowRemovePoints = true;
        public bool allowExponentEdit = true;
        public bool allowBoxSelect = true;
        public bool allowSegmentDrag = true;
        public bool showGrid = true;
        public int gridRows = 4;
        public int minPoints = 2;
        /// Draw each point's value as small text beside it.
        public bool showValueLabels = false;
        public Color curveColor = new Color(0.3f, 0.8f, 1f);
        /// Vertical frame-boundary markers. When showFrameLines is on and frameCount > 1, draw a faint
        /// vertical line at each animation frame's position across the envelope's life span, labelled with
        /// the frame index — so an author can read exactly which frame a part of the curve lands on. The
        /// LINES are drawn per-frame; the NUMBERS auto-thin (every Nth frame) when they'd otherwise overlap,
        /// so a long animation doesn't become an unreadable smear of digits.
        public bool showFrameLines = false;
        public int frameCount = 0;
    }

    public class ZuiEnvelope : VisualElement
    {
        // Visual constants — mirror ZUIEnvelopeDef's shipped defaults so the two halves look alike.
        const float Pad = 6f;
        const float HandleRadius = 3f;
        const float HandleHoverRadius = 5f;
        const float HitRadius = 8f;
        const float SegmentHitDistance = 6f;
        static readonly Color HandleFill = new Color(0.7f, 0.7f, 0.7f);
        static readonly Color HandleHoverFill = new Color(1f, 0.9f, 0.5f);
        static readonly Color HandleLockedFill = new Color(0.28f, 0.28f, 0.28f);
        static readonly Color SelectedFill = new Color(1f, 0.8f, 0.2f);   // ZUIEnvelopeDef.selectedColor
        static readonly Color GridColor = new Color(0.5f, 0.5f, 0.5f, 0.18f);
        static readonly Color BoxFill = new Color(0.3f, 0.7f, 1f, 0.12f);
        static readonly Color BoxLine = new Color(0.4f, 0.8f, 1f, 0.9f);
        static readonly Color FrameLineColor = new Color(0.55f, 0.6f, 0.72f, 0.15f);   // vertical per-frame markers
        static readonly Color FrameEdgeColor = new Color(0.55f, 0.6f, 0.72f, 0.30f);   // first/last frame — bookends
        static readonly Color FrameLabelColor = new Color(0.75f, 0.8f, 0.92f, 0.6f);   // the frame-index numbers

        readonly List<ZUIEnvelopePoint> _points;   // caller-owned; mutated in place
        readonly ZuiEnvelopeOptions _opt;

        readonly List<int> _selected = new();
        int _dragIndex = -1;
        int _dragSegment = -1;      // exponent-bend drag: index of the segment's LEFT point
        int _dragLine = -1;         // segment value drag: index of the segment's LEFT point
        int _hoverIndex = -1;
        int _hoverSegment = -1;
        bool _boxSelecting;
        Vector2 _boxStart, _boxCur;
        bool _gestureRecorded;      // OnBeforeMutate fired for the current gesture

        /// Called immediately BEFORE the first mutation of a gesture (drag start, insert, remove) —
        /// the hook for Undo.RecordObject on the owning asset.
        public Action OnBeforeMutate;
        /// Called after every mutation (each drag update, insert, remove).
        public Action OnChanged;
        /// Called when the selection changes (box select, click select, deletions).
        public Action OnSelectionChanged;

        /// The currently selected point indices (sorted ascending, read-only).
        public IReadOnlyList<int> Selected => _selected;

        public ZuiEnvelope(List<ZUIEnvelopePoint> points, ZuiEnvelopeOptions options,
            string tooltip, float width = 220f, float height = 80f)
        {
            _points = points ?? throw new ArgumentNullException(nameof(points));
            _opt = options ?? new ZuiEnvelopeOptions();

            AddToClassList("zui-envelope");
            style.width = width;
            style.height = height;
            this.tooltip = tooltip;
            focusable = true;   // Delete-key support

            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_dragIndex >= 0 || _dragSegment >= 0 || _dragLine >= 0 || _boxSelecting) return;
                _hoverIndex = -1; _hoverSegment = -1;
                MarkDirtyRepaint();
            });
        }

        /// Repaint after an external change to the point list (undo, another control editing it).
        public void Refresh()
        {
            _selected.RemoveAll(i => i >= _points.Count);
            MarkDirtyRepaint();
        }

        void BeginGesture()
        {
            if (_gestureRecorded) return;
            _gestureRecorded = true;
            OnBeforeMutate?.Invoke();
        }

        void EndGesture() => _gestureRecorded = false;

        void SetSelection(IEnumerable<int> indices)
        {
            _selected.Clear();
            if (indices != null) _selected.AddRange(indices);
            _selected.Sort();
            OnSelectionChanged?.Invoke();
        }

        ZUIEnvelopeEditState StateOf(int i)
        {
            if (_opt.anchorsLocked && (i == 0 || i == _points.Count - 1)) return ZUIEnvelopeEditState.NotEditable;
            return _points[i].editState;
        }

        static bool CanRemove(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable;

        // ── coordinate mapping (inset by Pad so edge handles never clip the border) ──
        /// True once the panel has laid this element out. Before that `contentRect` is NaN, and every
        /// mapping below would produce NaN — which, if a gesture somehow ran first, would write a NaN
        /// time/value straight into the caller's data. Guarded rather than assumed.
        public bool HasLayout => !float.IsNaN(contentRect.width) && contentRect.width > 0f
                              && !float.IsNaN(contentRect.height) && contentRect.height > 0f;

        Rect Plot()
        {
            var r = contentRect;
            if (!HasLayout) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(r.x + Pad, r.y + Pad, Mathf.Max(1f, r.width - Pad * 2f), Mathf.Max(1f, r.height - Pad * 2f));
        }

        Vector2 ToLocal(float time, float value)
        {
            var p = Plot();
            float tx = Mathf.InverseLerp(_opt.xMin, _opt.xMax, time);
            float ty = Mathf.InverseLerp(_opt.yMin, _opt.yMax, value);
            return new Vector2(p.x + tx * p.width, p.yMax - ty * p.height);
        }

        (float time, float value) ToDomain(Vector2 local)
        {
            var p = Plot();
            float tx = Mathf.Clamp01((local.x - p.x) / p.width);
            float ty = Mathf.Clamp01((p.yMax - local.y) / p.height);
            return ((float)Math.Round(Mathf.Lerp(_opt.xMin, _opt.xMax, tx), 5),
                    (float)Math.Round(Mathf.Lerp(_opt.yMin, _opt.yMax, ty), 5));
        }

        // ── painting ────────────────────────────────────────────────────────────────
        void Paint(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (!(r.width > 8f) || !(r.height > 8f)) return;
            var painter = mgc.painter2D;
            var plot = Plot();

            if (_opt.showGrid && _opt.gridRows > 0)
            {
                painter.strokeColor = GridColor;
                painter.lineWidth = 1f;
                for (int i = 1; i < _opt.gridRows; i++)
                {
                    float y = plot.y + plot.height * i / _opt.gridRows;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(plot.x, y));
                    painter.LineTo(new Vector2(plot.xMax, y));
                    painter.Stroke();
                }
            }

            DrawFrameMarkers(mgc, painter, plot);

            if (_points.Count >= 1)
            {
                painter.strokeColor = _opt.curveColor;
                painter.lineWidth = _hoverSegment >= 0 || _dragSegment >= 0 || _dragLine >= 0 ? 2.5f : 1.5f;
                painter.lineJoin = LineJoin.Round;
                painter.BeginPath();
                painter.MoveTo(ToLocal(_opt.xMin, _points[0].value));
                painter.LineTo(ToLocal(_points[0].time, _points[0].value));
                for (int i = 1; i < _points.Count; i++)
                {
                    var a = _points[i - 1];
                    var b = _points[i];
                    const int Samples = 24;
                    for (int s = 1; s <= Samples; s++)
                    {
                        float t = s / (float)Samples;
                        float v = ZUIEnvelopeEvaluator.Bend(a.value, b.value, t, b.exponent);
                        painter.LineTo(ToLocal(Mathf.Lerp(a.time, b.time, t), v));
                    }
                }
                painter.LineTo(ToLocal(_opt.xMax, _points[_points.Count - 1].value));
                painter.Stroke();
            }

            for (int i = 0; i < _points.Count; i++)
            {
                bool locked = StateOf(i) == ZUIEnvelopeEditState.NotEditable;
                bool hot = i == _hoverIndex || i == _dragIndex;
                bool sel = _selected.Contains(i);
                painter.fillColor = locked ? HandleLockedFill : sel ? SelectedFill : hot ? HandleHoverFill : HandleFill;
                Vector2 pt = ToLocal(_points[i].time, _points[i].value);
                painter.BeginPath();
                painter.Arc(pt, locked ? HandleRadius * 0.7f : hot || sel ? HandleHoverRadius : HandleRadius, 0f, 360f);
                painter.Fill();

                if (_opt.showValueLabels)
                {
                    // Beside the dot (mockup): to its right normally, flipped to the left near the
                    // right edge so the text never clips out of the plot.
                    string txt = _points[i].value.ToString("0.##");
                    bool rightHalf = pt.x > plot.x + plot.width * 0.78f;
                    var tp = rightHalf ? new Vector2(pt.x - 8f - txt.Length * 6.5f, pt.y - 15f)
                                       : new Vector2(pt.x + 7f, pt.y - 15f);
                    mgc.DrawText(txt, tp, 11f, new Color(1f, 1f, 1f, 0.9f));
                }
            }

            if (_boxSelecting)
            {
                var box = Rect.MinMaxRect(Mathf.Min(_boxStart.x, _boxCur.x), Mathf.Min(_boxStart.y, _boxCur.y),
                                          Mathf.Max(_boxStart.x, _boxCur.x), Mathf.Max(_boxStart.y, _boxCur.y));
                painter.fillColor = BoxFill;
                painter.BeginPath();
                painter.MoveTo(new Vector2(box.x, box.y));
                painter.LineTo(new Vector2(box.xMax, box.y));
                painter.LineTo(new Vector2(box.xMax, box.yMax));
                painter.LineTo(new Vector2(box.x, box.yMax));
                painter.ClosePath();
                painter.Fill();
                painter.strokeColor = BoxLine;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(box.x, box.y));
                painter.LineTo(new Vector2(box.xMax, box.y));
                painter.LineTo(new Vector2(box.xMax, box.yMax));
                painter.LineTo(new Vector2(box.x, box.yMax));
                painter.ClosePath();
                painter.Stroke();
            }
        }

        // Vertical frame-boundary markers (opt-in via ZuiEnvelopeOptions.showFrameLines/frameCount). A line
        // per frame at f/(frameCount-1) of the life span; numbers thinned to every labelStep frames so they
        // never overlap on a long animation. x is value-independent (ToLocal derives it from time only).
        void DrawFrameMarkers(MeshGenerationContext mgc, Painter2D painter, Rect plot)
        {
            if (!_opt.showFrameLines || _opt.frameCount < 2) return;
            int fc = _opt.frameCount;
            float pxPerFrame = plot.width / (fc - 1);
            int labelStep = Mathf.Max(1, Mathf.CeilToInt(22f / Mathf.Max(1f, pxPerFrame)));

            painter.lineWidth = 1f;
            for (int f = 0; f < fc; f++)
            {
                float x = ToLocal(Mathf.Lerp(_opt.xMin, _opt.xMax, f / (float)(fc - 1)), _opt.yMin).x;
                painter.strokeColor = (f == 0 || f == fc - 1) ? FrameEdgeColor : FrameLineColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, plot.y));
                painter.LineTo(new Vector2(x, plot.yMax));
                painter.Stroke();
            }
            // Numbers second so they sit above the lines; near the right edge they flip to the left of the
            // line so the last one never clips out of the plot.
            for (int f = 0; f < fc; f += labelStep)
            {
                float x = ToLocal(Mathf.Lerp(_opt.xMin, _opt.xMax, f / (float)(fc - 1)), _opt.yMin).x;
                string txt = f.ToString();
                float labelW = txt.Length * 5.5f + 3f;
                float tx = x + labelW > plot.xMax ? x - labelW : x + 2f;
                mgc.DrawText(txt, new Vector2(tx, plot.y + 1f), 9f, FrameLabelColor);
            }
        }

        // ── hit testing ─────────────────────────────────────────────────────────────
        int FindPointNear(Vector2 local)
        {
            int best = -1; float bestD = HitRadius;
            for (int i = 0; i < _points.Count; i++)
            {
                float d = Vector2.Distance(ToLocal(_points[i].time, _points[i].value), local);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// Returns the LEFT point index of the segment whose drawn curve passes near `local`, or -1.
        int FindSegmentNear(Vector2 local)
        {
            for (int i = 0; i < _points.Count - 1; i++)
            {
                var a = _points[i];
                var b = _points[i + 1];
                const int Samples = 16;
                Vector2 prev = ToLocal(a.time, a.value);
                for (int s = 1; s <= Samples; s++)
                {
                    float t = s / (float)Samples;
                    Vector2 cur = ToLocal(Mathf.Lerp(a.time, b.time, t),
                        ZUIEnvelopeEvaluator.Bend(a.value, b.value, t, b.exponent));
                    if (DistToSegment(local, prev, cur) <= SegmentHitDistance) return i;
                    prev = cur;
                }
            }
            return -1;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + t * ab);
        }

        // ── interaction ─────────────────────────────────────────────────────────────
        void OnPointerDown(PointerDownEvent e)
        {
            if (!_opt.editable) return;
            Vector2 local = e.localPosition;
            Focus();

            if (e.button == 1)
            {
                int segHit = _opt.allowExponentEdit && e.shiftKey ? FindSegmentNear(local) : -1;
                if (segHit >= 0)
                {
                    // Shift+RMB drag on a segment bends its exponent — the IMGUI gesture, 1:1.
                    BeginGesture();
                    _dragSegment = segHit;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
                if (RemovePointAt(FindPointNear(local))) e.StopPropagation();
                return;
            }
            if (e.button != 0) return;

            int point = FindPointNear(local);
            if (point >= 0)
            {
                if (e.clickCount == 2)
                {
                    // Double-click a point removes it (IMGUI parity).
                    if (RemovePointAt(point)) e.StopPropagation();
                    return;
                }
                if (StateOf(point) == ZUIEnvelopeEditState.NotEditable) return;
                BeginGesture();
                _dragIndex = point;
                if (!_selected.Contains(point)) SetSelection(new[] { point });
                this.CapturePointer(e.pointerId);
                DragTo(local, initial: true);
                e.StopPropagation();
                return;
            }

            int segment = FindSegmentNear(local);
            if (segment >= 0 && e.shiftKey && _opt.allowSegmentDrag)
            {
                // Shift+LMB drag on a segment drags both endpoint values vertically (IMGUI parity).
                BeginGesture();
                _dragLine = segment;
                SetSelection(null);
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            if (e.clickCount == 2 && _opt.allowAddPoints)
            {
                // Double-click empty space (or the line) inserts a point there and starts dragging it.
                InsertPointAt(local);
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            if (_opt.allowBoxSelect)
            {
                BeginBoxSelect(local);
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            }
        }

        // ── gesture bodies (shared by the pointer handlers and the internal test hooks) ──
        int InsertPointAt(Vector2 local)
        {
            if (!HasLayout) return -1;
            BeginGesture();
            var (time, value) = ToDomain(local);
            int insert = _points.Count;
            for (int i = 0; i < _points.Count; i++)
                if (time < _points[i].time) { insert = i; break; }
            _points.Insert(insert, new ZUIEnvelopePoint(time, value));
            _dragIndex = insert;
            SetSelection(new[] { insert });
            MarkDirtyRepaint();
            OnChanged?.Invoke();
            return insert;
        }

        bool RemovePointAt(int index)
        {
            if (index < 0 || index >= _points.Count) return false;
            if (!_opt.allowRemovePoints || _points.Count <= _opt.minPoints) return false;
            if (!CanRemove(StateOf(index))) return false;
            BeginGesture();
            _points.RemoveAt(index);
            _hoverIndex = -1;
            SetSelection(null);
            MarkDirtyRepaint();
            OnChanged?.Invoke();
            EndGesture();
            return true;
        }

        void BeginBoxSelect(Vector2 local)
        {
            _boxSelecting = true;
            _boxStart = _boxCur = local;
            SetSelection(null);
            MarkDirtyRepaint();
        }

        void BoxSelectTo(Vector2 local)
        {
            _boxCur = local;
            var box = Rect.MinMaxRect(Mathf.Min(_boxStart.x, _boxCur.x), Mathf.Min(_boxStart.y, _boxCur.y),
                                      Mathf.Max(_boxStart.x, _boxCur.x), Mathf.Max(_boxStart.y, _boxCur.y));
            var inBox = new List<int>();
            for (int i = 0; i < _points.Count; i++)
                if (box.Contains(ToLocal(_points[i].time, _points[i].value))) inBox.Add(i);
            SetSelection(inBox);
            MarkDirtyRepaint();
        }

        // ── programmatic API ────────────────────────────────────────────────────────
        // The same private bodies the pointer handlers use, exposed so tooling and verification
        // scripts can drive the control without synthesising pointer events (a synthetic SendEvent
        // can't populate the OS-driven pointer-target state UI Toolkit's dispatcher expects).

        /// Element-local position of a point in (time, value) space.
        public Vector2 PointToLocal(float time, float value) => ToLocal(time, value);
        /// Index of the point within grab distance of an element-local position, or -1.
        public int FindPointIndexNear(Vector2 local) => FindPointNear(local);
        /// LEFT point index of the segment passing near an element-local position, or -1.
        public int FindSegmentIndexNear(Vector2 local) => FindSegmentNear(local);
        /// Insert a point at an element-local position; returns its new index.
        public int InsertPoint(Vector2 local) { int i = InsertPointAt(local); EndGesture(); return i; }
        /// Remove a point, honouring editState/minPoints. False when the point refuses removal.
        public bool RemovePoint(int index) => RemovePointAt(index);
        /// Replace the selection with every point inside the element-local rectangle.
        public void SelectInBox(Vector2 fromLocal, Vector2 toLocal)
        { BeginBoxSelect(fromLocal); BoxSelectTo(toLocal); _boxSelecting = false; }
        /// Begin / continue / end a programmatic point drag (same clamping as a mouse drag).
        public void BeginDrag(int index) { _dragIndex = index; BeginGesture(); }
        public void DragToLocal(Vector2 local) { if (_dragIndex >= 0) DragTo(local, initial: false); }
        public void EndDrag() { _dragIndex = -1; EndGesture(); }

        void OnPointerMove(PointerMoveEvent e)
        {
            Vector2 local = e.localPosition;
            bool captured = this.HasPointerCapture(e.pointerId);

            if (_dragIndex >= 0 && captured) { DragTo(local, initial: false); e.StopPropagation(); return; }
            if (_dragSegment >= 0 && captured) { BendSegment(_dragSegment, e.deltaPosition.y); e.StopPropagation(); return; }
            if (_dragLine >= 0 && captured) { DragLine(_dragLine, e.deltaPosition.y); e.StopPropagation(); return; }
            if (_boxSelecting && captured)
            {
                BoxSelectTo(local);
                e.StopPropagation();
                return;
            }

            // plain hover feedback
            int point = FindPointNear(local);
            int segment = point < 0 ? FindSegmentNear(local) : -1;
            if (point != _hoverIndex || segment != _hoverSegment)
            {
                _hoverIndex = point;
                _hoverSegment = segment;
                MarkDirtyRepaint();
            }
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (_boxSelecting)
            {
                _boxSelecting = false;
                MarkDirtyRepaint();
            }
            _dragIndex = -1;
            _dragSegment = -1;
            _dragLine = -1;
            EndGesture();
            MarkDirtyRepaint();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Delete || !_opt.allowRemovePoints || _selected.Count == 0) return;
            BeginGesture();
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                int idx = _selected[i];
                if (idx < 0 || idx >= _points.Count) continue;
                if (_points.Count <= _opt.minPoints) break;
                if (!CanRemove(StateOf(idx))) continue;
                _points.RemoveAt(idx);
            }
            SetSelection(null);
            MarkDirtyRepaint();
            OnChanged?.Invoke();
            EndGesture();
            e.StopPropagation();
        }

        void DragTo(Vector2 local, bool initial)
        {
            if (!HasLayout) return;
            var (time, value) = ToDomain(local);
            var primary = _points[_dragIndex];

            if (_selected.Count > 1 && _selected.Contains(_dragIndex))
            {
                // Multi-drag: move every selected point by the primary's delta, respecting per-point axes.
                float dt = time - primary.time;
                float dv = value - primary.value;
                if (initial) return;   // first event just establishes the grab; deltas flow from here
                foreach (int i in _selected)
                {
                    var p = _points[i];
                    var s = StateOf(i);
                    if (s == ZUIEnvelopeEditState.NotEditable) continue;
                    if (s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.XEditable)
                    {
                        float tMin = i > 0 ? _points[i - 1].time : _opt.xMin;
                        float tMax = i < _points.Count - 1 ? _points[i + 1].time : _opt.xMax;
                        p.time = Mathf.Clamp(p.time + dt, tMin, tMax);
                    }
                    if (s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.YEditable)
                        p.value = Mathf.Clamp(p.value + dv, Mathf.Min(_opt.yMin, _opt.yMax), Mathf.Max(_opt.yMin, _opt.yMax));
                }
            }
            else
            {
                var state = StateOf(_dragIndex);
                if (state == ZUIEnvelopeEditState.Editable || state == ZUIEnvelopeEditState.XEditable)
                {
                    float tMin = _dragIndex > 0 ? _points[_dragIndex - 1].time : _opt.xMin;
                    float tMax = _dragIndex < _points.Count - 1 ? _points[_dragIndex + 1].time : _opt.xMax;
                    primary.time = Mathf.Clamp(time, tMin, tMax);
                }
                if (state == ZUIEnvelopeEditState.Editable || state == ZUIEnvelopeEditState.YEditable)
                    primary.value = value;
            }

            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }

        void BendSegment(int segment, float deltaY)
        {
            var a = _points[segment];
            var b = _points[segment + 1];
            // Drag toward the pointer: on a rising segment, dragging UP (negative deltaY) bows the
            // curve upward (earlier rise = exponent < 1); on a falling segment the sense inverts.
            float dir = b.value >= a.value ? 1f : -1f;
            b.exponent = Mathf.Clamp(b.exponent * Mathf.Pow(1.02f, dir * deltaY), 0.05f, 20f);
            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }

        void DragLine(int segment, float deltaY)
        {
            var plot = Plot();
            float dv = -deltaY / plot.height * (_opt.yMax - _opt.yMin);
            for (int i = segment; i <= segment + 1; i++)
            {
                var s = StateOf(i);
                if (s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.YEditable)
                    _points[i].value = Mathf.Clamp(_points[i].value + dv,
                        Mathf.Min(_opt.yMin, _opt.yMax), Mathf.Max(_opt.yMin, _opt.yMax));
            }
            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }
    }
}
