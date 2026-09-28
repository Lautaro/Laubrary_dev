using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI single-value <c>ZUI.Slider(value, min, max, label, style)</c> — the thumb slider
    /// (added for the Zounds UI Toolkit port, T-0470), styled entirely by the skin's slider classes: an inline label
    /// (`zui-skinslider__label`, at least the def's label width), the track filled (`zui-skinslider__fill`) up to the
    /// thumb and empty (`zui-skinslider__rest`) after it, the thumb (`zui-skinminmax__edge--min`, `--active` while
    /// dragged) and, when the def shows one, a value box of the def's value width. Press anywhere on the track sets the
    /// value, dragging follows, double-click resets to the default when one is given; values are rounded like the IMGUI
    /// slider's (5 decimals). Geometry comes in as a <see cref="ZuiSkinRangeSlider.Geometry"/>, copied once from the sheet.
    public class ZuiSkinThumbSlider : VisualElement
    {
        readonly ZuiSkinRangeSlider.Geometry _g;
        readonly Label _label;
        readonly VisualElement _fill, _rest, _thumb;
        readonly FloatField _field;
        readonly Action<float> _onChanged;
        readonly float _min, _max;
        readonly float? _default;
        float _value;
        bool _dragging;

        public float value => _value;
        public string text { get => _label.text; set { _label.text = value; Layout(); } }

        public ZuiSkinThumbSlider(string label, float value, float min, float max, ZuiSkinRangeSlider.Geometry geometry,
                                  Action<float> onChanged, float? defaultValue = null)
        {
            _g = geometry; _min = min; _max = max; _onChanged = onChanged; _default = defaultValue;
            AddToClassList("zui-skinthumb");
            style.flexShrink = 0;
            _label = new Label(label) { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("zui-skinslider__label");
            _label.style.position = Position.Absolute;
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _label.style.marginLeft = _label.style.marginRight = _label.style.marginTop = _label.style.marginBottom = 0;
            _fill = Part("zui-skinslider__fill"); _rest = Part("zui-skinslider__rest"); _thumb = Part("zui-skinminmax__edge--min");
            Add(_label); Add(_fill); Add(_rest); Add(_thumb);
            if (_g.showValueField && _g.valueWidth > 0f)
            {
                _field = new FloatField();
                _field.AddToClassList("zui-skinminmax__field");
                _field.style.position = Position.Absolute;
                _field.style.marginLeft = _field.style.marginRight = _field.style.marginTop = _field.style.marginBottom = 0;
                _field.RegisterValueChangedCallback(e => Set(Mathf.Clamp(e.newValue, _min, _max)));
                Add(_field);
            }
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            SetValueWithoutNotify(value);
        }

        VisualElement Part(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            e.style.position = Position.Absolute;
            return e;
        }

        public void SetValueWithoutNotify(float v) { _value = Mathf.Clamp(v, _min, _max); Layout(); }

        void Set(float v)
        {
            v = Mathf.Clamp(v, _min, _max);
            if (Mathf.Approximately(v, _value)) return;
            _value = v; Layout();
            _onChanged?.Invoke(_value);
        }

        void Carve(out Rect labelRect, out Rect slider, out Rect valueRect)
        {
            var total = new Rect(0f, 0f, layout.width, layout.height);
            labelRect = valueRect = Rect.zero; slider = total;
            if (!string.IsNullOrEmpty(_label.text))
            {
                float measured = _label.MeasureTextSize(_label.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x
                                 + _label.resolvedStyle.paddingLeft + _label.resolvedStyle.paddingRight + 4f;
                float lw = _g.labelWidth > 0f ? Mathf.Max(_g.labelWidth, measured) : measured;
                labelRect = new Rect(0f, 0f, lw, total.height);
                slider = new Rect(lw, 0f, total.width - lw, total.height);
            }
            if (_field != null)
            {
                valueRect = new Rect(slider.xMax - _g.valueWidth, slider.y, _g.valueWidth, slider.height);
                slider.width -= _g.valueWidth;
            }
        }

        float ThumbW => Mathf.Max(4f, _g.thumbWidth);

        void Layout()
        {
            if (float.IsNaN(layout.width) || layout.width <= 0f) return;
            Carve(out var lr, out var s, out var vr);
            Place(_label, lr);
            float thumbH = _g.thumbHeight > 0f ? _g.thumbHeight : s.height, trackH = Mathf.Min(_g.trackHeight, s.height);
            float tMin = s.x + ThumbW * 0.5f, tMax = s.xMax - ThumbW * 0.5f;
            float cx = tMin + Mathf.InverseLerp(_min, _max, _value) * Mathf.Max(1f, tMax - tMin);
            float ty = s.y + (s.height - trackH) * 0.5f;
            Place(_fill, new Rect(s.x, ty, cx - s.x, trackH));
            Place(_rest, new Rect(cx, ty, s.xMax - cx, trackH));
            Place(_thumb, new Rect(cx - ThumbW * 0.5f, s.y + (s.height - thumbH) * 0.5f, ThumbW, thumbH));
            _thumb.EnableInClassList("zui-skinminmax__edge--active", _dragging);
            if (_field != null)
            {
                Place(_field, vr);
                if (_field.focusController?.focusedElement != _field)
                {
                    string fmt = !string.IsNullOrEmpty(_g.valueFormat) ? _g.valueFormat : ZuiSkinTrackLabel.AutoFormat(_min, _max);
                    _field.SetValueWithoutNotify(float.Parse(_value.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        static void Place(VisualElement e, Rect r)
        {
            e.style.display = r.width > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        float Sample(float x)
        {
            Carve(out _, out var s, out _);
            float tMin = s.x + ThumbW * 0.5f, tMax = s.xMax - ThumbW * 0.5f;
            float v = Mathf.Lerp(_min, _max, Mathf.Clamp01(Mathf.InverseLerp(tMin, tMax, x)));
            return Mathf.Round(v * 100000f) / 100000f;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            Carve(out _, out var s, out _);
            if (!s.Contains(e.localPosition)) return;
            if (e.clickCount == 2 && _default.HasValue) { Set(_default.Value); e.StopPropagation(); return; }
            _dragging = true;
            this.CapturePointer(e.pointerId);
            Set(Sample(e.localPosition.x));
            Layout();
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e) { if (_dragging && this.HasPointerCapture(e.pointerId)) Set(Sample(e.localPosition.x)); }

        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            Layout();
        }
    }
}
