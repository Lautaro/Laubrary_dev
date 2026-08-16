// ZuiVectorEditor — a UI Toolkit control that edits a VECTOR (origin + direction + optional length) over a
// caller-owned ZuiVectorValue, the same "own the runtime data, control just edits it in place" pattern
// ZuiEnvelope uses for List<ZUIEnvelopePoint>. Built to spec from the user's own description (2026-08-16):
//   • click inside the frame        → sets the vector's ORIGIN there;
//   • drag (from that click, or     → sets DIRECTION (and LENGTH, if the caller's options allow it) —
//     from the existing arrow)        the arrow is "drawn" by the same click-drag gesture that placed it;
//   • right-click the arrow         → opens a context card (ZuiVectorDial) to point the arrow and adjust
//                                      length precisely, with toggleable input boxes/labels, an angle-mode
//                                      picker (local coords / degrees / radians), and N-way quantize snap.
// Default length is normalized (1.0) — meaningful only when ZuiVectorEditorOptions.allowLength is true;
// the caller decides that per instance, not the control.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Cookbook2D;

namespace Laubrary.Zui
{
    /// How the context card's dial displays/edits the vector's angle. Local = the raw domain-space
    /// direction (x, y) as two numbers; Degrees/Radians = a single angle value, 0 = up, clockwise —
    /// the same convention Cookbook2D and LauminationSetResolver already use project-wide.
    public enum ZuiVectorAngleMode { LocalCoordinates, Degrees, Radians }

    /// Per-instance configuration — the vector-editor analog of ZuiEnvelopeOptions.
    public class ZuiVectorEditorOptions
    {
        /// Domain bounds the frame maps to (e.g. a laumination frame's pixel rect). Vector's origin is
        /// expressed in this space, same convention as ZuiEnvelope's xMin/xMax/yMin/yMax.
        public float xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f;

        /// Whether THIS instance lets the user adjust length at all. Off (default) = the vector is always
        /// treated as normalized (length 1) — the caller's own consumer ignores whatever's in
        /// ZuiVectorValue.length. On = length is real data, edited by dragging the arrow tip / the dial.
        public bool allowLength = false;

        public bool editable = true;
        public Color arrowColor = new Color(1f, 0.65f, 0.15f);
        public Color originColor = new Color(0.9f, 0.9f, 0.9f);
    }

    /// The vector itself — caller-owned, mutated in place (same ownership model as List&lt;ZUIEnvelopePoint&gt;
    /// for ZuiEnvelope). `authored` mirrors how Point-mode MetaLayers already represent "nothing placed on
    /// this frame yet" — false means the control renders nothing and a right-click on empty space is a no-op.
    public class ZuiVectorValue
    {
        public bool authored;
        public Vector2 origin;
        public Vector2 direction = Vector2.up;
        /// Only meaningful when the owning ZuiVectorEditorOptions.allowLength is true.
        public float length = 1f;
    }

    public class ZuiVectorEditor : VisualElement
    {
        const float Pad = 8f;
        const float OriginRadius = 4f;
        const float OriginHoverRadius = 6f;
        const float ArrowHeadLen = 8f;
        const float ArrowHeadWidth = 5f;
        const float HitRadius = 10f;

        readonly ZuiVectorValue _value;
        readonly ZuiVectorEditorOptions _opt;

        bool _draggingOrigin;
        bool _draggingDirection;
        bool _hoverOrigin;
        bool _hoverArrow;
        bool _gestureRecorded;

        /// Called immediately BEFORE the first mutation of a gesture — the hook for Undo.RecordObject.
        public Action OnBeforeMutate;
        /// Called after every mutation (origin move, direction/length drag).
        public Action OnChanged;

        public ZuiVectorEditor(ZuiVectorValue value, ZuiVectorEditorOptions options, string tooltip,
            float width = 160f, float height = 160f)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _opt = options ?? new ZuiVectorEditorOptions();

