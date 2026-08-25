// ExplosiveJetForm — Kiln "Flame / agent3_fork_explosive" IT BREAKS HARDER, AND THEN IT BREAKS AGAIN (the detonation),
// GENERATION 7, as a PyreForm on the shared jet engine: the jet's puff physics with the emission clock rewritten into an
// authored blast schedule, the gen-5 death (hold / shrink / lead_die / opaq), the gen-6/7 fracture and second crack, and
// the debris (flash, chunks, gobs, dust, blast-timed sparks and ring fronts) — all through the stage overrides in
// ExplosiveJetProgram.cs, whose header carries the algorithm, the per-component ported / approximated / dropped list
// (nothing approximated, nothing dropped) and the RNG streams.
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3_fork_explosive (gen.py + flame3/, MANIFEST generation 7). The ten
// published draws are ONE program run with ten parameter sets — detonate / backdraft / chain / frag / fuelair / lash /
// muzzle / shatter / shockfront / starshell — so the form has a `variant` and one settings box per variant, each an
// `ExplosiveJetSettings` built by `ExplosiveJetDraws.<Draw>()` from that draw's contract values (D:/Claude@GDrive/Flame/
// GEN7/contract/agent3_fork_explosive/<draw>/params.json). Default variant = `detonate` (#001): gen.py's "the plain
// answer to the note, and the draw the whole generation was tuned on" — the contract's canonical draw. The shared
// placement dials default to detonate's own seat (0.50, 0.52).
//
// This form REPLACES `ForkBlastForm` / `PyreForkBlast` (ported from generation 5, which had no fracture, dust or shed
// swell, re-fitted the exposure per frame, used a default gradient for the EMBER ramp and lerped to grey for soot —
// Appendix D's 47-row divergence table). Every row of that table is closed here: the gen-7 algorithm whole, the
// once-per-clip FITTED hi / curve frozen per draw, the 10-stop EMBER ramp with per-stop ceilings, the per-pixel polar
// domain warp, source-px / canvas-width / loop-fraction units through `JetFrame`, draw values (not class defaults) in
// every box, rank-based `lead`, the two-ramp soot crossfade, every debris component, and the authored schedule.
//
// Dial names in each box ARE the contract's parameter keys (blast_span → blastSpan, frac_open → fracture.open, dust_n
// → dust.count …) so `SetContractParam` loads any draw and a reader of MANIFEST.md / Appendix A finds the same words;
// the meaning is in every [Tooltip]. The 141 keys are grouped by CONCERN into boxes that draw as titled sub-cards: the
// flat dials (frame, arc, physics, emission, death, look) then Blasts (the schedule, one card per detonation), Fracture,
// Fracture 2, Flash, Chunks, Gobs, Dust. Placement, units, the LUT cache, the publisher and the contract loader are
// the family's shared `JetFormBase`.
//
// SWARM: off = the authored multi-blast schedule, seats offset from Anchor X / Y; on = ONE detonation per swarm particle
// (particle i fires blast i mod N of the schedule, at that blast's phase and violence, CENTRED on the particle — the
// seat offset is the schedule's layout across one frame and means nothing on a particle), so a swarm of a three-blast
// draw is a field of single bangs staggered the way the schedule is.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    [Serializable]
    [PyreFormInfo("Explosive Jet", group: "Kiln/Flame", icon: "flame")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "Laubrary.PyrePlus.Forms.Kiln", "com.Lautaro-Arino.Laubrary.PyrePlus.Forms.Kiln", null)]
    public sealed class ExplosiveJetForm : JetFormBase
    {
        public override string DisplayName => "Explosive Jet";
        public override string Description =>
            "A DETONATION (Kiln Flame, agent3_fork_explosive gen 7): the jet's burning-gas puffs born all at once into an "
            + "authored blast schedule — a hard front that runs away from the body, a velocity tail that fills the middle, "
            + "a flash at the seat — then a fireball that holds its heat, goes solid and dies from the OUTSIDE IN, cracks "
            + "into uneven pieces that slide apart and crack again, sheds dust off its own skin, throws burning chunks with "
            + "trails and gobs that balloon as they die. Ten variants: Detonate (the plain answer), Backdraft (thrown up), "
            + "Chain (three staggered seats), Frag (five bangs, the most shrapnel), Fuelair (slow, cold, GHOST), Lash (a "
            + "swirled vortex), Muzzle (a directional cordite flash), Shatter (the break), Shockfront (flat ring fronts that "
            + "break with the gas), Starshell (seven toxic-green lobes). Loops seamlessly over the clip. Colour comes from "
            + "the variant's Ramp, NOT the layer Fill. SWARM: off = the schedule at Anchor X / Y; on = one blast per swarm "
            + "particle (blast i mod N), centred on it, turned by its orientation, sized by Swarm Size and its depth shading.";

        public enum Variant { Detonate, Backdraft, Chain, Frag, Fuelair, Lash, Muzzle, Shatter, Shockfront, Starshell }

        [Tooltip("Which of the ten published detonations this is. Each is its own settings box below; switching keeps the shared placement dials.")]
        public Variant variant = Variant.Detonate;

        // ── the variants ──
        [ZUIShowIf("variant", "Detonate")] [Tooltip("detonate's contract values (seed 101, 184 × 184, 30 frames @ 15 fps) — THE PLAIN ANSWER, the draw the generation was tuned on: one bang, a 180° fireball that holds, goes solid, cracks in three and dies from the outside in; EMBER into soot.")] public ExplosiveJetSettings detonate = ExplosiveJetDraws.Detonate();
        [ZUIShowIf("variant", "Backdraft")] [Tooltip("backdraft's contract values (seed 277, 196 × 224, 32 frames @ 13 fps) — thrown UP out of a seat near the floor with the hardest buoyancy and a little sag; the tallest exposure in the set; four pieces; EMBER.")] public ExplosiveJetSettings backdraft = ExplosiveJetDraws.Backdraft();
        [ZUIShowIf("variant", "Chain")] [Tooltip("chain's contract values (seed 241, 296 × 168, 32 frames @ 15 fps) — THREE bangs staggered across a wide frame, each its own seat, violence and share; DIRTY, the low-contrast one.")] public ExplosiveJetSettings chain = ExplosiveJetDraws.Chain();
        [ZUIShowIf("variant", "Frag")] [Tooltip("frag's contract values (seed 269, 184 × 152, 30 frames @ 16 fps) — FIVE bangs at five seats, short-lived, the most chunks with the longest trails and the hardest kick; GOLD into a hot orange.")] public ExplosiveJetSettings frag = ExplosiveJetDraws.Frag();
        [ZUIShowIf("variant", "Fuelair")] [Tooltip("fuelair's contract values (seed 257, 176 × 200, 32 frames @ 12 fps) — the SLOW one: the longest span, the softest skew, no chunks, no gobs, no second crack; GHOST, barely a fire.")] public ExplosiveJetSettings fuelair = ExplosiveJetDraws.Fuelair();
        [ZUIShowIf("variant", "Lash")] [Tooltip("lash's contract values (seed 283, 184 × 184, 32 frames @ 15 fps) — the VORTEX: four lobes swirled 96° with the noise turning once a loop, two bangs; WHIRL, magenta-black to white.")] public ExplosiveJetSettings lash = ExplosiveJetDraws.Lash();
        [ZUIShowIf("variant", "Muzzle")] [Tooltip("muzzle's contract values (seed 223, 232 × 156, 28 frames @ 18 fps) — a DIRECTIONAL flash: a 26° cone from the left, four bangs in quick succession, the flash stretched along the aim; CORDITE into burnt orange.")] public ExplosiveJetSettings muzzle = ExplosiveJetDraws.Muzzle();
        [ZUIShowIf("variant", "Shatter")] [Tooltip("shatter's contract values (seed 211, 216 × 216, 30 frames @ 15 fps) — THE BREAK: the biggest frame, a certain crack into four with the widest cut and the likeliest second crack; SOLAR, the hottest ramp.")] public ExplosiveJetSettings shatter = ExplosiveJetDraws.Shatter();
        [ZUIShowIf("variant", "Shockfront")] [Tooltip("shockfront's contract values (seed 233, 232 × 208, 30 frames @ 14 fps) — two bangs from the left edge behind the only RING: flat 44° fronts that break along the same seams as the gas; VIOLET, the transparent one.")] public ExplosiveJetSettings shockfront = ExplosiveJetDraws.Shockfront();
        [ZUIShowIf("variant", "Starshell")] [Tooltip("starshell's contract values (seed 293, 208 × 208, 30 frames @ 17 fps) — seven LOBES in three bangs, the chemical one; TOXIC, black-green through lime to white.")] public ExplosiveJetSettings starshell = ExplosiveJetDraws.Starshell();

        /// The shared placement dials start on detonate's own seat (50 % across, 52 % down).
        public ExplosiveJetForm() { anchorX = new ZUIValue(0.50f); anchorY = new ZUIValue(0.52f); }

        public override JetSettings Active => variant switch
        {
            Variant.Backdraft => backdraft, Variant.Chain => chain, Variant.Frag => frag, Variant.Fuelair => fuelair,
            Variant.Lash => lash, Variant.Muzzle => muzzle, Variant.Shatter => shatter, Variant.Shockfront => shockfront,
            Variant.Starshell => starshell, _ => detonate,
        };

        protected override JetProgram Program => ExplosiveJetProgram.Default;
        protected override JetShade Shader => ExplosiveJetShade.Default;

        protected override bool TrySetVariant(string name)
        {
            if (!Enum.TryParse(name, true, out Variant v)) return false;
            variant = v; return true;
        }

        // ── swarm: one detonation per particle ──

        /// Particle i renders blast i mod N of the schedule on its own: a shallow clone of the box (a field copy, microseconds,
        /// made per instance per frame so every dial is the box's LIVE value) whose schedule is that ONE blast at its own
        /// phase and violence with the seat offset zeroed — the particle is the seat. No schedule = the steady stream, shared.
        protected override JetSettings InstanceSettings(JetSettings s, int instanceIndex)
        {
            if (s is not ExplosiveJetSettings e || !e.HasSchedule) return s;
            int n = e.blasts.Count;
            var b = e.blasts[((instanceIndex % n) + n) % n];
            var c = (ExplosiveJetSettings)e.ShallowClone();
            c.blasts = new List<ExplosiveBlast> { new ExplosiveBlast { at = b.at, pow = b.pow, share = 1f, offX = 0f, offY = 0f } };
            return c;
        }

        // ── contract loading ──

        /// The explosive keys on top of the family's: the blast schedule's four parallel arrays become the `blasts` cards
        /// (`blast_at` sizes the list; `blast_pow` / `blast_share` / `blast_off` fill their columns, an empty array = the
        /// source's "all 1 / all 0"); prefixed keys go to their concern's box (`frac_p` → Fracture › Chance, `frac_n` →
        /// Pieces, `dust_n` / `gob_n` / `chunk_n` → Count, `*_r` → Radius, the rest by name); everything else is the base's.
        public override bool SetContractParam(string key, object value)
        {
            var e = (ExplosiveJetSettings)Active;
            switch (key)
            {
                case "blast_at":
                {
                    if (value is not IList at) return false;
                    var list = new List<ExplosiveBlast>(at.Count);
                    for (int i = 0; i < at.Count; i++) list.Add(new ExplosiveBlast { at = Convert.ToSingle(at[i]) });
                    e.blasts = list;
                    return true;
                }
                case "blast_pow": return FillColumn(e, value, (b, v) => b.pow = Convert.ToSingle(v));
                case "blast_share": return FillColumn(e, value, (b, v) => b.share = Convert.ToSingle(v));
                case "blast_off":
                    return FillColumn(e, value, (b, v) =>
                    {
                        if (v is IList xy && xy.Count == 2) { b.offX = Convert.ToSingle(xy[0]); b.offY = Convert.ToSingle(xy[1]); }
                    });
            }
            if (key.StartsWith("frac2_")) return SetBoxed(e.fracture2, key.Substring(6), value, "chance", null);
            if (key.StartsWith("frac_")) return SetBoxed(e.fracture, key.Substring(5), value, "chance", "pieces");
            if (key.StartsWith("flash_")) return SetBoxed(e.flash, key.Substring(6), value, null, null);
            if (key.StartsWith("chunk_")) return SetBoxed(e.chunks, key.Substring(6), value, null, "count");
            if (key.StartsWith("gob_")) return SetBoxed(e.gobs, key.Substring(4), value, null, "count");
            if (key.StartsWith("dust_")) return SetBoxed(e.dust, key.Substring(5), value, null, "count");
            return base.SetContractParam(key, value);
        }

        /// Apply one parallel-array column of the schedule onto the cards; an empty array is the source's default column.
        static bool FillColumn(ExplosiveJetSettings e, object value, Action<ExplosiveBlast, object> set)
        {
            if (value is not IList col) return false;
            if (col.Count == 0) return true;
            while (e.blasts.Count < col.Count) e.blasts.Add(new ExplosiveBlast());
            for (int i = 0; i < col.Count; i++) set(e.blasts[i], col[i]);
            return true;
        }

        /// The contract's one-letter keys map to the box's named dial; the rest match by name with underscores dropped.
        static bool SetBoxed(object box, string rest, object value, string pKey, string nKey)
        {
            string name = rest switch
            {
                "p" when pKey != null => pKey,
                "n" when nKey != null => nKey,
                "r" => "radius",
                _ => rest.Replace("_", ""),
            };
            return SetField(box, name, value);
        }
    }
}
