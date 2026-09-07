// JetForm — Kiln "Flame / agent3" THE FLAMETHROWER STREAM (the directional jet), generation 2, as a PyreForm — the
// first consumer of the shared jet engine (PyreJetEngine.cs, whose header carries the algorithm, the per-component
// ported / approximated / dropped list and the hand-off note for the radial and explosive forks).
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3 (gen.py + flame3/, MANIFEST "generation 2 — THE FLAMETHROWER
// STREAM, REGRADED"). The five published draws are ONE program (`flame3.jet.frame` + `grad.shade`) run with five
// parameter sets — needle / cone / drooping / swept / ringed — so the form has a `variant` and one settings box per
// variant, each a `JetSettings` built by `JetDraws.<Draw>()` from that draw's contract values (D:/Claude@GDrive/Flame/
// GEN2/contract/agent3/<draw>/params.json). Default variant = `gout` (#002): MANIFEST calls it "the classic … the draw
// that carries the territory … the flagship"; `lance` is #001 only by the listing order. The shared placement dials
// default to gout's own nozzle.
//
// Dial names in each box ARE the contract's parameter keys (round_at → roundAt, warp_cell → warpCell …) so
// `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words; the meaning is in
// every [Tooltip]. Placement, units, the swarm, the LUT cache and the contract loader are the family's shared
// `JetFormBase`; this file is only the five variants and the base program that draws them.
//
// The loop: phase = frame / frameCount, exactly periodic by construction (slot ages mod 1, noise scrolled by whole
// lattice periods per axis, integer pulse / sweep counts) — one period = the clip, whatever the frame count.
using System;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [PyreFormInfo("Jet", group: "Kiln/Flame", icon: "flame")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public sealed class JetForm : JetFormBase
    {
        public override string DisplayName => "Jet";
        public override string Description =>
            "A DIRECTIONAL jet of burning gas thrown from a nozzle — the flamethrower stream (Kiln Flame, agent3 gen 2): "
            + "puffs leave fast, small and stretched along their velocity, slow under drag, fatten by entrainment and only "
            + "lift under buoyancy once they are slow, so the stream is a needle at the root, a cone through the middle "
            + "and a rolling boil at the tip; a shared domain warp scrolling downstream turns the puffs into one sheet of "
            + "fire. Colour is a continuous linear-light ramp with per-stop opacity ceilings, crossfading into a second "
            + "ramp where the gas goes sooty. Five variants: Lance (a cutting torch with standing shock diamonds), Gout "
            + "(the classic weapon gout shedding fireballs), Sputter (fuel-rich, drooping, guttering twice a loop), Whip "
            + "(the weapon swung — an S-curve), Wyrm (a lance collared by vortex rings). Loops seamlessly over the clip. "
            + "Colour comes from the variant's Ramp, NOT the layer Fill. SWARM: off = one jet from Anchor X / Y aimed "
            + "by the variant's Aim; on = one jet per swarm particle rooted at its position, turned by its orientation, "
            + "sized by Swarm Size and its depth shading, heats summing where they overlap.";

        public enum Variant { Lance, Gout, Sputter, Whip, Wyrm }

        [Tooltip("Which of the five published jets this is. Each is its own settings box below; switching keeps the shared placement dials.")]
        [ZUILabel("Jet type")]
        public Variant variant = Variant.Gout;

        // ── the variants ──
        [ZUIShowIf("variant", "Lance")] [Tooltip("lance's contract values (seed 11, 168 × 52, 28 frames @ 16 fps) — THE NEEDLE: a cutting torch, indigo → cyan → white, standing shock diamonds the gas travels through.")] public JetSettings lance = JetDraws.Lance();
        [ZUIShowIf("variant", "Gout")] [Tooltip("gout's contract values (seed 23, 160 × 92, 30 frames @ 15 fps) — THE CLASSIC: a broad cone rolling over at the tip, brick → white with a greasy soot crossfade, fireballs tumbling off the end.")] public JetSettings gout = JetDraws.Gout();
        [ZUIShowIf("variant", "Sputter")] [Tooltip("sputter's contract values (seed 37, 152 × 116, 30 frames @ 13 fps) — THE DIRTY ONE: fuel-rich, drooping under its own weight, guttering twice a loop, an ember ramp that never reaches white, 16 shades.")] public JetSettings sputter = JetDraws.Sputter();
        [ZUIShowIf("variant", "Whip")] [Tooltip("whip's contract values (seed 53, 168 × 104, 32 frames @ 15 fps) — THE WEAPON SWUNG: the aim sweeps 20° either side once a loop and the stream is an S-curve, olive → gold → cream with a hot orange head, 24 shades.")] public JetSettings whip = JetDraws.Whip();
        [ZUIShowIf("variant", "Wyrm")] [Tooltip("wyrm's contract values (seed 71, 180 × 100, 30 frames @ 13 fps) — THE RINGED ONE: a coherent lance shedding three vortex rings a loop, violet → periwinkle → cyan-white, the most transparent, 32 shades.")] public JetSettings wyrm = JetDraws.Wyrm();

        /// The shared placement dials start on gout's own nozzle (6 % in, 68 % down).
        public JetForm() { anchorX = new ZUIValue(0.06f); anchorY = new ZUIValue(0.68f); }

        public override JetSettings Active => variant switch
        {
            Variant.Lance => lance, Variant.Sputter => sputter, Variant.Whip => whip, Variant.Wyrm => wyrm, _ => gout,
        };

        protected override JetProgram Program => JetProgram.Default;

        protected override bool TrySetVariant(string name)
        {
            if (!Enum.TryParse(name, true, out Variant v)) return false;
            variant = v; return true;
        }
    }
}