            AddToClassList("zui-vector-editor");
            style.width = width;
            style.height = height;
            this.tooltip = tooltip;

            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_draggingOrigin || _draggingDirection) return;
                _hoverOrigin = false; _hoverArrow = false;
                MarkDirtyRepaint();
            });
        }

        /// Repaint after an external change to the value (undo, a numeric field editing it directly).
        public void Refresh() => MarkDirtyRepaint();

        void BeginGesture()
        {
            if (_gestureRecorded) return;
            _gestureRecorded = true;
            OnBeforeMutate?.Invoke();
        }

        void EndGesture() => _gestureRecorded = false;

        public bool HasLayout => !float.IsNaN(contentRect.width) && contentRect.width > 0f
                              && !float.IsNaN(contentRect.height) && contentRect.height > 0f;

        Rect Plot()
        {
            var r = contentRect;
            if (!HasLayout) return new Rect(0f, 0f, 1f, 1f);
            return new Rect(r.x + Pad, r.y + Pad, Mathf.Max(1f, r.width - Pad * 2f), Mathf.Max(1f, r.height - Pad * 2f));
        }

        Vector2 ToLocal(Vector2 domain)
        {
            var p = Plot();
            float tx = Mathf.InverseLerp(_opt.xMin, _opt.xMax, domain.x);
            float ty = Mathf.InverseLerp(_opt.yMin, _opt.yMax, domain.y);
            return new Vector2(p.x + tx * p.width, p.yMax - ty * p.height);
        }

        Vector2 ToDomain(Vector2 local)
        {
            var p = Plot();
            float tx = Mathf.Clamp01((local.x - p.x) / p.width);
            float ty = Mathf.Clamp01((p.yMax - local.y) / p.height);
            return new Vector2(Mathf.Lerp(_opt.xMin, _opt.xMax, tx), Mathf.Lerp(_opt.yMin, _opt.yMax, ty));
        }

        // The arrow's ON-SCREEN length is a display convention, not a literal unit conversion: when length
        // editing is off the vector is normalized (always drawn at a fixed, readable size); when on, `length`
        // is displayed relative to the frame's own size so 1.0 reads as "a sensible default reach" and the
        // user can see it grow/shrink as they drag — the AUTHORED value (what a consumer reads back) is
        // exactly what's in ZuiVectorValue.length, untouched by this display scaling.
        float DisplayPixelLength()
        {
            float basePx = Mathf.Min(Plot().width, Plot().height) * 0.35f;
            return _opt.allowLength ? Mathf.Max(0.02f, _value.length) * basePx : basePx;
        }

        Vector2 OriginLocal() => ToLocal(_value.origin);

        Vector2 DirectionLocal()
        {
            // Domain direction.y follows the project's "+Y = up" convention; local space grows downward, so
            // the Y term flips here — the SAME flip ToLocal applies to a position, applied to a pure direction.
            var d = _value.direction.sqrMagnitude > 1e-6f ? _value.direction.normalized : Vector2.up;
            return new Vector2(d.x, -d.y);
        }

        Vector2 ArrowEndLocal() => OriginLocal() + DirectionLocal() * DisplayPixelLength();

        // ── painting ────────────────────────────────────────────────────────────────
        void Paint(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (!(r.width > 8f) || !(r.height > 8f)) return;
            var painter = mgc.painter2D;
            var plot = Plot();

            painter.strokeColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(plot.x, plot.y));
            painter.LineTo(new Vector2(plot.xMax, plot.y));
            painter.LineTo(new Vector2(plot.xMax, plot.yMax));
            painter.LineTo(new Vector2(plot.x, plot.yMax));
            painter.ClosePath();
            painter.Stroke();

            if (!_value.authored) return;

            Vector2 originLocal = OriginLocal();
            Vector2 endLocal = ArrowEndLocal();

            // Shaft.
            painter.strokeColor = _opt.arrowColor;
            painter.lineWidth = _hoverArrow || _draggingDirection ? 2.5f : 1.75f;
            painter.BeginPath();
            painter.MoveTo(originLocal);
            painter.LineTo(endLocal);
            painter.Stroke();

            // Arrowhead — two short strokes back from the tip, like a chevron.
            Vector2 dir = (endLocal - originLocal);
            if (dir.sqrMagnitude > 1e-6f)
            {
                dir = dir.normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                Vector2 back = endLocal - dir * ArrowHeadLen;
                Vector2 left = back + perp * ArrowHeadWidth;
                Vector2 right = back - perp * ArrowHeadWidth;
                painter.fillColor = _opt.arrowColor;
                painter.BeginPath();
                painter.MoveTo(endLocal);
                painter.LineTo(left);
                painter.LineTo(right);
                painter.ClosePath();
                painter.Fill();
            }

            // Origin handle.
            float originRadius = _hoverOrigin || _draggingOrigin ? OriginHoverRadius : OriginRadius;
            painter.fillColor = _opt.originColor;
            painter.BeginPath();
            painter.Arc(originLocal, originRadius, 0f, 360f);
            painter.Fill();
        }

        // ── hit testing ─────────────────────────────────────────────────────────────
        bool NearOrigin(Vector2 local) => Vector2.Distance(local, OriginLocal()) <= HitRadius;
        bool NearArrow(Vector2 local) => Vector2.Distance(local, ArrowEndLocal()) <= HitRadius;

        // ── interaction ─────────────────────────────────────────────────────────────
        void OnPointerDown(PointerDownEvent e)
        {
            if (!_opt.editable) return;
            Vector2 local = e.localPosition;

            if (e.button == 1)
            {
                if (_value.authored && NearArrow(local))
                {
                    OpenContextCard();
                    e.StopPropagation();
                }
                return;
            }
            if (e.button != 0) return;

            if (_value.authored && NearOrigin(local))
            {
                BeginGesture();
                _draggingOrigin = true;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            if (_value.authored && NearArrow(local))
            {
                BeginGesture();
                _draggingDirection = true;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            // Click inside the frame, not on the existing origin/arrow: place the vector here. If nothing
            // was authored yet, seed a sensible default direction/length rather than a zero vector (a fresh
            // click should immediately show a visible arrow, not an invisible point).
            BeginGesture();
            bool firstPlacement = !_value.authored;
            _value.authored = true;
            _value.origin = ToDomain(local);
            if (firstPlacement)
            {
                _value.direction = Vector2.up;
                _value.length = 1f;
            }
            // Immediately start a direction drag from this same gesture — "click draws the arrow" per spec.
            _draggingDirection = true;
            this.CapturePointer(e.pointerId);
            MarkDirtyRepaint();
            OnChanged?.Invoke();
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            Vector2 local = e.localPosition;
            bool captured = this.HasPointerCapture(e.pointerId);

            if (_draggingOrigin && captured)
            {
                _value.origin = ToDomain(local);
                MarkDirtyRepaint();
                OnChanged?.Invoke();
                e.StopPropagation();
                return;
            }
            if (_draggingDirection && captured)
            {
                DragDirectionTo(local);
                e.StopPropagation();
                return;
            }

            bool hoverO = _value.authored && NearOrigin(local);
            bool hoverA = !hoverO && _value.authored && NearArrow(local);
            if (hoverO != _hoverOrigin || hoverA != _hoverArrow)
            {
                _hoverOrigin = hoverO;
                _hoverArrow = hoverA;
                MarkDirtyRepaint();
            }
        }

        void DragDirectionTo(Vector2 local)
        {
            Vector2 delta = local - OriginLocal();
            if (delta.sqrMagnitude > 1e-6f)
            {
                // Undo the local-space Y flip to recover a domain-space direction (+Y = up).
                Vector2 domainDir = new Vector2(delta.x, -delta.y).normalized;
                _value.direction = domainDir;
                if (_opt.allowLength)
                {
                    float basePx = Mathf.Min(Plot().width, Plot().height) * 0.35f;
                    _value.length = Mathf.Max(0.02f, delta.magnitude / Mathf.Max(1f, basePx));
                }
            }
            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            _draggingOrigin = false;
            _draggingDirection = false;
            EndGesture();
            MarkDirtyRepaint();
        }

        // ── programmatic API (mirrors ZuiEnvelope's, for tooling/verification without synthetic pointer events) ──
        public Vector2 ValueToLocal(Vector2 domain) => ToLocal(domain);
        public Vector2 LocalToValue(Vector2 local) => ToDomain(local);

        // ── context card ────────────────────────────────────────────────────────────
        void OpenContextCard() => ZuiVectorDial.OpenCard(this, _value, _opt, () =>
        {
            MarkDirtyRepaint();
            OnChanged?.Invoke();
        }, OnBeforeMutate);
    }
}
