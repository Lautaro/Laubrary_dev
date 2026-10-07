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
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiMicroSlider : VisualElement
    {
        static readonly CustomStyleProperty<Color> SliderFillLeftProperty = new("--zui-slider-fill-left");
        static readonly CustomStyleProperty<Color> SliderFillRightProperty = new("--zui-slider-fill-right");
        static readonly CustomStyleProperty<Color> SliderTrackColorProperty = new("--zui-slider-track-color");

        float _value, _min, _max;
        readonly float? _default;
        readonly Action<float> _onChanged;
        readonly Action _onBeforeMutate;
        readonly Label _caption, _valueLabel;
        // Display prefs (#10): togglable via the right-click menu when a prefsKey is given, persisted in EditorPrefs.
        // Without a key they're fixed at the ctor defaults (value label = showValue arg, numeric input off) and no
        // menu is offered — a stateless slider can't remember a toggle across ZUI's element rebuilds.
        readonly string _prefsKey;
        FloatField _numField;
        bool _showValueLabel;
        bool _showNumInput;
        readonly int _decimals;
        readonly bool _logarithmic;
        bool _dragging, _gestureOpen;
        int _undoGroup = -1;   // the Undo group the open gesture collapses into — see ZuiUndoGesture
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
            Action onBeforeMutate = null, int decimals = -1, string prefsKey = null, bool logarithmic = false)
        {
            _min = min; _max = Mathf.Max(min + 1e-6f, max);
            _logarithmic = logarithmic && min > 0f;
            _value = Mathf.Clamp(value, _min, _max);
            // Clamp the default into the track the same way SetValue would, so the tooltip never PROMISES a
            // number the double-click cannot actually produce (a default outside a narrowed [Range] used to
            // read as a broken reset).
            _onChanged = onChanged;
            _default = defaultValue.HasValue ? Mathf.Clamp(defaultValue.Value, _min, _max) : (float?)null;
            _onBeforeMutate = onBeforeMutate; _decimals = decimals;
            // The display-options menu is now the DEFAULT for every ZUI MicroSlider — when no explicit prefsKey is
            // given, the caption is the persistence key ("lbl:<label>"), so a slider remembers its toggles by name.
            // An explicit prefsKey still wins (e.g. to disambiguate two same-named sliders).
            _prefsKey = string.IsNullOrEmpty(prefsKey) ? "lbl:" + (label ?? "") : prefsKey;
            _showValueLabel = EditorPrefs.GetBool(PrefKey("val"), showValue);
            _showNumInput   = EditorPrefs.GetBool(PrefKey("num"), false);
            // Append the drag-modifier hint to the hover tooltip so the Shift-fine / double-click gestures are
            // discoverable (the element itself is what receives the hover — the caption ignores picking).
            // THE ONE PLACE the reset suffix is composed: every slider in the toolkit — hand-written or drawn
            // by ZuiReflect — gets the same sentence, with the actual number spelled out rather than the word
            // "default", so the author can tell what a double-click will do without performing it first.
            this.tooltip = tooltip + "  ·  Drag to set; Shift = fine"
                + (_default.HasValue ? ". Double-click to reset to " + Format(_default.Value) + "." : ".")
                + (HasPrefs ? "  ·  Right-click for display options." : "");

            AddToClassList("zui-microslider");
            AddToClassList("zui-slider");

            _caption = new Label(label) { pickingMode = PickingMode.Ignore, tooltip = tooltip };
            _caption.AddToClassList("zui-microslider__caption");
            _caption.AddToClassList("zui-slider__label");
            Add(_caption);

            _valueLabel = new Label { pickingMode = PickingMode.Ignore };
            _valueLabel.AddToClassList("zui-microslider__value");
            _valueLabel.AddToClassList("zui-slider__value");
            Add(_valueLabel);

            // The optional numeric-input field (#10) sits over the right of the track; hidden unless toggled on.
            // It swallows its own pointer-downs so typing/clicking it never starts a track drag.
            _numField = new FloatField { isDelayed = true };
            if (decimals >= 0) _numField.formatString = "F" + Mathf.Clamp(decimals, 0, 7);
            _numField.AddToClassList("zui-microslider__numfield");
            _numField.AddToClassList("zui-slider__input");
            _numField.RegisterCallback<PointerDownEvent>(ev => ev.StopPropagation());
            _numField.RegisterValueChangedCallback(ev => { _onBeforeMutate?.Invoke(); SetValue(ev.newValue, notify: true); });
            Add(_numField);

            ApplyDisplay();
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
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
            if (_showNumInput) _numField.SetValueWithoutNotify(_value);
            MarkDirtyRepaint();
            if (notify) _onChanged?.Invoke(_value);
        }

        bool HasPrefs => !string.IsNullOrEmpty(_prefsKey);
        string PrefKey(string sub) => "zui.microslider." + _prefsKey + "." + sub;

        // Apply the current display prefs: the numeric field replaces the in-track readout when both would show.
        void ApplyDisplay()
        {
            bool showVal = _showValueLabel && !_showNumInput;
            EnableInClassList("zui-slider--show-value", showVal);
            EnableInClassList("zui-slider--show-input", _showNumInput);
            if (_showNumInput) _numField.SetValueWithoutNotify(_value);
            if (showVal) UpdateValueLabel();
            MarkDirtyRepaint();
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            // Reset each time: removing a parent class must restore the fallback palette.
            _resolvedFillLeft = FillLeft;
            _resolvedFillRight = FillRight;
            _resolvedTrackColor = TrackColor;
            if (e.customStyle.TryGetValue(SliderFillLeftProperty, out Color fillLeft)) _resolvedFillLeft = fillLeft;
            if (e.customStyle.TryGetValue(SliderFillRightProperty, out Color fillRight)) _resolvedFillRight = fillRight;
            if (e.customStyle.TryGetValue(SliderTrackColorProperty, out Color trackColor)) _resolvedTrackColor = trackColor;
            MarkDirtyRepaint();
        }

        void ShowDisplayMenu()
        {
            var menu = Z.Menu(this);
            menu.Toggle("Value in slider", "Show the value as text inside the slider track.", _showValueLabel,
                on => { _showValueLabel = on; EditorPrefs.SetBool(PrefKey("val"), on); ApplyDisplay(); });
            menu.Toggle("Numeric input", "Show an editable field to type an exact value (replaces the in-track readout).",
                _showNumInput,
                on => { _showNumInput = on; EditorPrefs.SetBool(PrefKey("num"), on); ApplyDisplay(); });
            menu.Show();
        }

        /// How this slider prints a number — the in-track readout and the tooltip's reset hint must agree, or
        /// "resets to 0.5" sits above a track that then reads "1" (same value, two conventions).
        /// Decimals scale to the range, matching the old MicroSlider's AutoFormat: a 0..1 dial wants more
        /// places than a 0..360 one.
        string Format(float v)
        {
            float span = _max - _min;
            string fmt = _decimals >= 0 ? "F" + _decimals : span <= 3f ? "0.##" : span <= 40f ? "0.#" : "0";
            return v.ToString(fmt);
        }

        void UpdateValueLabel()
        {
            if (!_showValueLabel) return;
            _valueLabel.text = Format(_value);
        }

        float ValueFromX(float localX)
        {
            float w = Mathf.Max(1f, contentRect.width);
            return ValueFromNormalized(localX / w);
        }

        float ValueFromNormalized(float t) => _logarithmic
            ? Mathf.Exp(Mathf.Lerp(Mathf.Log(_min), Mathf.Log(_max), Mathf.Clamp01(t)))
            : Mathf.Lerp(_min, _max, Mathf.Clamp01(t));

        float NormalizedFromValue(float v) => _logarithmic
            ? Mathf.InverseLerp(Mathf.Log(_min), Mathf.Log(_max), Mathf.Log(Mathf.Clamp(v, _min, _max)))
            : Mathf.InverseLerp(_min, _max, v);

        void OpenGesture()
        {
            if (_gestureOpen) return;
            _gestureOpen = true;
            _undoGroup = ZuiUndoGesture.Begin();   // one drag is one Undo step, however many moves it raises
            _onBeforeMutate?.Invoke();   // fires once per drag, before the first mutation (the Undo contract)
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
            // Right-click opens the display-options menu (only meaningful — and persistable — when keyed).
            if (e.button == 1)
            {
                if (HasPrefs) { ShowDisplayMenu(); e.StopPropagation(); }
                return;
            }
            if (e.button != 0) return;
            if (e.clickCount == 2 && _default.HasValue)
            {
                OpenGesture();
                SetValue(_default.Value, notify: true);
                CloseGesture();
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
                float w = Mathf.Max(1f, contentRect.width);
                SetValue(ValueFromNormalized(NormalizedFromValue(_value) + (x - _lastMoveX) / w * FineFactor), notify: true);
            }
            else SetValue(ValueFromX(x), notify: true);
            _lastMoveX = x;
            e.StopPropagation();
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
            float t = NormalizedFromValue(_value);
            float split = Mathf.Round(t * r.width);

            var p = mgc.painter2D;
            // Empty track first (the whole width), then the fill over the left part as a HORIZONTAL gradient
            // (dark at the left, lighter toward the fill edge). Darker overall than a flat accent so the label
            // and value drawn on top stay readable. The element's border-radius + overflow:hidden round it.
            FillRect(p, 0f, 0f, r.width, r.height, _resolvedTrackColor);
            if (split > 0.5f) FillGradientH(p, split, r.height, _resolvedFillLeft, _resolvedFillRight);
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
        Color _resolvedFillLeft = new Color(28f / 255f, 44f / 255f, 78f / 255f, 0.92f);
        Color _resolvedFillRight = new Color(70f / 255f, 120f / 255f, 200f / 255f, 0.85f);
        Color _resolvedTrackColor = new Color(0f, 0f, 0f, 0.30f);
    }
}
