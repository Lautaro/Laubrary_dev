using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit twin of the IMGUI ZUI.BandSliders (added for the Zounds UI Toolkit port, T-0459): a row of vertical
    /// bars, one per value, each set by dragging its height, with the same rules —
    ///
    /// - each bar is a groove (`zui-skinslider__rest`) with a fill (`zui-skinslider__fill`) from the baseline to its value,
    ///   so a skin that styles a slider's track and fill styles these bars identically;
    /// - pressing sets the bar under the pointer, dragging across bars paints them and fills in any the pointer skipped;
    /// - double-click resets a bar to the default when one is given;
    /// - a value outside the range is drawn at the edge with a bright cap and only changes if that bar is dragged;
    /// - the hovered or dragged bar is lightened, and a baseline strictly inside the range is drawn as one line across.
    ///
    /// Nothing is changed in place: <c>onChanged</c> receives a changed copy, so the caller can record Undo first.
    public class ZuiSkinBandSliders : VisualElement
    {
        static readonly CustomStyleProperty<float> BandGap = new CustomStyleProperty<float>("--zui-band-gap");
        static readonly CustomStyleProperty<float> BandCapThickness = new CustomStyleProperty<float>("--zui-band-cap-thickness");
        static readonly CustomStyleProperty<float> BandBaselineThickness = new CustomStyleProperty<float>("--zui-band-baseline-thickness");
        const float DefaultGap = 2f, DefaultCapThickness = 2f, DefaultBaselineThickness = 1f;
        float[] _values;
        readonly float _min, _max, _baseline;
        readonly float? _default;
        readonly Action<float[]> _onChanged;
        readonly Action _onBeforeMutate;
        readonly Func<int, string> _tooltipFor;
        VisualElement[] _bars, _fills, _caps, _hovers;
        readonly VisualElement _baseLine;
        int _lastIndex = -1, _hovered = -1;
        float _lastValue;
        bool _dragging;
        float _gap = DefaultGap, _capThickness = DefaultCapThickness, _baselineThickness = DefaultBaselineThickness;

        public ZuiSkinBandSliders(float[] values, float min, float max, float baseline, Action<float[]> onChanged,
                                  float? defaultValue = null, Func<int, string> tooltipFor = null, Action onBeforeMutate = null)
        {
            _min = min; _max = max; _baseline = baseline; _default = defaultValue;
            _onChanged = onChanged; _onBeforeMutate = onBeforeMutate; _tooltipFor = tooltipFor;
            AddToClassList("zui-skinband");
            AddToClassList("zui-band-sliders");
            _baseLine = new VisualElement { pickingMode = PickingMode.Ignore };
            _baseLine.AddToClassList("zui-band-sliders__baseline");
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            SetValues(values);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => { _hovered = -1; Layout(); });
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        /// Replaces the values shown (an edit made elsewhere, an undo); rebuilds the bars when the count changed.
        public void SetValues(float[] values)
        {
            int n = values?.Length ?? 0;
            bool rebuild = _values == null || _values.Length != n;
            _values = values != null ? (float[])values.Clone() : new float[0];
            if (rebuild)
            {
                Clear();
                _bars = new VisualElement[n]; _fills = new VisualElement[n]; _caps = new VisualElement[n]; _hovers = new VisualElement[n];
                for (int i = 0; i < n; i++)
                {
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.AddToClassList("zui-skinslider__rest");
                    bar.AddToClassList("zui-skinband__bar");
                    bar.AddToClassList("zui-band-sliders__band");
                    var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                    fill.AddToClassList("zui-skinslider__fill");
                    fill.AddToClassList("zui-band-sliders__fill");
                    var hover = new VisualElement { pickingMode = PickingMode.Ignore };
                    hover.AddToClassList("zui-band-sliders__hover");
                    var cap = new VisualElement { pickingMode = PickingMode.Ignore };
                    cap.AddToClassList("zui-band-sliders__overflow");
                    Add(bar); Add(fill); Add(hover); Add(cap);
                    _bars[i] = bar; _fills[i] = fill; _hovers[i] = hover; _caps[i] = cap;
                }
                Add(_baseLine);
            }
            Layout();
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            _gap = Metric(e.customStyle.TryGetValue(BandGap, out float gap) ? gap : DefaultGap, DefaultGap);
            _capThickness = Metric(e.customStyle.TryGetValue(BandCapThickness, out float cap) ? cap : DefaultCapThickness, DefaultCapThickness);
            _baselineThickness = Metric(e.customStyle.TryGetValue(BandBaselineThickness, out float baseline) ? baseline : DefaultBaselineThickness, DefaultBaselineThickness);
            Layout();
        }

        static float Metric(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);

        float Slot => _values.Length > 0 ? layout.width / _values.Length : 0f;
        int IndexAt(float x) => Mathf.Clamp((int)(x / Mathf.Max(1e-3f, Slot)), 0, _values.Length - 1);
        float ValueAt(float y) => Mathf.Clamp(Mathf.Lerp(_max, _min, Mathf.InverseLerp(0f, layout.height, y)), _min, _max);
        float YOf(float v) => Mathf.Lerp(layout.height, 0f, Mathf.InverseLerp(_min, _max, v));

        void Layout()
        {
            float w = layout.width, h = layout.height;
            if (float.IsNaN(w) || w <= 0f || _bars == null) return;
            float slot = Slot, yBase = YOf(Mathf.Clamp(_baseline, _min, _max));
            for (int i = 0; i < _values.Length; i++)
            {
                float x = i * slot + _gap * 0.5f, bw = Mathf.Max(1f, slot - _gap);
                _bars[i].style.left = x; _bars[i].style.width = bw;
                float v = _values[i], yv = YOf(Mathf.Clamp(v, _min, _max));
                var f = _fills[i];
                f.style.left = x; f.style.width = bw;
                f.style.top = Mathf.Min(yv, yBase); f.style.height = Mathf.Max(1f, Mathf.Abs(yBase - yv));
                var hv = _hovers[i];
                hv.style.left = x; hv.style.width = bw;
                hv.style.display = i == _hovered || (_dragging && i == _lastIndex) ? DisplayStyle.Flex : DisplayStyle.None;
                var c = _caps[i];
                bool outside = v > _max || v < _min;
                c.style.display = outside ? DisplayStyle.Flex : DisplayStyle.None;
                c.style.left = x; c.style.width = bw; c.style.height = _capThickness; c.style.top = v > _max ? 0f : h - _capThickness;
            }
            _baseLine.style.display = _baseline > _min && _baseline < _max ? DisplayStyle.Flex : DisplayStyle.None;
            _baseLine.style.height = _baselineThickness;
            _baseLine.style.top = yBase - _baselineThickness * 0.5f;
        }

        void Set(float[] copy, int i, float v, ref bool changed)
        {
            if (!Mathf.Approximately(copy[i], v)) { copy[i] = v; changed = true; }
        }

        void Commit(float[] copy, bool changed)
        {
            if (!changed) return;
            _onBeforeMutate?.Invoke();
            _values = copy;
            Layout();
            _onChanged?.Invoke((float[])copy.Clone());
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || _values.Length == 0) return;
            var copy = (float[])_values.Clone();
            bool changed = false;
            int i = IndexAt(e.localPosition.x);
            if (e.clickCount == 2 && _default.HasValue) Set(copy, i, Mathf.Clamp(_default.Value, _min, _max), ref changed);
            else
            {
                _dragging = true; _lastIndex = i; _lastValue = ValueAt(e.localPosition.y);
                Set(copy, i, _lastValue, ref changed);
                this.CapturePointer(e.pointerId);
            }
            Commit(copy, changed);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_values.Length == 0) return;
            int i = IndexAt(e.localPosition.x);
            if (!_dragging || !this.HasPointerCapture(e.pointerId))
            {
                if (i != _hovered) { _hovered = i; tooltip = _tooltipFor?.Invoke(i) ?? tooltip; Layout(); }
                return;
            }
            float v = ValueAt(e.localPosition.y);
            var copy = (float[])_values.Clone();
            bool changed = false;
            // Fill in every bar between the last one touched and this one, so a quick sweep leaves no gaps.
            int from = _lastIndex < 0 ? i : _lastIndex, step = i >= from ? 1 : -1;
            for (int k = from; k != i + step; k += step)
            {
                float t = i == from ? 1f : (float)(k - from) / (i - from);
                Set(copy, k, Mathf.Lerp(_lastValue, v, t), ref changed);
            }
            _lastIndex = i; _lastValue = v;
            Commit(copy, changed);
        }

        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false; _lastIndex = -1;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            Layout();
        }
    }
}
