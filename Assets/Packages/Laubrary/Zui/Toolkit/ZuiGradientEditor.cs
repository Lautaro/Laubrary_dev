// ZuiGradientEditor — the COMPOSABLE pieces of a ZuiGradient editor, so a host can arrange them freely:
//   • Output — the objective PREVIEW strip (painted from ZuiGradient.ToLut(), i.e. the final ramp WITH every
//     transform applied). Read-only; it is what actually renders.
//   • Source — the editable base GradientField (the raw ramp the user edits; transforms apply on top).
//   • Adjust — a collapsible "Adjust" box of the transforms (Hue/Sat/Brightness/Contrast/Phase MultiCont, Quantise,
//     Cycle+Reverse).
// Editing Source or any transform re-bakes Output. ZuiGradientControl stacks all three (Output, Source, Adjust);
// ZuiFillControl puts Output in the Fill header beside the square+label, Source just below, and Adjust in the
// collapsible body — so the objective + source ramps stay visible even when the fill's controls fold away.
// Editor-only (uses UnityEditor's GradientField).

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public sealed class ZuiGradientEditor
    {
        public readonly Image Output;          // objective preview (ToLut) — read-only, the final ramp
        public readonly GradientField Source;  // editable base ramp
        public readonly ZuiBox Adjust;         // the transforms, in a collapsible box

        readonly ZuiGradient _g;
        Texture2D _lut;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        public ZuiGradientEditor(ZuiGradient g, string tooltip = null)
        {
            _g = g ?? throw new ArgumentNullException(nameof(g));
            _g.EnsureTransformAnim();   // non-null ZUIValue companions for the MultiCont controls

            Output = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = "Objective preview — the ramp exactly as it renders, every transform applied (at life 0). Read-only.",
            };
            Output.style.height = 22;   // fixed height; a column parent stretches it full-width (no flexGrow needed)
            Output.style.minWidth = 120f;
            Output.RegisterCallback<DetachFromPanelEvent>(_ => DisposeLut());

            Source = new GradientField { value = _g.gradient, tooltip = "The SOURCE ramp you edit — the transforms below apply on top of it." };
            Source.RegisterValueChangedCallback(e => Mutate(() => _g.gradient = e.newValue));

            Adjust = Z.Box("Adjust",
                "Non-destructive transforms applied on top of the base ramp. Hue / Saturation / Brightness / Contrast "
                + "/ Phase are animatable over the particle's life (Static / Min-Max / Curve via the ⋯ menu).");
            AddVal(Adjust, "Hue",        _g.hueShiftAnim,   -1f, 1f, "Rotate the hue of the whole ramp (±1 = ±180°). Animatable over life.");
            AddVal(Adjust, "Saturation", _g.saturationAnim,  0f, 2f, "Multiply saturation across the ramp (1 = unchanged). Animatable over life.");
            AddVal(Adjust, "Brightness", _g.brightnessAnim,  0f, 2f, "Multiply brightness across the ramp (1 = unchanged). Animatable over life.");
            AddVal(Adjust, "Contrast",   _g.contrastAnim,    0f, 2f, "Contrast around mid-grey (1 = unchanged). Animatable over life.");
            // Phase gets a Y-axis colour legend (#9): at phase v the ramp origin shows the base colour at v, so the
            // envelope's vertical strip + tinted points read as "this phase lands on THIS colour". The whole-ramp
            // transforms (Hue/Sat/Brightness/Contrast) act on every stop at once, so a single "colour at value v" is
            // undefined for them — deliberately no strip there.
            AddVal(Adjust, "Phase",      _g.phaseAnim,       0f, 2f, "Scroll the ramp along its length, 0..2. 0→1 plays it FORWARD, 1→2 plays it "
                                                                    + "back REVERSED, and 2 lands exactly where 0 did — so animating Phase over life (a rising Curve 0→2) "
                                                                    + "scrolls the gradient in a SEAMLESS loop, no jump, no shader. The mirrored second half is what makes "
                                                                    + "it smooth (the ramp mirrors instead of snapping from its end back to its start).",
                v => _g.gradient != null ? _g.gradient.Evaluate(Mathf.PingPong(v, 1f)) : Color.clear);
            Adjust.Add(Z.MicroSlider("Quantise", _g.quantiseSteps, 0, 16,
                "Snap the ramp to N discrete bands (0 = smooth) — the gradient Posterize. Not animatable (a shifting "
                + "band count reads as flicker, not motion).",
                v => Mutate(() => _g.quantiseSteps = Mathf.RoundToInt(v)), decimals: 0, prefsKey: "grad.quantise"));
            Adjust.Add(Z.Row(
                Z.Toggle("Cycle", "This ramp wants to colour-cycle (a ZuiPaletteCycle driver advances the phase at runtime).",
                    _g.cycle, v => Mutate(() => _g.cycle = v)),
                Z.Toggle("Reverse", "Sample the gradient backwards (1-t).",
                    _g.reverse, v => Mutate(() => _g.reverse = v))));

            Refresh();
        }

        // A colour transform as a MultiCont (Z.Value) over life. The wall-clock timing (Dur/Warm/Loop) + Value-range
        // rows are hidden — a gradient transform is sampled over the 0..1 life with a fixed range, so they're noise
        // (matches Pyre's own Val()). Edits re-bake the Output preview.
        void AddVal(VisualElement parent, string label, ZUIValue v, float min, float max, string tip, Func<float, Color> yColor = null)
        {
            var o = new ZuiValueControl.Options
            {
                controlWidth = 150f, hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
            };
            o.WithRange(min, max);
            if (yColor != null) o.WithYColor(yColor);
            parent.Add(Z.Value(label, v, o, tip,
                () => { Refresh(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke()));
        }

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            Refresh();
            OnChanged?.Invoke();
        }

        void Refresh()
        {
            DisposeLut();
            _lut = _g.ToLut(256);
            _lut.hideFlags = HideFlags.HideAndDontSave;
            Output.image = _lut;
        }

        void DisposeLut()
        {
            if (_lut != null) { UnityEngine.Object.DestroyImmediate(_lut); _lut = null; }
        }
    }
}
