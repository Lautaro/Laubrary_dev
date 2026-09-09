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
            Action onBeforeMutate = null, int decimals = -1, string prefsKey = null)
        {
            _min = min; _max = Mathf.Max(min + 1e-6f, max);
            _value = Mathf.Clamp(value, _min, _max);
            _onChanged = onChanged; _default = defaultValue;
            _onBeforeMutate = onBeforeMutate; _decimals = decimals;
            // The display-options menu is now the DEFAULT for every ZUI MicroSlider — when no explicit prefsKey is
            // given, the caption is the persistence key ("lbl:<label>"), so a slider remembers its toggles by name.
            // An explicit prefsKey still wins (e.g. to disambiguate two same-named sliders).
            _prefsKey = string.IsNullOrEmpty(prefsKey) ? "lbl:" + (label ?? "") : prefsKey;
            _showValueLabel = EditorPrefs.GetBool(PrefKey("val"), showValue);
            _showNumInput   = EditorPrefs.GetBool(PrefKey("num"), false);
            // Append the drag-modifier hint to the hover tooltip so the Shift-fine / double-click gestures are
            // discoverable (the element itself is what receives the hover — the caption ignores picking).
            this.tooltip = tooltip + "  ·  Drag to set; Shift = fine"
                + (_default.HasValue ? "; double-click resets to default." : ".")
                + (HasPrefs ? "  ·  Right-click for display options." : "");

            AddToClassList("zui-microslider");

            _caption = new Label(label) { pickingMode = PickingMode.Ignore, tooltip = tooltip };
            _caption.AddToClassList("zui-microslider__caption");
            Add(_caption);

            _valueLabel = new Label { pickingMode = PickingMode.Ignore };
            _valueLabel.AddToClassList("zui-microslider__value");
            Add(_valueLabel);

            // The optional numeric-input field (#10) sits over the right of the track; hidden unless toggled on.
            // It swallows its own pointer-downs so typing/clicking it never starts a track drag.
            _numField = new FloatField { isDelayed = true };
            _numField.AddToClassList("zui-microslider__numfield");
            _numField.style.position = Position.Absolute;
            _numField.style.right = 2; _numField.style.top = 1; _numField.style.bottom = 1;
            _numField.style.width = 46; _numField.style.marginLeft = 0; _numField.style.marginRight = 0;
            _numField.RegisterCallback<PointerDownEvent>(ev => ev.StopPropagation());
            _numField.RegisterValueChangedCallback(ev => { _onBeforeMutate?.Invoke(); SetValue(ev.newValue, notify: true); });
            Add(_numField);

            ApplyDisplay();
            generateVisualContent += OnGenerate;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
        }

        /// Grow to hold this slider's own CAPTION when the caption is a name nobody chose a width for — a
        /// REFLECTED field name, which is where a 20-character label meets the 150px default. That is T-0257's
        /// remedy (widen the control rather than shorten the name back into jargon) applied automatically
        /// instead of one dial at a time. Measured live: SpriteFx's Outline modifier drew "Inner Softness" and
        /// "Inner Softness Curve" as two dials whose captions both elided to "Inner Softness …", so the word
        /// that told them apart was the one that got cut.
        ///
        /// It only ever GROWS a clipped dial and never past the room its parent gives, so a row that already
        /// fits cannot start spilling, and the growth converges (a wider caption box ends the condition that
        /// asked for it). Measured after layout, because a caption's width is unknowable before the panel has
        /// a font.
        public ZuiMicroSlider FitCaption(float floor)
        {
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_caption == null || parent == null || _caption.resolvedStyle.display == DisplayStyle.None) return;
                float need = _caption.MeasureTextSize(_caption.text ?? string.Empty, 0f,
                                 MeasureMode.Undefined, 0f, MeasureMode.Undefined).x;
                float have = _caption.contentRect.width;
                float own = resolvedStyle.width;
                if (float.IsNaN(need) || float.IsNaN(have) || float.IsNaN(own) || have <= 0f) return;
                if (need <= have + 1.5f) return;
                // The room a dial has is NOT its immediate parent's width: a ZuiValueControl wraps its slider in
                // a Z.Row that shrink-wraps to the slider itself, so the parent measures exactly `own` and would
                // cap the growth at zero. A shrink-wrapping ancestor imposes no constraint — it follows its
                // child — so walk up past those to the first one that is genuinely wider, and let THAT be the cap.
                float room = own;
                for (var p = parent; p != null; p = p.hierarchy.parent)
                {
                    float pw = p.contentRect.width;
                    if (float.IsNaN(pw) || pw <= 0f) continue;
                    if (pw > own + 1f) { room = pw; break; }
                }
                float want = Mathf.Clamp(own + (need - have) + 2f, Mathf.Max(floor, own), room);
                if (want > own + 0.5f) style.width = want;
            });
            return this;
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
            _valueLabel.style.display = showVal ? DisplayStyle.Flex : DisplayStyle.None;
            _numField.style.display = _showNumInput ? DisplayStyle.Flex : DisplayStyle.None;
            // The caption keeps clear of whatever is drawn at the right end of the track, and gets the width
            // back when nothing is.
            _caption.EnableInClassList("zui-microslider__caption--reserve", showVal || _showNumInput);
            if (_showNumInput) _numField.SetValueWithoutNotify(_value);
            if (showVal) UpdateValueLabel();
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

        void UpdateValueLabel()
        {
            if (!_showValueLabel) return;
            // Decimals scale to the range, matching the old MicroSlider's AutoFormat: a 0..1 dial wants
            // more places than a 0..360 one.
            float span = _max - _min;
            string fmt = _decimals >= 0 ? "F" + _decimals : span <= 3f ? "0.##" : span <= 40f ? "0.#" : "0";
            _valueLabel.text = _value.ToString(fmt);
        }

        // T-0291 — before this element's first layout pass, contentRect.width is NaN; Mathf.Max(1f, NaN) is
        // itself NaN (the comparison is false), which poisons Clamp01 and then Lerp, handing the caller's
        // callback a NaN it has no reason to expect. Resolve to a safe width instead, and never return NaN
        // even if localX itself is somehow non-finite.
        static bool HasValidWidth(float w) => !float.IsNaN(w) && !float.IsInfinity(w);

        float ValueFromX(float localX)
        {
            if (float.IsNaN(localX) || float.IsInfinity(localX)) return _value;
            float w = HasValidWidth(contentRect.width) ? Mathf.Max(1f, contentRect.width) : 1f;
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
                _gestureOpen = false;
                e.StopPropagation();
                return;
            }
            _dragging = true;
            _lastMoveX = e.localPosition.x;
            this.CapturePointer(e.pointerId);
            OpenGesture();
            // Shift = gentle: start a relative fine drag from the CURRENT value (no jump to the press point);
            // a normal press jumps the value to where you clicked (absolute). T-0291 — before this element's
            // first layout pass contentRect.width is NaN, so a press landing in that window is deferred
            // rather than resolved against a bogus width: the drag stays open (OnMove) and will jump on the
            // next move, by which point layout has normally run.
            if (!e.shiftKey && HasValidWidth(contentRect.width))
                SetValue(ValueFromX(e.localPosition.x), notify: true);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            float x = e.localPosition.x;
            if (e.shiftKey)
            {
                // Fine/relative: nudge by a fraction of the normal value-per-pixel, accumulated from the last x.
                // T-0291 — same NaN-width guard as ValueFromX; skip the nudge rather than poison _value.
                if (!HasValidWidth(contentRect.width)) { _lastMoveX = x; e.StopPropagation(); return; }
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
