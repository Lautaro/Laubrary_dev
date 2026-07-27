// ZuiGradientControl — the UI Toolkit control that edits a ZuiGradient (base Gradient + transform knobs).
// The top strip is a LIVE preview of the TRUE evaluated ramp: it is painted from ZuiGradient.ToLut(), the
// exact same LUT the palette-cycle shader consumes, so what you see is byte-for-byte what runs. Any knob
// change (or a base-gradient edit) re-bakes the strip, so reverse / hue / sat / brightness / contrast /
// quantise all show their real result immediately. Editor-only (uses UnityEditor's GradientField).

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiGradientControl : VisualElement
    {
        readonly ZuiGradient _g;
        readonly Image _preview;   // painted from ToLut() — the runtime-exact ramp
        Texture2D _lut;            // owned; re-baked on every change, destroyed on detach

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        public ZuiGradientControl(ZuiGradient g, string tooltip = null)
        {
            _g = g ?? throw new ArgumentNullException(nameof(g));
            AddToClassList("zui-gradient-control");
            style.flexDirection = FlexDirection.Column;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _preview = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = "The gradient exactly as it evaluates at runtime — every transform applied.",
            };
            _preview.style.height = 22;
            _preview.style.marginBottom = 4;
            Add(_preview);

            var field = new GradientField { value = _g.gradient, tooltip = "Base gradient — the transforms below apply on top of it." };
            field.RegisterValueChangedCallback(e => Mutate(() => _g.gradient = e.newValue));
            Add(field);

            Add(Z.Toggle("Reverse", "Sample the gradient backwards (1-t).", _g.reverse, v => Mutate(() => _g.reverse = v)));
            AddSlider("Hue",        _g.hueShift,    -1f, 1f, "Rotate the hue of the whole ramp (±1 = ±180°).", v => _g.hueShift = v);
            AddSlider("Saturation", _g.saturation,   0f, 2f, "Multiply saturation across the ramp (1 = unchanged).",        v => _g.saturation = v);
            AddSlider("Brightness", _g.brightness,   0f, 2f, "Multiply brightness across the ramp (1 = unchanged).",        v => _g.brightness = v);
            AddSlider("Contrast",   _g.contrast,     0f, 2f, "Contrast around mid-grey (1 = unchanged).",                   v => _g.contrast = v);

            var q = Z.SliderInt(_g.quantiseSteps, 0, 16, "Snap the ramp to N discrete bands (0 = smooth) — the gradient Posterize.",
                                v => Mutate(() => _g.quantiseSteps = v), 200f);
            q.label = "Quantise";
            Add(q);

            Add(Z.Toggle("Cycle", "This ramp wants to colour-cycle (a ZuiPaletteCycle driver advances the phase at runtime).",
                         _g.cycle, v => Mutate(() => _g.cycle = v)));

            RefreshPreview();
            RegisterCallback<DetachFromPanelEvent>(_ => DisposeLut());
        }

        void AddSlider(string label, float value, float min, float max, string tip, Action<float> set)
        {
            var s = Z.Slider(value, min, max, tip, v => Mutate(() => set(v)), 200f);
            s.label = label;
            Add(s);
        }

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            RefreshPreview();
            OnChanged?.Invoke();
        }

        void RefreshPreview()
        {
            DisposeLut();
            _lut = _g.ToLut(256);
            _lut.hideFlags = HideFlags.HideAndDontSave;
            _preview.image = _lut;
        }

        void DisposeLut()
        {
            if (_lut != null) { UnityEngine.Object.DestroyImmediate(_lut); _lut = null; }
        }
    }
}
