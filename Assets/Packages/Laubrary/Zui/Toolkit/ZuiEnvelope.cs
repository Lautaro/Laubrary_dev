// ZuiEnvelope — the UI Toolkit counterpart of ZUI.Envelope: a DAW-style multi-point envelope
// editor over the SAME runtime data the IMGUI control edits (List<ZUIEnvelopePoint>, sampled by
// ZUIEnvelopeEvaluator), so existing assets and play-mode evaluation are untouched. Drawing is
// Painter2D (the Trial-2 pattern), interaction is pointer-capture based.
//
// Ported core: exponent-bent curve rendering, grid, per-edit-state handles with hover, drag
// (per-axis permissions, neighbor-clamped time), click-empty-to-insert, right-click-to-remove,
// segment-drag exponent bending, anchor locking. NOT yet ported (deliberate, tracked in
// CHANGELOG/zui.md): loop/trim markers, box-select of multiple points, value labels — add them
// here when the first migrated tool actually needs them, not speculatively.
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
        public bool showGrid = true;
        public int gridRows = 4;
        public int minPoints = 2;
        public Color curveColor = new Color(0.3f, 0.8f, 1f);
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
        static readonly Color GridColor = new Color(0.5f, 0.5f, 0.5f, 0.18f);

        readonly List<ZUIEnvelopePoint> _points;   // caller-owned; mutated in place
        readonly ZuiEnvelopeOptions _opt;

        int _dragIndex = -1;
        int _dragSegment = -1;    // exponent-bend drag: index of the segment's LEFT point
        int _hoverIndex = -1;
        int _hoverSegment = -1;

        /// Called immediately BEFORE the first mutation of a gesture (drag start, insert, remove) —
        /// the hook for Undo.RecordObject on the owning asset.
        public Action OnBeforeMutate;
        /// Called after every mutation (each drag update, insert, remove).
        public Action OnChanged;

        public ZuiEnvelope(List<ZUIEnvelopePoint> points, ZuiEnvelopeOptions options,
            string tooltip, float width = 220f, float height = 80f)
        {
            _points = points ?? throw new ArgumentNullException(nameof(points));
            _opt = options ?? new ZuiEnvelopeOptions();

            AddToClassList("zui-envelope");
            style.width = width;
            style.height = height;
            this.tooltip = tooltip;

            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_dragIndex >= 0 || _dragSegment >= 0) return;
                _hoverIndex = -1; _hoverSegment = -1;
                MarkDirtyRepaint();
            });
        }

        /// Repaint after an external change to the point list (undo, another control editing it).
        public void Refresh() => MarkDirtyRepaint();

        ZUIEnvelopeEditState StateOf(int i)
        {
            if (_opt.anchorsLocked && (i == 0 || i == _points.Count - 1)) return ZUIEnvelopeEditState.NotEditable;
            return _points[i].editState;
        }

        // ── coordinate mapping (inset by Pad so edge handles never clip the border) ──
        Rect Plot()
        {
            var r = contentRect;
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

            if (_points.Count >= 1)
            {
                painter.strokeColor = _opt.curveColor;
                painter.lineWidth = _hoverSegment >= 0 || _dragSegment >= 0 ? 2.5f : 1.5f;
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
                painter.fillColor = locked ? HandleLockedFill : hot ? HandleHoverFill : HandleFill;
                painter.BeginPath();
                painter.Arc(ToLocal(_points[i].time, _points[i].value),
                    locked ? HandleRadius * 0.7f : hot ? HandleHoverRadius : HandleRadius, 0f, 360f);
                painter.Fill();
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

            if (e.button == 1)
            {
                int hit = FindPointNear(local);
                if (hit >= 0 && _opt.allowRemovePoints && _points.Count > _opt.minPoints
                    && StateOf(hit) == ZUIEnvelopeEditState.Editable)
                {
                    OnBeforeMutate?.Invoke();
                    _points.RemoveAt(hit);
                    _hoverIndex = -1;
                    MarkDirtyRepaint();
                    OnChanged?.Invoke();
                    e.StopPropagation();
                }
                return;
            }
            if (e.button != 0) return;

            int point = FindPointNear(local);
            if (point >= 0)
            {
                if (StateOf(point) == ZUIEnvelopeEditState.NotEditable) return;
                OnBeforeMutate?.Invoke();
                _dragIndex = point;
                this.CapturePointer(e.pointerId);
                DragTo(local);
                e.StopPropagation();
                return;
            }

            int segment = _opt.allowExponentEdit ? FindSegmentNear(local) : -1;
            if (segment >= 0)
            {
                OnBeforeMutate?.Invoke();
                _dragSegment = segment;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            if (_opt.allowAddPoints)
            {
                OnBeforeMutate?.Invoke();
                var (time, value) = ToDomain(local);
                int insert = _points.Count;
                for (int i = 0; i < _points.Count; i++)
                    if (time < _points[i].time) { insert = i; break; }
                _points.Insert(insert, new ZUIEnvelopePoint(time, value));
                _dragIndex = insert;
                this.CapturePointer(e.pointerId);
                MarkDirtyRepaint();
                OnChanged?.Invoke();
                e.StopPropagation();
            }
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            Vector2 local = e.localPosition;
            if (_dragIndex >= 0 && this.HasPointerCapture(e.pointerId))
            {
                DragTo(local);
                e.StopPropagation();
                return;
            }
            if (_dragSegment >= 0 && this.HasPointerCapture(e.pointerId))
            {
                BendSegment(_dragSegment, e.deltaPosition.y);
                e.StopPropagation();
                return;
            }

            // plain hover feedback
            int point = FindPointNear(local);
            int segment = point < 0 && _opt.allowExponentEdit ? FindSegmentNear(local) : -1;
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
            if (_dragIndex < 0 && _dragSegment < 0) return;
            _dragIndex = -1;
            _dragSegment = -1;
            MarkDirtyRepaint();
        }

        void DragTo(Vector2 local)
        {
            var p = _points[_dragIndex];
            var state = StateOf(_dragIndex);
            var (time, value) = ToDomain(local);

            if (state == ZUIEnvelopeEditState.Editable || state == ZUIEnvelopeEditState.XEditable)
            {
                float tMin = _dragIndex > 0 ? _points[_dragIndex - 1].time : _opt.xMin;
                float tMax = _dragIndex < _points.Count - 1 ? _points[_dragIndex + 1].time : _opt.xMax;
                p.time = Mathf.Clamp(time, tMin, tMax);
            }
            if (state == ZUIEnvelopeEditState.Editable || state == ZUIEnvelopeEditState.YEditable)
                p.value = value;

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
    }
}
