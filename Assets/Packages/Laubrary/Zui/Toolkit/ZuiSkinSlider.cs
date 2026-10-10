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
            AddToClassList("zui-slider");

            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("zui-skinslider__fill");
            _fill.AddToClassList("zui-slider__fill");
            _rest = new VisualElement { pickingMode = PickingMode.Ignore };
            _rest.AddToClassList("zui-skinslider__rest");
            _rest.AddToClassList("zui-slider__rest");
            _label = ZuiSkinTrackLabel.Create();
            _label.AddToClassList("zui-slider__label");
            Add(_fill); Add(_rest); Add(_label);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            SetValueWithoutNotify(value);
        }

        public void SetValueWithoutNotify(float v) { _value = Mathf.Clamp(v, _min, _max); Refresh(); }

        // The default tick: one line, or, where the label's text crosses it, two stubs at the top and bottom edges so it
        // never draws through a letter (_defaultMarkLow is the bottom stub, hidden while the tick is one line).
        VisualElement _defaultMark, _defaultMarkLow;
        TextField _typeIn;
        Func<string, float?> _parseTyped;
        Action<float> _onTyped;

        /// <summary>
        /// Draws a thin tick on the track where the default value sits (the value a double-click returns to), so a
        /// changed value is visible at a glance. Needs the default given to the constructor. Where the label's text
        /// sits over the tick, the tick shrinks to a stub at each edge of the track, so it never crosses a letter.
        /// </summary>
        public ZuiSkinSlider WithDefaultMark()
        {
            if (!_default.HasValue || _defaultMark != null) return this;
            VisualElement Mark()
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.AddToClassList("zui-skinslider__default");
                m.style.position = Position.Absolute;
                m.style.width = 1f;
                m.style.backgroundColor = new Color(1f, 1f, 1f, 0.6f);
                Insert(IndexOf(_label), m);
                return m;
            }
            _defaultMark = Mark();
            _defaultMarkLow = Mark();
            Refresh();
            return this;
        }

        /// <summary>The default tick (kept checks): its top part, and its bottom stub (shown only while the text crosses it).</summary>
        public VisualElement DefaultMarkForTest => _defaultMark;
        public VisualElement DefaultMarkLowForTest => _defaultMarkLow;
        /// <summary>The label's text, as drawn (kept checks).</summary>
        public Label LabelForTest => _label;

        /// <summary>Places the default tick: a line from 2 px below the top to 2 px above the bottom, or, when the label's
        /// text spans the tick's x, a stub at each edge that stops short of the glyphs (the text is centred both ways).</summary>
        void PlaceDefaultMark()
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            float x = (_max > _min ? Mathf.InverseLerp(_min, _max, _default.Value) : 0f) * (float.IsNaN(w) ? 0f : w);
            _defaultMark.style.left = Length.Percent((_max > _min ? Mathf.InverseLerp(_min, _max, _default.Value) : 0f) * 100f);
            _defaultMarkLow.style.left = _defaultMark.style.left;
            bool crosses = true;   // before the first layout: assume the text is there
            float half = 0f;
            if (!float.IsNaN(w) && w > 0f && !float.IsNaN(h) && h > 0f && !string.IsNullOrEmpty(_label.text))
            {
                var sz = _label.MeasureTextSize(_label.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
                float fs = _label.resolvedStyle.fontSize;
                if (float.IsNaN(fs) || fs <= 0f) fs = 12f;
                // The glyphs' band: half a cap height plus a descender's worth either side of the middle.
                half = Mathf.Ceil(fs * 0.45f);
                crosses = sz.x <= 0f || (x >= (w - sz.x) * 0.5f - 2f && x <= (w + sz.x) * 0.5f + 2f);
            }
            else if (string.IsNullOrEmpty(_label.text)) crosses = false;
            if (!crosses || float.IsNaN(h) || h <= 0f)
            {
                _defaultMark.style.top = 2f; _defaultMark.style.bottom = 2f; _defaultMark.style.height = StyleKeyword.Auto;
                _defaultMarkLow.style.display = DisplayStyle.None;
                return;
            }
            // A stub from the edge to 1 px short of the glyph band, at least 2 px long.
            float stub = Mathf.Max(2f, Mathf.Floor(h * 0.5f - half - 1f));
            _defaultMark.style.top = 0f; _defaultMark.style.bottom = StyleKeyword.Auto; _defaultMark.style.height = stub;
            _defaultMarkLow.style.display = DisplayStyle.Flex;
            _defaultMarkLow.style.top = StyleKeyword.Auto; _defaultMarkLow.style.bottom = 0f; _defaultMarkLow.style.height = stub;
        }

        /// <summary>
        /// Lets the value be typed: Ctrl+click on the track opens a text box over it, holding the value as
        /// <c>format</c> shows it; Enter (or leaving the box) hands the parsed SLIDER value to <paramref name="onTyped"/>,
        /// Escape cancels. <paramref name="parse"/> turns the typed text into a slider value (null: not a number, ignored);
        /// the result is clamped to the slider's range. The typed value is not passed to the drag callback, so the caller
        /// can record it as one discrete edit.
        /// </summary>
        public ZuiSkinSlider WithTypeIn(Func<string, float?> parse, Action<float> onTyped)
        {
            _parseTyped = parse; _onTyped = onTyped;
            return this;
        }

        void OpenTypeIn()
        {
            if (_typeIn == null)
            {
                _typeIn = new TextField { isDelayed = false };
                _typeIn.AddToClassList("zui-skinslider__typein");
                _typeIn.style.position = Position.Absolute;
                _typeIn.style.left = 0f; _typeIn.style.right = 0f; _typeIn.style.top = 0f; _typeIn.style.bottom = 0f;
                _typeIn.style.marginLeft = 0f; _typeIn.style.marginRight = 0f; _typeIn.style.marginTop = 0f; _typeIn.style.marginBottom = 0f;
                _typeIn.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CloseTypeIn(true); e.StopPropagation(); }
                    else if (e.keyCode == KeyCode.Escape) { CloseTypeIn(false); e.StopPropagation(); }
                }, TrickleDown.TrickleDown);
                _typeIn.RegisterCallback<FocusOutEvent>(_ => CloseTypeIn(true));
                _typeIn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                Add(_typeIn);
            }
            _typeIn.style.display = DisplayStyle.Flex;
            _typeIn.SetValueWithoutNotify(_format(_value));
            _typeIn.schedule.Execute(() => { _typeIn.Focus(); _typeIn.SelectAll(); });
        }

        void CloseTypeIn(bool commit)
        {
            if (_typeIn == null || _typeIn.style.display == DisplayStyle.None) return;
            _typeIn.style.display = DisplayStyle.None;
            if (!commit || _parseTyped == null) return;
            float? v = _parseTyped(_typeIn.value);
            if (!v.HasValue || float.IsNaN(v.Value)) return;
            float nv = Mathf.Clamp(v.Value, _min, _max);
            _value = nv; Refresh();
            _onTyped?.Invoke(nv);
        }

        /// <summary>Types a value as if entered in the box (kept checks).</summary>
        public void TypeForTest(string text) { OpenTypeIn(); _typeIn.SetValueWithoutNotify(text); CloseTypeIn(true); }

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
                        : string.IsNullOrEmpty(_text) ? v : _text + " " + v;   // "Chance 100", as the min/max bars read "Volume 90-100"
            ZuiSkinTrackLabel.Fit(_label, full, _mode == LabelMode.LabelAndValue ? v : null, resolvedStyle.width);
            if (_defaultMark != null) PlaceDefaultMark();   // after the text is fitted: the tick steps around it
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
            if (_parseTyped != null && (e.ctrlKey || e.commandKey))
            {
                OpenTypeIn();
                e.StopPropagation();
                return;
            }
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
