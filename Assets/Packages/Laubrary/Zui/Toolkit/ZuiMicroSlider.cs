// ZuiMicroSlider — the old IMGUI ZUI MicroSlider, rebuilt for UI Toolkit.
//
// A filled box whose FILL is the value: no thumb, the "handle" is just the edge between the filled and
// empty parts of the track, and the label (and optional value) sit INSIDE the track. Click or drag
// anywhere on it to set the value (absolute — the value jumps to where you press); hold SHIFT to drag
// GENTLY (a small relative nudge per pixel, from the current value, no jump) for dialling in precise small
// values; double-click resets to the default if one was given. This is the control that gave old ZUI
// windows their look, and it packs into far less height than Unity's own Slider (which needs a separate
// thumb lane and usually a value field beside it).
//
// Drawn with Painter2D (the track rects) plus two child Labels (caption left, value right) that sit on
// top of the generated mesh. Everything themeable is a USS custom property read off resolvedStyle, so
// the whole toolkit's slider look tunes from ZuiToolkit.uss, not from numbers buried here.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiMicroSlider : VisualElement
    {
        float _value, _min, _max;
        readonly float? _default;
        readonly Action<float> _onChanged;
        readonly Action _onBeforeMutate;
        readonly Label _caption, _valueLabel;
        readonly bool _showValue;
        readonly int _decimals;
        bool _dragging, _gestureOpen;
        float _lastMoveX;   // local-space x of the previous applied move (for Shift fine/relative dragging)

        // Shift fine-drag sensitivity: the value moves this fraction of the NORMAL value-per-pixel while Shift
        // is held (so ~0.15× — a gentle nudge for setting precise small values). Matches ZuiScrub's Shift-fine
        // convention (Shift = gentle everywhere).
        const float FineFactor = 0.15f;

        public float value
        {
            get => _value;
            set { SetValue(value, notify: false); }
        }

        public ZuiMicroSlider(string label, float value, float min, float max, string tooltip,
            Action<float> onChanged, bool showValue = true, float? defaultValue = null,
            Action onBeforeMutate = null, int decimals = -1)
        {
            _min = min; _max = Mathf.Max(min + 1e-6f, max);
            _value = Mathf.Clamp(value, _min, _max);
            _onChanged = onChanged; _default = defaultValue; _showValue = showValue;
            _onBeforeMutate = onBeforeMutate; _decimals = decimals;
            // Append the drag-modifier hint to the hover tooltip so the Shift-fine / double-click gestures are
            // discoverable (the element itself is what receives the hover — the caption ignores picking).
            this.tooltip = tooltip + "  ·  Drag to set; Shift = fine"
                + (_default.HasValue ? "; double-click resets to default." : ".");

            AddToClassList("zui-microslider");

            _caption = new Label(label) { pickingMode = PickingMode.Ignore, tooltip = tooltip };
            _caption.AddToClassList("zui-microslider__caption");
            Add(_caption);

            _valueLabel = new Label { pickingMode = PickingMode.Ignore };
            _valueLabel.AddToClassList("zui-microslider__value");
            _valueLabel.style.display = showValue ? DisplayStyle.Flex : DisplayStyle.None;
            Add(_valueLabel);

            UpdateValueLabel();
            generateVisualContent += OnGenerate;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        float Round(float v) => _decimals >= 0 ? (float)Math.Round(v, _decimals)
                                               : (float)Math.Round(v, 5);

        void SetValue(float v, bool notify)
        {
            v = Round(Mathf.Clamp(v, _min, _max));
            if (Mathf.Approximately(v, _value) && notify) return;
            _value = v;
            UpdateValueLabel();
            MarkDirtyRepaint();
            if (notify) _onChanged?.Invoke(_value);
        }

        void UpdateValueLabel()
        {
            if (!_showValue) return;
            // Decimals scale to the range, matching the old MicroSlider's AutoFormat: a 0..1 dial wants
            // more places than a 0..360 one.
            float span = _max - _min;
            string fmt = _decimals >= 0 ? "F" + _decimals : span <= 3f ? "0.##" : span <= 40f ? "0.#" : "0";
            _valueLabel.text = _value.ToString(fmt);
        }

        float ValueFromX(float localX)
        {
            float w = Mathf.Max(1f, contentRect.width);
            return Mathf.Lerp(_min, _max, Mathf.Clamp01(localX / w));
        }

        void OpenGesture()
        {
            if (_gestureOpen) return;
            _gestureOpen = true;
            _onBeforeMutate?.Invoke();   // fires once per drag, before the first mutation (the Undo contract)
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2 && _default.HasValue)
            {
                OpenGesture();
                SetValue(_default.Value, notify: true);
                _gestureOpen = false;
                e.StopPropagation();
                return;
            }
            _dragging = true;
            _lastMoveX = e.localPosition.x;
            this.CapturePointer(e.pointerId);
            OpenGesture();
            // Shift = gentle: start a relative fine drag from the CURRENT value (no jump to the press point);
            // a normal press jumps the value to where you clicked (absolute).
            if (!e.shiftKey) SetValue(ValueFromX(e.localPosition.x), notify: true);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            float x = e.localPosition.x;
            if (e.shiftKey)
            {
                // Fine/relative: nudge by a fraction of the normal value-per-pixel, accumulated from the last x.
                float span = _max - _min;
                float w = Mathf.Max(1f, contentRect.width);
                SetValue(_value + (x - _lastMoveX) / w * span * FineFactor, notify: true);
            }
            else SetValue(ValueFromX(x), notify: true);
            _lastMoveX = x;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            _gestureOpen = false;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 1f || r.height <= 1f) return;
            float t = Mathf.InverseLerp(_min, _max, _value);
            float split = Mathf.Round(t * r.width);

            var p = mgc.painter2D;
            // Empty track first (the whole width), then the fill over the left part as a HORIZONTAL gradient
            // (dark at the left, lighter toward the fill edge). Darker overall than a flat accent so the label
            // and value drawn on top stay readable. The element's border-radius + overflow:hidden round it.
            FillRect(p, 0f, 0f, r.width, r.height, TrackColor);
            if (split > 0.5f) FillGradientH(p, split, r.height, FillLeft, FillRight);
        }

        static void FillRect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        // Painter2D has no gradient fill, so the horizontal gradient is a run of ~2px vertical strips, each a
        // solid colour lerped across the fill width. Cheap (a fill is < ~300px) and exact enough at this size.
        static void FillGradientH(Painter2D p, float w, float h, Color left, Color right)
        {
            const float step = 2f;
            for (float x = 0f; x < w; x += step)
            {
                float t = w > 1f ? Mathf.Clamp01((x + step * 0.5f) / w) : 0f;
                float wSeg = Mathf.Min(step, w - x);
                FillRect(p, x, 0f, wSeg, h, Color.Lerp(left, right, t));
            }
        }

        // Theme colours — a dark base at the left brightening toward the fill edge. Fields so a future pass
        // can bind them to USS custom properties.
        public Color FillLeft = new Color(28f / 255f, 44f / 255f, 78f / 255f, 0.92f);
        public Color FillRight = new Color(70f / 255f, 120f / 255f, 200f / 255f, 0.85f);
        public Color TrackColor = new Color(0f, 0f, 0f, 0.30f);
    }
}
