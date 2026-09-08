// OrbForm — Kiln "Energy Projectile / agent2" THE ORB, generation 2, as a PyreForm.
//
// Source: D:/CODEZ/Kiln/projects/Energy Projectile/agents/agent2 (gen.py + orbcanvas.py, MANIFEST "generation 2 — the
// slider came back: 4"). The algorithm and the per-component ported / approximated / dropped list are in PyreOrb.cs's
// header. The five published draws are five different PROGRAMS (a torn-and-smeared wake, a persistence trail, a fan
// of prominences, a dissolving shell, shed filaments) that share one canvas model, so — as Arc Burst did — they are
// one form with a `variant` and one settings box per variant, each box's defaults being that draw's literals and
// contract values (D:/Claude@GDrive/Energy Projectile/GEN2/contract/agent2/<draw>/params.json). Canonical variant =
// `emberdrift` (#001 of the set, the gen-1 grade-4 rebuilt; the Appendix A summary — "additive kernels + plane-wave
// turbulence, smear + soften" — describes it). The shared geometry dials default to emberdrift's frame.
//
// Dial names in each box ARE the contract's parameter keys (turb_scale → turbScale, emit_life → emitLife …) so
// Envelopes: a dial the program reads as a per-frame AMOUNT (placement, the tone map, the glow, the nose squash, every
// field amplitude / falloff exponent / radius / width, the smear decay, the tearing weights) is a ZUIValue — Static draws
// the same bytes as a plain float, a Curve drives it over the layer's life. `Prepare` resolves the form's dials and the
// ACTIVE variant's into `live` (the shared StyleSettings dials) and `own` (the variant's) structs the program reads. Noise
// frequencies / anisotropy (the texture would swim), seeds, octaves, emit periods / lives / counts, smear taps, the
// ember fade exponent and the finish (soften, floor, despeckle) stay plain.
// `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning is in
// every [Tooltip]. Units: the orb's placement and radius are canvas fractions; the wake is in core radii; everything
// inside a box is in the archetype's own source px / field units (see PyreOrb.cs — the picture is the source's,
// magnified to the dialled radius), so the form reads the same at Pyre's 64 px as at the contract's 192 × 96.
//
// The alpha window's a1 / acurve / amax carry the source's answered hardness (HARDNESS = 0.40 applied through its
// mix(soft, hard)); they are dials here, so the "how hard" question the agent asked is a slider in the box.
using System;
using Laubrary.SpriteFx;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [PyreFormInfo("Orb", group: "Kiln/Energy Projectile", icon: "sparkle")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public sealed class OrbForm : PyreForm, IPlusFieldPublisher, IPlusRampProbe
    {
        public override string DisplayName => "Orb";
        public override string Description =>
            "A compact glowing ball of ENERGY with a trailing wake, travelling toward +x (Kiln Energy Projectile, agent2 "
            + "gen 2): one additive energy field of soft kernels and looping plane-wave turbulence, speed-smeared and "
            + "softened, read through the variant's own colour ramp by tone = 1 − exp(−E·gain) with a gradient alpha "
            + "window. Five variants: Emberdrift (boiling body, torn smeared wake, embers), Wisp (nucleus in a cloud, a "
            + "snaking persistence trail), Coronal (granulated star, prominences bent back into a fan), Membrane (a "
            + "see-through bubble shedding a dissolving veil and motes), Voltcore (a hyper-bright bead, filaments and a "
            + "plasma tail). Colour comes from the variant's Ramp, NOT the layer Fill. To aim it, add a Rotate geometry "
            + "modifier. SWARM: off = one orb at Nose across frame / Travel line; on = one orb per swarm particle at its position, "
            + "sized by Size per swarm particle and the particle's depth shading, all sharing the clip's loop clock.";

        /// Colour is the variant's ramp + the tone map, never the layer Fill.
        public override bool UsesFill => false;

        public enum Variant { Emberdrift, Wisp, Coronal, Membrane, Voltcore }

        [Tooltip("Which of the five published orbs this is. Each is its own program with its own settings box below; switching keeps the shared placement dials.")]
        [ZUILabel("Orb type")]
        public Variant variant = Variant.Emberdrift;

        // ── placement (shared) ──
        [Tooltip("Full name: \"Nose across frame\". Where the orb's nose (the core centre) sits across the canvas, as a fraction of the width. The wake trails to the LEFT of it (travel is toward +x). Source: 0.755 of a 192 px frame. This is an offset WITHIN the orb's own drawing frame, relative to the node's own Position — it is not a second placement; the node's Position box still moves the whole orb.")]
        [ZUILabel("Nose X")] [ZUIGroup("Placement & size", Tooltip = "Where the orb sits inside its own frame and how big it is — every length the variant draws is measured against these.")]
        [Range(0.2f, 0.95f)] public ZUIValue noseX = new ZUIValue(0.755f);
        [Tooltip("The travel axis down the canvas, as a fraction of the height (0 = top). Source: 0.52.")]
        [ZUILabel("Travel line")] [ZUIGroup("Placement & size")]
        [Range(0.1f, 0.9f)] public ZUIValue axisY = new ZUIValue(0.52f);
        [Tooltip("Core radius R as a fraction of the canvas width; every length inside the variant scales with it (the source's 16 px core on a 192 px frame = 0.083). At 64 px that is a 5 px core.")]
        [ZUILabel("Core size")] [ZUIGroup("Placement & size")]
        [Range(0.03f, 0.25f)] public ZUIValue radius = new ZUIValue(16f / 192f);
        [Tooltip("Wake length L in core radii — how far behind the nose the trail, the shells or the tail reach. The source runs its wakes at 5–9 radii (emberdrift 7.6).")]
        [ZUILabel("Trail length")] [ZUIGroup("Placement & size")]
        [Range(1f, 12f)] public ZUIValue wake = new ZUIValue(122f / 16f);

        // ── finish (shared) ──
        [Tooltip("Binomial soften passes over the summed field before the tone map — one pixel of radius per pass that fuses the seams between shapes. The source uses exactly 1 (a second pass blurs the head's definition back off).")]
        [ZUILabel("Blur passes")] [ZUIGroup("Cleanup", Tooltip = "The last pass over the finished picture: fuse the seams, drop what is too faint to show, and clear the lone pixels a sampled falloff leaves behind.", Advanced = true)]
        [Range(0, 3)] public int softenPasses = 1;
        [Tooltip("Full name: \"Hide below alpha\". Alpha (0..255) under which a pixel is dropped entirely — nothing carries colour it cannot show.")]
        [ZUILabel("Hide alpha")] [ZUIGroup("Cleanup", Advanced = true)]
        [Range(0, 16)] public int floor = 3;
        [Tooltip("Full name: \"Remove stray specks\". Drop lit pixels that are BOTH faint (alpha under Speck alpha limit) and isolated (fewer than 2 lit 4-neighbours): the sampled boundary of a wake's falloff, not artwork.")]
        [ZUILabel("De-speck")] [ZUIGroup("Cleanup", Advanced = true)]
        public bool despeckle = true;
        [ZUIShowIf("despeckle", "True")]
        [Tooltip("Full name: \"Speck alpha limit\". Alpha (0..255) below which an isolated pixel counts as haze to despeckle; a real spark above it is never touched.")]
        [ZUILabel("Speck alpha")] [ZUIGroup("Cleanup", Advanced = true)]
        [Range(1, 255)] public int despeckleBelow = 40;

        // ── the variants ──
        [ZUIShowIf("variant", "Emberdrift")] [Tooltip("emberdrift's literals and contract values (seed 2101, 192 × 96, 20 frames).")] public EmberdriftSettings emberdrift = new EmberdriftSettings();
        [ZUIShowIf("variant", "Wisp")] [Tooltip("wisp's literals and contract values (seed 2202, 200 × 80, 24 frames).")] public WispSettings wisp = new WispSettings();
        [ZUIShowIf("variant", "Coronal")] [Tooltip("coronal's literals and contract values (seed 2303, 184 × 108, 24 frames).")] public CoronalSettings coronal = new CoronalSettings();
        [ZUIShowIf("variant", "Membrane")] [Tooltip("membrane's literals and contract values (seed 2404, 180 × 100, 24 frames).")] public MembraneSettings membrane = new MembraneSettings();
        [ZUIShowIf("variant", "Voltcore")] [Tooltip("voltcore's literals and contract values (seed 2505, 184 × 104, 24 frames).")] public VoltcoreSettings voltcore = new VoltcoreSettings();

        // ── swarm ──
        [PyreSwarmOnly]
        [Tooltip("Full name: \"Swarm orb size\". Size of each swarm particle's orb as a fraction of the solo Radius (the swarm's own size/depth shading multiplies it).")]
        [ZUILabel("Swarm size")] [ZUIGroup("Placement & size")]
        [Range(0.1f, 1f)] public ZUIValue swarmSize = new ZUIValue(0.5f);

        // ── settings boxes ─────────────────────────────────────────────────────────────────────────────────────

        /// The tone → pixel dials every variant carries (orbcanvas.Style): a gain into the tone map and the ALPHA WINDOW.
        /// a1 / acurve / amax hold the source's mix(soft, hard) at HARDNESS 0.40 — the answered slider.
        [Serializable] public abstract class StyleSettings
        {
            [Tooltip("The signature colour ramp: position 0 = the faintest energy (the halo and the wake live there), 1 = the white-hot nucleus. Interpolated in sRGB like the source. Presets: PyreRampPresets.OrbEmber / Frost / Gold / Toxin / Volt.")]
            [ZUILabel("Colour ramp")] [ZUIGroup("Colour", Tooltip = "The colour scheme, and how far up it the picture reads.")]
            public PyreRamp ramp;
            [Tooltip("Scales the field into the tone map 1 − exp(−E·gain): higher = the same field reads hotter and further up the ramp. The one knob that rescales the whole picture against the ramp rather than its parts against each other.")]
            [ZUILabel("Brightness")] [ZUIGroup("Colour")]
            [Range(0.2f, 3f)] public ZUIValue gain = new ZUIValue(1f);
            [Tooltip("Tone value where opacity lifts off (below it the pixel is transparent).")]
            [ZUILabel("Fade-in point")] [ZUIGroup("Opacity", Tooltip = "How a pixel's brightness turns into how solid it looks — the difference between glowing fog and a ball with an edge.")]
            [Range(0f, 0.1f)] public ZUIValue a0 = new ZUIValue(0.02f);
            [Tooltip("Tone value where opacity reaches its maximum — THE hardness dial: near 1 opacity is still climbing at the centre (fog with a bright patch), lower and the climb finishes inside the body (a solid nucleus, a gradient rim, no cliff). The source's answered value.")]
            [ZUILabel("Solid point")] [ZUIGroup("Opacity")]
            [Range(0.2f, 1f)] public ZUIValue a1 = new ZUIValue(0.92f);
            [Tooltip("Power curve of the alpha climb: below 1 alpha rises fast early, which puts an EDGE on the head without drawing a line.")]
            [ZUILabel("Fade shape")] [ZUIGroup("Opacity")]
            [Range(0.3f, 2f)] public ZUIValue acurve = new ZUIValue(1f);
            [Tooltip("Full name: \"Maximum opacity\". Opacity ceiling: below 1 the body is never fully opaque anywhere (Membrane keeps 0.89 — being a window is its whole subject).")]
            [ZUILabel("Max alpha")] [ZUIGroup("Opacity")]
            [Range(0.1f, 1f)] public ZUIValue amax = new ZUIValue(1f);
            [Tooltip("Compression of the LEADING half of the body along the travel axis: every reference head is blunter in front than behind.")]
            [ZUILabel("Front flatten")] [ZUIGroup("Body & halo", Tooltip = "The shape of the ball itself and the light it sits in.")]
            [Range(0.6f, 1f)] public ZUIValue noseSquash = new ZUIValue(0.9f);
            [Tooltip("Halo width as a multiple of the core radius (the light the orb sits in, a low-amplitude term of the same field).")]
            [ZUILabel("Halo width")] [ZUIGroup("Body & halo")]
            [Range(1f, 4f)] public ZUIValue glowWide = new ZUIValue(2.4f);
            [Tooltip("Strength of the halo's tail lobe behind the orb, relative to the halo.")]
            [ZUILabel("Halo trail")] [ZUIGroup("Body & halo")]
            [Range(0f, 1.5f)] public ZUIValue glowTail = new ZUIValue(0.6f);
            [Tooltip("Full name: \"Halo brightness\". Halo field amplitude: 0.11 lands at the ramp's dark end on its own.")]
            [ZUILabel("Halo glow")] [ZUIGroup("Body & halo")]
            [Range(0f, 0.4f)] public ZUIValue glowAmp = new ZUIValue(0.11f);

            /// The shared dials resolved at one layer life — what the programs and the tone map read (slots 10–18).
            public struct Shared { public float gain, a0, a1, acurve, amax, noseSquash, glowWide, glowTail, glowAmp; }
            [NonSerialized] public Shared live;
            public virtual void Resolve(in PyreFormPrepareCtx ctx) => live = new Shared
            {
                gain = ctx.Eval(gain, 10), a0 = ctx.Eval(a0, 11), a1 = ctx.Eval(a1, 12), acurve = ctx.Eval(acurve, 13), amax = ctx.Eval(amax, 14),
                noseSquash = ctx.Eval(noseSquash, 15), glowWide = ctx.Eval(glowWide, 16), glowTail = ctx.Eval(glowTail, 17), glowAmp = ctx.Eval(glowAmp, 18),
            };
            /// The Static values (no renderer funnel) — for the ramp probe, which runs outside a frame.
            public void ResolveStatic() => live = new Shared
            {
                gain = gain.staticValue, a0 = a0.staticValue, a1 = a1.staticValue, acurve = acurve.staticValue, amax = amax.staticValue,
                noseSquash = noseSquash.staticValue, glowWide = glowWide.staticValue, glowTail = glowTail.staticValue, glowAmp = glowAmp.staticValue,
            };
        }

        [Serializable] public sealed class EmberdriftSettings : StyleSettings
        {
            public EmberdriftSettings() { ramp = PyreRampPresets.OrbEmber(); gain = new ZUIValue(0.58f); a0 = new ZUIValue(0.018f); a1 = new ZUIValue(0.684f); acurve = new ZUIValue(0.788f); amax = new ZUIValue(1f); noseSquash = new ZUIValue(0.86f); glowWide = new ZUIValue(2.5f); glowTail = new ZUIValue(0.65f); glowAmp = new ZUIValue(0.12f); }
            [Tooltip("Primary wake turbulence: base frequency in radians per source px (0.40 — a noise cell the same order as the wake is tall, or it tears into bars).")]
            [ZUILabel("Tear size")] [ZUIGroup("Wake", Tooltip = "The torn, smeared trail streaming off the back.")]
            [Range(0.05f, 1f)] public float turbScale = 0.40f;
            [Tooltip("Octaves of the primary wake turbulence (5 — fewer and three plane waves have a resultant direction: corduroy).")]
            [ZUILabel("Tear detail")] [ZUIGroup("Noise detail", Tooltip = "The texture underneath this variant, octave by octave. Rarely the answer to a look problem — reach for the size and strength dials above first.", Advanced = true)]
            [Range(1, 8)] public int turbOct = 5;
            [Tooltip("Anisotropy of the primary wake turbulence: cells this many times longer along the travel axis than tall.")]
            [ZUILabel("Tear stretch")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAniso = 3.0f;
            [Tooltip("Weight of the primary wake turbulence in the tearing mix.")]
            [ZUILabel("Tear strength")] [ZUIGroup("Wake")]
            [Range(0f, 1.5f)] public ZUIValue turbW = new ZUIValue(0.62f);
            [Tooltip("Seed offset of the primary wake turbulence (a different realisation of the same texture).")]
            [ZUILabel("Tear seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOff = 7;
            [Tooltip("Full name: \"Fine tear size\". Secondary wake turbulence frequency (radians per source px) — a second field at another scale so no single octave can comb the wake.")]
            // T-0284 — the four secondary-texture dials carried the SAME labels as their primaries, and three
            // of the pairs land on consecutive rows of one box (measured 23 px apart in Noise detail). Each
            // now uses its own declared full name, which is how turbW2 was already disambiguated as
            // "Tear amount" beside turbW's "Tear strength" — this finishes that pass.
            [ZUILabel("Fine tear size")] [ZUIGroup("Wake")]
            [Range(0.05f, 1f)] public float turbScale2 = 0.17f;
            [Tooltip("Full name: \"Fine tear detail\". How much fine structure the second texture carries: more shows smaller detail riding on the tongues, fewer leaves them smooth.")]
            [ZUILabel("Fine tear detail")] [ZUIGroup("Noise detail", Advanced = true)]   // T-0284
            [Range(1, 8)] public int turbOct2 = 3;
            [Tooltip("Full name: \"Fine tear stretch\". Stretches the second texture along the travel axis: higher draws long streaks, 1 draws round cells.")]
            [ZUILabel("Fine tear stretch")] [ZUIGroup("Noise detail", Advanced = true)]   // T-0284
            [Range(0.5f, 6f)] public float turbAniso2 = 1.5f;
            [Tooltip("Full name: \"Fine tear strength\". Weight of the secondary wake turbulence in the tearing mix.")]
            [ZUILabel("Tear amount")] [ZUIGroup("Wake")]
            [Range(0f, 1.5f)] public ZUIValue turbW2 = new ZUIValue(0.55f);
            [Tooltip("Full name: \"Fine tear seed\". A different random draw of the same texture — the character of the wake is unchanged, only which tongues land where.")]
            [ZUILabel("Fine tear seed")] [ZUIGroup("Noise detail", Advanced = true)]   // T-0284
            [Range(0, 99)] public int turbSeedOff2 = 23;
            [Tooltip("Full name: \"Wake smear length\". Speed smudge of the wake: taps in source px (9 at decay 0.80 ≈ 4 px of exposure trail — enough to pull every tongue into a streak without dissolving the tearing). The core is NOT smeared: the crisp nose / torn tail asymmetry is how a still frame shows direction.")]
            [ZUILabel("Wake smear")] [ZUIGroup("Wake")]
            [Range(0, 30)] public int smearTaps = 9;
            [Tooltip("Full name: \"Wake smear falloff\". How far back the smear carries: high keeps every tongue bright most of the way down the wake, low pulls it in tight behind the ball.")]
            [ZUILabel("Wake falloff")] [ZUIGroup("Wake")]
            [Range(0.3f, 0.99f)] public ZUIValue smearDecay = new ZUIValue(0.80f);
            [Tooltip("Full name: \"Smear as motion blur\". Normalise the smear (a motion blur: energy redistributed) rather than accumulate it (a light streak).")]
            [ZUILabel("Motion blur")] [ZUIGroup("Wake")]
            public bool smearNorm = true;
            [Tooltip("Core-warp turbulence frequency (radians per source px): the noise is applied to the DISTANCE so the whole body boils rather than wearing a noisy outline.")]
            [ZUILabel("Boil size")] [ZUIGroup("Body", Tooltip = "The boiling ball at the front.")]
            [Range(0.05f, 1f)] public float turbScaleBody = 0.135f;
            [Tooltip("How fine the boiling is: more octaves put smaller cells crawling over the body, fewer make it churn in big slow lobes.")]
            [ZUILabel("Boil detail")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(1, 8)] public int turbOctBody = 4;
            [Tooltip("Stretches the boiling cells along the travel axis, so the body churns lengthwise rather than evenly.")]
            [ZUILabel("Boil stretch")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAnisoBody = 1.25f;
            [Tooltip("A different random draw of the same boiling — the body churns the same amount, just in other places.")]
            [ZUILabel("Boil seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOffBody = 1;
            [Tooltip("How far the turbulence warps the core's radius (in radii).")]
            [ZUILabel("Boil depth")] [ZUIGroup("Body")]
            [Range(0f, 0.6f)] public ZUIValue coreWarp = new ZUIValue(0.200f);
            [Tooltip("Full name: \"Core brightness\". Core field amplitude — steep and hot (3.15 at p 1.75): white only at the middle, then yellow, orange, deep red, nothing, across the same 16 px.")]
            [ZUILabel("Core glow")] [ZUIGroup("Body")]
            [Range(0.5f, 6f)] public ZUIValue coreAmp = new ZUIValue(3.15f);
            [Tooltip("Core falloff exponent: above 1 leaves the plateau slowly and reaches zero with zero slope (no boundary anywhere); below 1 is a flat mid-tone with a hard edge.")]
            [ZUILabel("Core softness")] [ZUIGroup("Body")]
            [Range(0.5f, 4f)] public ZUIValue coreP = new ZUIValue(1.75f);
            [Tooltip("An ember burst is shed every this many frames (must divide the frame count for an exact loop).")]
            [ZUILabel("Burst every")] [ZUIGroup("Embers", Tooltip = "The sparks shed backward off the body.")]
            [Range(1, 6)] public int emitPeriod = 2;
            [Tooltip("Full name: \"Ember lifetime\". Frames an ember lives.")]
            [ZUILabel("Ember life")] [ZUIGroup("Embers")]
            [Range(1, 24)] public int emitLife = 8;
            [Tooltip("Full name: \"Embers per burst\". Embers per shed burst (3 — fine and dispersing, not countable beads on a line).")]
            [ZUILabel("Ember count")] [ZUIGroup("Embers")]
            [Range(0, 8)] public int embersPerPiece = 3;
            [Tooltip("Full name: \"Ember brightness\". Ember field amplitude at birth.")]
            [ZUILabel("Ember glow")] [ZUIGroup("Embers")]
            [Range(0f, 3f)] public ZUIValue emberAmp = new ZUIValue(1.30f);
            [Tooltip("Full name: \"Ember fade shape\". How an ember dies: above 1 it holds its brightness then drops away near the end, below 1 it dims the moment it is born.")]
            [ZUILabel("Ember fade")] [ZUIGroup("Embers")]
            [Range(0.5f, 3f)] public float emberFadeP = 1.5f;
            [Tooltip("Ember streak: how many times longer than wide along its own velocity.")]
            [ZUILabel("Ember streak")] [ZUIGroup("Embers")]
            [Range(1f, 5f)] public ZUIValue emberElong = new ZUIValue(2.4f);

            /// This variant's own envelopes resolved at one layer life (slots 20 onward; variants share them — only the active one is resolved).
            public struct Own { public float coreWarp, coreAmp, coreP, turbW, turbW2, smearDecay, emberAmp, emberElong; }
            [NonSerialized] public Own own;
            public override void Resolve(in PyreFormPrepareCtx ctx) { base.Resolve(ctx); own = new Own { coreWarp = ctx.Eval(coreWarp, 20), coreAmp = ctx.Eval(coreAmp, 21), coreP = ctx.Eval(coreP, 22), turbW = ctx.Eval(turbW, 23), turbW2 = ctx.Eval(turbW2, 24), smearDecay = ctx.Eval(smearDecay, 25), emberAmp = ctx.Eval(emberAmp, 26), emberElong = ctx.Eval(emberElong, 27) }; }
        }

        [Serializable] public sealed class WispSettings : StyleSettings
        {
            public WispSettings() { ramp = PyreRampPresets.OrbFrost(); gain = new ZUIValue(0.95f); a0 = new ZUIValue(0.015f); a1 = new ZUIValue(0.746f); acurve = new ZUIValue(0.902f); amax = new ZUIValue(1f); noseSquash = new ZUIValue(0.90f); glowWide = new ZUIValue(2.9f); glowTail = new ZUIValue(0.55f); glowAmp = new ZUIValue(0.10f); }
            [Tooltip("Full name: \"Trail smoothness\". How many overlapping stamps build the trail: high draws one continuous tube, low leaves it beaded.")]
            [ZUILabel("Trail smooth")] [ZUIGroup("Trail", Tooltip = "The snaking persistence trail behind the nucleus.")]
            [Range(10, 200)] public int tubeStamps = 90;
            [Tooltip("Full name: \"Trail edge softness\". Above 1 the trail fades out with no boundary anywhere; below 1 it is flat with a visible edge.")]
            [ZUILabel("Trail edge")] [ZUIGroup("Trail")]
            [Range(0.5f, 4f)] public ZUIValue tubeP = new ZUIValue(1.60f);
            [Tooltip("Each trail stamp is this many times wider (along x) than tall.")]
            [ZUILabel("Trail stretch")] [ZUIGroup("Trail")]
            [Range(1f, 4f)] public ZUIValue tubeXstretch = new ZUIValue(2.2f);
            [Tooltip("Full name: \"Trail smear length\". Speed smudge of the trail: taps in source px (20, long and unnormalised, so the trail reads as a light streak building up behind the head).")]
            [ZUILabel("Trail smear")] [ZUIGroup("Trail")]
            [Range(0, 40)] public int smearTaps = 20;
            [Tooltip("Full name: \"Trail smear falloff\". How far back the streak carries before it dies out.")]
            [ZUILabel("Trail falloff")] [ZUIGroup("Trail")]
            [Range(0.3f, 0.99f)] public ZUIValue smearDecay = new ZUIValue(0.86f);
            [Tooltip("Full name: \"Smear as motion blur\". Normalise the trail smear (off in the source: accumulate).")]
            [ZUILabel("Motion blur")] [ZUIGroup("Trail")]
            public bool smearNorm = false;
            [Tooltip("Full name: \"Trail brightness\". Scale on the smeared trail before it joins the field (0.40 tames the accumulated streak).")]
            [ZUILabel("Trail glow")] [ZUIGroup("Trail")]
            [Range(0f, 1.5f)] public ZUIValue smearPostScale = new ZUIValue(0.40f);
            [Tooltip("Full name: \"Cloud brightness\". Amplitude of the diffuse cloud round the nucleus (1.62R × 1.50R).")]
            [ZUILabel("Cloud glow")] [ZUIGroup("Head", Tooltip = "The nucleus and the diffuse cloud around it.")]
            [Range(0f, 3f)] public ZUIValue cloudAmp = new ZUIValue(1.00f);
            [Tooltip("Full name: \"Cloud softness\". Cloud falloff exponent (2.1: a halo around a core, not the core itself).")]
            [ZUILabel("Cloud soft")] [ZUIGroup("Head")]
            [Range(0.5f, 4f)] public ZUIValue cloudP = new ZUIValue(2.10f);
            [Tooltip("Full name: \"Core brightness\". Nucleus amplitude (breathes ±10 % at two cycles per loop).")]
            [ZUILabel("Core glow")] [ZUIGroup("Head")]
            [Range(0.5f, 6f)] public ZUIValue nucleusAmp = new ZUIValue(3.60f);
            [Tooltip("Flat top of the nucleus as a fraction of its radius.")]
            [ZUILabel("Core flat top")] [ZUIGroup("Head")]
            [Range(0f, 0.5f)] public ZUIValue nucleusFlat = new ZUIValue(0.06f);
            [Tooltip("Above 1 the nucleus fades out of its own halo with no boundary; below 1 it becomes a flat disc with an edge.")]
            [ZUILabel("Core softness")] [ZUIGroup("Head")]
            [Range(0.5f, 4f)] public ZUIValue nucleusP = new ZUIValue(1.85f);
            [Tooltip("Full name: \"Second core brightness\". Amplitude of the second nucleus riding 0.62R behind the first, so the centre has structure.")]
            [ZUILabel("Core glow 2")] [ZUIGroup("Head")]
            [Range(0f, 3f)] public ZUIValue nucleus2Amp = new ZUIValue(1.05f);
            [Tooltip("Veil turbulence frequency (radians per source px): two faint sheets drifting back through the trail.")]
            [ZUILabel("Veil size")] [ZUIGroup("Veils", Tooltip = "Two faint sheets drifting back through the trail.")]
            [Range(0.02f, 1f)] public float turbScaleVeil = 0.11f;
            [Tooltip("How much fine structure the sheets carry: more octaves shreds them, fewer keeps them as broad drifting slabs.")]
            [ZUILabel("Veil detail")] [ZUIGroup("Noise detail", Tooltip = "The texture underneath this variant, octave by octave. Rarely the answer to a look problem — reach for the size and strength dials above first.", Advanced = true)]
            [Range(1, 8)] public int turbOctVeil = 2;
            [Tooltip("Stretches the sheets along the travel axis, so they read as drifting streaks rather than patches.")]
            [ZUILabel("Veil stretch")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAnisoVeil = 3.2f;
            [Tooltip("A different random draw of the same sheets — same character, other places.")]
            [ZUILabel("Veil seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOffVeil = 3;
            [Tooltip("Veil amplitude.")]
            [ZUILabel("Veil strength")] [ZUIGroup("Veils")]
            [Range(0f, 1.5f)] public ZUIValue veilAmp = new ZUIValue(0.30f);

            /// This variant's own envelopes resolved at one layer life (slots 20 onward; variants share them — only the active one is resolved).
            public struct Own { public float smearDecay, smearPostScale, tubeP, tubeXstretch, cloudAmp, cloudP, nucleusAmp, nucleusFlat, nucleusP, nucleus2Amp, veilAmp; }
            [NonSerialized] public Own own;
            public override void Resolve(in PyreFormPrepareCtx ctx) { base.Resolve(ctx); own = new Own { smearDecay = ctx.Eval(smearDecay, 20), smearPostScale = ctx.Eval(smearPostScale, 21), tubeP = ctx.Eval(tubeP, 22), tubeXstretch = ctx.Eval(tubeXstretch, 23), cloudAmp = ctx.Eval(cloudAmp, 24), cloudP = ctx.Eval(cloudP, 25), nucleusAmp = ctx.Eval(nucleusAmp, 26), nucleusFlat = ctx.Eval(nucleusFlat, 27), nucleusP = ctx.Eval(nucleusP, 28), nucleus2Amp = ctx.Eval(nucleus2Amp, 29), veilAmp = ctx.Eval(veilAmp, 30) }; }
        }

        [Serializable] public sealed class CoronalSettings : StyleSettings
        {
            public CoronalSettings() { ramp = PyreRampPresets.OrbGold(); gain = new ZUIValue(0.95f); a0 = new ZUIValue(0.018f); a1 = new ZUIValue(0.716f); acurve = new ZUIValue(0.834f); amax = new ZUIValue(1f); noseSquash = new ZUIValue(0.90f); glowWide = new ZUIValue(2.6f); glowTail = new ZUIValue(0.50f); glowAmp = new ZUIValue(0.13f); }
            [Tooltip("Prominences alive at once (13 — at seven they converged onto two streamlines and the star grew a moustache).")]
            [ZUILabel("Flare count")] [ZUIGroup("Prominences", Tooltip = "The fan of flares leaving the star's surface and bending back.")]
            [Range(1, 30)] public int prominences = 13;
            [Tooltip("How far each flare reaches off the surface — more stamps draw a longer arc.")]
            [ZUILabel("Flare length")] [ZUIGroup("Prominences")]
            [Range(3, 40)] public int prominenceSegments = 15;
            [Tooltip("Full name: \"Flare brightness\". Prominence stamp amplitude at the root (summed, so crossings are the hottest part of the fan).")]
            [ZUILabel("Flare glow")] [ZUIGroup("Prominences")]
            [Range(0f, 2f)] public ZUIValue prominenceAmp = new ZUIValue(0.46f);
            [Tooltip("Full name: \"Flare softness\". Above 1 each flare fades out with no boundary; below 1 it draws as a hard-edged spike.")]
            [ZUILabel("Flare soft")] [ZUIGroup("Prominences")]
            [Range(0.5f, 4f)] public ZUIValue prominenceP = new ZUIValue(1.55f);
            [Tooltip("Full name: \"Flare smear length\". Speed smudge over the prominences only: taps in source px (5 — enough to knit them into one corona, not enough to stop them being thirteen).")]
            [ZUILabel("Flare smear")] [ZUIGroup("Prominences")]
            [Range(0, 20)] public int smearTaps = 5;
            [Tooltip("Full name: \"Flare smear falloff\". How far the flares are dragged back into one corona before the smear dies out.")]
            [ZUILabel("Flare falloff")] [ZUIGroup("Prominences")]
            [Range(0.3f, 0.99f)] public ZUIValue smearDecay = new ZUIValue(0.72f);
            [Tooltip("Full name: \"Smear as motion blur\". On, the smear redistributes the flares into a corona; off, it stacks them into a brighter streak than the flares themselves.")]
            [ZUILabel("Motion blur")] [ZUIGroup("Prominences")]
            public bool smearNorm = true;
            [Tooltip("Granulation turbulence frequency (radians per source px): the noise MULTIPLIES the body, so the disc keeps a clean limb while its surface has cells.")]
            [ZUILabel("Granule size")] [ZUIGroup("Star body", Tooltip = "The granulated disc itself.")]
            [Range(0.05f, 1f)] public float turbScaleBody = 0.28f;
            [Tooltip("Full name: \"Granule detail\". How fine the surface cells are: more octaves gives small mottling, fewer gives broad patches.")]
            [ZUILabel("Granule det.")] [ZUIGroup("Noise detail", Tooltip = "The texture underneath this variant, octave by octave. Rarely the answer to a look problem — reach for the size and strength dials above first.", Advanced = true)]
            [Range(1, 8)] public int turbOctBody = 4;
            [Tooltip("Full name: \"Granule stretch\". Stretches the cells along the travel axis, so the surface reads as combed rather than granular.")]
            [ZUILabel("Granule str.")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAnisoBody = 1.0f;
            [Tooltip("A different random draw of the same granulation.")]
            [ZUILabel("Granule seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOffBody = 5;
            [Tooltip("Full name: \"Star brightness\". How hot the disc reads — the whole star climbs the ramp with it.")]
            [ZUILabel("Star glow")] [ZUIGroup("Star body")]
            [Range(0.5f, 6f)] public ZUIValue coreAmp = new ZUIValue(3.05f);
            [Tooltip("Above 1 the disc leaves its bright middle slowly and reaches nothing with no edge; below 1 it is a flat plate with a rim.")]
            [ZUILabel("Star softness")] [ZUIGroup("Star body")]
            [Range(0.5f, 4f)] public ZUIValue coreP = new ZUIValue(1.80f);
            [Tooltip("How much the granulation modulates the body (±30 %).")]
            [ZUILabel("Granule depth")] [ZUIGroup("Star body")]
            [Range(0f, 1f)] public ZUIValue coreGranulation = new ZUIValue(0.30f);
            [Tooltip("Full name: \"Rim brightness\". Limb amplitude: a bright soft ring just inside the edge that makes the disc a sphere with an edge-on atmosphere, kept low so it does not whiten the whole disc.")]
            [ZUILabel("Rim glow")] [ZUIGroup("Rim", Tooltip = "The bright soft ring just inside the edge that makes the disc read as a sphere.")]
            [Range(0f, 2f)] public ZUIValue limbAmp = new ZUIValue(0.62f);
            [Tooltip("Limb radius as a fraction of the core radius.")]
            [ZUILabel("Rim position")] [ZUIGroup("Rim")]
            [Range(0.3f, 1.2f)] public ZUIValue limbR = new ZUIValue(0.76f);
            [Tooltip("Limb half-width as a fraction of the core radius (wide = a brightening, not a line).")]
            [ZUILabel("Rim thickness")] [ZUIGroup("Rim")]
            [Range(0.05f, 1f)] public ZUIValue limbW = new ZUIValue(0.44f);
            [Tooltip("Above 1 the rim blends into the disc; below 1 it reads as a drawn line round the edge.")]
            [ZUILabel("Rim softness")] [ZUIGroup("Rim")]
            [Range(0.5f, 4f)] public ZUIValue limbP = new ZUIValue(1.60f);

            /// This variant's own envelopes resolved at one layer life (slots 20 onward; variants share them — only the active one is resolved).
            public struct Own { public float coreAmp, coreP, smearDecay, prominenceAmp, prominenceP, coreGranulation, limbAmp, limbR, limbW, limbP; }
            [NonSerialized] public Own own;
            public override void Resolve(in PyreFormPrepareCtx ctx) { base.Resolve(ctx); own = new Own { coreAmp = ctx.Eval(coreAmp, 20), coreP = ctx.Eval(coreP, 21), smearDecay = ctx.Eval(smearDecay, 22), prominenceAmp = ctx.Eval(prominenceAmp, 23), prominenceP = ctx.Eval(prominenceP, 24), coreGranulation = ctx.Eval(coreGranulation, 25), limbAmp = ctx.Eval(limbAmp, 26), limbR = ctx.Eval(limbR, 27), limbW = ctx.Eval(limbW, 28), limbP = ctx.Eval(limbP, 29) }; }
        }

        [Serializable] public sealed class MembraneSettings : StyleSettings
        {
            public MembraneSettings() { ramp = PyreRampPresets.OrbToxin(); gain = new ZUIValue(0.90f); a0 = new ZUIValue(0.015f); a1 = new ZUIValue(0.70f); acurve = new ZUIValue(0.98f); amax = new ZUIValue(0.892f); noseSquash = new ZUIValue(0.92f); glowWide = new ZUIValue(2.3f); glowTail = new ZUIValue(0.45f); glowAmp = new ZUIValue(0.10f); }
            [Tooltip("Full name: \"Shell shed every\". A shell is shed every this many frames (must divide the frame count for an exact loop).")]
            [ZUILabel("Shell every")] [ZUIGroup("Shed shells", Tooltip = "The dissolving veil the bubble leaves behind it.")]
            [Range(1, 6)] public int emitPeriod = 2;
            [Tooltip("Full name: \"Shell lifetime\". Frames a shed shell lives while it thins and breaks.")]
            [ZUILabel("Shell life")] [ZUIGroup("Shed shells")]
            [Range(1, 30)] public int emitLife = 12;
            [Tooltip("Full name: \"Shell brightness\". Shed shell amplitude at birth (filled soft blobs the noise eats holes in — a skin coming apart leaves haze, not rings).")]
            [ZUILabel("Shell glow")] [ZUIGroup("Shed shells")]
            [Range(0f, 4f)] public ZUIValue shellAmp = new ZUIValue(2.05f);
            [Tooltip("Full name: \"Shell softness\". Above 1 a shed shell fades away with no boundary; below 1 it leaves a visible disc behind the bubble.")]
            [ZUILabel("Shell soft")] [ZUIGroup("Shed shells")]
            [Range(0.5f, 4f)] public ZUIValue shellP = new ZUIValue(1.50f);
            [Tooltip("Shell-break turbulence frequency (radians per source px).")]
            [ZUILabel("Break-up size")] [ZUIGroup("Shed shells")]
            [Range(0.05f, 1f)] public float turbScaleShell = 0.22f;
            [Tooltip("Full name: \"Break-up detail\". How fine the holes eaten in a shed shell are: more octaves shreds it, fewer takes big bites out of it.")]
            [ZUILabel("Break det.")] [ZUIGroup("Noise detail", Tooltip = "The texture underneath this variant, octave by octave. Rarely the answer to a look problem — reach for the size and strength dials above first.", Advanced = true)]
            [Range(1, 8)] public int turbOctShell = 3;
            [Tooltip("Full name: \"Break-up stretch\". Stretches the break-up along the travel axis, so a shell tears into lengthwise ribbons.")]
            [ZUILabel("Break str.")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAnisoShell = 1.2f;
            [Tooltip("A different random draw of the same break-up.")]
            [ZUILabel("Break-up seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOffShell = 11;
            [Tooltip("Full name: \"Shell smear length\". Speed smudge over the shed shells: taps in source px.")]
            [ZUILabel("Shell smear")] [ZUIGroup("Shed shells")]
            [Range(0, 20)] public int smearTaps = 6;
            [Tooltip("Full name: \"Shell smear falloff\". How far a shed shell is dragged backward before the smear dies out.")]
            [ZUILabel("Shell falloff")] [ZUIGroup("Shed shells")]
            [Range(0.3f, 0.99f)] public ZUIValue smearDecay = new ZUIValue(0.74f);
            [Tooltip("Full name: \"Smear as motion blur\". On, the smear spreads a shell without brightening it; off, it stacks into a streak brighter than the shell.")]
            [ZUILabel("Motion blur")] [ZUIGroup("Shed shells")]
            public bool smearNorm = true;
            [Tooltip("Full name: \"Mote shed every\". A mote pair is shed every this many frames (2 — at 1 twenty 1 px motes were grain, not a spray).")]
            [ZUILabel("Mote every")] [ZUIGroup("Motes", Tooltip = "The countable specks the skin comes apart into.")]
            [Range(1, 6)] public int emitPeriodMotes = 2;
            [Tooltip("Frames a mote lives.")]
            [ZUILabel("Mote lifetime")] [ZUIGroup("Motes")]
            [Range(1, 30)] public int emitLifeMotes = 10;
            [Tooltip("Full name: \"Motes per shed\". Motes per shed event.")]
            [ZUILabel("Mote count")] [ZUIGroup("Motes")]
            [Range(0, 8)] public int motesPerPiece = 2;
            [Tooltip("Full name: \"Mote brightness\". Mote amplitude at birth (1.6–2.8 px streaks — the only countable objects in the generation).")]
            [ZUILabel("Mote glow")] [ZUIGroup("Motes")]
            [Range(0f, 3f)] public ZUIValue moteAmp = new ZUIValue(1.55f);
            [Tooltip("Mote streak elongation along its velocity.")]
            [ZUILabel("Mote streak")] [ZUIGroup("Motes")]
            [Range(1f, 5f)] public ZUIValue moteElong = new ZUIValue(2.2f);
            [Tooltip("Full name: \"Interior brightness\". Interior fill amplitude — the WINDOW: chosen to land near 100/255, so the inside is see-through and not a dimmer shell.")]
            [ZUILabel("Interior glow")] [ZUIGroup("Bubble", Tooltip = "The see-through interior and the skin around it.")]
            [Range(0f, 3f)] public ZUIValue windowAmp = new ZUIValue(1.30f);
            [Tooltip("Full name: \"Interior softness\". Above 1 the inside fades evenly out to the skin; below 1 it fills flat and the bubble stops reading as see-through.")]
            [ZUILabel("Interior soft")] [ZUIGroup("Bubble")]
            [Range(0.5f, 4f)] public ZUIValue windowP = new ZUIValue(1.45f);
            [Tooltip("Full name: \"Skin brightness\". Skin amplitude — under a stop above the interior, so the bubble is a filled translucent sphere and not an eye.")]
            [ZUILabel("Skin glow")] [ZUIGroup("Bubble")]
            [Range(0f, 3f)] public ZUIValue skinAmp = new ZUIValue(1.05f);
            [Tooltip("Skin radius as a fraction of the core radius.")]
            [ZUILabel("Skin position")] [ZUIGroup("Bubble")]
            [Range(0.3f, 1.2f)] public ZUIValue skinR = new ZUIValue(0.78f);
            [Tooltip("Full name: \"Skin thickness\". Skin half-width as a fraction of the core radius (0.55: a thickening toward the edge, not an outline).")]
            [ZUILabel("Skin thick")] [ZUIGroup("Bubble")]
            [Range(0.05f, 1f)] public ZUIValue skinW = new ZUIValue(0.55f);
            [Tooltip("Above 1 the skin is a thickening toward the edge; below 1 it becomes an outline drawn round the bubble.")]
            [ZUILabel("Skin softness")] [ZUIGroup("Bubble")]
            [Range(0.5f, 4f)] public ZUIValue skinP = new ZUIValue(1.70f);
            [Tooltip("Full name: \"Ripple strength\". Amplitude of the surface-tension ripples running round the skin.")]
            [ZUILabel("Ripple amt.")] [ZUIGroup("Ripples", Tooltip = "Surface tension running round the skin.")]
            [Range(0f, 1.5f)] public ZUIValue ripAmp = new ZUIValue(0.38f);
            [Tooltip("Ripple order: cycles round the skin (4-fold reads as a taut membrane).")]
            [ZUILabel("Ripple count")] [ZUIGroup("Ripples")]
            [Range(1, 12)] public int ripOrder = 4;
            [Tooltip("Ripple modulation depth (±16 %).")]
            [ZUILabel("Ripple depth")] [ZUIGroup("Ripples")]
            [Range(0f, 1f)] public ZUIValue ripDepth = new ZUIValue(0.16f);
            [Tooltip("Full name: \"Ripple position\". Ripple ring radius as a fraction of the core radius.")]
            [ZUILabel("Ripple pos.")] [ZUIGroup("Ripples")]
            [Range(0.3f, 1.3f)] public ZUIValue ripR = new ZUIValue(0.90f);
            [Tooltip("Full name: \"Ripple thickness\". Ripple ring half-width as a fraction of the core radius.")]
            [ZUILabel("Ripple thick")] [ZUIGroup("Ripples")]
            [Range(0.05f, 1f)] public ZUIValue ripW = new ZUIValue(0.30f);
            [Tooltip("Full name: \"Ripple softness\". Above 1 the ripple ring blends into the skin; below 1 it draws as a distinct band.")]
            [ZUILabel("Ripple soft")] [ZUIGroup("Ripples")]
            [Range(0.5f, 4f)] public ZUIValue ripP = new ZUIValue(1.50f);
            [Tooltip("Full name: \"Knot brightness\". Nucleus amplitude — the dense knot swimming a figure-eight inside the bubble, biased forward onto the leading wall.")]
            [ZUILabel("Knot glow")] [ZUIGroup("Inner knot", Tooltip = "The dense knot swimming inside the bubble.")]
            [Range(0f, 5f)] public ZUIValue nucleusAmp = new ZUIValue(2.40f);
            [Tooltip("Flat top of the nucleus as a fraction of its radius.")]
            [ZUILabel("Knot flat top")] [ZUIGroup("Inner knot")]
            [Range(0f, 0.5f)] public ZUIValue nucleusFlat = new ZUIValue(0.05f);
            [Tooltip("Above 1 the knot fades into the bubble around it; below 1 it reads as a solid pellet.")]
            [ZUILabel("Knot softness")] [ZUIGroup("Inner knot")]
            [Range(0.5f, 4f)] public ZUIValue nucleusP = new ZUIValue(1.55f);

            /// This variant's own envelopes resolved at one layer life (slots 20 onward; variants share them — only the active one is resolved).
            public struct Own { public float smearDecay, nucleusAmp, nucleusFlat, nucleusP, shellAmp, shellP, moteAmp, moteElong, windowAmp, windowP, skinAmp, skinR, skinW, skinP, ripAmp, ripDepth, ripR, ripW, ripP; }
            [NonSerialized] public Own own;
            public override void Resolve(in PyreFormPrepareCtx ctx) { base.Resolve(ctx); own = new Own { smearDecay = ctx.Eval(smearDecay, 20), nucleusAmp = ctx.Eval(nucleusAmp, 21), nucleusFlat = ctx.Eval(nucleusFlat, 22), nucleusP = ctx.Eval(nucleusP, 23), shellAmp = ctx.Eval(shellAmp, 24), shellP = ctx.Eval(shellP, 25), moteAmp = ctx.Eval(moteAmp, 26), moteElong = ctx.Eval(moteElong, 27), windowAmp = ctx.Eval(windowAmp, 28), windowP = ctx.Eval(windowP, 29), skinAmp = ctx.Eval(skinAmp, 30), skinR = ctx.Eval(skinR, 31), skinW = ctx.Eval(skinW, 32), skinP = ctx.Eval(skinP, 33), ripAmp = ctx.Eval(ripAmp, 34), ripDepth = ctx.Eval(ripDepth, 35), ripR = ctx.Eval(ripR, 36), ripW = ctx.Eval(ripW, 37), ripP = ctx.Eval(ripP, 38) }; }
        }

        [Serializable] public sealed class VoltcoreSettings : StyleSettings
        {
            public VoltcoreSettings() { ramp = PyreRampPresets.OrbVolt(); gain = new ZUIValue(1.00f); a0 = new ZUIValue(0.016f); a1 = new ZUIValue(0.726f); acurve = new ZUIValue(0.856f); amax = new ZUIValue(1f); noseSquash = new ZUIValue(0.90f); glowWide = new ZUIValue(2.2f); glowTail = new ZUIValue(0.55f); glowAmp = new ZUIValue(0.11f); }
            [Tooltip("Full name: \"Filament shed every\". A filament pair is shed every this many frames (1: there are always four at different stages of coming apart).")]
            [ZUILabel("Fil. every")] [ZUIGroup("Filaments", Tooltip = "The branching arcs thrown off the ball.")]
            [Range(1, 6)] public int emitPeriod = 1;
            [Tooltip("Full name: \"Filament lifetime\". Frames a filament lives: brightest and tightest at birth, a scatter of sparks by the end.")]
            [ZUILabel("Fil. life")] [ZUIGroup("Filaments")]
            [Range(1, 16)] public int emitLife = 4;
            [Tooltip("Full name: \"Filaments per shed\". How many arcs each shed event launches at once.")]
            [ZUILabel("Fil. count")] [ZUIGroup("Filaments")]
            [Range(0, 6)] public int branchesPerPiece = 2;
            [Tooltip("Full name: \"Filament length\". Random-walk steps per filament (each step turns by up to ±0.85 rad and is dragged backward more the older the filament is).")]
            [ZUILabel("Fil. length")] [ZUIGroup("Filaments")]
            [Range(1, 20)] public int filamentSteps = 9;
            [Tooltip("Full name: \"Filament brightness\". Filament amplitude at birth.")]
            [ZUILabel("Fil. glow")] [ZUIGroup("Filaments")]
            [Range(0f, 3f)] public ZUIValue filamentAmp = new ZUIValue(1.55f);
            [Tooltip("Full name: \"Filament streak\". Filament streak elongation along its step.")]
            [ZUILabel("Fil. streak")] [ZUIGroup("Filaments")]
            [Range(1f, 5f)] public ZUIValue filamentElong = new ZUIValue(1.9f);
            [Tooltip("Full name: \"Filament smear length\". Speed smudge over the filaments: taps in source px.")]
            [ZUILabel("Fil. smear")] [ZUIGroup("Filaments")]
            [Range(0, 20)] public int smearTaps = 7;
            [Tooltip("Full name: \"Filament smear falloff\". How far a filament is dragged backward before the smear dies out.")]
            [ZUILabel("Fil. falloff")] [ZUIGroup("Filaments")]
            [Range(0.3f, 0.99f)] public ZUIValue smearDecay = new ZUIValue(0.78f);
            [Tooltip("Full name: \"Smear as motion blur\". On, the smear spreads a filament without brightening it; off, it stacks into a streak brighter than the arc.")]
            [ZUILabel("Motion blur")] [ZUIGroup("Filaments")]
            public bool smearNorm = true;
            [Tooltip("Full name: \"Tail smoothness\". How many overlapping stamps build the tail: high draws one continuous plume, low leaves it beaded.")]
            [ZUILabel("Tail smooth")] [ZUIGroup("Plasma tail", Tooltip = "The tapering tube of charged haze off the back.")]
            [Range(10, 200)] public int tailStamps = 60;
            [Tooltip("Full name: \"Tail edge softness\". Above 1 the tail fades out with no boundary; below 1 it draws as a hard-edged wedge.")]
            [ZUILabel("Tail edge")] [ZUIGroup("Plasma tail")]
            [Range(0.5f, 4f)] public ZUIValue tailP = new ZUIValue(1.70f);
            [Tooltip("Full name: \"Tail smear length\". Speed smudge of the tail: taps in source px, accumulated (a light streak brighter than its source — right for a plasma tail).")]
            [ZUILabel("Tail smear")] [ZUIGroup("Plasma tail")]
            [Range(0, 30)] public int tailSmearTaps = 9;
            [Tooltip("Full name: \"Tail smear falloff\". How far back the tail streak carries before it dies out.")]
            [ZUILabel("Tail falloff")] [ZUIGroup("Plasma tail")]
            [Range(0.3f, 0.99f)] public ZUIValue tailSmearDecay = new ZUIValue(0.80f);
            [Tooltip("Full name: \"Tail smear as motion blur\". Off (the source) the streak accumulates and reads brighter than the tail that cast it — which is what makes it look like plasma rather than blur.")]
            [ZUILabel("Tail m.blur")] [ZUIGroup("Plasma tail")]
            public bool tailSmearNorm = false;
            [Tooltip("Full name: \"Tail brightness\". Scale on the smeared tail before it joins the field (0.34: the construction stays, it just stops being the loudest thing).")]
            [ZUILabel("Tail glow")] [ZUIGroup("Plasma tail")]
            [Range(0f, 1.5f)] public ZUIValue tailPostScale = new ZUIValue(0.34f);
            [Tooltip("Full name: \"Haze brightness\". Envelope amplitude (1.50R × 1.26R, nose-squashed 0.94 and nudged 0.08R forward): a haze the ball wears, sized against the ball.")]
            [ZUILabel("Haze glow")] [ZUIGroup("Haze & charge", Tooltip = "The halo the ball wears and the charge crawling inside it.")]
            [Range(0f, 3f)] public ZUIValue envelopeAmp = new ZUIValue(0.80f);
            [Tooltip("Envelope falloff exponent (2.3 — at 0.5 it drew a literal purple circle).")]
            [ZUILabel("Haze softness")] [ZUIGroup("Haze & charge")]
            [Range(0.5f, 4f)] public ZUIValue envelopeP = new ZUIValue(2.30f);
            [Tooltip("Charge turbulence frequency (radians per source px): low-frequency noise gated to the inside of the envelope.")]
            [ZUILabel("Charge size")] [ZUIGroup("Haze & charge")]
            [Range(0.05f, 1f)] public float turbScaleCharge = 0.20f;
            [Tooltip("How fine the crawling charge is: more octaves gives fizzing speckle, fewer gives slow broad swells.")]
            [ZUILabel("Charge detail")] [ZUIGroup("Noise detail", Tooltip = "The texture underneath this variant, octave by octave. Rarely the answer to a look problem — reach for the size and strength dials above first.", Advanced = true)]
            [Range(1, 8)] public int turbOctCharge = 3;
            [Tooltip("Full name: \"Charge stretch\". Stretches the charge along the travel axis, so it streams rather than pulses.")]
            [ZUILabel("Charge str.")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0.5f, 6f)] public float turbAnisoCharge = 1.6f;
            [Tooltip("A different random draw of the same charge.")]
            [ZUILabel("Charge seed")] [ZUIGroup("Noise detail", Advanced = true)]
            [Range(0, 99)] public int turbSeedOffCharge = 2;
            [Tooltip("Full name: \"Charge strength\". Charge amplitude.")]
            [ZUILabel("Charge amt.")] [ZUIGroup("Haze & charge")]
            [Range(0f, 2f)] public ZUIValue chargeAmp = new ZUIValue(0.60f);
            [Tooltip("Full name: \"Bead brightness\". Bead amplitude — the one thing allowed to be near-opaque, about 6 px of it.")]
            [ZUILabel("Bead glow")] [ZUIGroup("Bead & ball", Tooltip = "The near-opaque bead and the round body it is a highlight on.")]
            [Range(0.5f, 6f)] public ZUIValue beadAmp = new ZUIValue(3.60f);
            [Tooltip("Flat top of the bead as a fraction of its radius.")]
            [ZUILabel("Bead flat top")] [ZUIGroup("Bead & ball")]
            [Range(0f, 0.5f)] public ZUIValue beadFlat = new ZUIValue(0.12f);
            [Tooltip("Above 1 the bead melts into the ball; below 1 it reads as a hard white pellet.")]
            [ZUILabel("Bead softness")] [ZUIGroup("Bead & ball")]
            [Range(0.5f, 4f)] public ZUIValue beadP = new ZUIValue(1.45f);
            [Tooltip("Full name: \"Bead halo brightness\". Amplitude of the bead's soft halo (0.70R × 0.64R).")]
            [ZUILabel("Bead halo")] [ZUIGroup("Bead & ball")]
            [Range(0f, 3f)] public ZUIValue bead2Amp = new ZUIValue(1.05f);
            [Tooltip("Full name: \"Ball brightness\". Ball amplitude — the round body the bead is a highlight ON (2.15 against an envelope of 0.80 wins the silhouette).")]
            [ZUILabel("Ball glow")] [ZUIGroup("Bead & ball")]
            [Range(0f, 5f)] public ZUIValue ballAmp = new ZUIValue(2.15f);
            [Tooltip("Ball falloff exponent (soft enough not to reintroduce an outline).")]
            [ZUILabel("Ball softness")] [ZUIGroup("Bead & ball")]
            [Range(0.5f, 4f)] public ZUIValue ballP = new ZUIValue(1.95f);

            /// This variant's own envelopes resolved at one layer life (slots 20 onward; variants share them — only the active one is resolved).
            public struct Own { public float smearDecay, filamentAmp, filamentElong, tailP, tailSmearDecay, tailPostScale, envelopeAmp, envelopeP, chargeAmp, beadAmp, beadFlat, beadP, bead2Amp, ballAmp, ballP; }
            [NonSerialized] public Own own;
            public override void Resolve(in PyreFormPrepareCtx ctx) { base.Resolve(ctx); own = new Own { smearDecay = ctx.Eval(smearDecay, 20), filamentAmp = ctx.Eval(filamentAmp, 21), filamentElong = ctx.Eval(filamentElong, 22), tailP = ctx.Eval(tailP, 23), tailSmearDecay = ctx.Eval(tailSmearDecay, 24), tailPostScale = ctx.Eval(tailPostScale, 25), envelopeAmp = ctx.Eval(envelopeAmp, 26), envelopeP = ctx.Eval(envelopeP, 27), chargeAmp = ctx.Eval(chargeAmp, 28), beadAmp = ctx.Eval(beadAmp, 29), beadFlat = ctx.Eval(beadFlat, 30), beadP = ctx.Eval(beadP, 31), bead2Amp = ctx.Eval(bead2Amp, 32), ballAmp = ctx.Eval(ballAmp, 33), ballP = ctx.Eval(ballP, 34) }; }
        }

        // ── runtime ──
        [NonSerialized] float[] _E, _A, _scratch, _tone, _alpha;
        [NonSerialized] byte[] _aBytes;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpA;
        [NonSerialized] List<(int, int)> _emit;
        [NonSerialized] double[] _lut; [NonSerialized] int _lutHash;

        /// The form's own envelopes resolved at one layer life (slots 0–4).
        public struct Live { public float noseX, axisY, radius, wake, swarmSize; }
        [NonSerialized] public Live live;

        public override void Prepare(in PyreFormPrepareCtx ctx)
        {
            live = new Live { noseX = ctx.Eval(noseX, 0), axisY = ctx.Eval(axisY, 1), radius = ctx.Eval(radius, 2), wake = ctx.Eval(wake, 3), swarmSize = ctx.Eval(swarmSize, 4) };
            Active.Resolve(ctx);
        }

        StyleSettings Active => variant switch
        {
            Variant.Wisp => wisp, Variant.Coronal => coronal, Variant.Membrane => membrane, Variant.Voltcore => voltcore, _ => emberdrift,
        };

        OrbSource Source => variant switch
        {
            Variant.Wisp => OrbSource.Wisp, Variant.Coronal => OrbSource.Coronal, Variant.Membrane => OrbSource.Membrane,
            Variant.Voltcore => OrbSource.Voltcore, _ => OrbSource.Emberdrift,
        };

        /// The LUT entry the comparer's lut_at reads for t (index round-half-even of t·255, t taken at the probe's own
        /// one-decimal precision) and the window alpha at that entry's position i/255.
        Color IPlusRampProbe.ProbeRamp(float t)
        {
            Active.ResolveStatic();   // the probe runs outside a frame: the Static values are the answer
            var st = MakeStyle(Active);
            double td = Math.Round((double)t, 1);
            int i = Mathf.Clamp((int)Math.Round(td * 255.0, MidpointRounding.ToEven), 0, 255);
            return new Color((float)(st.lut[i * 3] / 255.0), (float)(st.lut[i * 3 + 1] / 255.0), (float)(st.lut[i * 3 + 2] / 255.0), (float)st.Alpha(i / 255.0));
        }

        void IPlusFieldPublisher.PublishFields(Action<string, float[]> sink)
        {
            if (_dumpH != null) sink("H", _dumpH);
            if (_dumpT != null) sink("ramp_t", _dumpT);
            if (_dumpA != null) sink("alpha_f", _dumpA);
            _dumpH = _dumpT = _dumpA = null;
        }

        OrbStyle MakeStyle(StyleSettings s)
        {
            int h = s.ramp != null ? RampHash(s.ramp) : 0;
            if (_lut == null || _lutHash != h) { _lut = PyreOrb.BakeLut(s.ramp); _lutHash = h; }
            return new OrbStyle { gain = s.live.gain, a0 = s.live.a0, a1 = s.live.a1, acurve = s.live.acurve, amax = s.live.amax, floor = floor, lut = _lut };
        }

        /// The shared identity, not a private copy of it: a ramp is its stops, its blend space AND its Adjust
        /// knobs, and folding only the stops here meant turning a knob repainted nothing until something else
        /// happened to invalidate `_lut`.
        static int RampHash(PyreRamp r) => PyreShade.RampHash(r);

        public override void Render(in PyreFormCtx ctx, Color32[] target)
        {
            // The renderer Prepares before Render; a direct caller (a test, a probe) may not — same funnel, same life, idempotent.
            Prepare(ctx.PrepareCtxAt(ctx.life));
            int W = ctx.W, H = ctx.H, n = W * H;
            if (_E == null || _E.Length != n) { _E = new float[n]; _A = new float[n]; _scratch = new float[n]; _aBytes = new byte[n]; _tone = _alpha = null; }
            bool dump = PyreFormDebug.FieldSink != null;
            if (dump && (_tone == null || _tone.Length != n)) { _tone = new float[n]; _alpha = new float[n]; }
            Array.Clear(_E, 0, n);
            _emit ??= new List<(int, int)>();

            // The spec seed IS the Kiln seed (layer 0 of seed 2101 draws emberdrift's own turbulence and embers); further
            // layers decorrelate by a large stride, swarm instances by their index.
            long seed = (long)ctx.seed + (long)ctx.layerSalt * 1000003L;
            var src = Source;
            var st = Active;
            int t = ctx.frameIndex, N = Math.Max(1, ctx.frameCount);
            double uSolo = Math.Max(live.radius * W, 0.5) / src.R;

            if (ctx.swarm == null)
                DrawOne(MakeFrame(W, H, live.noseX * W, live.axisY * H, uSolo, 1.0, src), t, N, seed, st);
            else
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    double u = uSolo * live.swarmSize * Math.Max(sp.sizeMul, 0.01f);
                    // swarm positions are y-up canvas px; the programs run y-down, flipped back at the write
                    DrawOne(MakeFrame(W, H, sp.x, H - sp.y, u, sp.brightMul, src), t, N, seed + sp.index * 104729L, st);
                }

            PyreFieldOps.BinomialBlur(_E, W, H, softenPasses, _scratch);
            var style = MakeStyle(st);
            PyreOrb.Rasterise(_E, W, H, style, ctx.alpha, despeckle, despeckleBelow, target, dump ? _tone : null, dump ? _alpha : null, _aBytes);
            if (dump)
            {
                _dumpH = FlippedCopy(_E, W, H); _dumpT = FlippedCopy(_tone, W, H); _dumpA = FlippedCopy(_alpha, W, H);
            }
            ApplyPixelModifiers(ctx, target);
        }

        static OrbFrame MakeFrame(int W, int H, double ox, double oy, double u, double amp, in OrbSource src) => new OrbFrame
        {
            W = W, H = H, u = u, ox = ox, oy = oy, nx = src.nx, cy = src.cy, R = src.R, L = 0, amp = amp,
        };

        void DrawOne(OrbFrame fr, int t, int N, long seed, StyleSettings st)
        {
            fr.L = live.wake * fr.R;
            switch (variant)
            {
                case Variant.Wisp: PyreOrb.Wisp(wisp, fr, t, N, seed, _E, _A, _scratch); break;
                case Variant.Coronal: PyreOrb.Coronal(coronal, fr, t, N, seed, _E, _A, _scratch); break;
                case Variant.Membrane: PyreOrb.Membrane(membrane, fr, t, N, seed, _E, _A, _scratch, _emit); break;
                case Variant.Voltcore: PyreOrb.Voltcore(voltcore, fr, t, N, seed, _E, _A, _scratch, _emit); break;
                default: PyreOrb.Emberdrift(emberdrift, fr, t, N, seed, _E, _A, _scratch, _emit); break;
            }
        }

        /// Planes are computed y-down (the source's frame); the harness expects renderer buffers (y-up) and flips them itself.
        static float[] FlippedCopy(float[] p, int W, int H)
        {
            var o = new float[W * H];
            for (int y = 0; y < H; y++) Array.Copy(p, y * W, o, (H - 1 - y) * W, W);
            return o;
        }

        /// The layer's pixel modifiers, per lit canvas pixel (the heat-ramp forms' convention).
        void ApplyPixelModifiers(in PyreFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyreRenderer.Hash(ctx.seed, PyreRenderer.ModParticleIndex, ctx.layerSalt, 0x1f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var c = target[i];
                    if (c.a == 0) continue;
                    var col = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
                    float alpha = c.a / 255f;
                    var info = new Laubrary.SpriteFx.PixelInfo(x, y, x + 0.5f, y + 0.5f, ctx.frameIndex, ctx.phase, ctx.life, pixHash, W, H);
                    bool keep = true;
                    for (int m = 0; m < ctx.pix.Length && keep; m++) keep = ctx.pix[m].ApplyPixel(ref col, ref alpha, info);
                    target[i] = keep
                        ? new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(col.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(col.g * 255f), 0, 255),
                                      (byte)Mathf.Clamp(Mathf.RoundToInt(col.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255))
                        : default;
                }
        }

        // ── contract loading, for the parity runs ──
        [NonSerialized] double _cW, _cH, _cCy, _cNx, _cR, _cL;

        /// Apply a contract param by its key: `tag`/`draw` picks the variant; `w`/`h`/`cy`/`nx`/`R`/`L` (source px) are
        /// converted to the placement dials on Pyre's square max(w, h) canvas with the frame letterboxed at the
        /// centre (top row (S − h)/2, which is what `PyreParityDump.DumpOptions.cropH` cuts back out); `ramp` picks the
        /// preset by its Kiln name; `HARDNESS` is informational (already folded into a1 / acurve / amax); every other
        /// numeric key goes to a same-named field of the form or of the ACTIVE variant's box. Formula strings return
        /// false (they are frozen literals of the program).
        public bool SetContractParam(string key, object value)
        {
            switch (key)
            {
                case "tag": case "draw": case "variant":
                    if (Enum.TryParse(value?.ToString(), true, out Variant v)) { variant = v; return true; }
                    return false;
                case "w": _cW = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "h": _cH = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "cy": _cCy = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "nx": _cNx = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "R": _cR = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "L": _cL = Convert.ToDouble(value); ResolveGeometry(); return true;
                case "ramp": { var r = PyreRampPresets.Orb(value?.ToString()); if (r == null) return false; Active.ramp = r; return true; }
                case "HARDNESS": case "frames": case "fps": case "seed": return true;
            }
            if (value is string) return false;
            string name = key.Replace("_", "");
            if (SetField(this, name, value)) return true;
            return SetField(Active, name, value);
        }

        void ResolveGeometry()
        {
            if (_cW <= 0 || _cH <= 0) return;
            double S = Math.Max(_cW, _cH), top = Math.Floor((S - _cH) / 2.0);
            if (_cNx > 0) noseX = new ZUIValue((float)(_cNx / S));
            if (_cCy > 0) axisY = new ZUIValue((float)((top + _cCy) / S));
            if (_cR > 0) radius = new ZUIValue((float)(_cR / S));
            if (_cR > 0 && _cL > 0) wake = new ZUIValue((float)(_cL / _cR));
        }

        static bool SetField(object owner, string name, object value)
        {
            foreach (var fi in owner.GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.FieldType == typeof(int)) fi.SetValue(owner, Convert.ToInt32(value));
                else if (fi.FieldType == typeof(float)) fi.SetValue(owner, Convert.ToSingle(value));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(owner, value is bool b ? b : Convert.ToSingle(value) != 0f);
                else if (fi.FieldType == typeof(ZUIValue)) fi.SetValue(owner, new ZUIValue(Convert.ToSingle(value)));   // a contract scalar = the Static value
                else return false;
                return true;
            }
            return false;
        }
    }
}
