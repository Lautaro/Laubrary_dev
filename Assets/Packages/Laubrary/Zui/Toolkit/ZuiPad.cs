// ZuiPad — the UI Toolkit counterpart of ZUI.PositionPad: a small square drag-pad for a PLAIN
// Vector2 (screen/pixel offsets, UV nudges — anything that isn't an animatable per-frame value).
// Deliberately tiny, same as the IMGUI original: no numeric readout, no curve mode — just the pad.
// Compose it with other controls via Z.Row for a compact combined widget.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiPad : VisualElement
    {
        readonly Rect _range;
        readonly bool _flipY;
        readonly VisualElement _dot;
        Vector2 _value;

        /// Fired with the new value on every drag update.
        public event Action<Vector2> OnChanged;

        public Vector2 Value
        {
            get => _value;
            set { _value = Clamp(value); PlaceDot(); }
        }

        /// `range` bounds the value on both axes — dragging to the pad's edge yields the range's
        /// min/max. `flipY` (default true) makes dragging UP increase Y — the natural feel for a
        /// fresh "position" value; pass false only to match a value with an established Y-down
        /// convention elsewhere (the same field visually inverted between two controls editing it
        /// is worse than the pad alone not feeling natural).
        public ZuiPad(Vector2 initial, Rect range, string tooltip, float size = 56f, bool flipY = true)
        {
            _range = range;
            _flipY = flipY;
            _value = Clamp(initial);

            AddToClassList("zui-pad");
            AddToClassList("zui-kbd-focus");
            style.width = size;
            style.height = size;
            this.tooltip = tooltip + "  ·  Drag to set. Tab to focus, arrows to nudge, Shift = coarse.";
            // T-0316 — a pad had no field of any kind, so it could never take keyboard focus at all.
            focusable = true;
            tabIndex = 0;

            _dot = new VisualElement();
            _dot.AddToClassList("zui-pad__dot");
            _dot.pickingMode = PickingMode.Ignore;
            Add(_dot);

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                this.CapturePointer(e.pointerId);
                this.Focus();
                SetFromLocal(e.localPosition);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!this.HasPointerCapture(e.pointerId)) return;
                SetFromLocal(e.localPosition);
                e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            });
            RegisterCallback<GeometryChangedEvent>(_ => PlaceDot());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        // Arrow keys nudge the point the same way a drag does — a fraction of the pad's own range per
        // press, Shift = a coarser nudge (matches ZuiValue2DControl's pad). Goes through the SAME
        // `OnChanged` event a pointer drag fires (SetFromLocal, below) — there is no separate
        // "before mutate" hook on this control (its only mutation notification IS OnChanged), so a caller's
        // Undo wrapper sees a key-nudge exactly like a drag step.
        const float StepFrac = 0.02f, CoarseStepFrac = 0.1f;

        void OnKeyDown(KeyDownEvent e)
        {
            float dx = 0f, dy = 0f;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: dx = -1f; break;
                case KeyCode.RightArrow: dx = 1f; break;
                case KeyCode.UpArrow: dy = _flipY ? 1f : -1f; break;
                case KeyCode.DownArrow: dy = _flipY ? -1f : 1f; break;
                default: return;
            }
            float frac = e.shiftKey ? CoarseStepFrac : StepFrac;
            Vector2 next = Clamp(_value + new Vector2(dx * _range.width * frac, dy * _range.height * frac));
            if (next != _value)
            {
                _value = next;
                PlaceDot();
                OnChanged?.Invoke(_value);
            }
            e.StopPropagation();
        }

        void SetFromLocal(Vector3 local)
        {
            var r = contentRect;
            if (!(r.width > 0f) || !(r.height > 0f)) return;
            float tx = Mathf.Clamp01(local.x / r.width);
            float ty = Mathf.Clamp01(local.y / r.height);
            if (_flipY) ty = 1f - ty;
            _value = new Vector2(
                (float)Math.Round(_range.xMin + tx * _range.width, 5),
                (float)Math.Round(_range.yMin + ty * _range.height, 5));
            PlaceDot();
            OnChanged?.Invoke(_value);
        }

        Vector2 Clamp(Vector2 v) => new Vector2(
            Mathf.Clamp(v.x, _range.xMin, _range.xMax),
            Mathf.Clamp(v.y, _range.yMin, _range.yMax));

        void PlaceDot()
        {
            const float dot = 6f;
            var r = contentRect;
            if (!(r.width > 0f)) return;
            float tx = _range.width > 0f ? (_value.x - _range.xMin) / _range.width : 0.5f;
            float ty = _range.height > 0f ? (_value.y - _range.yMin) / _range.height : 0.5f;
            if (_flipY) ty = 1f - ty;
            _dot.style.left = tx * r.width - dot * 0.5f;
            _dot.style.top = ty * r.height - dot * 0.5f;
        }
    }
}
