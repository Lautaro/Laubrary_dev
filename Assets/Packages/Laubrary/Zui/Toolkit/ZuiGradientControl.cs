// ZuiGradientControl — the UI Toolkit control that edits a ZuiGradient (base Gradient + transform knobs).
// Layout: an always-visible live PREVIEW strip (painted from ZuiGradient.ToLut(), the runtime-exact ramp), then
// the base GradientField, then a collapsible "Adjust" sub-section holding the transforms. The four COLOUR
// transforms (Hue / Saturation / Brightness / Contrast) are MultiCont (Z.Value / ZUIValue) — Static / Min-Max /
// Curve over the particle's life, via the ⋯ menu — so they can animate; Reverse / Cycle are toggles and Quantise
// is an int MicroSlider (a shifting band count reads as flicker, not motion). Folding the Adjust box away leaves
// just [preview + base ramp] — the envelope-style "collapse but keep the preview" the whole control was built for.
// Editor-only (uses UnityEditor's GradientField).

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiGradientControl : VisualElement
    {
        readonly ZuiGradient _g;
        readonly Image _preview;   // painted from ToLut() — the runtime-exact ramp; always visible
        Texture2D _lut;            // owned; re-baked on every change, destroyed on detach

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        /// <param name="collapsible">Reserved (the transforms always live in the collapsible "Adjust" box now).</param>
        public ZuiGradientControl(ZuiGradient g, string tooltip = null, bool collapsible = true)
        {
            _g = g ?? throw new ArgumentNullException(nameof(g));
            _g.EnsureTransformAnim();   // non-null ZUIValue companions to bind the MultiCont controls to
            AddToClassList("zui-gradient-control");
            style.flexDirection = FlexDirection.Column;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _preview = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = "The gradient exactly as it evaluates at runtime — every transform applied (at life 0).",
            };
            _preview.style.height = 22;
            _preview.style.marginBottom = 4;
            _preview.style.flexGrow = 1f;
            _preview.style.minWidth = 120f;   // stays a usable ramp even in a shrink-to-fit parent
            Add(_preview);

            var field = new GradientField { value = _g.gradient, tooltip = "Base gradient — the transforms below apply on top of it." };
            field.RegisterValueChangedCallback(e => Mutate(() => _g.gradient = e.newValue));
            Add(field);

            // The transforms, in their OWN collapsible sub-section (a folding titled box) so they don't sprawl —
            // fold it and only the preview + base ramp remain.
            var adjust = Z.Box("Adjust",
                "Non-destructive transforms applied on top of the base ramp. Hue / Saturation / Brightness / Contrast "
                + "are animatable over the particle's life (Static / Min-Max / Curve via the ⋯ menu).");
            adjust.Add(Z.Toggle("Reverse", "Sample the gradient backwards (1-t).", _g.reverse, v => Mutate(() => _g.reverse = v)));
            AddVal(adjust, "Hue",        _g.hueShiftAnim,   -1f, 1f, "Rotate the hue of the whole ramp (±1 = ±180°). Animatable over life.");
            AddVal(adjust, "Saturation", _g.saturationAnim,  0f, 2f, "Multiply saturation across the ramp (1 = unchanged). Animatable over life.");
            AddVal(adjust, "Brightness", _g.brightnessAnim,  0f, 2f, "Multiply brightness across the ramp (1 = unchanged). Animatable over life.");
            AddVal(adjust, "Contrast",   _g.contrastAnim,    0f, 2f, "Contrast around mid-grey (1 = unchanged). Animatable over life.");
            adjust.Add(Z.MicroSlider("Quantise", _g.quantiseSteps, 0, 16,
                "Snap the ramp to N discrete bands (0 = smooth) — the gradient Posterize. Not animatable (a shifting "
                + "band count reads as flicker, not motion).",
                v => Mutate(() => _g.quantiseSteps = Mathf.RoundToInt(v)), decimals: 0));
            adjust.Add(Z.Toggle("Cycle", "This ramp wants to colour-cycle (a ZuiPaletteCycle driver advances the phase at runtime).",
                _g.cycle, v => Mutate(() => _g.cycle = v)));
            Add(adjust);

            RefreshPreview();
            RegisterCallback<DetachFromPanelEvent>(_ => DisposeLut());
        }

        // A colour transform as a MultiCont (Z.Value) — Static / Min-Max / Curve over life. Mirrors how ZuiReflect
        // builds a ranged ZUIValue control (an Options with a range, then Z.Value). Edits re-bake the preview.
        void AddVal(VisualElement parent, string label, ZUIValue v, float min, float max, string tip)
        {
            var o = new ZuiValueControl.Options { controlWidth = 150f };
            o.WithRange(min, max);
            parent.Add(Z.Value(label, v, o, tip,
                () => { RefreshPreview(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke()));
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
