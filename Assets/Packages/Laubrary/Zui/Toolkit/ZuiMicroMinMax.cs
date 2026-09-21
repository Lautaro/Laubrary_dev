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
// else. Double-click in Range resets both handles to the given defaults, or the full min/max span if none
// were given; in Fixed it resets to the given default (low, or high if only that was given) and does nothing
// at all if neither was given — the same "no default, no-op" contract as ZuiMicroSlider (T-0372: it used to
// invent a reset-to-track-minimum instead, which read as the value collapsing).
//
// Fixed/Range mode (T-0360): RIGHT-CLICK opens a Fixed/Range menu, same gesture as Z.Value's mode menu.
// Range is the two-handle band above; Fixed collapses the control to a single MicroSlider-style fill so
// setting one value is one drag instead of two (dragging both handles onto the same spot). Switching mode
// collapses high to the low value (→ Fixed) or, going the other way, nudges high off low so there is a band
// to grab again — unless low is already at the top of the track, where nothing is left to nudge high INTO,
// in which case low is nudged down instead so the band still has width (→ Range; T-0372, a value already at
// max used to switch to Range as an empty-looking zero-width band). A caller that constructs the control with
// low == high starts in Fixed; anything else starts in Range — no new constructor argument, so every existing
// caller is unaffected.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiMicroMinMax : VisualElement
    {
        /// Fixed = one value, one drag (drawn as a single MicroSlider-style fill). Range = the two-handle band.
        public enum RangeMode { Fixed, Range }

        float _low, _high, _min, _max;
        readonly float? _lowDefault, _highDefault;
        readonly Action<float, float> _onChanged;
        readonly Action _onBeforeMutate;
        readonly Label _caption, _valueLabel;
        readonly bool _showValue;
        readonly int _decimals;
        readonly string _baseTooltip;

        RangeMode _mode;
        public RangeMode Mode => _mode;

        // Which handle a drag gesture is moving: both == a pan (span held fixed). Only used in Range mode.
        enum Grab { Low, High, Both }
        bool _dragging, _gestureOpen;
        int _undoGroup = -1;   // the Undo group the open gesture collapses into — see ZuiUndoGesture
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
            _baseTooltip = tooltip;
            // Range by default — UNLESS both ends already came in equal, which reads as "this was already a
            // fixed value" (T-0360).
            _mode = Mathf.Approximately(_low, _high) ? RangeMode.Fixed : RangeMode.Range;

            UpdateTooltip();

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
            _valueLabel.text = _mode == RangeMode.Fixed
                ? _low.ToString(fmt)
                : _low.ToString(fmt) + " – " + _high.ToString(fmt);
        }

        void UpdateTooltip()
        {
            string hint = _mode == RangeMode.Fixed
                ? "Drag to set one value; Shift = fine"
                : "Drag an edge to move one handle, the middle to pan both; Shift = fine";
            this.tooltip = _baseTooltip + "  ·  " + hint
                + ((_lowDefault.HasValue || _highDefault.HasValue) ? "; double-click resets to default." : ".")
                + "  ·  Right-click for Fixed/Range mode.";
        }

        void ShowModeMenu()
        {
            var menu = Z.Menu(this);
            menu.Radio(null, new[] { "Fixed", "Range" }, (int)_mode,
                "Fixed sets one value with a single drag. Range sets a low–high band with two handles.",
                i => SetMode((RangeMode)i), closeOnSelect: true);
            menu.Show();
        }

        void SetMode(RangeMode mode)
        {
            if (_mode == mode) return;
            OpenGesture();
            _mode = mode;
            if (_mode == RangeMode.Fixed)
            {
                // Collapse to the low value — the same value the band's low edge already showed.
                SetValues(_low, _low, notify: true);
            }
            else
            {
                // Nudge high off low so there is a band to grab again, instead of handing back a control
                // whose only usable gesture is the razor-thin "outside the band" jump. At the very top of the
                // track low + nudge clamps straight back down to low (T-0372: Alpha 1.00 -> Fixed -> Range gave
                // "1.00 - 1.00", an empty-looking band) — nudge the LOW end down instead whenever the high end
                // has nowhere left to go.
                float nudge = Mathf.Max((_max - _min) * 0.05f, 1e-3f);
                if (_low + nudge <= _max) SetValues(_low, _low + nudge, notify: true);
                else SetValues(Mathf.Max(_min, _low - nudge), _low, notify: true);
            }
            CloseGesture();
            UpdateTooltip();
            UpdateValueLabel();
            MarkDirtyRepaint();
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
            _undoGroup = ZuiUndoGesture.Begin();   // one drag is one Undo step, however many moves it raises
            _onBeforeMutate?.Invoke();
        }

        void CloseGesture()
        {
            if (!_gestureOpen) return;
            _gestureOpen = false;
            ZuiUndoGesture.End(_undoGroup);
            _undoGroup = -1;
        }

        void OnDown(PointerDownEvent e)
        {
            // Right-click opens the Fixed/Range mode menu — same gesture as Z.Value's mode menu.
            if (e.button == 1)
            {
                ShowModeMenu();
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                if (_mode == RangeMode.Fixed)
                {
                    // Same contract as ZuiMicroSlider: double-click resets to the caller's default, and does
                    // NOTHING when none was given — it must never invent one by falling back to the track's
                    // minimum, which read as "the value nearly vanished" for a control like Scale where 0.01
                    // is just the bottom of the track, not a meaningful value (T-0372).
                    if (!_lowDefault.HasValue && !_highDefault.HasValue) return;
                    OpenGesture();
                    float rv = _lowDefault ?? _highDefault.Value;
                    SetValues(rv, rv, notify: true);
                    CloseGesture();
                }
                else
                {
                    // Range has no single "the default" to fall back to doing nothing for, so an unset side
                    // resets to that side of the whole track — the band a fresh control already opens with.
                    OpenGesture();
                    float rlo = _lowDefault ?? _min, rhi = _highDefault ?? _max;
                    SetValues(rlo, rhi, notify: true);
                    CloseGesture();
                }
                e.StopPropagation();
                return;
            }

            float x = e.localPosition.x;
            if (_mode == RangeMode.Range)
            {
                float xLo = XFromValue(_low), xHi = XFromValue(_high);
                _grab = Mathf.Abs(x - xLo) <= Mathf.Abs(x - xHi) && Mathf.Abs(x - xLo) <= HandleGrabPx ? Grab.Low
                      : Mathf.Abs(x - xHi) <= HandleGrabPx ? Grab.High
                      : x > xLo && x < xHi ? Grab.Both
                      // Outside the band: jump the nearer handle straight to the press point.
                      : Mathf.Abs(x - xLo) <= Mathf.Abs(x - xHi) ? Grab.Low : Grab.High;
            }

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
            if (_mode == RangeMode.Fixed)
            {
                float v = absolute ? ValueFromX(x) : _low + FineDelta(x);
                SetValues(v, v, notify: true);
                return;
            }
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
            CloseGesture();
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 1f || r.height <= 1f) return;

            var p = mgc.painter2D;
            FillRect(p, 0f, 0f, r.width, r.height, TrackColor);

            if (_mode == RangeMode.Fixed)
            {
                // Same "fill from the left up to the value" picture as a plain ZuiMicroSlider — one handle,
                // one drag — so switching Fixed/Range reads as the same control changing shape, not a
                // different control replacing it.
                float split = Mathf.Round(XFromValue(_low));
                if (split > 0.5f) FillGradientH(p, 0f, split, r.height, FillLeft, FillRight);
                return;
            }

            float xLo = XFromValue(_low), xHi = XFromValue(_high);
            if (xHi - xLo > 0.5f) FillGradientH(p, xLo, xHi - xLo, r.height, FillLeft, FillRight);
            else
            {
                // A band that collapsed to zero width (both handles on the same value) still needs to read as
                // "there is a band here", not as an empty track that looks broken or unset — a thin fixed-width
                // tick at the shared value, same idea as the Fixed-mode fill's own single edge (T-0372).
                const float tick = 2f;
                FillRect(p, Mathf.Clamp(xLo - tick * 0.5f, 0f, Mathf.Max(0f, r.width - tick)), 0f, tick, r.height,
                    Color.Lerp(FillLeft, FillRight, 0.5f));
            }
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
