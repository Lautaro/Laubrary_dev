// ZuiGradientEditor — the COMPOSABLE pieces of a ZuiGradient editor, so a host can arrange them freely:
//   • Output — the objective PREVIEW strip (painted from ZuiGradient.ToLut(), i.e. the final ramp WITH every
//     transform applied). Read-only; it is what actually renders.
//   • Source — the editable base ramp (ZuiRampControl, i.e. Unity's own GradientField plus the "★" library and the
//     blend-space row); transforms apply on top.
//   • Adjust — a collapsible "Adjust" box of the transforms (Hue/Sat/Brightness/Contrast/Phase MultiCont, Quantise,
//     Cycle+Reverse).
// Editing Source or any transform re-bakes Output. ZuiGradientControl stacks all three (Output, Source, Adjust);
// ZuiFillControl puts Output in the Fill header beside the square+label, Source just below, and Adjust in the
// collapsible body — so the objective + source ramps stay visible even when the fill's controls fold away.
//
// T-0223 — Source opens Unity's own gradient editor again, and so does every ramp site: ZuiRampControl is now a
// GradientField wearing the "★" library and the blend-space row, so there is ONE colour editor in the package and
// it is the one everybody already knows. T-0221's bespoke stop editor is gone; what T-0221 built and kept is the
// STORAGE — ZuiGradient still owns an unbounded stop list, so a 14-stop palette from the library is held and
// rendered whole, and the 8-key cap is paid only for the field's picture and only written back if edited.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public sealed class ZuiGradientEditor
    {
        public readonly Image Output;            // objective preview (ToLut) — read-only, the final ramp
        public readonly ZuiRampControl Source;   // editable base ramp — Unity's GradientField + blend space (T-0223)
        public readonly ZuiBox Adjust;           // the transforms, in a collapsible box
        public readonly Button Library;        // T-0205 — the project's saved-gradient library (browse / save)

        readonly ZuiGradient _g;
        readonly bool _lifeFollowsPosition;
        Texture2D _lut;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        /// <param name="lifeFollowsPosition">Set when `g` backs an OverLife ZuiFill, where the ramp position IS the
        /// life (ZuiFill.EvalGrad samples both from the same value). The Output preview then bakes each texel's
        /// colour transforms (hue/sat/brightness/contrast) at ITS OWN position instead of a fixed life-0 snapshot,
        /// so an authored Curve shows the true per-position render instead of one flat value smeared across the
        /// whole strip. Linear/Radial/Noise fills keep the life-0 snapshot (there, position and life are genuinely
        /// independent — the transforms animate over TIME, not position, so a single frozen snapshot is correct).</summary>
        public ZuiGradientEditor(ZuiGradient g, string tooltip = null, bool lifeFollowsPosition = false)
        {
            _g = g ?? throw new ArgumentNullException(nameof(g));
            _lifeFollowsPosition = lifeFollowsPosition;
            _g.EnsureTransformAnim();   // non-null ZUIValue companions for the MultiCont controls
            _g.EnsureStops();           // convert a pre-stop-list gradient before the stop editor reads it

            Output = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = _lifeFollowsPosition
                    ? "Objective preview — the ramp exactly as it renders, every transform applied at EACH position's own life (this fill is Over Life, where position IS life). Read-only."
                    : "Objective preview — the ramp exactly as it renders, every transform applied (at life 0). Read-only.",
            };
            Output.style.height = 22;   // fixed height; a column parent stretches it full-width (no flexGrow needed)
            Output.style.minWidth = 120f;
            Output.RegisterCallback<DetachFromPanelEvent>(_ => DisposeLut());
            // Detaching frees the LUT, and a host that merely HIDES this control (a folded section, a toggle bar,
            // a reparent) detaches it too — after which the preview came back permanently blank, because nothing
            // re-baked it. Re-baking on attach costs one 256×1 texture and is the only thing that makes the strip
            // survive being folded away and reopened.
            Output.RegisterCallback<AttachToPanelEvent>(_ => Refresh());

            // The SOURCE ramp: Unity's own gradient editor over this gradient. `showLibrary: false` because this
            // editor already carries the "★" on the Output row below — one library button per gradient, not two.
            Source = new ZuiRampControl(_g, "The SOURCE ramp you edit — click it for Unity's gradient editor. The "
                                          + "transforms below apply on top of it.", showLibrary: false)
            {
                OnBeforeMutate = () => OnBeforeMutate?.Invoke(),
                OnChanged = () => { Refresh(); OnChanged?.Invoke(); },
            };

            // T-0205 — one shared "project's saved gradients" library, reachable from every ZuiGradient site
            // (this control backs Fill's Gradient fill, RampByQuantity, OverPhase, Procedural noise) AND from
            // ZuiRampControl's own Library button (via ZuiRampGradientBridge) — the owner's ask that ramp
            // controls and Fill's gradient picker share the same saved palette.
            Library = Z.Button("★", "This project's saved gradients — click to apply one, or save the CURRENT "
                              + "ramp under a new name.", OpenLibrary).W(22f);
            // T-0311 — the glyph was drawn clipped in every gradient in every tool: a 22px slot, less the
            // button's default 6+6 padding and 1+1 border, leaves 8.0px of content for a 12.9px "★".
            // Zeroed rather than widened, following the same call the dirty dot got in T-0307: the button
            // sits on the ramp's Output row beside a flex-grow preview strip, so 22px is the width the row
            // was designed around, and a 20px content box is comfortably more than the glyph needs.
            Library.style.paddingLeft = 0f;
            Library.style.paddingRight = 0f;

            Adjust = Z.Box("Adjust",
                "Non-destructive transforms applied on top of the base ramp. Hue / Saturation / Brightness / Contrast "
                + "/ Phase are animatable over the particle's life (right-click one for Static / Min-Max / Curve).");
            // T-0140 — packed 2-3 per row instead of six stacked full-width rows (caught live by a project
            // owner screenshot): these are all compact Z.Value rows (controlWidth 150), the same "share a
            // row, don't stack" rule already applied everywhere else in this codebase.
            var hue = BuildVal("Hue",        _g.hueShiftAnim,   -1f, 1f, "Rotate the hue of the whole ramp (±1 = ±180°). Animatable over life.");
            var sat = BuildVal("Saturation", _g.saturationAnim,  0f, 2f, "Multiply saturation across the ramp (1 = unchanged). Animatable over life.");
            var bri = BuildVal("Brightness", _g.brightnessAnim,  0f, 2f, "Multiply brightness across the ramp (1 = unchanged). Animatable over life.");
            var con = BuildVal("Contrast",   _g.contrastAnim,    0f, 2f, "Contrast around mid-grey (1 = unchanged). Animatable over life.");
            // Phase gets a Y-axis colour legend (#9): at phase v the ramp origin shows the base colour at v, so the
            // envelope's vertical strip + tinted points read as "this phase lands on THIS colour". The whole-ramp
            // transforms (Hue/Sat/Brightness/Contrast) act on every stop at once, so a single "colour at value v" is
            // undefined for them — deliberately no strip there.
            var pha = BuildVal("Phase",      _g.phaseAnim,       0f, 2f, "Scroll the ramp along its length, 0..2. 0→1 plays it FORWARD, 1→2 plays it "
                                                                    + "back REVERSED, and 2 lands exactly where 0 did — so animating Phase over life (a rising Curve 0→2) "
                                                                    + "scrolls the gradient in a SEAMLESS loop, no jump, no shader. The mirrored second half is what makes "
                                                                    + "it smooth (the ramp mirrors instead of snapping from its end back to its start).",
                v => _g.HasRamp ? _g.EvalRamp(Mathf.PingPong(v, 1f)) : Color.clear);
            Adjust.Add(Z.HGroup(hue, sat, bri));
            Adjust.Add(Z.HGroup(con, pha));

            // Locked (a form-declared band palette, e.g. Torch/ArcBurst): quantising can never go smooth again, so
            // the slider floors at 1 (no reachable "0 = smooth") and reads as "Bands" — the domain word every other
            // banded control in Pyre already uses — instead of "Quantise".
            var quantiseCtrl = _g.bandLocked
                ? Z.MicroSlider("Bands", _g.quantiseSteps, 1, 16,
                    "How many discrete colour steps this palette samples from the ramp below. Purely a resolution "
                    + "knob — it never touches the ramp itself, so raising/lowering it and coming back loses nothing.",
                    v => Mutate(() => _g.quantiseSteps = Mathf.Max(1, Mathf.RoundToInt(v))), decimals: 0, prefsKey: "grad.bands")
                : Z.MicroSlider("Quantise", _g.quantiseSteps, 0, 16,
                    "Snap the ramp to N discrete bands (0 = smooth) — the gradient Posterize. Not animatable (a shifting "
                    + "band count reads as flicker, not motion).",
                    v => Mutate(() => _g.quantiseSteps = Mathf.RoundToInt(v)), decimals: 0, prefsKey: "grad.quantise");
            Adjust.Add(Z.HGroup(
                quantiseCtrl,
                Z.Toggle("Cycle", "This ramp wants to colour-cycle (a ZuiPaletteCycle driver advances the phase at runtime).",
                    _g.cycle, v => Mutate(() => _g.cycle = v)),
                Z.Toggle("Reverse", "Sample the gradient backwards (1-t).",
                    _g.reverse, v => Mutate(() => _g.reverse = v))));

            Refresh();
        }

        // A colour transform as a MultiCont (Z.Value) over life. The wall-clock timing (Dur/Warm/Loop) + Value-range
        // rows are hidden — a gradient transform is sampled over the 0..1 life with a fixed range, so they're noise
        // (matches Pyre's own Val()). Edits re-bake the Output preview. Returns the built control (T-0140 — was
        // "AddVal", adding itself straight into the parent; now the caller packs several into one Z.HGroup row).
        VisualElement BuildVal(string label, ZUIValue v, float min, float max, string tip, Func<float, Color> yColor = null)
        {
            var o = new ZuiValueControl.Options
            {
                controlWidth = 150f, hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
            };
            o.WithRange(min, max);
            if (yColor != null) o.WithYColor(yColor);
            return Z.Value(label, v, o, tip,
                () => { Refresh(); OnChanged?.Invoke(); },
                () => OnBeforeMutate?.Invoke());
        }

        // The saved-gradient library, in stops: saving keeps every stop whatever the count, and applying one
        // replaces this gradient's stops wholesale (one Undo-recorded gesture) rather than going through an
        // 8-key Gradient in between.
        void OpenLibrary()
        {
            ZuiGradientPresetPopup.Show(Library, () => _g, applied =>
            {
                Mutate(() =>
                {
                    var stops = _g.Stops;
                    stops.Clear();
                    foreach (var s in applied.Stops) stops.Add(new ZuiGradientStop(s.pos, s.color));
                    _g.BlendMode = applied.BlendMode;
                    _g.MarkStopsChanged();
                });
                Source.Refresh();
            });
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
            _lut = _g.ToLut(256, lifeFollowsPosition: _lifeFollowsPosition);
            _lut.hideFlags = HideFlags.HideAndDontSave;
            Output.image = _lut;
        }

        void DisposeLut()
        {
            if (_lut != null) { UnityEngine.Object.DestroyImmediate(_lut); _lut = null; }
        }
    }
}
