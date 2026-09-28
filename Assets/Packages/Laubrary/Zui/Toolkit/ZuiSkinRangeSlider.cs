using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI <c>ZUI.SliderRange(rect, ref min, ref max, absMin, absMax, label, style)</c>, the
    /// thumb-style two-value slider (added for the Zounds UI Toolkit port, T-0469), styled ENTIRELY by the skin's slider
    /// classes — the same boxes the IMGUI one draws from its slider def:
    ///
    /// - an inline label on the left (`zui-skinslider__label`), as wide as its text plus 4;
    /// - the track: empty (`zui-skinslider__rest`) left of the min thumb, fill (`zui-skinslider__fill`) between the thumbs,
    ///   empty again right of the max thumb, <c>trackHeight</c> tall and centred; for a bipolar def whose range is
    ///   collapsed, the fill runs from the centre marker to the value instead;
    /// - the min and max thumbs (`zui-skinminmax__edge--min` / `--max`, with `--active` while dragged), drawn side by side
    ///   when they would overlap ("fused"), and a bipolar centre marker (`--center`);
    /// - on the right, the value area: one number box when the range is collapsed, else two half boxes
    ///   (`zui-skinminmax__field`), 60 % of the def's value width in all — or, in compact mode, a plain "min-max" text;
    ///
    /// with the IMGUI rules: double-click on the bipolar centre toggles collapse-at-centre / ±5 % spread; double-click
    /// elsewhere collapses toward the clicked side, or expands a collapsed range by ±5 % of the whole range; press on a
    /// thumb drags it, on the fill (or on fused thumbs) pans the range, on empty track moves the nearer thumb there;
    /// values are rounded like the IMGUI slider's (5 decimals). Geometry comes in as <see cref="Geometry"/>, copied once
    /// from the sheet, never read from it at runtime.
    public class ZuiSkinRangeSlider : VisualElement
    {
        public struct Geometry
        {
            public float thumbWidth, thumbHeight, trackHeight, valueWidth, labelWidth;
            public bool showValueField, bipolar;
            public float bipolarCenter;   // NaN = middle of the range
            public string valueFormat;    // null = automatic
        }

        const float ExpandFraction = 0.1f;
        const float CenterGrab = 6f;

        readonly Geometry _g;
        readonly Label _label, _compact;
        readonly VisualElement _emptyL, _fill, _emptyR, _fillHover, _thumbMin, _thumbMax, _thumbCenter;
        readonly FloatField _fieldOne, _fieldMin, _fieldMax;
        readonly Action<float, float> _onChanged;
        readonly Action _onBeforeMutate;
        float _min, _max, _absMin, _absMax;
        enum Drag { None, Min, Max, Fill }
        Drag _drag;
        float _anchorMin, _anchorMax, _anchorX;
        Vector2 _pointer = new Vector2(-1f, -1f);
        Func<float, float, string> _compactText;

        public float min => _min;
        public float max => _max;

        public ZuiSkinRangeSlider(string label, float lo, float hi, float absMin, float absMax, Geometry geometry,
                                  Action<float, float> onChanged, Action onBeforeMutate = null, Func<float, float, string> compactText = null)
        {
            _g = geometry; _absMin = absMin; _absMax = absMax; _onChanged = onChanged; _onBeforeMutate = onBeforeMutate; _compactText = compactText;
            AddToClassList("zui-skinrange");
            style.flexShrink = 0;

            _label = new Label(label) { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("zui-skinslider__label");
            _label.AddToClassList("zui-skinrange__label");
            Abs(_label);
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _label.style.marginLeft = 0; _label.style.marginRight = 0; _label.style.marginTop = 0; _label.style.marginBottom = 0;
            _emptyL = Part("zui-skinslider__rest"); _fill = Part("zui-skinslider__fill"); _emptyR = Part("zui-skinslider__rest");
            _fillHover = Part(null); _fillHover.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            _thumbCenter = Part("zui-skinminmax__edge--center");
            _thumbMin = Part("zui-skinminmax__edge--min"); _thumbMax = Part("zui-skinminmax__edge--max");
            Add(_label); Add(_emptyL); Add(_fill); Add(_emptyR); Add(_fillHover); Add(_thumbMin); Add(_thumbMax); Add(_thumbCenter);

            _fieldOne = Field(v => Set(v, v));
            _fieldMin = Field(v => Set(Mathf.Clamp(v, _absMin, _absMax), Mathf.Max(_max, Mathf.Clamp(v, _absMin, _absMax))));
            _fieldMax = Field(v => Set(_min, Mathf.Clamp(v, _min, _absMax)));
            _compact = new Label { pickingMode = PickingMode.Ignore };
            _compact.AddToClassList("zui-skinrange__compact");
            Abs(_compact);
            _compact.style.unityTextAlign = TextAnchor.MiddleLeft;
            _compact.style.color = new Color(1f, 1f, 1f, 0.7f);
            Add(_compact);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => { _pointer = new Vector2(-1f, -1f); Layout(); });
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
            SetValuesWithoutNotify(lo, hi);
        }

        static void Abs(VisualElement e) { e.style.position = Position.Absolute; }

        VisualElement Part(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (cls != null) e.AddToClassList(cls);
            Abs(e);
            return e;
        }

        FloatField Field(Action<float> onSet)
        {
            var f = new FloatField();
            f.AddToClassList("zui-skinminmax__field");
            f.AddToClassList("zui-skinrange__field");
            Abs(f);
            f.style.marginLeft = 0; f.style.marginRight = 0; f.style.marginTop = 0; f.style.marginBottom = 0;
            f.RegisterValueChangedCallback(e => { if (!Mathf.Approximately(e.newValue, e.previousValue)) onSet(e.newValue); });
            Add(f);
            return f;
        }

        public void SetValuesWithoutNotify(float lo, float hi)
        {
            _min = Mathf.Clamp(lo, _absMin, _absMax);
            _max = Mathf.Clamp(hi, _min, _absMax);
            Layout();
        }

        void Set(float lo, float hi)
        {
            lo = Mathf.Clamp(lo, _absMin, _absMax);
            hi = Mathf.Clamp(hi, lo, _absMax);
            if (Mathf.Approximately(lo, _min) && Mathf.Approximately(hi, _max)) return;
            _onBeforeMutate?.Invoke();
            _min = lo; _max = hi;
            Layout();
            _onChanged?.Invoke(_min, _max);
        }

        // ─────────────────────────── geometry (the IMGUI carve and thumb maths) ───────────────────────────

        string Fmt => !string.IsNullOrEmpty(_g.valueFormat) ? _g.valueFormat : ZuiSkinTrackLabel.AutoFormat(_absMin, _absMax);
        bool Collapsed => _min.ToString(Fmt) == _max.ToString(Fmt);
        float ThumbW => Mathf.Max(4f, _g.thumbWidth);
        bool Compact => _compactText != null;

        void Carve(out Rect labelRect, out Rect sliderRect, out Rect valueRect)
        {
            var total = new Rect(0f, 0f, layout.width, layout.height);
            labelRect = valueRect = Rect.zero;
            sliderRect = total;
            if (!string.IsNullOrEmpty(_label.text))
            {
                float measured = _label.MeasureTextSize(_label.text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x
                                 + _label.resolvedStyle.paddingLeft + _label.resolvedStyle.paddingRight + 4f;
                float lw = _g.labelWidth > 0f ? Mathf.Max(_g.labelWidth, measured) : measured;
                labelRect = new Rect(total.x, total.y, lw, total.height);
                sliderRect = new Rect(total.x + lw, total.y, total.width - lw, total.height);
            }
            if (Compact)
            {
                float cw = CompactWidth;
                valueRect = new Rect(sliderRect.xMax - cw, sliderRect.y, cw, sliderRect.height);
                sliderRect.width -= cw;
            }
            else if (_g.showValueField && _g.valueWidth > 0f)
            {
                float vw = _g.valueWidth * 0.6f;
                valueRect = new Rect(sliderRect.xMax - vw, sliderRect.y, vw, sliderRect.height);
                sliderRect.width -= vw;
            }
        }

        /// <summary>The compact label's fixed width (host-provided so it matches the IMGUI reservation exactly).</summary>
        public float CompactWidth { get; set; }

        void Travel(Rect s, out float tMin, out float tLen) { tMin = s.x + ThumbW * 0.5f; tLen = Mathf.Max(1f, s.xMax - ThumbW * 0.5f - tMin); }
        float XOf(float v, Rect s) { Travel(s, out var a, out var l); return a + Mathf.InverseLerp(_absMin, _absMax, v) * l; }
        float Sample(float x, Rect s)
        {
            Travel(s, out var a, out var l);
            float v = Mathf.Lerp(_absMin, _absMax, Mathf.Clamp01(Mathf.InverseLerp(a, a + l, x)));
            return Mathf.Round(v * 100000f) / 100000f;
        }
        float Center => Mathf.Clamp(float.IsNaN(_g.bipolarCenter) ? (_absMin + _absMax) * 0.5f : _g.bipolarCenter, _absMin, _absMax);

        static void Place(VisualElement e, Rect r, bool show = true)
        {
            e.style.display = show && r.width > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        void Layout()
        {
            if (float.IsNaN(layout.width) || layout.width <= 0f) return;
            Carve(out var labelRect, out var s, out var valueRect);
            Place(_label, labelRect, labelRect.width > 0f);
            float thumbW = ThumbW, thumbH = _g.thumbHeight > 0f ? _g.thumbHeight : s.height, trackH = Mathf.Min(_g.trackHeight, s.height);
            float trackY = s.y + (s.height - trackH) * 0.5f;
            float cxMin = XOf(_min, s), cxMax = XOf(_max, s);
            bool fused = cxMax - cxMin < thumbW;
            float drawMax = fused ? cxMin + thumbW : cxMax;
            Rect Thumb(float cx) => new Rect(cx - thumbW * 0.5f, s.y + (s.height - thumbH) * 0.5f, thumbW, thumbH);

            if (_g.bipolar && Collapsed)
            {
                float bx = XOf(Center, s);
                float l = Mathf.Min(bx, cxMin), r = Mathf.Max(bx, cxMin);
                Place(_emptyL, new Rect(s.x, trackY, l - s.x, trackH));
                Place(_fill, new Rect(l, trackY, r - l, trackH));
                Place(_emptyR, new Rect(r, trackY, s.xMax - r, trackH));
                _fillHover.style.display = DisplayStyle.None;
            }
            else
            {
                Place(_emptyL, new Rect(s.x, trackY, cxMin - s.x, trackH));
                var fillRect = new Rect(cxMin, trackY, cxMax - cxMin, trackH);
                Place(_fill, fillRect);
                Place(_emptyR, new Rect(drawMax, trackY, s.xMax - drawMax, trackH));
                bool hovered = !fused && fillRect.width > 0f && (_drag == Drag.Fill ||
                               (_drag == Drag.None && new Rect(cxMin, s.y, cxMax - cxMin, s.height).Contains(_pointer)));
                Place(_fillHover, fillRect, hovered);
            }
            Place(_thumbMin, Thumb(cxMin));
            Place(_thumbMax, Thumb(drawMax));
            _thumbMin.EnableInClassList("zui-skinminmax__edge--active", _drag == Drag.Min || (fused && _drag == Drag.Fill));
            _thumbMax.EnableInClassList("zui-skinminmax__edge--active", _drag == Drag.Max || (fused && _drag == Drag.Fill));
            if (_g.bipolar) Place(_thumbCenter, Thumb(XOf(Center, s)));
            else _thumbCenter.style.display = DisplayStyle.None;

            // Value area.
            bool collapsed = Collapsed;
            _compact.style.display = Compact ? DisplayStyle.Flex : DisplayStyle.None;
            if (Compact)
            {
                Place(_compact, new Rect(valueRect.x + 10f, valueRect.y, valueRect.width - 15f, valueRect.height));
                _compact.text = _compactText(_min, _max);
                _fieldOne.style.display = _fieldMin.style.display = _fieldMax.style.display = DisplayStyle.None;
            }
            else if (valueRect.width > 0f)
            {
                var fmt = Fmt;
                float P(float v) => float.Parse(v.ToString(fmt), System.Globalization.CultureInfo.InvariantCulture);
                if (collapsed)
                {
                    Place(_fieldOne, valueRect);
                    _fieldMin.style.display = _fieldMax.style.display = DisplayStyle.None;
                    if (_fieldOne.focusController?.focusedElement != _fieldOne) _fieldOne.SetValueWithoutNotify(P(_min));
                }
                else
                {
                    float half = (valueRect.width - 2f) * 0.5f;
                    _fieldOne.style.display = DisplayStyle.None;
                    Place(_fieldMin, new Rect(valueRect.x, valueRect.y, half, valueRect.height));
                    Place(_fieldMax, new Rect(valueRect.x + half + 2f, valueRect.y, half, valueRect.height));
                    if (_fieldMin.focusController?.focusedElement != _fieldMin) _fieldMin.SetValueWithoutNotify(P(_min));
                    if (_fieldMax.focusController?.focusedElement != _fieldMax) _fieldMax.SetValueWithoutNotify(P(_max));
                }
            }
            else _fieldOne.style.display = _fieldMin.style.display = _fieldMax.style.display = DisplayStyle.None;
        }

        // ─────────────────────────── input (the IMGUI rules) ───────────────────────────

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            Carve(out _, out var s, out _);
            var m = (Vector2)e.localPosition;
            if (!s.Contains(m)) return;
            float cxMin = XOf(_min, s), cxMax = XOf(_max, s), thumbW = ThumbW;
            bool fused = cxMax - cxMin < thumbW;
            float drawMax = fused ? cxMin + thumbW : cxMax;
            bool collapsed = Collapsed;

            if (e.clickCount == 2)
            {
                if (_g.bipolar && Mathf.Abs(m.x - XOf(Center, s)) <= CenterGrab)
                {
                    var fmt = Fmt;
                    bool atCenter = collapsed && _min.ToString(fmt) == Center.ToString(fmt);
                    if (atCenter)
                    {
                        float half = (_absMax - _absMin) * ExpandFraction * 0.5f;
                        Set(Mathf.Max(_absMin, Center - half), Mathf.Min(_absMax, Center + half));
                    }
                    else Set(Center, Center);
                }
                else if (collapsed)
                {
                    float range = _absMax - _absMin;
                    if (range > 0f)
                    {
                        float half = range * ExpandFraction * 0.5f, spread = range * ExpandFraction;
                        float nMin = Mathf.Max(_absMin, _min - half), nMax = Mathf.Min(_absMax, _min + half);
                        if (nMin == _absMin) nMax = Mathf.Min(_absMax, _absMin + spread);
                        else if (nMax == _absMax) nMin = Mathf.Max(_absMin, _absMax - spread);
                        Set(nMin, nMax);
                    }
                }
                else
                {
                    if (m.x <= (cxMin + drawMax) * 0.5f) Set(_min, _min); else Set(_max, _max);
                }
                _drag = Drag.None;
                e.StopPropagation();
                return;
            }

            float thumbH = _g.thumbHeight > 0f ? _g.thumbHeight : s.height;
            Rect Thumb(float cx) => new Rect(cx - thumbW * 0.5f, s.y + (s.height - thumbH) * 0.5f, thumbW, thumbH);
            if (fused)
            {
                if (new Rect(cxMin - thumbW * 0.5f, s.y, thumbW * 2f, s.height).Contains(m)) StartPan(m.x);
                else { bool left = m.x < cxMin; _drag = left ? Drag.Min : Drag.Max; DragTo(m.x, s); }
            }
            else
            {
                bool hitMin = Thumb(cxMin).Contains(m), hitMax = Thumb(drawMax).Contains(m);
                bool hitFill = !hitMin && !hitMax && cxMax - cxMin > 0f && new Rect(cxMin, s.y, cxMax - cxMin, s.height).Contains(m);
                if (hitFill) StartPan(m.x);
                else
                {
                    if (!hitMin && !hitMax) hitMin = Mathf.Abs(m.x - cxMin) <= Mathf.Abs(m.x - cxMax);
                    _drag = hitMin ? Drag.Min : Drag.Max;
                    DragTo(m.x, s);
                }
            }
            this.CapturePointer(e.pointerId);
            Layout();
            e.StopPropagation();
        }

        void StartPan(float x) { _drag = Drag.Fill; _anchorMin = _min; _anchorMax = _max; _anchorX = x; }

        void DragTo(float x, Rect s)
        {
            float v = Sample(x, s);
            if (_drag == Drag.Min) Set(Mathf.Clamp(v, _absMin, _max), _max);
            else if (_drag == Drag.Max) Set(_min, Mathf.Clamp(v, _min, _absMax));
        }

        void OnMove(PointerMoveEvent e)
        {
            _pointer = e.localPosition;
            if (_drag != Drag.None && this.HasPointerCapture(e.pointerId))
            {
                Carve(out _, out var s, out _);
                if (_drag == Drag.Fill)
                {
                    Travel(s, out _, out var len);
                    float dVal = (e.localPosition.x - _anchorX) / len * (_absMax - _absMin);
                    float span = _anchorMax - _anchorMin;
                    float nMin = Mathf.Clamp(_anchorMin + dVal, _absMin, _absMax - span);
                    Set(nMin, nMin + span);
                }
                else DragTo(e.localPosition.x, s);
            }
            Layout();
        }

        void OnUp(PointerUpEvent e)
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            Layout();
        }
    }
}
