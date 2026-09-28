using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI ZUI MicroSlider, built from ordinary elements and styled ENTIRELY by USS, so a skin
    /// can reproduce a sheet-styled look exactly (added for the Zounds UI Toolkit port, T-0458):
    ///
    /// - the track is two children side by side, `__fill` (from the start to the value) and `__rest` (the remainder),
    ///   each styled as its own box, exactly as the IMGUI one draws the fill box and the track box on either side of the
    ///   value;
    /// - the label is centred across the whole track in one of the IMGUI modes (label only, value only, "label: value"),
    ///   shrunk and then reduced to the value alone when it does not fit, exactly as the IMGUI one fits it;
    /// - press anywhere on the track sets the value from the pointer, dragging follows it, double-click resets to the
    ///   default when one is given.
    ///
    /// It deliberately does not paint anything: every pixel comes from USS (backgrounds, borders, radii, text), which is
    /// what lets a copied skin match an old look pixel for pixel. The existing ZuiMicroSlider (painted, with its own
    /// colours) is unchanged.
    public class ZuiSkinSlider : VisualElement
    {
        public enum LabelMode { LabelOnly, ValueOnly, LabelAndValue, None }

        readonly VisualElement _fill, _rest;
        readonly Label _label;
        readonly Action<float> _onChanged;
        readonly Action _onBeforeMutate;
        float _value, _min, _max;
        float? _default;
        string _text;
        LabelMode _mode;
        Func<float, string> _format;
        bool _dragging;

        public float value { get => _value; set => SetValueWithoutNotify(value); }
        public string text { get => _text; set { _text = value; Refresh(); } }

        public ZuiSkinSlider(string text, float value, float min, float max, string tooltip, Action<float> onChanged,
                             LabelMode mode = LabelMode.LabelOnly, float? defaultValue = null, Func<float, string> format = null,
                             Action onBeforeMutate = null)
        {
            _text = text; _min = min; _max = max; _default = defaultValue; _mode = mode;
            string fmt = ZuiSkinTrackLabel.AutoFormat(min, max);
            _format = format ?? (v => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture));
            _onChanged = onChanged; _onBeforeMutate = onBeforeMutate;
            this.tooltip = tooltip;
            AddToClassList("zui-skinslider");
            style.flexDirection = FlexDirection.Row;
            style.flexShrink = 0;

            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("zui-skinslider__fill");
            _rest = new VisualElement { pickingMode = PickingMode.Ignore };
            _rest.AddToClassList("zui-skinslider__rest");
            _label = ZuiSkinTrackLabel.Create();
            Add(_fill); Add(_rest); Add(_label);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            SetValueWithoutNotify(value);
        }

        public void SetValueWithoutNotify(float v) { _value = Mathf.Clamp(v, _min, _max); Refresh(); }

        void Refresh()
        {
            float t = _max > _min ? Mathf.InverseLerp(_min, _max, _value) : 0f;
            _fill.style.width = Length.Percent(t * 100f);
            _rest.style.width = Length.Percent((1f - t) * 100f);
            _fill.style.display = t > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _rest.style.display = t < 1f ? DisplayStyle.Flex : DisplayStyle.None;
            string v = _format(_value);
            string full = _mode == LabelMode.None ? "" : _mode == LabelMode.ValueOnly ? v
                        : _mode == LabelMode.LabelOnly ? _text
                        : string.IsNullOrEmpty(_text) ? v : _text + ": " + v;
            ZuiSkinTrackLabel.Fit(_label, full, _mode == LabelMode.LabelAndValue ? v : null, resolvedStyle.width);
        }

        void SetFromPointer(Vector2 local)
        {
            float w = resolvedStyle.width;
            if (w <= 0f) return;
            float nv = Mathf.Round(Mathf.Lerp(_min, _max, Mathf.Clamp01(local.x / w)) * 100000f) / 100000f;   // IMGUI ZUI's 5-decimal cleanup
            if (Mathf.Approximately(nv, _value)) return;
            _onBeforeMutate?.Invoke();
            _value = nv; Refresh();
            _onChanged?.Invoke(_value);
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2 && _default.HasValue)
            {
                _onBeforeMutate?.Invoke();
                _value = Mathf.Clamp(_default.Value, _min, _max); Refresh();
                _onChanged?.Invoke(_value);
                e.StopPropagation();
                return;
            }
            _dragging = true;
            this.CapturePointer(e.pointerId);
            SetFromPointer(e.localPosition);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e) { if (_dragging && this.HasPointerCapture(e.pointerId)) SetFromPointer(e.localPosition); }

        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
        }
    }
}
