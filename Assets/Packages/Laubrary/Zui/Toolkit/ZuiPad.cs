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
            style.width = size;
            style.height = size;
            this.tooltip = tooltip;

            _dot = new VisualElement();
            _dot.AddToClassList("zui-pad__dot");
            _dot.pickingMode = PickingMode.Ignore;
            Add(_dot);

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                this.CapturePointer(e.pointerId);
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
