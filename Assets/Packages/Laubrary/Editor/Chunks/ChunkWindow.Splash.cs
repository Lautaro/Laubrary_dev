using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    /// SCAFFOLD OWNER: AgentHQ T-0033 (Particle Splash module). ChunkWindow already calls BuildParticleSplash
    /// from its BuildAsset in the right place, stacked among the other Chunks 2.0 module sections; this file is
    /// all that task's UI work needs to touch. Follows the same folded-until-on section shape as
    /// PyreWindow.CherryFraming's "Cherry Framing" panel: a header checkbox on Z.Section itself (bound to the
    /// module's own enabled flag) rather than a body toggle, and nothing else built while it's off.
    public partial class ChunkWindow
    {
        void BuildParticleSplash(VisualElement root, ChunkSpec c)
        {
            var m = c.particleSplash;

            var s = Z.Section("Particle Splash",
                "Sprays palette-sampled pixel particles outward from a source sprite's own opaque footprint — a " +
                "standalone splash of colour with no fragment, pyre or timeline involved.",
                "chunks.splash");
            s.SetHeaderToggle(m.enabled,
                "Spray palette-sampled pixel particles out of the source sprite's own footprint.",
                v => DialAndRebuild("Particle splash", () => m.enabled = v));
            root.Add(s);

            // Folded off: nothing else to build, matching every other module section's "off = short window" rule.
            if (!m.enabled) return;

            // Empty is a MEANINGFUL setting here, not an unfinished field: the module now inherits whatever the
            // Fragment Slicer is fracturing (its Source Visual's first frame, else its Source Sprite), so a
            // splash left empty sprays the colours of the very art that just came apart. That only reads as a
            // deliberate choice if the field SAYS so, so the tooltip is composed for the current state and
            // names what is actually being inherited — the same honest-precedence idiom Pyre Spawn uses for
            // its overridden single blast. DialAndRebuild (not Dial) because assigning a sprite changes which
            // of those states the tooltip must read for; a stale "empty means…" tooltip on a filled field is
            // exactly the conditional-tooltip bug the layout rules forbid.
            string srcTip = SplashSourceTip(m, c);
            s.Add(Z.Field("Source Sprite", srcTip,
                Z.Object<Sprite>(m.sprite, srcTip,
                    v => DialAndRebuild("Splash source sprite", () => m.sprite = v), 200f)));

            const string footprintTip = "Spawn each particle from a random OPAQUE pixel of the source sprite " +
                "instead of a single point at the burst origin. Turn off to spray from a point while still using " +
                "the sprite's own colours.";
            s.Add(Z.Toggle("Emit From Footprint", footprintTip, m.emitFromFootprint,
                v => Dial("Emit from footprint", () => m.emitFromFootprint = v)));

            // The four ranges pack TWO PER ROW. A Z.MinMax is a short control (a slider flanked by its own two
            // numeric fields), so one per full-width row left roughly half the pane empty on each of four rows
            // — the space-economy rule. The sliders are a touch narrower than a solo range would be so a pair
            // still sits comfortably in a half-screen pane.
            const string countTip = "How many particles a splash sprays. Each burst picks one random count in between.";
            const string sizeTip = "Each particle's pixel size at the spec's Pixels/Unit. 1–3px is the design " +
                "intent; the range goes a little wider so a bigger splash stays reachable.";
            s.Add(Z.Row(
                Z.Field("Count", countTip,
                    Z.MinMax(m.countMin, m.countMax, 0f, 64f, countTip,
                        (lo, hi) => Dial("Splash count", () =>
                        {
                            m.countMin = Mathf.RoundToInt(lo);
                            m.countMax = Mathf.RoundToInt(hi);
                        }), 120f, isInt: true)),
                Z.HSpace(),
                Z.Field("Particle Size (px)", sizeTip,
                    Z.MinMax(m.sizePxMin, m.sizePxMax, 0.5f, 8f, sizeTip,
                        (lo, hi) => Dial("Splash particle size", () =>
                        {
                            m.sizePxMin = lo;
                            m.sizePxMax = hi;
                        }), 120f))));

            const string speedTip = "Launch speed, world units per second. Each particle picks one random speed in between.";
            const string lifeTip = "Particle lifetime, seconds. Each particle picks one random life in between.";
            s.Add(Z.Row(
                Z.Field("Speed", speedTip,
                    Z.MinMax(m.speedMin, m.speedMax, 0f, 20f, speedTip,
                        (lo, hi) => Dial("Splash speed", () =>
                        {
                            m.speedMin = lo;
                            m.speedMax = hi;
                        }), 120f)),
                Z.HSpace(),
                Z.Field("Life (s)", lifeTip,
                    Z.MinMax(m.lifeMin, m.lifeMax, 0.02f, 3f, lifeTip,
                        (lo, hi) => Dial("Splash life", () =>
                        {
                            m.lifeMin = lo;
                            m.lifeMax = hi;
                        }), 120f))));

            const string inheritTip = "Aim the spray along the burst's own direction instead of the fixed angle. " +
                "This is what lets a Follow Emitter throw the spray backwards out of a moving character.";
            s.Add(Z.Toggle("Follow Burst Direction", inheritTip, m.inheritBurstDirection,
                v => DialAndRebuild("Splash follow burst direction", () => m.inheritBurstDirection = v)));

            const string dirTip = "Centre direction of the spray cone. 0 = right, 90 = up.";
            const string spreadTip = "Cone half-angle around the direction. 0 = a tight jet, 180 = a full circle.";
            // The fixed angle is meaningless while the spray follows the burst, but the row stays put and only
            // that one control greys out — hiding it would reflow every control under it mid-edit, which the
            // layout rules forbid.
            var dirField = Z.MicroSlider("Direction °", m.directionDeg, 0f, 360f, dirTip,
                v => Dial("Splash direction", () => m.directionDeg = v), Wide, showValue: true, decimals: 0);
            dirField.SetEnabled(!m.inheritBurstDirection);
            s.Add(Z.Row(
                dirField,
                Z.HSpace(),
                Z.MicroSlider("Spread °", m.spreadDeg, 0f, 180f, spreadTip,
                    v => Dial("Splash spread", () => m.spreadDeg = v), Wide, showValue: true)));

            const string gravityTip = "Downward acceleration, world units per second squared.";
            const string dragTip = "Air resistance. 0 = none, ~1 = noticeable, ~3 = soupy.";
            s.Add(Z.Row(
                Num2("Gravity", gravityTip, m.gravity, v => m.gravity = v),
                Z.HSpace(),
                Z.MicroSlider("Drag", m.drag, 0f, 5f, dragTip,
                    v => Dial("Splash drag", () => m.drag = v), Wide, showValue: true)));

            const string alphaTip = "Opacity across a particle's life, left (spawn) to right (death).";
            s.Add(Z.Field("Alpha Over Life", alphaTip,
                Z.Curve(m.alphaOverLife, alphaTip, v => Dial("Splash alpha over life", () => m.alphaOverLife = v))));

            const string seedTip = "Fixes every random pick (spawn pixel, size, speed, direction, life) so a " +
                "splash resolves identically every time. 0 = reroll on every play.";
            s.Add(Int2("Seed", seedTip, m.seed, v => m.seed = v));
        }

        /// What the Source Sprite field is FOR, read for the CURRENT state. Goes through ResolveSprite(c) —
        /// the same call the runtime makes — so an empty field can name the sprite it is actually inheriting
        /// instead of describing a fallback chain the user then has to trace by hand.
        static string SplashSourceTip(ParticleSplashModule m, ChunkSpec c)
        {
            if (m.sprite != null)
                return "Sprite to sample colours AND the emission footprint from. Clear it to inherit whatever " +
                       "the Fragment Slicer is fracturing instead.";

            var inherited = m.ResolveSprite(c);
            if (inherited != null)
                return "Empty on purpose: this splash INHERITS the fractured art (" + inherited.name + ") from " +
                       "the Fragment Slicer / Sample Source above, so it sprays the colours of the very thing " +
                       "that just came apart. Pick a sprite here to spray a different one instead.";

            return "Empty — and there is nothing above to inherit either, so this falls back to the burst's own " +
                   "supplied palette, then to plain white particles sprayed from the burst origin. Set a " +
                   "Fragment Slicer source (or a Sample Source) to inherit that art, or pick a sprite here.";
        }
    }
}
