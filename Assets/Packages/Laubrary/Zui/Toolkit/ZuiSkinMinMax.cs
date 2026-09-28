using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI ZUI MicroMinMax (a min/max range inside one chromeless track), built from ordinary
    /// elements and styled entirely by USS, for the Zounds UI Toolkit port (T-0458). Behaviour is the IMGUI one, point
    /// for point (ZUISlider.DrawMicroMinMax):
    ///
    /// - COLLAPSED (both ends show the same number in the format) it looks and acts like a single slider: fill from the
    ///   start (or from the centre, for a bipolar style) to the value, and a press sets both ends.
    /// - Otherwise: rest | fill | rest, with an edge line at each end (`__edge--min/--max`, styled as the style's thumb,
    ///   hover and pressed included). A press within 6 px of an edge drags that edge; inside the fill pans both; on the
    ///   empty rail moves the nearer edge.
    /// - Double-click: collapsed -> expands symmetrically by 10 % of the range (keeping the spread if one side clips);
    ///   expanded -> collapses toward the clicked side. On a bipolar style, double-click on the centre marker collapses to
    ///   the centre, and again expands around it.
    /// - The label is centred and fitted like the IMGUI one (LabelOnly / ValuesOnly "a-b" / LabelAndValues "label a-b").
    /// - Optional number boxes on the right (each 60 % of the style's value width, 4 px gaps), merged into one while
    ///   collapsed.
    public class ZuiSkinMinMax : VisualElement
    {
        public enum LabelMode { None, LabelOnly, ValuesOnly, LabelAndValues }

        readonly VisualElement _track, _restL, _fill, _restR, _edgeMin, _edgeMax, _center;
        readonly Label _label;
        readonly FloatField _fieldMin, _fieldMax, _fieldOne;
        readonly Action<float, float> _onChanged;
        readonly Action _onBeforeMutate;
        readonly string _text, _fmt;
        readonly float _absMin, _absMax;
        readonly bool _bipolar;
        readonly float _center01;
        readonly LabelMode _mode;
        float _lo, _hi;
        enum Drag { None, Min, Max, Fill }
        Drag _drag;
        float _anchorLo, _anchorHi, _anchorX;

        const float EdgeGrab = 6f, ExpandFraction = 0.1f;

        public float min => _lo;
        public float max => _hi;

        public ZuiSkinMinMax(string text, float lo, float hi, float absMin, float absMax, string tooltip,
                             Action<float, float> onChanged, LabelMode mode = LabelMode.LabelAndValues, bool bipolar = false,
                             float bipolarCenter = float.NaN, bool showFields = false, float valueWidth = 40f,
                             string format = null, Action onBeforeMutate = null)
        {
            _text = text; _absMin = absMin; _absMax = absMax; _mode = mode; _bipolar = bipolar;
            _fmt = string.IsNullOrEmpty(format) ? ZuiSkinTrackLabel.AutoFormat(absMin, absMax) : format;
            float c = float.IsNaN(bipolarCenter) ? (absMin + absMax) * 0.5f : Mathf.Clamp(bipolarCenter, absMin, absMax);
            _center01 = absMax > absMin ? Mathf.InverseLerp(absMin, absMax, c) : 0f;
            _onChanged = onChanged; _onBeforeMutate = onBeforeMutate;
            this.tooltip = tooltip;
            AddToClassList("zui-skinminmax");
            style.flexDirection = FlexDirection.Row;
            style.flexShrink = 0;

            _track = new VisualElement();
            _track.AddToClassList("zui-skinminmax__track");
            _track.style.flexGrow = 1; _track.style.flexShrink = 1;
            Add(_track);
            VisualElement Part(string cls, bool absolute)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList(cls);
                if (absolute) { e.style.position = Position.Absolute; e.style.top = 0; e.style.bottom = 0; }
                _track.Add(e);
                return e;
            }
            _restL = Part("zui-skinslider__rest", true);
            _fill = Part("zui-skinslider__fill", true);
            _restR = Part("zui-skinslider__rest", true);
            _edgeMin = Part("zui-skinminmax__edge--min", true);
            _edgeMax = Part("zui-skinminmax__edge--max", true);
            _center = Part("zui-skinminmax__edge--center", true);
            _edgeMin.pickingMode = _edgeMax.pickingMode = _center.pickingMode = PickingMode.Position;   // for :hover, as the IMGUI thumbs react to the pointer
            _label = ZuiSkinTrackLabel.Create();
            _track.Add(_label);

            if (showFields && valueWidth > 0f)
            {
                float per = valueWidth * 0.6f;
                FloatField Field(Action<float> set)
                {
                    var f = new FloatField { isDelayed = true };
                    f.AddToClassList("zui-skinminmax__field");
                    f.style.width = per; f.style.marginLeft = 4f; f.style.flexShrink = 0;
                    f.RegisterValueChangedCallback(e => set(e.newValue));
                    Add(f);
                    return f;
                }
                _fieldMin = Field(v => Commit(Mathf.Clamp(v, _absMin, _hi), _hi));
                _fieldMax = Field(v => Commit(_lo, Mathf.Clamp(v, _lo, _absMax)));
                _fieldOne = new FloatField { isDelayed = true };
                _fieldOne.AddToClassList("zui-skinminmax__field");
                _fieldOne.style.width = per * 2f + 4f; _fieldOne.style.marginLeft = 4f; _fieldOne.style.flexShrink = 0;
                _fieldOne.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, _absMin, _absMax); Commit(v, v); });
                Add(_fieldOne);
            }

            _track.RegisterCallback<PointerDownEvent>(OnDown);
            _track.RegisterCallback<PointerMoveEvent>(OnMove);
            _track.RegisterCallback<PointerUpEvent>(OnUp);
            _track.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            SetValuesWithoutNotify(lo, hi);
        }

        public void SetValuesWithoutNotify(float lo, float hi)
        {
            _lo = Mathf.Clamp(lo, _absMin, _absMax);
            _hi = Mathf.Clamp(hi, _lo, _absMax);
            Refresh();
        }

        string F(float v) => v.ToString(_fmt, CultureInfo.InvariantCulture);
        bool Collapsed => F(_lo) == F(_hi);
        float T(float v) => _absMax > _absMin ? Mathf.InverseLerp(_absMin, _absMax, v) : 0f;

        static void Span(VisualElement e, float a01, float b01)
        {
            e.style.left = Length.Percent(a01 * 100f);
            e.style.width = Length.Percent(Mathf.Max(0f, b01 - a01) * 100f);
            e.style.display = b01 - a01 > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Edge(VisualElement e, float at01, bool show)
        {
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            e.style.left = Length.Percent(at01 * 100f);
            float w = e.resolvedStyle.width;
            e.style.marginLeft = float.IsNaN(w) ? 0f : -w * 0.5f;
        }

        void Refresh()
        {
            float a = T(_lo), b = T(_hi);
            bool collapsed = Collapsed;
            if (collapsed)
            {
                float from = _bipolar ? Mathf.Min(_center01, a) : 0f, to = _bipolar ? Mathf.Max(_center01, a) : a;
                Span(_restL, 0f, from); Span(_fill, from, to); Span(_restR, to, 1f);
            }
            else { Span(_restL, 0f, a); Span(_fill, a, b); Span(_restR, b, 1f); }
            Edge(_edgeMin, a, !collapsed);
            Edge(_edgeMax, b, !collapsed);
            Edge(_center, _center01, _bipolar);

            string vals = collapsed ? F(_lo) : F(_lo) + "-" + F(_hi);
            string text = _mode == LabelMode.None ? "" : _mode == LabelMode.LabelOnly ? _text
                        : _mode == LabelMode.ValuesOnly ? vals
                        : string.IsNullOrEmpty(_text) ? vals : _text + " " + vals;
            ZuiSkinTrackLabel.Fit(_label, text, _mode == LabelMode.LabelAndValues ? vals : null, _track.resolvedStyle.width);

            if (_fieldOne != null)
            {
                _fieldOne.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
                _fieldMin.style.display = _fieldMax.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                _fieldOne.SetValueWithoutNotify(float.Parse(F(_lo), CultureInfo.InvariantCulture));
                _fieldMin.SetValueWithoutNotify(float.Parse(F(_lo), CultureInfo.InvariantCulture));
                _fieldMax.SetValueWithoutNotify(float.Parse(F(_hi), CultureInfo.InvariantCulture));
            }
        }

        void Commit(float lo, float hi)
        {
            lo = Mathf.Clamp(lo, _absMin, _absMax); hi = Mathf.Clamp(hi, lo, _absMax);
            if (Mathf.Approximately(lo, _lo) && Mathf.Approximately(hi, _hi)) { Refresh(); return; }
            _onBeforeMutate?.Invoke();
            _lo = lo; _hi = hi; Refresh();
            _onChanged?.Invoke(_lo, _hi);
        }

        float Sample(float x)
        {
            float w = Mathf.Max(1f, _track.resolvedStyle.width);
            return Mathf.Round(Mathf.Lerp(_absMin, _absMax, Mathf.Clamp01(x / w)) * 100000f) / 100000f;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            float w = Mathf.Max(1f, _track.resolvedStyle.width);
            float mx = e.localPosition.x;
            float xMin = T(_lo) * w, xMax = T(_hi) * w;
            bool collapsed = Collapsed;
            e.StopPropagation();

            if (e.clickCount == 2)
            {
                float cx = _center01 * w;
                if (_bipolar && Mathf.Abs(mx - cx) <= EdgeGrab)
                {
                    float cv = Mathf.Lerp(_absMin, _absMax, _center01);
                    if (collapsed && F(_lo) == F(cv)) { float half = (_absMax - _absMin) * ExpandFraction * 0.5f; Commit(Mathf.Max(_absMin, cv - half), Mathf.Min(_absMax, cv + half)); }
                    else Commit(cv, cv);
                    return;
                }
                if (collapsed)
                {
                    float range = _absMax - _absMin; if (range <= 0f) return;
                    float half = range * ExpandFraction * 0.5f, spread = range * ExpandFraction;
                    float nMin = Mathf.Max(_absMin, _lo - half), nMax = Mathf.Min(_absMax, _lo + half);
                    if (nMin == _absMin) nMax = Mathf.Min(_absMax, _absMin + spread);
                    else if (nMax == _absMax) nMin = Mathf.Max(_absMin, _absMax - spread);
                    Commit(nMin, nMax);
                }
                else if (mx <= (xMin + xMax) * 0.5f) Commit(_lo, _lo);
                else Commit(_hi, _hi);
                return;
            }

            if (collapsed)
            {
                float v = Sample(mx);
                Commit(v, v);
                Begin(e, Drag.Fill, mx);
                return;
            }
            bool nearMin = Mathf.Abs(mx - xMin) <= EdgeGrab, nearMax = Mathf.Abs(mx - xMax) <= EdgeGrab;
            if (!nearMin && !nearMax && mx > xMin && mx < xMax) { Begin(e, Drag.Fill, mx); return; }
            bool grabMin = nearMin;
            if (nearMin && nearMax) grabMin = mx <= (xMin + xMax) * 0.5f;
            else if (!nearMin && !nearMax) grabMin = Mathf.Abs(mx - xMin) <= Mathf.Abs(mx - xMax);
            Begin(e, grabMin ? Drag.Min : Drag.Max, mx);
            DragEdge(mx);
        }

        void Begin(PointerDownEvent e, Drag d, float mx)
        {
            _drag = d; _anchorLo = _lo; _anchorHi = _hi; _anchorX = mx;
            _track.CapturePointer(e.pointerId);
            _edgeMin.EnableInClassList("zui-skinminmax__edge--active", d == Drag.Min);
            _edgeMax.EnableInClassList("zui-skinminmax__edge--active", d == Drag.Max);
        }

        void DragEdge(float mx)
        {
            float v = Sample(mx);
            if (_drag == Drag.Min) Commit(Mathf.Clamp(v, _absMin, _hi), _hi);
            else Commit(_lo, Mathf.Clamp(v, _lo, _absMax));
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_drag == Drag.None || !_track.HasPointerCapture(e.pointerId)) return;
            float mx = e.localPosition.x;
            if (_drag == Drag.Fill)
            {
                float w = Mathf.Max(1f, _track.resolvedStyle.width);
                float dVal = (mx - _anchorX) / w * (_absMax - _absMin);
                float span = _anchorHi - _anchorLo;
                float nMin = Mathf.Clamp(_anchorLo + dVal, _absMin, _absMax - span);
                Commit(nMin, nMin + span);
            }
            else DragEdge(mx);
        }

        void OnUp(PointerUpEvent e)
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            _edgeMin.RemoveFromClassList("zui-skinminmax__edge--active");
            _edgeMax.RemoveFromClassList("zui-skinminmax__edge--active");
            if (_track.HasPointerCapture(e.pointerId)) _track.ReleasePointer(e.pointerId);
        }
    }
}
