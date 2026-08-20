// OrbForm — Kiln "Energy Projectile / agent2" THE ORB, generation 2, as a PlusForm.
//
// Source: D:/CODEZ/Kiln/projects/Energy Projectile/agents/agent2 (gen.py + orbcanvas.py, MANIFEST "generation 2 — the
// slider came back: 4"). The algorithm and the per-component ported / approximated / dropped list are in PlusOrb.cs's
// header. The five published draws are five different PROGRAMS (a torn-and-smeared wake, a persistence trail, a fan
// of prominences, a dissolving shell, shed filaments) that share one canvas model, so — as Arc Burst did — they are
// one form with a `variant` and one settings box per variant, each box's defaults being that draw's literals and
// contract values (D:/Claude@GDrive/Energy Projectile/GEN2/contract/agent2/<draw>/params.json). Canonical variant =
// `emberdrift` (#001 of the set, the gen-1 grade-4 rebuilt; the Appendix A summary — "additive kernels + plane-wave
// turbulence, smear + soften" — describes it). The shared geometry dials default to emberdrift's frame.
//
// Dial names in each box ARE the contract's parameter keys (turb_scale → turbScale, emit_life → emitLife …) so
// `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning is in
// every [Tooltip]. Units: the orb's placement and radius are canvas fractions; the wake is in core radii; everything
// inside a box is in the archetype's own source px / field units (see PlusOrb.cs — the picture is the source's,
// magnified to the dialled radius), so the form reads the same at PyrePlus's 64 px as at the contract's 192 × 96.
//
// The alpha window's a1 / acurve / amax carry the source's answered hardness (HARDNESS = 0.40 applied through its
// mix(soft, hard)); they are dials here, so the "how hard" question the agent asked is a slider in the box.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    [Serializable]
    [PlusFormInfo("Orb", group: "Kiln/Energy Projectile", icon: "sparkle")]
    public sealed class OrbForm : PlusForm, IPlusFieldPublisher, IPlusRampProbe
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
            + "modifier. SWARM: off = one orb at Nose X / Axis Y; on = one orb per swarm particle at its position, "
            + "sized by Swarm Size and the particle's depth shading, all sharing the clip's loop clock.";

        /// Colour is the variant's ramp + the tone map, never the layer Fill.
        public override bool UsesFill => false;

        public enum Variant { Emberdrift, Wisp, Coronal, Membrane, Voltcore }

        [Tooltip("Which of the five published orbs this is. Each is its own program with its own settings box below; switching keeps the shared placement dials.")]
        public Variant variant = Variant.Emberdrift;

        // ── placement (shared) ──
        [Tooltip("Where the orb's nose (the core centre) sits across the canvas, as a fraction of the width. The wake trails to the LEFT of it (travel is toward +x). Source: 0.755 of a 192 px frame.")]
        [Range(0.2f, 0.95f)] public float noseX = 0.755f;
        [Tooltip("The travel axis down the canvas, as a fraction of the height (0 = top). Source: 0.52.")]
        [Range(0.1f, 0.9f)] public float axisY = 0.52f;
        [Tooltip("Core radius R as a fraction of the canvas width; every length inside the variant scales with it (the source's 16 px core on a 192 px frame = 0.083). At 64 px that is a 5 px core.")]
        [Range(0.03f, 0.25f)] public float radius = 16f / 192f;
        [Tooltip("Wake length L in core radii — how far behind the nose the trail, the shells or the tail reach. The source runs its wakes at 5–9 radii (emberdrift 7.6).")]
        [Range(1f, 12f)] public float wake = 122f / 16f;

        // ── finish (shared) ──
        [Tooltip("Binomial soften passes over the summed field before the tone map — one pixel of radius per pass that fuses the seams between shapes. The source uses exactly 1 (a second pass blurs the head's definition back off).")]
        [Range(0, 3)] public int softenPasses = 1;
        [Tooltip("Alpha (0..255) under which a pixel is dropped entirely — nothing carries colour it cannot show.")]
        [Range(0, 16)] public int floor = 3;
        [Tooltip("Drop lit pixels that are BOTH faint (alpha under Despeckle Below) and isolated (fewer than 2 lit 4-neighbours): the sampled boundary of a wake's falloff, not artwork.")]
        public bool despeckle = true;
        [ZUIShowIf("despeckle", "True")]
        [Tooltip("Alpha (0..255) below which an isolated pixel counts as haze to despeckle; a real spark above it is never touched.")]
        [Range(1, 255)] public int despeckleBelow = 40;

        // ── the variants ──
        [ZUIShowIf("variant", "Emberdrift")] [Tooltip("emberdrift's literals and contract values (seed 2101, 192 × 96, 20 frames).")] public EmberdriftSettings emberdrift = new EmberdriftSettings();
        [ZUIShowIf("variant", "Wisp")] [Tooltip("wisp's literals and contract values (seed 2202, 200 × 80, 24 frames).")] public WispSettings wisp = new WispSettings();
        [ZUIShowIf("variant", "Coronal")] [Tooltip("coronal's literals and contract values (seed 2303, 184 × 108, 24 frames).")] public CoronalSettings coronal = new CoronalSettings();
        [ZUIShowIf("variant", "Membrane")] [Tooltip("membrane's literals and contract values (seed 2404, 180 × 100, 24 frames).")] public MembraneSettings membrane = new MembraneSettings();
        [ZUIShowIf("variant", "Voltcore")] [Tooltip("voltcore's literals and contract values (seed 2505, 184 × 104, 24 frames).")] public VoltcoreSettings voltcore = new VoltcoreSettings();

        // ── swarm ──
        [PlusSwarmOnly]
        [Tooltip("Size of each swarm particle's orb as a fraction of the solo Radius (the swarm's own size/depth shading multiplies it).")]
        [Range(0.1f, 1f)] public float swarmSize = 0.5f;

        // ── settings boxes ─────────────────────────────────────────────────────────────────────────────────────

        /// The tone → pixel dials every variant carries (orbcanvas.Style): a gain into the tone map and the ALPHA WINDOW.
        /// a1 / acurve / amax hold the source's mix(soft, hard) at HARDNESS 0.40 — the answered slider.
        [Serializable] public abstract class StyleSettings
        {
            [Tooltip("The signature colour ramp: position 0 = the faintest energy (the halo and the wake live there), 1 = the white-hot nucleus. Interpolated in sRGB like the source. Presets: PlusRampPresets.OrbEmber / Frost / Gold / Toxin / Volt.")]
            public PlusRamp ramp;
            [Tooltip("Scales the field into the tone map 1 − exp(−E·gain): higher = the same field reads hotter and further up the ramp. The one knob that rescales the whole picture against the ramp rather than its parts against each other.")]
            [Range(0.2f, 3f)] public float gain = 1f;
            [Tooltip("Tone value where opacity lifts off (below it the pixel is transparent).")]
            [Range(0f, 0.1f)] public float a0 = 0.02f;
            [Tooltip("Tone value where opacity reaches its maximum — THE hardness dial: near 1 opacity is still climbing at the centre (fog with a bright patch), lower and the climb finishes inside the body (a solid nucleus, a gradient rim, no cliff). The source's answered value.")]
            [Range(0.2f, 1f)] public float a1 = 0.92f;
            [Tooltip("Power curve of the alpha climb: below 1 alpha rises fast early, which puts an EDGE on the head without drawing a line.")]
            [Range(0.3f, 2f)] public float acurve = 1f;
            [Tooltip("Opacity ceiling: below 1 the body is never fully opaque anywhere (Membrane keeps 0.89 — being a window is its whole subject).")]
            [Range(0.1f, 1f)] public float amax = 1f;
            [Tooltip("Compression of the LEADING half of the body along the travel axis: every reference head is blunter in front than behind.")]
            [Range(0.6f, 1f)] public float noseSquash = 0.9f;
            [Tooltip("Halo width as a multiple of the core radius (the light the orb sits in, a low-amplitude term of the same field).")]
            [Range(1f, 4f)] public float glowWide = 2.4f;
            [Tooltip("Strength of the halo's tail lobe behind the orb, relative to the halo.")]
            [Range(0f, 1.5f)] public float glowTail = 0.6f;
            [Tooltip("Halo field amplitude: 0.11 lands at the ramp's dark end on its own.")]
            [Range(0f, 0.4f)] public float glowAmp = 0.11f;
        }

        [Serializable] public sealed class EmberdriftSettings : StyleSettings
        {
            public EmberdriftSettings() { ramp = PlusRampPresets.OrbEmber(); gain = 0.58f; a0 = 0.018f; a1 = 0.684f; acurve = 0.788f; amax = 1f; noseSquash = 0.86f; glowWide = 2.5f; glowTail = 0.65f; glowAmp = 0.12f; }
            [Tooltip("Primary wake turbulence: base frequency in radians per source px (0.40 — a noise cell the same order as the wake is tall, or it tears into bars).")]
            [Range(0.05f, 1f)] public float turbScale = 0.40f;
            [Tooltip("Octaves of the primary wake turbulence (5 — fewer and three plane waves have a resultant direction: corduroy).")]
            [Range(1, 8)] public int turbOct = 5;
            [Tooltip("Anisotropy of the primary wake turbulence: cells this many times longer along the travel axis than tall.")]
            [Range(0.5f, 6f)] public float turbAniso = 3.0f;
            [Tooltip("Weight of the primary wake turbulence in the tearing mix.")]
            [Range(0f, 1.5f)] public float turbW = 0.62f;
            [Tooltip("Seed offset of the primary wake turbulence (a different realisation of the same texture).")]
            [Range(0, 99)] public int turbSeedOff = 7;
            [Tooltip("Secondary wake turbulence frequency (radians per source px) — a second field at another scale so no single octave can comb the wake.")]
            [Range(0.05f, 1f)] public float turbScale2 = 0.17f;
            [Tooltip("Octaves of the secondary wake turbulence.")]
            [Range(1, 8)] public int turbOct2 = 3;
            [Tooltip("Anisotropy of the secondary wake turbulence.")]
            [Range(0.5f, 6f)] public float turbAniso2 = 1.5f;
            [Tooltip("Weight of the secondary wake turbulence in the tearing mix.")]
            [Range(0f, 1.5f)] public float turbW2 = 0.55f;
            [Tooltip("Seed offset of the secondary wake turbulence.")]
            [Range(0, 99)] public int turbSeedOff2 = 23;
            [Tooltip("Speed smudge of the wake: taps in source px (9 at decay 0.80 ≈ 4 px of exposure trail — enough to pull every tongue into a streak without dissolving the tearing). The core is NOT smeared: the crisp nose / torn tail asymmetry is how a still frame shows direction.")]
            [Range(0, 30)] public int smearTaps = 9;
            [Tooltip("Weight ratio per px of the wake smear.")]
            [Range(0.3f, 0.99f)] public float smearDecay = 0.80f;
            [Tooltip("Normalise the smear (a motion blur: energy redistributed) rather than accumulate it (a light streak).")]
            public bool smearNorm = true;
            [Tooltip("Core-warp turbulence frequency (radians per source px): the noise is applied to the DISTANCE so the whole body boils rather than wearing a noisy outline.")]
            [Range(0.05f, 1f)] public float turbScaleBody = 0.135f;
            [Tooltip("Octaves of the core-warp turbulence.")]
            [Range(1, 8)] public int turbOctBody = 4;
            [Tooltip("Anisotropy of the core-warp turbulence.")]
            [Range(0.5f, 6f)] public float turbAnisoBody = 1.25f;
            [Tooltip("Seed offset of the core-warp turbulence.")]
            [Range(0, 99)] public int turbSeedOffBody = 1;
            [Tooltip("How far the turbulence warps the core's radius (in radii).")]
            [Range(0f, 0.6f)] public float coreWarp = 0.200f;
            [Tooltip("Core field amplitude — steep and hot (3.15 at p 1.75): white only at the middle, then yellow, orange, deep red, nothing, across the same 16 px.")]
            [Range(0.5f, 6f)] public float coreAmp = 3.15f;
            [Tooltip("Core falloff exponent: above 1 leaves the plateau slowly and reaches zero with zero slope (no boundary anywhere); below 1 is a flat mid-tone with a hard edge.")]
            [Range(0.5f, 4f)] public float coreP = 1.75f;
            [Tooltip("An ember burst is shed every this many frames (must divide the frame count for an exact loop).")]
            [Range(1, 6)] public int emitPeriod = 2;
            [Tooltip("Frames an ember lives.")]
            [Range(1, 24)] public int emitLife = 8;
            [Tooltip("Embers per shed burst (3 — fine and dispersing, not countable beads on a line).")]
            [Range(0, 8)] public int embersPerPiece = 3;
            [Tooltip("Ember field amplitude at birth.")]
            [Range(0f, 3f)] public float emberAmp = 1.30f;
            [Tooltip("Ember fade exponent over its life: (1 − age)^p.")]
            [Range(0.5f, 3f)] public float emberFadeP = 1.5f;
            [Tooltip("Ember streak: how many times longer than wide along its own velocity.")]
            [Range(1f, 5f)] public float emberElong = 2.4f;
        }

        [Serializable] public sealed class WispSettings : StyleSettings
        {
            public WispSettings() { ramp = PlusRampPresets.OrbFrost(); gain = 0.95f; a0 = 0.015f; a1 = 0.746f; acurve = 0.902f; amax = 1f; noseSquash = 0.90f; glowWide = 2.9f; glowTail = 0.55f; glowAmp = 0.10f; }
            [Tooltip("Stamps summed down the snaking trail path (90).")]
            [Range(10, 200)] public int tubeStamps = 90;
            [Tooltip("Falloff exponent of each trail stamp.")]
            [Range(0.5f, 4f)] public float tubeP = 1.60f;
            [Tooltip("Each trail stamp is this many times wider (along x) than tall.")]
            [Range(1f, 4f)] public float tubeXstretch = 2.2f;
            [Tooltip("Speed smudge of the trail: taps in source px (20, long and unnormalised, so the trail reads as a light streak building up behind the head).")]
            [Range(0, 40)] public int smearTaps = 20;
            [Tooltip("Weight ratio per px of the trail smear.")]
            [Range(0.3f, 0.99f)] public float smearDecay = 0.86f;
            [Tooltip("Normalise the trail smear (off in the source: accumulate).")]
            public bool smearNorm = false;
            [Tooltip("Scale on the smeared trail before it joins the field (0.40 tames the accumulated streak).")]
            [Range(0f, 1.5f)] public float smearPostScale = 0.40f;
            [Tooltip("Amplitude of the diffuse cloud round the nucleus (1.62R × 1.50R).")]
            [Range(0f, 3f)] public float cloudAmp = 1.00f;
            [Tooltip("Cloud falloff exponent (2.1: a halo around a core, not the core itself).")]
            [Range(0.5f, 4f)] public float cloudP = 2.10f;
            [Tooltip("Nucleus amplitude (breathes ±10 % at two cycles per loop).")]
            [Range(0.5f, 6f)] public float nucleusAmp = 3.60f;
            [Tooltip("Flat top of the nucleus as a fraction of its radius.")]
            [Range(0f, 0.5f)] public float nucleusFlat = 0.06f;
            [Tooltip("Nucleus falloff exponent.")]
            [Range(0.5f, 4f)] public float nucleusP = 1.85f;
            [Tooltip("Amplitude of the second nucleus riding 0.62R behind the first, so the centre has structure.")]
            [Range(0f, 3f)] public float nucleus2Amp = 1.05f;
            [Tooltip("Veil turbulence frequency (radians per source px): two faint sheets drifting back through the trail.")]
            [Range(0.02f, 1f)] public float turbScaleVeil = 0.11f;
            [Tooltip("Octaves of the veil turbulence.")]
            [Range(1, 8)] public int turbOctVeil = 2;
            [Tooltip("Anisotropy of the veil turbulence.")]
            [Range(0.5f, 6f)] public float turbAnisoVeil = 3.2f;
            [Tooltip("Seed offset of the veil turbulence.")]
            [Range(0, 99)] public int turbSeedOffVeil = 3;
            [Tooltip("Veil amplitude.")]
            [Range(0f, 1.5f)] public float veilAmp = 0.30f;
        }

        [Serializable] public sealed class CoronalSettings : StyleSettings
        {
            public CoronalSettings() { ramp = PlusRampPresets.OrbGold(); gain = 0.95f; a0 = 0.018f; a1 = 0.716f; acurve = 0.834f; amax = 1f; noseSquash = 0.90f; glowWide = 2.6f; glowTail = 0.50f; glowAmp = 0.13f; }
            [Tooltip("Prominences alive at once (13 — at seven they converged onto two streamlines and the star grew a moustache).")]
            [Range(1, 30)] public int prominences = 13;
            [Tooltip("Stamps along each prominence.")]
            [Range(3, 40)] public int prominenceSegments = 15;
            [Tooltip("Prominence stamp amplitude at the root (summed, so crossings are the hottest part of the fan).")]
            [Range(0f, 2f)] public float prominenceAmp = 0.46f;
            [Tooltip("Prominence stamp falloff exponent.")]
            [Range(0.5f, 4f)] public float prominenceP = 1.55f;
            [Tooltip("Speed smudge over the prominences only: taps in source px (5 — enough to knit them into one corona, not enough to stop them being thirteen).")]
            [Range(0, 20)] public int smearTaps = 5;
            [Tooltip("Weight ratio per px of the prominence smear.")]
            [Range(0.3f, 0.99f)] public float smearDecay = 0.72f;
            [Tooltip("Normalise the prominence smear.")]
            public bool smearNorm = true;
            [Tooltip("Granulation turbulence frequency (radians per source px): the noise MULTIPLIES the body, so the disc keeps a clean limb while its surface has cells.")]
            [Range(0.05f, 1f)] public float turbScaleBody = 0.28f;
            [Tooltip("Octaves of the granulation.")]
            [Range(1, 8)] public int turbOctBody = 4;
            [Tooltip("Anisotropy of the granulation.")]
            [Range(0.5f, 6f)] public float turbAnisoBody = 1.0f;
            [Tooltip("Seed offset of the granulation.")]
            [Range(0, 99)] public int turbSeedOffBody = 5;
            [Tooltip("Star body amplitude.")]
            [Range(0.5f, 6f)] public float coreAmp = 3.05f;
            [Tooltip("Star body falloff exponent.")]
            [Range(0.5f, 4f)] public float coreP = 1.80f;
            [Tooltip("How much the granulation modulates the body (±30 %).")]
            [Range(0f, 1f)] public float coreGranulation = 0.30f;
            [Tooltip("Limb amplitude: a bright soft ring just inside the edge that makes the disc a sphere with an edge-on atmosphere, kept low so it does not whiten the whole disc.")]
            [Range(0f, 2f)] public float limbAmp = 0.62f;
            [Tooltip("Limb radius as a fraction of the core radius.")]
            [Range(0.3f, 1.2f)] public float limbR = 0.76f;
            [Tooltip("Limb half-width as a fraction of the core radius (wide = a brightening, not a line).")]
            [Range(0.05f, 1f)] public float limbW = 0.44f;
            [Tooltip("Limb falloff exponent.")]
            [Range(0.5f, 4f)] public float limbP = 1.60f;
        }

        [Serializable] public sealed class MembraneSettings : StyleSettings
        {
            public MembraneSettings() { ramp = PlusRampPresets.OrbToxin(); gain = 0.90f; a0 = 0.015f; a1 = 0.70f; acurve = 0.98f; amax = 0.892f; noseSquash = 0.92f; glowWide = 2.3f; glowTail = 0.45f; glowAmp = 0.10f; }
            [Tooltip("A shell is shed every this many frames (must divide the frame count for an exact loop).")]
            [Range(1, 6)] public int emitPeriod = 2;
            [Tooltip("Frames a shed shell lives while it thins and breaks.")]
            [Range(1, 30)] public int emitLife = 12;
            [Tooltip("Shed shell amplitude at birth (filled soft blobs the noise eats holes in — a skin coming apart leaves haze, not rings).")]
            [Range(0f, 4f)] public float shellAmp = 2.05f;
            [Tooltip("Shed shell falloff exponent.")]
            [Range(0.5f, 4f)] public float shellP = 1.50f;
            [Tooltip("Shell-break turbulence frequency (radians per source px).")]
            [Range(0.05f, 1f)] public float turbScaleShell = 0.22f;
            [Tooltip("Octaves of the shell-break turbulence.")]
            [Range(1, 8)] public int turbOctShell = 3;
            [Tooltip("Anisotropy of the shell-break turbulence.")]
            [Range(0.5f, 6f)] public float turbAnisoShell = 1.2f;
            [Tooltip("Seed offset of the shell-break turbulence.")]
            [Range(0, 99)] public int turbSeedOffShell = 11;
            [Tooltip("Speed smudge over the shed shells: taps in source px.")]
            [Range(0, 20)] public int smearTaps = 6;
            [Tooltip("Weight ratio per px of the shell smear.")]
            [Range(0.3f, 0.99f)] public float smearDecay = 0.74f;
            [Tooltip("Normalise the shell smear.")]
            public bool smearNorm = true;
            [Tooltip("A mote pair is shed every this many frames (2 — at 1 twenty 1 px motes were grain, not a spray).")]
            [Range(1, 6)] public int emitPeriodMotes = 2;
            [Tooltip("Frames a mote lives.")]
            [Range(1, 30)] public int emitLifeMotes = 10;
            [Tooltip("Motes per shed event.")]
            [Range(0, 8)] public int motesPerPiece = 2;
            [Tooltip("Mote amplitude at birth (1.6–2.8 px streaks — the only countable objects in the generation).")]
            [Range(0f, 3f)] public float moteAmp = 1.55f;
            [Tooltip("Mote streak elongation along its velocity.")]
            [Range(1f, 5f)] public float moteElong = 2.2f;
            [Tooltip("Interior fill amplitude — the WINDOW: chosen to land near 100/255, so the inside is see-through and not a dimmer shell.")]
            [Range(0f, 3f)] public float windowAmp = 1.30f;
            [Tooltip("Interior fill falloff exponent.")]
            [Range(0.5f, 4f)] public float windowP = 1.45f;
            [Tooltip("Skin amplitude — under a stop above the interior, so the bubble is a filled translucent sphere and not an eye.")]
            [Range(0f, 3f)] public float skinAmp = 1.05f;
            [Tooltip("Skin radius as a fraction of the core radius.")]
            [Range(0.3f, 1.2f)] public float skinR = 0.78f;
            [Tooltip("Skin half-width as a fraction of the core radius (0.55: a thickening toward the edge, not an outline).")]
            [Range(0.05f, 1f)] public float skinW = 0.55f;
            [Tooltip("Skin falloff exponent.")]
            [Range(0.5f, 4f)] public float skinP = 1.70f;
            [Tooltip("Amplitude of the surface-tension ripples running round the skin.")]
            [Range(0f, 1.5f)] public float ripAmp = 0.38f;
            [Tooltip("Ripple order: cycles round the skin (4-fold reads as a taut membrane).")]
            [Range(1, 12)] public int ripOrder = 4;
            [Tooltip("Ripple modulation depth (±16 %).")]
            [Range(0f, 1f)] public float ripDepth = 0.16f;
            [Tooltip("Ripple ring radius as a fraction of the core radius.")]
            [Range(0.3f, 1.3f)] public float ripR = 0.90f;
            [Tooltip("Ripple ring half-width as a fraction of the core radius.")]
            [Range(0.05f, 1f)] public float ripW = 0.30f;
            [Tooltip("Ripple ring falloff exponent.")]
            [Range(0.5f, 4f)] public float ripP = 1.50f;
            [Tooltip("Nucleus amplitude — the dense knot swimming a figure-eight inside the bubble, biased forward onto the leading wall.")]
            [Range(0f, 5f)] public float nucleusAmp = 2.40f;
            [Tooltip("Flat top of the nucleus as a fraction of its radius.")]
            [Range(0f, 0.5f)] public float nucleusFlat = 0.05f;
            [Tooltip("Nucleus falloff exponent.")]
            [Range(0.5f, 4f)] public float nucleusP = 1.55f;
        }

        [Serializable] public sealed class VoltcoreSettings : StyleSettings
        {
            public VoltcoreSettings() { ramp = PlusRampPresets.OrbVolt(); gain = 1.00f; a0 = 0.016f; a1 = 0.726f; acurve = 0.856f; amax = 1f; noseSquash = 0.90f; glowWide = 2.2f; glowTail = 0.55f; glowAmp = 0.11f; }
            [Tooltip("A filament pair is shed every this many frames (1: there are always four at different stages of coming apart).")]
            [Range(1, 6)] public int emitPeriod = 1;
            [Tooltip("Frames a filament lives: brightest and tightest at birth, a scatter of sparks by the end.")]
            [Range(1, 16)] public int emitLife = 4;
            [Tooltip("Branches per shed event.")]
            [Range(0, 6)] public int branchesPerPiece = 2;
            [Tooltip("Random-walk steps per filament (each step turns by up to ±0.85 rad and is dragged backward more the older the filament is).")]
            [Range(1, 20)] public int filamentSteps = 9;
            [Tooltip("Filament amplitude at birth.")]
            [Range(0f, 3f)] public float filamentAmp = 1.55f;
            [Tooltip("Filament streak elongation along its step.")]
            [Range(1f, 5f)] public float filamentElong = 1.9f;
            [Tooltip("Speed smudge over the filaments: taps in source px.")]
            [Range(0, 20)] public int smearTaps = 7;
            [Tooltip("Weight ratio per px of the filament smear.")]
            [Range(0.3f, 0.99f)] public float smearDecay = 0.78f;
            [Tooltip("Normalise the filament smear.")]
            public bool smearNorm = true;
            [Tooltip("Stamps down the plasma tail (60).")]
            [Range(10, 200)] public int tailStamps = 60;
            [Tooltip("Tail stamp falloff exponent.")]
            [Range(0.5f, 4f)] public float tailP = 1.70f;
            [Tooltip("Speed smudge of the tail: taps in source px, accumulated (a light streak brighter than its source — right for a plasma tail).")]
            [Range(0, 30)] public int tailSmearTaps = 9;
            [Tooltip("Weight ratio per px of the tail smear.")]
            [Range(0.3f, 0.99f)] public float tailSmearDecay = 0.80f;
            [Tooltip("Normalise the tail smear (off in the source).")]
            public bool tailSmearNorm = false;
            [Tooltip("Scale on the smeared tail before it joins the field (0.34: the construction stays, it just stops being the loudest thing).")]
            [Range(0f, 1.5f)] public float tailPostScale = 0.34f;
            [Tooltip("Envelope amplitude (1.50R × 1.26R, nose-squashed 0.94 and nudged 0.08R forward): a haze the ball wears, sized against the ball.")]
            [Range(0f, 3f)] public float envelopeAmp = 0.80f;
            [Tooltip("Envelope falloff exponent (2.3 — at 0.5 it drew a literal purple circle).")]
            [Range(0.5f, 4f)] public float envelopeP = 2.30f;
            [Tooltip("Charge turbulence frequency (radians per source px): low-frequency noise gated to the inside of the envelope.")]
            [Range(0.05f, 1f)] public float turbScaleCharge = 0.20f;
            [Tooltip("Octaves of the charge turbulence.")]
            [Range(1, 8)] public int turbOctCharge = 3;
            [Tooltip("Anisotropy of the charge turbulence.")]
            [Range(0.5f, 6f)] public float turbAnisoCharge = 1.6f;
            [Tooltip("Seed offset of the charge turbulence.")]
            [Range(0, 99)] public int turbSeedOffCharge = 2;
            [Tooltip("Charge amplitude.")]
            [Range(0f, 2f)] public float chargeAmp = 0.60f;
            [Tooltip("Bead amplitude — the one thing allowed to be near-opaque, about 6 px of it.")]
            [Range(0.5f, 6f)] public float beadAmp = 3.60f;
            [Tooltip("Flat top of the bead as a fraction of its radius.")]
            [Range(0f, 0.5f)] public float beadFlat = 0.12f;
            [Tooltip("Bead falloff exponent.")]
            [Range(0.5f, 4f)] public float beadP = 1.45f;
            [Tooltip("Amplitude of the bead's soft halo (0.70R × 0.64R).")]
            [Range(0f, 3f)] public float bead2Amp = 1.05f;
            [Tooltip("Ball amplitude — the round body the bead is a highlight ON (2.15 against an envelope of 0.80 wins the silhouette).")]
            [Range(0f, 5f)] public float ballAmp = 2.15f;
            [Tooltip("Ball falloff exponent (soft enough not to reintroduce an outline).")]
            [Range(0.5f, 4f)] public float ballP = 1.95f;
        }

        // ── runtime ──
        [NonSerialized] float[] _E, _A, _scratch, _tone, _alpha;
        [NonSerialized] byte[] _aBytes;
        [NonSerialized] float[] _dumpH, _dumpT, _dumpA;
        [NonSerialized] List<(int, int)> _emit;
        [NonSerialized] double[] _lut; [NonSerialized] int _lutHash;

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
            if (_lut == null || _lutHash != h) { _lut = PlusOrb.BakeLut(s.ramp); _lutHash = h; }
            return new OrbStyle { gain = s.gain, a0 = s.a0, a1 = s.a1, acurve = s.acurve, amax = s.amax, floor = floor, lut = _lut };
        }

        static int RampHash(PlusRamp r)
        {
            unchecked
            {
                int h = (int)2166136261u ^ (int)r.space;
                if (r.stops != null) foreach (var s in r.stops) { h = (h ^ s.pos.GetHashCode()) * 16777619; h = (h ^ s.color.GetHashCode()) * 16777619; }
                return h;
            }
        }

        public override void Render(in PlusFormCtx ctx, Color32[] target)
        {
            int W = ctx.W, H = ctx.H, n = W * H;
            if (_E == null || _E.Length != n) { _E = new float[n]; _A = new float[n]; _scratch = new float[n]; _aBytes = new byte[n]; _tone = _alpha = null; }
            bool dump = PlusFormDebug.FieldSink != null;
            if (dump && (_tone == null || _tone.Length != n)) { _tone = new float[n]; _alpha = new float[n]; }
            Array.Clear(_E, 0, n);
            _emit ??= new List<(int, int)>();

            // The spec seed IS the Kiln seed (layer 0 of seed 2101 draws emberdrift's own turbulence and embers); further
            // layers decorrelate by a large stride, swarm instances by their index.
            long seed = (long)ctx.seed + (long)ctx.layerSalt * 1000003L;
            var src = Source;
            var st = Active;
            int t = ctx.frameIndex, N = Math.Max(1, ctx.frameCount);
            double uSolo = Math.Max(radius * W, 0.5) / src.R;

            if (ctx.swarm == null)
                DrawOne(MakeFrame(W, H, noseX * W, axisY * H, uSolo, 1.0, src), t, N, seed, st);
            else
                for (int i = 0; i < ctx.swarm.Length; i++)
                {
                    var sp = ctx.swarm[i];
                    if (sp.own < 0f || sp.own > 1f) continue;
                    double u = uSolo * swarmSize * Math.Max(sp.sizeMul, 0.01f);
                    // swarm positions are y-up canvas px; the programs run y-down, flipped back at the write
                    DrawOne(MakeFrame(W, H, sp.x, H - sp.y, u, sp.brightMul, src), t, N, seed + sp.index * 104729L, st);
                }

            PlusFieldOps.BinomialBlur(_E, W, H, softenPasses, _scratch);
            var style = MakeStyle(st);
            PlusOrb.Rasterise(_E, W, H, style, ctx.alpha, despeckle, despeckleBelow, target, dump ? _tone : null, dump ? _alpha : null, _aBytes);
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
            fr.L = wake * fr.R;
            switch (variant)
            {
                case Variant.Wisp: PlusOrb.Wisp(wisp, fr, t, N, seed, _E, _A, _scratch); break;
                case Variant.Coronal: PlusOrb.Coronal(coronal, fr, t, N, seed, _E, _A, _scratch); break;
                case Variant.Membrane: PlusOrb.Membrane(membrane, fr, t, N, seed, _E, _A, _scratch, _emit); break;
                case Variant.Voltcore: PlusOrb.Voltcore(voltcore, fr, t, N, seed, _E, _A, _scratch, _emit); break;
                default: PlusOrb.Emberdrift(emberdrift, fr, t, N, seed, _E, _A, _scratch, _emit); break;
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
        void ApplyPixelModifiers(in PlusFormCtx ctx, Color32[] target)
        {
            if (ctx.pix == null || ctx.pix.Length == 0) return;
            int W = ctx.W, H = ctx.H;
            int pixHash = PyrePlusRenderer.Hash(ctx.seed, PyrePlusRenderer.ModParticleIndex, ctx.layerSalt, 0x1f);
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
        /// converted to the placement dials on PyrePlus's square max(w, h) canvas with the frame letterboxed at the
        /// centre (top row (S − h)/2, which is what `PlusParityDump.DumpOptions.cropH` cuts back out); `ramp` picks the
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
                case "ramp": { var r = PlusRampPresets.Orb(value?.ToString()); if (r == null) return false; Active.ramp = r; return true; }
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
            if (_cNx > 0) noseX = (float)(_cNx / S);
            if (_cCy > 0) axisY = (float)((top + _cCy) / S);
            if (_cR > 0) radius = (float)(_cR / S);
            if (_cR > 0 && _cL > 0) wake = (float)(_cL / _cR);
        }

        static bool SetField(object owner, string name, object value)
        {
            foreach (var fi in owner.GetType().GetFields())
            {
                if (!string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.FieldType == typeof(int)) fi.SetValue(owner, Convert.ToInt32(value));
                else if (fi.FieldType == typeof(float)) fi.SetValue(owner, Convert.ToSingle(value));
                else if (fi.FieldType == typeof(bool)) fi.SetValue(owner, value is bool b ? b : Convert.ToSingle(value) != 0f);
                else return false;
                return true;
            }
            return false;
        }
    }
}
