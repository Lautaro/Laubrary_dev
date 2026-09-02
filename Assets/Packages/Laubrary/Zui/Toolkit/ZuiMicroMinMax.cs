// ZuiMicroMinMax — a two-handle range slider drawn in the ZuiMicroSlider idiom: a filled BAND between the
// low and high values inside an otherwise empty track, label at the left, "low – high" readout at the
// right, both sitting inside the track. No native `MinMaxSlider`, no separate flanking numeric fields —
// this is the embedded-style range control the ui-layout-rules.md "MicroMinMax not built yet" note asked
// for, so a min/max pair reads exactly as compact as a single MicroSlider.
//
// Grab behaviour: press near the LOW edge moves only the low handle; press near the HIGH edge moves only
// the high handle; press INSIDE the band pans both together (span held fixed); press OUTSIDE the band jumps
// the nearer handle to the press point (an absolute jump, matching MicroSlider's own click-to-set). Shift
// held during any of these drags is the same "fine" relative nudge MicroSlider and ZuiScrub use everywhere
// else. Double-click resets both handles to the given defaults, or the full min/max span if none were given.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiMicroMinMax : VisualElement
    {
        float _low, _high, _min, _max;
        readonly float? _lowDefault, _highDefault;
        readonly Action<float, float> _onChanged;
        readonly Action _onBeforeMutate;
        readonly Label _caption, _valueLabel;
        readonly bool _showValue;
        readonly int _decimals;

        // Which handle a drag gesture is moving: both == a pan (span held fixed).
        enum Grab { Low, High, Both }
        bool _dragging, _gestureOpen;
        Grab _grab;
        float _lastMoveX;
        float _panLow, _panHigh; // low/high at gesture start, for a Both (pan) drag's relative math

        // Same fine-drag factor as ZuiMicroSlider/ZuiScrub — Shift always means "gentle" across ZUI.
        const float FineFactor = 0.15f;
        // Pixel radius around a handle's screen position that counts as "grabbing that handle" rather
        // than panning the band or jumping to a click.
        const float HandleGrabPx = 6f;

        public float low => _low;
        public float high => _high;

        public ZuiMicroMinMax(string label, float low, float high, float min, float max, string tooltip,
            Action<float, float> onChanged, bool showValue = true, float? lowDefault = null,
            float? highDefault = null, Action onBeforeMutate = null, int decimals = -1)
        {
            _min = min; _max = Mathf.Max(min + 1e-6f, max);
            _low = Mathf.Clamp(low, _min, _max);
            _high = Mathf.Clamp(Mathf.Max(high, _low), _min, _max);
            _onChanged = onChanged; _lowDefault = lowDefault; _highDefault = highDefault;
            _onBeforeMutate = onBeforeMutate; _showValue = showValue; _decimals = decimals;

            this.tooltip = tooltip + "  ·  Drag an edge to move one handle, the middle to pan both; Shift = fine"
                + ((_lowDefault.HasValue || _highDefault.HasValue) ? "; double-click resets to default." : ".");

            AddToClassList("zui-microslider");
            AddToClassList("zui-microminmax");

            _caption = new Label(label) { pickingMode = PickingMode.Ignore, tooltip = tooltip };
            _caption.AddToClassList("zui-microslider__caption");
            Add(_caption);

            _valueLabel = new Label { pickingMode = PickingMode.Ignore };
            _valueLabel.AddToClassList("zui-microslider__value");
            Add(_valueLabel);

            UpdateValueLabel();
            generateVisualContent += OnGenerate;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        float Round(float v) => _decimals >= 0 ? (float)Math.Round(v, _decimals) : (float)Math.Round(v, 5);

        void SetValues(float lo, float hi, bool notify)
        {
            lo = Round(Mathf.Clamp(lo, _min, _max));
            hi = Round(Mathf.Clamp(Mathf.Max(hi, lo), _min, _max));
            if (Mathf.Approximately(lo, _low) && Mathf.Approximately(hi, _high) && notify) return;
            _low = lo; _high = hi;
            UpdateValueLabel();
            MarkDirtyRepaint();
            if (notify) _onChanged?.Invoke(_low, _high);
        }

        void UpdateValueLabel()
        {
            if (!_showValue) return;
            float span = _max - _min;
            string fmt = _decimals >= 0 ? "F" + _decimals : span <= 3f ? "0.##" : span <= 40f ? "0.#" : "0";
            _valueLabel.text = _low.ToString(fmt) + " – " + _high.ToString(fmt);
        }

        float XFromValue(float v)
        {
            float w = Mathf.Max(1f, contentRect.width);
            return Mathf.InverseLerp(_min, _max, v) * w;
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
            _onBeforeMutate?.Invoke();
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                OpenGesture();
                float rlo = _lowDefault ?? _min, rhi = _highDefault ?? _max;
                SetValues(rlo, rhi, notify: true);
                _gestureOpen = false;
                e.StopPropagation();
                return;
            }

            float x = e.localPosition.x;
            float xLo = XFromValue(_low), xHi = XFromValue(_high);
            _grab = Mathf.Abs(x - xLo) <= Mathf.Abs(x - xHi) && Mathf.Abs(x - xLo) <= HandleGrabPx ? Grab.Low
                  : Mathf.Abs(x - xHi) <= HandleGrabPx ? Grab.High
                  : x > xLo && x < xHi ? Grab.Both
                  // Outside the band: jump the nearer handle straight to the press point.
                  : Mathf.Abs(x - xLo) <= Mathf.Abs(x - xHi) ? Grab.Low : Grab.High;

            _dragging = true;
            _lastMoveX = x;
            _panLow = _low; _panHigh = _high;
            this.CapturePointer(e.pointerId);
            OpenGesture();
            if (!e.shiftKey) ApplyDrag(x, absolute: true);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            float x = e.localPosition.x;
            ApplyDrag(x, absolute: !e.shiftKey);
            _lastMoveX = x;
            e.StopPropagation();
        }

        void ApplyDrag(float x, bool absolute)
        {
            switch (_grab)
            {
                case Grab.Low:
                    SetValues(absolute ? ValueFromX(x) : _low + FineDelta(x), _high, notify: true);
                    break;
                case Grab.High:
                    SetValues(_low, absolute ? ValueFromX(x) : _high + FineDelta(x), notify: true);
                    break;
                default: // Both — pan, span held fixed, clamped so neither handle leaves [min,max]
                    float span = _panHigh - _panLow;
                    // T-0204 — a band that already spans the WHOLE track (a fresh layer's default full-range
                    // Lifetime: low==min, high==max) has nowhere to pan: `_max - span` equals `_min`, so the
                    // clamp below always resolves to the same low/high pair and the drag is a silent no-op.
                    // Since a full-width band also means almost every press classifies as Grab.Both (the only
                    // way in is a razor-thin HandleGrabPx sliver at each edge), this made the control read as
                    // "there is no way to change it" (owner) even though pointer events were arriving fine —
                    // degrade to the same nearer-handle move an OUTSIDE-the-band press already gets, so a press
                    // anywhere in a full (or nearly full) band still does something.
                    if (span >= _max - _min - 1e-4f)
                    {
                        if (Mathf.Abs(x - XFromValue(_panLow)) <= Mathf.Abs(x - XFromValue(_panHigh)))
                            SetValues(absolute ? ValueFromX(x) : _low + FineDelta(x), _high, notify: true);
                        else
                            SetValues(_low, absolute ? ValueFromX(x) : _high + FineDelta(x), notify: true);
                        break;
                    }
                    float target = absolute ? ValueFromX(x) - span * 0.5f
                        : _low + FineDelta(x);
                    target = Mathf.Clamp(target, _min, _max - span);
                    SetValues(target, target + span, notify: true);
                    break;
            }
        }

        float FineDelta(float x)
        {
            float span = _max - _min;
            float w = Mathf.Max(1f, contentRect.width);
            return (x - _lastMoveX) / w * span * FineFactor;
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
            float xLo = XFromValue(_low), xHi = XFromValue(_high);

            var p = mgc.painter2D;
            FillRect(p, 0f, 0f, r.width, r.height, TrackColor);
            if (xHi - xLo > 0.5f) FillGradientH(p, xLo, xHi - xLo, r.height, FillLeft, FillRight);
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

        // Same cheap strip-gradient approach as ZuiMicroSlider — Painter2D has no native gradient fill.
        static void FillGradientH(Painter2D p, float xStart, float w, float h, Color left, Color right)
        {
            const float step = 2f;
            for (float x = 0f; x < w; x += step)
            {
                float t = w > 1f ? Mathf.Clamp01((x + step * 0.5f) / w) : 0f;
                float wSeg = Mathf.Min(step, w - x);
                FillRect(p, xStart + x, 0f, wSeg, h, Color.Lerp(left, right, t));
            }
        }

        public Color FillLeft = new Color(28f / 255f, 44f / 255f, 78f / 255f, 0.92f);
        public Color FillRight = new Color(70f / 255f, 120f / 255f, 200f / 255f, 0.85f);
        public Color TrackColor = new Color(0f, 0f, 0f, 0.30f);
    }
}
