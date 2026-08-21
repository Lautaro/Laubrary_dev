// RadialJetForm — Kiln "Flame / agent3_fork_radial" THE DIRECTION IS GONE (the radial / arc jet), generation 3, as a
// PlusForm on the shared jet engine: the same puff physics as JetForm, emitted into a 124°–360° arc through the stage
// overrides in RadialJetProgram.cs (whose header carries the algorithm, the per-component ported / approximated /
// dropped list and the hand-off note for the explosive fork).
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3_fork_radial (gen.py + flame3/, MANIFEST "generation 3 — THE
// DIRECTION IS GONE"). The eight published draws are ONE program run with eight parameter sets — corona / fan / crown /
// whirl / shockring / maw / starburst / halo — so the form has a `variant` and one settings box per variant, each a
// `RadialJetSettings` built by `RadialJetDraws.<Draw>()` from that draw's contract values (D:/Claude@GDrive/Flame/GEN3/
// contract/agent3_fork_radial/<draw>/params.json). Default variant = `corona` (#001): the MANIFEST's "plain answer" to
// the note, "the same gout, opened to 360 degrees" — the draw the generation is defined against. The shared placement
// dials default to corona's own centre (0.50, 0.54).
//
// Dial names in each box ARE the contract's parameter keys (src_r → srcR, lobe_depth → lobeDepth, ring_flat → ringFlat
// …) so `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning
// is in every [Tooltip]. Placement, units, the swarm (one radial jet per particle, centred on it), the LUT cache and
// the contract loader are the family's shared `JetFormBase`.
using System;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    [Serializable]
    [PlusFormInfo("Radial Jet", group: "Kiln/Flame", icon: "flame")]
    public sealed class RadialJetForm : JetFormBase
    {
        public override string DisplayName => "Radial Jet";
        public override string Description =>
            "Burning gas thrown OUTWARD from a centre into a wide arc or a full 360° disc (Kiln Flame, agent3_fork_radial "
            + "gen 3): the directional jet's puff physics — fast, small and stretched at birth, slowed by drag, fattened by "
            + "entrainment, lifting only once slow — emitted in every direction at once, with a polar domain warp that "
            + "scrolls outward so the turbulence travels with the gas everywhere. Eight variants: Corona (an even breathing "
            + "disc), Fan (a 124° sheet thrown sideways), Crown (a burner RING with a dark centre, blue ports → orange tips), "
            + "Whirl (three curved arms rotating once a loop), Shockring (concentric rings expanding face on), Maw (a 164° "
            + "wall belched up out of a vent, guttering), Starburst (seven tongues with real gaps), Halo (a thin cold haze "
            + "drifting through standing shells). Loops seamlessly over the clip. Colour comes from the variant's Ramp, NOT "
            + "the layer Fill. SWARM: off = one jet centred on Anchor X / Y; on = one jet per swarm particle centred on it, "
            + "turned by its orientation, sized by Swarm Size and its depth shading, heats summing where they overlap.";

        public enum Variant { Corona, Fan, Crown, Whirl, Shockring, Maw, Starburst, Halo }

        [Tooltip("Which of the eight published radial jets this is. Each is its own settings box below; switching keeps the shared placement dials.")]
        public Variant variant = Variant.Corona;

        // ── the variants ──
        [ZUIShowIf("variant", "Corona")] [Tooltip("corona's contract values (seed 101, 176 × 176, 30 frames @ 15 fps) — THE PLAIN ANSWER: an even 360° sheet, brick → white with a greasy soot edge, breathing and boiling; licks up, sits down.")] public RadialJetSettings corona = RadialJetDraws.Corona();
        [ZUIShowIf("variant", "Fan")] [Tooltip("fan's contract values (seed 113, 144 × 216, 30 frames @ 15 fps) — THE ARC: a 124° sheet thrown sideways, olive → cream with no white, a hot orange old edge, rolling over as it goes.")] public RadialJetSettings fan = RadialJetDraws.Fan();
        [ZUIShowIf("variant", "Crown")] [Tooltip("crown's contract values (seed 127, 184 × 180, 32 frames @ 14 fps) — THE BURNER: gas leaves a ring of 26 source lumps so the middle stays dark; blue at the ports crossfading to orange tips by AGE; rises hard.")] public RadialJetSettings crown = RadialJetDraws.Crown();
        [ZUIShowIf("variant", "Whirl")] [Tooltip("whirl's contract values (seed 139, 176 × 176, 32 frames @ 15 fps) — THE FIRE WHIRL: three lobes swirled 142° into curved arms, the pattern spinning exactly once a loop; magenta-black → white.")] public RadialJetSettings whirl = RadialJetDraws.Whirl();
        [ZUIShowIf("variant", "Shockring")] [Tooltip("shockring's contract values (seed 151, 176 × 176, 30 frames @ 13 fps) — THE RINGED ONE: three rings a loop expanding FACE ON over a low slow disc; violet → cyan-white, the transparent one.")] public RadialJetSettings shockring = RadialJetDraws.Shockring();
        [ZUIShowIf("variant", "Maw")] [Tooltip("maw's contract values (seed 163, 200 × 152, 30 frames @ 13 fps) — THE VENT: a 164° wall belched straight up, guttering twice a loop, the only draw with sag; ember only, no white.")] public RadialJetSettings maw = RadialJetDraws.Maw();
        [ZUIShowIf("variant", "Starburst")] [Tooltip("starburst's contract values (seed 173, 176 × 176, 28 frames @ 17 fps) — THE STAR: corona's disc gathered into seven tongues with real gaps; orange → white, high key, it flickers rather than boils.")] public RadialJetSettings starburst = RadialJetDraws.Starburst();
        [ZUIShowIf("variant", "Halo")] [Tooltip("halo's contract values (seed 181, 192 × 192, 32 frames @ 12 fps) — THE COLD ONE: a thin pale teal haze barely holding, drifting through standing concentric shells; the lowest ceilings in the set.")] public RadialJetSettings halo = RadialJetDraws.Halo();

        /// The shared placement dials start on corona's own centre (50 % across, 54 % down).
        public RadialJetForm() { anchorX = 0.50f; anchorY = 0.54f; }

        public override JetSettings Active => variant switch
        {
            Variant.Fan => fan, Variant.Crown => crown, Variant.Whirl => whirl, Variant.Shockring => shockring,
            Variant.Maw => maw, Variant.Starburst => starburst, Variant.Halo => halo, _ => corona,
        };

        protected override JetProgram Program => RadialJetProgram.Default;

        protected override bool TrySetVariant(string name)
        {
            if (!Enum.TryParse(name, true, out Variant v)) return false;
            variant = v; return true;
        }
    }
}
