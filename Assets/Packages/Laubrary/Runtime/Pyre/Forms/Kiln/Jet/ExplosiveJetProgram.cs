// ExplosiveJetProgram — Kiln "Flame / agent3_fork_explosive" GENERATION 7 ("IT BREAKS HARDER, AND THEN IT BREAKS
// AGAIN"): the jet engine's puff physics untouched, the EMISSION CLOCK rewritten — a blast is the same puffs born in an
// instant. Every slot belongs to one DETONATION of an authored schedule (`blast_at` / `blast_pow` / `blast_share` /
// `blast_off`), is born `at + span·u^skew` after it (hard attack, ragged tail), the first gas out runs away from the body
// (`blast_front`), a long velocity tail fills the middle (`vel_spread`), and every puff knows its RANK in its blast's
// speed order (`lead`). Then the gen-5 death — the amplitude HELD to the back of the life (`hold`), the body contracting
// from the OUTSIDE IN (`shrink` × lead^1.4), the fast front expiring first (`lead_die`), a solid body (`opaq`); the gen-6/7
// FRACTURE — past `frac_at` the mass is cut into `frac_n` UNEVEN wedges that close towards their own bisector, slide apart
// by a constant-width cut, kick, spin and drift, each piece cracking AGAIN at its own seam (`frac2_*`), the shed gas
// travelling with its piece (`frac_body`); and the debris — a FLASH at the seat, burning CHUNKS with trails that outrun
// the gas, GOBS of shed mass that balloon as they die, DUST shed off the body's own skin while it burns, sparks and the
// shock RING launched at the bang itself (`ring_arc` fronts that break along the same seams as the gas).
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3_fork_explosive/flame3/jet.py (gen 7; `diff -r` against agent3's
// flame3: jet.py, grad.py (`opaq` only — the `CeilingExponent` seam) and out.py (WebP durations, not rendering) differ;
// field / noise / lut are byte-identical, so `JetField`, `JetNoise` and `JetShade` are reused as they are). PyreForkBlast.cs
// (the port this one REPLACES) was ported from generation 5 and dropped most of this — see Appendix D.
//
// Component list (contract components.json), status in this port:
//   warp       PORTED   the polar `_warp_out` (the radial fork's, verbatim in the explosive jet.py: (24, 16, 4), outward
//                       scroll snapped to whole radial periods, `warp_spin`, radial / tangential basis, × clip(r/7)) — `Warp`.
//   root       PORTED   with a schedule: the seat lump under the blast ENVELOPE max over blasts of pw·fade(s, .04)·(1 − s)²
//                       at s = age/(0.85·life), round above spread 60; without one: the radial root (ring / lump) — `Root`.
//   flash      PORTED   per blast: r = flash_r·pw·(1 + grow·s), amp = flash_amp·pw·fade(s, .05)·(1 − s)³ over flash_life
//                       of the LOOP, elongated along the aim — `Flash` (in the field, before the gas).
//   emit       PORTED   blast groups as contiguous blocks (`_blast_groups`), angles stratified PER BLOCK, births, front,
//                       vel_spread, rank lead, plates + grip + bisector cut (`_plates` / `_rep` / `_cut` / `_plate_geom`),
//                       then `_emit` whole: lead_die, hold, shrink × lead^1.4, swell, the fracture displacement (angle,
//                       kick, drift, cut) and the second crack, shed_swell, the room-bounded shed kick — `BuildSlots` + `Emit`.
//   rings      PORTED   launched at blast m's phase / position / violence; `ring_flat` + `ring_arc` fronts tapered over
//                       the last 30 % of each end, breaking along the plate geometry; the edge-on O — `Rings` (delegates
//                       to the base only when there is no schedule and no flat ring — identical there).
//   chunks     PORTED   default_rng(seed·6367 + 29): lag u², own drag, sag, trail of 3 at earlier ages thinning 0.26/step,
//                       shrink × 0.80 and the hold decay ^1.5 — `Chunks`.
//   gobs       PORTED   default_rng(seed·3571 + 91): born u^1.7·gob_early, swell as they die, aspect 1 + 1.3·e^(−s/.34),
//                       tint soot·s·1.15, trail of 2 — `Gobs`.
//   dust       PORTED   default_rng(seed·2749 + 617): born on the body's skin at the front's own travel at that age × onr,
//                       own reach / drag, sag, shrinking 0.42 over its life, tint soot·(born + s/2) — `Dust`.
//   sparks     PORTED   blast-timed births at + ph²·span, rise × clip(buoy/0.18, .35, 2), vs 0.55..1.22 × pw — `Sparks`.
//   shade      PORTED   the shared `JetShade` with `opaq` as the exponent on the opacity ceiling — `ExplosiveJetShade`.
//   despeckle  PORTED   the shared `JetShade.Despeckle`.
//   Nothing approximated, nothing dropped. `lo` / `hi` / `curve` are the contract's FITTED values, frozen per draw.
//
// RNG: slot table seed·7919 + 13 (per-block uniforms / stratified + permutation, then vs / rs / as / ls / drift / shed,
// then birth u and the vel_spread u — `_plates` draws from its OWN stream seed·15013 + 401 in between, so the order here
// is exactly the source's), sparks seed·104729 + 77, chunks seed·6367 + 29, gobs seed·3571 + 91, dust seed·2749 + 617,
// lattices seed·1000 + k. `_plate_geom` is a pure function of the seed, so `Rings` recomputes it rather than caching.
//
// Engine seams used: the virtual stages `BuildSlots` / `Warp` / `Root` / `Emit` / `Rings` / `Sparks` / `Frame` and the
// shade's `CeilingExponent`; helpers `LeadRank` / `StratifiedPermuted` / `Fade` / `Mod1` / `RoundHalfEven`. Seams added:
// `JetSlots` unsealed (this table adds columns), `JetSettings.ShallowClone` (the swarm's per-particle blast), `hi` range
// to 16, `JetFormBase.Shader` / `InstanceSettings` virtuals — attributes and hooks, no arithmetic; the base jet and the
// radial jet render byte-identically (re-proven by their parity dumps). The polar warp is restated here rather than inherited because `RadialJetProgram` is sealed around its own
// settings type — it is the same thirty lines the source duplicates between the two forks.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre.Forms.Kiln
{
    /// One authored detonation of the schedule (the contract's parallel `blast_at` / `blast_pow` / `blast_share` /
    /// `blast_off` arrays, one entry each).
    [Serializable]
    public sealed class ExplosiveBlast
    {
        [Tooltip("Full name: \"Blast at\". Loop phase this detonation fires at (0..1). Uneven spacing between blasts is what makes a schedule STAGGERED.")]
        [ZUILabel("Blast at")] [ZUIGroup("Blasts", Tooltip = "The authored schedule of detonations: when each one fires, how violent it is, its share of the puffs, and where its seat sits.")]
        [Range(0f, 1f)] public float at = 0f;
        [Tooltip("Full name: \"Violence\". This blast's violence: scales its puffs' speed AND amplitude, its flash, ring, chunks, gobs, dust and sparks (contract `blast_pow`).")]
        [ZUILabel("Violence")] [ZUIGroup("Blasts")]
        [Range(0.1f, 2f)] public float pow = 1f;
        [Tooltip("Full name: \"Share\". This blast's share of the slots, relative to the other blasts' shares (normalised; contract `blast_share`).")]
        [ZUILabel("Share")] [ZUIGroup("Blasts")]
        [Range(0.05f, 2f)] public float share = 1f;
        [Tooltip("Full name: \"Seat X\". Seat offset across the source frame, as a fraction of its width (contract `blast_off` x).")]
        [ZUILabel("Seat X")] [ZUIGroup("Blasts")]
        [Range(-0.5f, 0.5f)] public float offX = 0f;
        [Tooltip("Full name: \"Seat Y\". Seat offset down the source frame, as a fraction of its height (contract `blast_off` y; + = down).")]
        [ZUILabel("Seat Y")] [ZUIGroup("Blasts")]
        [Range(-0.5f, 0.5f)] public float offY = 0f;
    }

    /// `frac_*`: the first crack. Field initialisers are the JetSpec class defaults.
    [Serializable]
    public sealed class ExplosiveFracture
    {
        [Tooltip("Full name: \"Crack chance\". PROBABILITY a detonation cracks (contract `frac_p`), rolled per blast off the draw's seed — so a three-blast draw can crack on its second bang and not its first, the same way every loop. 0 = never.")]
        [ZUILabel("Crack chance")] [ZUIGroup("Fracture", Tooltip = "THE CRACK: past Crack at the body is cut into uneven wedges that close towards their own bisectors, slide apart, kick, spin and drift.")]
        [Range(0f, 1f)] public float chance = 0f;
        [Tooltip("Full name: \"Pieces\". Pieces the body is cut into (`frac_n`). Their widths are UNEVEN (0.55..1.45 of the mean) — equal wedges read as a pinwheel, not a broken thing.")]
        [ZUILabel("Pieces")] [ZUIGroup("Fracture")]
        [Range(2, 12)] public int pieces = 6;
        [Tooltip("Full name: \"Crack at\". Age (fraction of a puff's life) the crack opens at (`frac_at`); each piece is delayed by up to Stagger.")]
        [ZUILabel("Crack at")] [ZUIGroup("Fracture")]
        [Range(0f, 1f)] public float at = 0.52f;
        [Tooltip("Full name: \"Crack open\". How far each piece closes up towards its own bisector, radians (`frac_open`) — the WEDGE-shaped half of the gap; × a per-piece 0.3..1.7 multiplier so no two seams let go by the same amount.")]
        [ZUILabel("Crack open")] [ZUIGroup("Fracture")]
        [Range(0f, 1.6f)] public float open = 0.30f;
        [Tooltip("Full name: \"Piece kick\". Per-piece extra travel, ± (`frac_kick`, × reach): zero-mean, so half the pieces are pulled in and half pushed out — they separate from EACH OTHER, not from the origin.")]
        [ZUILabel("Piece kick")] [ZUIGroup("Fracture")]
        [Range(0f, 1f)] public float kick = 0.22f;
        [Tooltip("Full name: \"Piece spin\". Degrees a piece turns as it goes (`frac_spin`, ± per piece).")]
        [ZUILabel("Piece spin")] [ZUIGroup("Fracture")]
        [Range(0f, 30f)] public float spin = 8f;
        [Tooltip("Full name: \"Gas grip\". Share of the gas that follows its piece (`frac_grip`); the rest stays and bridges the crack as wisps, so the tear is ragged rather than cut.")]
        [ZUILabel("Gas grip")] [ZUIGroup("Fracture")]
        [Range(0f, 1f)] public float grip = 0.82f;
        [Tooltip("Full name: \"Crack rotate\". Turns the crack pattern round the arc (`frac_rot`, fraction of the arc) so every radial draw does not split along the same axis.")]
        [ZUILabel("Crack rotate")] [ZUIGroup("Fracture", Advanced = true)]
        [Range(0f, 1f)] public float rot = 0f;
        [Tooltip("Full name: \"Piece drift\". Per-piece translation in its OWN random direction, canvas widths (`frac_drift`) — wedges that only slide radially stay a rosette; this shears them past each other.")]
        [ZUILabel("Piece drift")] [ZUIGroup("Fracture")]
        [Range(0f, 0.4f)] public float drift = 0.05f;
        [Tooltip("Full name: \"Stagger\". A piece's crack is delayed by up to this much life (`frac_stagger`), so the break RUNS through the mass instead of happening all at once.")]
        [ZUILabel("Stagger")] [ZUIGroup("Fracture")]
        [Range(0f, 0.5f)] public float stagger = 0.16f;
        [Tooltip("Full name: \"Crack cut\". The CONSTANT-WIDTH half of the gap, canvas widths (`frac_cut`): a displacement across each piece's own bisector, so the crack is as wide at the hub as at the shell and reaches the middle (an angle alone heals over at the root).")]
        [ZUILabel("Crack cut")] [ZUIGroup("Fracture")]
        [Range(0f, 0.3f)] public float cut = 0f;
        [Tooltip("Full name: \"Piece body\". 0 = only the SHELL separates (the slow core stays and holds the pieces together); 1 = the whole piece leaves, core and all, and what is left in the middle is a hole because the gas is visibly somewhere else (`frac_body`).")]
        [ZUILabel("Piece body")] [ZUIGroup("Fracture")]
        [Range(0f, 1f)] public float body = 0f;
    }

    /// `frac2_*`: the second crack — every piece is split at its own random seam and rolls whether that seam opens.
    [Serializable]
    public sealed class ExplosiveFracture2
    {
        [Tooltip("Full name: \"2nd crack chance\". PROBABILITY a piece breaks AGAIN (`frac2_p`), rolled PER PIECE — two of four pieces splitting is a thing breaking up; four of four is a symmetry operation. 0 = one crack only.")]
        [ZUILabel("Crack2 chance")] [ZUIGroup("Fracture 2", Tooltip = "THE SECOND CRACK: every piece from Fracture splits at its own random seam and rolls whether that seam opens later. Expert-only refinement — leave at 0 for a single, cleaner break.", Advanced = true)]
        [Range(0f, 1f)] public float chance = 0f;
        [Tooltip("Full name: \"2nd crack at\". Age the second crack opens at (`frac2_at`); never before the first one + 0.02.")]
        [ZUILabel("2nd crack at")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 1f)] public float at = 0.72f;
        [Tooltip("Full name: \"2nd crack open\". The second crack's angular closing, radians (`frac2_open`).")]
        [ZUILabel("2nd open")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 1.6f)] public float open = 0.45f;
        [Tooltip("Full name: \"2nd crack cut\". The second crack's constant-width cut, canvas widths (`frac2_cut`).")]
        [ZUILabel("2nd cut")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 0.3f)] public float cut = 0f;
        [Tooltip("Full name: \"2nd piece kick\". Per-sub-piece extra travel (`frac2_kick`, × reach, drawn −0.80..0.95).")]
        [ZUILabel("2nd kick")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 1f)] public float kick = 0.18f;
        [Tooltip("Full name: \"2nd piece drift\". Per-sub-piece translation in its own direction, canvas widths (`frac2_drift`).")]
        [ZUILabel("2nd drift")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 0.4f)] public float drift = 0.06f;
        [Tooltip("Full name: \"2nd piece spin\". Degrees a sub-piece turns (`frac2_spin`, ± per sub-piece).")]
        [ZUILabel("2nd spin")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 30f)] public float spin = 7f;
        [Tooltip("Full name: \"2nd stagger\". Per-sub-piece delay of the second crack, fraction of life (`frac2_stagger`).")]
        [ZUILabel("2nd stagger")] [ZUIGroup("Fracture 2", Advanced = true)]
        [Range(0f, 0.5f)] public float stagger = 0.14f;
    }

    /// `flash_*`: the instant itself — a hard bright core at the seat, gone in two or three frames.
    [Serializable]
    public sealed class ExplosiveFlash
    {
        [Tooltip("Full name: \"Flash radius\". Radius of the detonation core, source px (`flash_r`); 0 = none. It is the brightest thing in the file for a tenth of the loop and then not there at all.")]
        [ZUILabel("Flash radius")] [ZUIGroup("Flash", Tooltip = "THE INSTANT: a hard bright core at each detonation's seat, gone in a few frames.")]
        [Range(0f, 20f)] public float radius = 0f;
        [Tooltip("Full name: \"Flash heat\". Heat of the flash (`flash_amp`); × the blast's violence.")]
        [ZUILabel("Heat")] [ZUIGroup("Flash")]
        [Range(0f, 6f)] public float amp = 3f;
        [Tooltip("Full name: \"Flash duration\". Life of the flash as a fraction of the LOOP, not of a puff's life (`flash_life`) — it is brief; decays as (1 − s)³.")]
        [ZUILabel("Duration")] [ZUIGroup("Flash")]
        [Range(0.01f, 0.5f)] public float life = 0.10f;
        [Tooltip("Full name: \"Flash growth\". Growth of the core over its life (`flash_grow`): r × (1 + grow·s) — a fireball's core expands while it cools; shrinking it reads as a light switched off.")]
        [ZUILabel("Growth")] [ZUIGroup("Flash")]
        [Range(0f, 4f)] public float grow = 1.6f;
        [Tooltip("Full name: \"Flash elongate\". > 1 stretches the flash along the aim (`flash_elong`) — a muzzle flash.")]
        [ZUILabel("Elongate")] [ZUIGroup("Flash", Advanced = true)]
        [Range(1f, 4f)] public float elong = 1f;
    }

    /// `chunk_*`: burning fragments thrown clear of the fireball, each dragging a short trail.
    [Serializable]
    public sealed class ExplosiveChunks
    {
        [Tooltip("Full name: \"Chunk count\". Fragments per blast (`chunk_n`; own stream seed·6367 + 29). Not sparks: 2–4 px lumps with their own lower drag, sagging under gravity, outliving the fireball that threw them.")]
        [ZUILabel("Count")] [ZUIGroup("Chunks", Tooltip = "BURNING FRAGMENTS thrown clear of the fireball with trails: they outrun the gas, arc over under gravity and go out on their own against an empty frame.")]
        [Range(0, 60)] public int count = 0;
        [Tooltip("Full name: \"Chunk radius\". Fragment radius, source px (`chunk_r`; × 0.70..1.45 per chunk).")]
        [ZUILabel("Radius")] [ZUIGroup("Chunks")]
        [Range(0.5f, 6f)] public float radius = 2.4f;
        [Tooltip("Full name: \"Chunk reach\". Travel as a multiple of Reach (`chunk_reach`) — they OUTRUN the gas.")]
        [ZUILabel("Reach")] [ZUIGroup("Chunks")]
        [Range(0.2f, 3f)] public float reach = 1.55f;
        [Tooltip("Full name: \"Chunk drag\". Exponential drag of a fragment (`chunk_drag`); lower than the gas's, so it keeps going after the gas has stalled.")]
        [ZUILabel("Drag")] [ZUIGroup("Chunks")]
        [Range(0.05f, 4f)] public float drag = 0.75f;
        [Tooltip("Full name: \"Chunk life\". Life as a multiple of Life (`chunk_life`).")]
        [ZUILabel("Life")] [ZUIGroup("Chunks")]
        [Range(0.3f, 3f)] public float life = 1.4f;
        [Tooltip("Full name: \"Chunk sag\". Fall by the end of life, canvas widths (`chunk_sag`; × 0.45..1.35 per chunk, applied as s²).")]
        [ZUILabel("Sag")] [ZUIGroup("Chunks")]
        [Range(0f, 1f)] public float sag = 0.30f;
        [Tooltip("Full name: \"Chunk trail\". Blobs of trail drawn behind each fragment (`chunk_trail`): the same closed-form position at earlier ages, thinning 26 % a step.")]
        [ZUILabel("Trail")] [ZUIGroup("Chunks", Advanced = true)]
        [Range(1, 6)] public int trail = 3;
        [Tooltip("Full name: \"Chunk heat\". Heat of a fragment (`chunk_amp`); × the blast's violence.")]
        [ZUILabel("Heat")] [ZUIGroup("Chunks")]
        [Range(0f, 4f)] public float amp = 1.5f;
        [Tooltip("Full name: \"Chunk arc width\". Its arc as a multiple of the gas arc (`chunk_wide`), capped at a half turn.")]
        [ZUILabel("Arc width")] [ZUIGroup("Chunks")]
        [Range(0.2f, 2f)] public float wide = 1.25f;
    }

    /// `gob_*`: shedding burning MASS — lumps torn off the blast in its first phase that then dissipate.
    [Serializable]
    public sealed class ExplosiveGobs
    {
        [Tooltip("Full name: \"Gob count\". Gobs per loop (`gob_n`; own stream seed·3571 + 91). Gas that has come off the mass: several times a spark's size, leaving in the first half of the blast span, ballooning as it dies — the opposite of the body, which dies by shrinking.")]
        [ZUILabel("Count")] [ZUIGroup("Gobs", Tooltip = "SHED BURNING MASS: lumps torn off the opening front in the blast's first phase that balloon and thin as they die.")]
        [Range(0, 60)] public int count = 0;
        [Tooltip("Full name: \"Gob radius\". Gob radius at birth, source px (`gob_r`; × 0.62..1.55 per gob).")]
        [ZUILabel("Radius")] [ZUIGroup("Gobs")]
        [Range(1f, 10f)] public float radius = 4.5f;
        [Tooltip("Full name: \"Gob reach\". Travel as a multiple of Reach (`gob_reach`).")]
        [ZUILabel("Reach")] [ZUIGroup("Gobs")]
        [Range(0.2f, 2f)] public float reach = 0.85f;
        [Tooltip("Full name: \"Gob drag\". Exponential drag of a gob (`gob_drag`); it decelerates hard.")]
        [ZUILabel("Drag")] [ZUIGroup("Gobs")]
        [Range(0.05f, 4f)] public float drag = 1.30f;
        [Tooltip("Full name: \"Gob life\". Life as a multiple of Life (`gob_life`).")]
        [ZUILabel("Life")] [ZUIGroup("Gobs")]
        [Range(0.3f, 3f)] public float life = 1.35f;
        [Tooltip("Full name: \"Gob heat\". Heat of a gob (`gob_amp`); × the blast's violence; decays as (1 − s)².")]
        [ZUILabel("Heat")] [ZUIGroup("Gobs")]
        [Range(0f, 4f)] public float amp = 1.25f;
        [Tooltip("Full name: \"Gob swell\". It DISSIPATES: radius × (1 + swell·s) while its heat falls (`gob_swell`).")]
        [ZUILabel("Swell")] [ZUIGroup("Gobs")]
        [Range(0f, 4f)] public float swell = 1.60f;
        [Tooltip("Full name: \"Gob sag\". Fall by the end of life, canvas widths (`gob_sag`).")]
        [ZUILabel("Sag")] [ZUIGroup("Gobs")]
        [Range(0f, 1f)] public float sag = 0.10f;
        [Tooltip("Full name: \"Gob arc width\". Its arc as a multiple of the gas arc (`gob_wide`).")]
        [ZUILabel("Arc width")] [ZUIGroup("Gobs")]
        [Range(0.2f, 2f)] public float wide = 1.15f;
        [Tooltip("Full name: \"Gob early birth\". Gobs are born inside this fraction of the blast span, piled at the front by u^1.7 (`gob_early`) — a gob that leaves late is just a second, smaller explosion.")]
        [ZUILabel("Early birth")] [ZUIGroup("Gobs")]
        [Range(0f, 1f)] public float early = 0.45f;
        [Tooltip("Full name: \"Gob trail\". Blobs of trail behind each gob (`gob_trail`), thinning 30 % a step.")]
        [ZUILabel("Trail")] [ZUIGroup("Gobs", Advanced = true)]
        [Range(1, 6)] public int trail = 2;
    }

    /// `dust_*`: particles shed off the burning body's own SKIN while it burns (not sparks, not gobs).
    [Serializable]
    public sealed class ExplosiveDust
    {
        [Tooltip("Full name: \"Dust count\". Particles per loop (`dust_n`; own stream seed·2749 + 617). Born ON the body's surface, at the radius the fireball has actually reached at that moment, and drifting off it slowly — which is what reads as the blast SHEDDING rather than as more ejecta from the middle.")]
        [ZUILabel("Count")] [ZUIGroup("Dust", Tooltip = "PARTICLES shed off the burning body's own skin, continuously, at the radius it has actually reached.")]
        [Range(0, 200)] public int count = 0;
        [Tooltip("Full name: \"Dust radius\". Particle radius, source px (`dust_r`; × 0.60..1.55 per particle; contracts 42 % over its life).")]
        [ZUILabel("Radius")] [ZUIGroup("Dust")]
        [Range(0.5f, 5f)] public float radius = 1.9f;
        [Tooltip("Full name: \"Born from\". Earliest body age a particle comes off at, fraction of life (`dust_from`). On a fracturing draw it sits on the crack.")]
        [ZUILabel("Born from")] [ZUIGroup("Dust")]
        [Range(0f, 1f)] public float from = 0.30f;
        [Tooltip("Full name: \"Born to\". Latest body age a particle comes off at (`dust_to`).")]
        [ZUILabel("Born to")] [ZUIGroup("Dust")]
        [Range(0f, 1f)] public float to = 0.85f;
        [Tooltip("Full name: \"Birth bias\". > 1 piles the births at From (`dust_bias`: born = from + (to − from)·u^bias).")]
        [ZUILabel("Birth bias")] [ZUIGroup("Dust", Advanced = true)]
        [Range(0.2f, 4f)] public float bias = 1f;
        [Tooltip("Full name: \"Skin depth\". The body radius they come off, as a share of the front's travel at that age (`dust_where`; × 0.88..1.18 so they come off the outer skin, not one circle).")]
        [ZUILabel("Skin depth")] [ZUIGroup("Dust", Advanced = true)]
        [Range(0.2f, 1.5f)] public float where = 0.85f;
        [Tooltip("Full name: \"Dust reach\". A particle's own travel after leaving, multiple of Reach (`dust_reach`).")]
        [ZUILabel("Reach")] [ZUIGroup("Dust")]
        [Range(0f, 1.5f)] public float reach = 0.20f;
        [Tooltip("Full name: \"Dust drag\". Exponential drag of a particle (`dust_drag`).")]
        [ZUILabel("Drag")] [ZUIGroup("Dust")]
        [Range(0.05f, 5f)] public float drag = 2.0f;
        [Tooltip("Full name: \"Dust life\". Life as a multiple of Life (`dust_life`).")]
        [ZUILabel("Life")] [ZUIGroup("Dust")]
        [Range(0.1f, 3f)] public float life = 0.55f;
        [Tooltip("Full name: \"Dust heat\". Heat of a particle (`dust_amp`); decays as (1 − s)^1.7.")]
        [ZUILabel("Heat")] [ZUIGroup("Dust")]
        [Range(0f, 4f)] public float amp = 1.05f;
        [Tooltip("Full name: \"Dust sag\". Fall by the end of life, canvas widths (`dust_sag`).")]
        [ZUILabel("Sag")] [ZUIGroup("Dust")]
        [Range(0f, 1f)] public float sag = 0.10f;
        [Tooltip("Full name: \"Dust scatter\". Radians of spray either side of straight out (`dust_scatter`).")]
        [ZUILabel("Scatter")] [ZUIGroup("Dust")]
        [Range(0f, 1.6f)] public float scatter = 0.45f;
        [Tooltip("Full name: \"Dust arc width\". Its arc as a multiple of the gas arc (`dust_wide`).")]
        [ZUILabel("Arc width")] [ZUIGroup("Dust")]
        [Range(0.2f, 2f)] public float wide = 1f;
        [Tooltip("Full name: \"Dust trail\". Blobs of trail behind each particle (`dust_trail`), thinning 34 % a step.")]
        [ZUILabel("Trail")] [ZUIGroup("Dust", Advanced = true)]
        [Range(1, 6)] public int trail = 1;
    }

    /// The gen-7 `JetSpec`: the base's dials, the radial keys (the explosive fork carries them too), and the detonation.
    /// Field initialisers are the JetSpec CLASS defaults; a form always holds one of `ExplosiveJetDraws`' ten factories.
    [Serializable]
    public sealed class ExplosiveJetSettings : JetSettings
    {
        // ── the arc (gen 3) ──
        [Tooltip("Full name: \"Angle bias\". Angle-distribution power across the arc: 1 = uniform (a disc is actually filled); > 1 biases towards the aim. Wide arcs (Spread ≥ 60°) draw their base angles STRATIFIED and permuted PER BLAST, so each detonation covers the arc evenly.")]
        [ZUILabel("Angle bias")] [ZUIGroup("Arc shape", Tooltip = "How the disc is filled and torn into tongues.")]
        [Range(0.5f, 3f)] public float bias = 1.7f;
        [Tooltip("Full name: \"Burn radius\". Birth radius, canvas WIDTHS of the source frame: > 0 = the gas leaves a ring rather than a point.")]
        [ZUILabel("Burn radius")] [ZUIGroup("Arc shape")]
        [Range(0f, 0.3f)] public float srcR = 0f;
        [Tooltip("Full name: \"Swirl\". Degrees a puff is carried AROUND the seat over its life — a blast curled by a vortex (lash's hooked arms); gobs take 60 % of it, sparks all of it.")]
        [ZUILabel("Swirl")] [ZUIGroup("Arc shape")]
        [Range(-360f, 360f)] public float swirl = 0f;
        [Tooltip("Full name: \"Pattern spin\". Whole turns per loop the emission pattern rotates (integer, so the loop stays exact).")]
        [ZUILabel("Pattern spin")] [ZUIGroup("Arc shape")]
        [Range(-3, 3)] public int spin = 0;
        [Tooltip("Full name: \"Tongue count\". Gather the arc into N tongues with real gaps (the birth angles REDISTRIBUTED by φ − depth·sin φ). 0 = an even sheet. A fracture is NOT a lobe: lobes shape the blast at birth, a fracture cuts a body that was whole.")]
        [ZUILabel("Tongue count")] [ZUIGroup("Arc shape")]
        [Range(0, 12)] public int lobes = 0;
        [Tooltip("Full name: \"Tongue clump\". How hard the tongues clump, 0..0.95.")]
        [ZUILabel("Tongue clump")] [ZUIGroup("Arc shape")]
        [Range(0f, 0.95f)] public float lobeDepth = 0f;
        [Tooltip("Full name: \"Tongue kick\". Extra travel on a lobe axis against between them.")]
        [ZUILabel("Tongue kick")] [ZUIGroup("Arc shape")]
        [Range(0f, 1f)] public float lobeKick = 0f;
        [Tooltip("Full name: \"Lump count\". With Src R > 0 and NO schedule: the root is this many lumps round the birth circle. A detonation has no standing source — with a schedule the seat lump burns out with each blast instead.")]
        [ZUILabel("Lump count")] [ZUIGroup("Arc shape", Advanced = true)]
        [Range(0, 48)] public int rootK = 0;
        [Tooltip("Full name: \"Rings face-on\". Rings seen FACE ON (an expanding circle in the picture plane — a shockwave) instead of the edge-on O. With a schedule, ring m launches at blast m's phase, from its seat, at its violence.")]
        [ZUILabel("Rings face-on")] [ZUIGroup("Arc shape")]
        public bool ringFlat = false;
        [Tooltip("Full name: \"Ring arc angle\". HALF-angle of a flat ring, degrees: < 180 draws only the part of the circle within that angle of the aim — a directional blast's leading FRONT, tapered to nothing over the last 30 % of each end. A fracturing draw breaks the front along the same seams as the gas.")]
        [ZUILabel("Ring arc")] [ZUIGroup("Arc shape")]
        [Range(5f, 180f)] public float ringArc = 180f;
        [Tooltip("Full name: \"Turbulence spin\". Whole turns per loop the polar noise texture rotates around the seat (integer, so the loop stays exact).")]
        [ZUILabel("Turb. spin")] [ZUIGroup("Arc shape", Advanced = true)]
        [Range(-3, 3)] public int warpSpin = 0;

        // ── the detonation (gen 4) ──
        [Tooltip("Full name: \"Blast schedule\". The authored schedule: every slot belongs to one of these detonations, as contiguous blocks sized by their shares, and the flash / ring / chunks / gobs / dust / sparks fire at the same instants. Empty = a steady stream (generation 3's jet). SWARM ON: each particle fires ONE blast of this list (particle i → blast i mod N) at its own position.")]
        [ZUILabel("Schedule")] [ZUIGroup("Blasts", Tooltip = "The authored schedule: when each detonation fires and how the puffs are timed onto it.")]
        public List<ExplosiveBlast> blasts = new List<ExplosiveBlast>();
        [Tooltip("Full name: \"Blast span\". Birth times of a blast's slots are spread over this much of the loop (`blast_span`).")]
        [ZUILabel("Blast span")] [ZUIGroup("Blasts")]
        [Range(0.01f, 0.5f)] public float blastSpan = 0.10f;
        [Tooltip("Full name: \"Blast skew\". Birth offset = span·u^skew: > 1 piles the births at the front (`blast_skew`) — hard attack, ragged tail, which a symmetric pulse cannot make.")]
        [ZUILabel("Blast skew")] [ZUIGroup("Blasts")]
        [Range(0.5f, 4f)] public float blastSkew = 2.4f;
        [Tooltip("Full name: \"Blast front\". Extra speed for the first gas out (`blast_front`: speed × (1 + front·(1 − u^skew))), so the blast has a FRONT that runs away from the body instead of expanding as one shell.")]
        [ZUILabel("Blast front")] [ZUIGroup("Blasts")]
        [Range(0f, 1.5f)] public float blastFront = 0f;
        [Tooltip("Full name: \"Velocity spread\". 0 = one speed ± jitter = a SHELL (a smoke ring with a hole). > 0 scales each puff's speed by 1 − k·u^1.4, a long tail towards zero, so the slow gas never leaves the middle and the fireball FILLS (`vel_spread`).")]
        [ZUILabel("Vel spread")] [ZUIGroup("Blasts")]
        [Range(0f, 0.95f)] public float velSpread = 0f;
        [Tooltip("Full name: \"Swell\". Radius gained per unit AGE, canvas widths (`swell`): stalled gas keeps entraining air — it is what closes the centre of a fireball filled by Vel Spread.")]
        [ZUILabel("Swell")] [ZUIGroup("Blasts")]
        [Range(0f, 0.3f)] public float swell = 0f;

        // ── how it dies (gen 5) ──
        [Tooltip("Full name: \"Hold\". > 0 holds the amplitude up and drops it late (`hold`: decay = 1 − s^(1 + hold) instead of 1 − s) — at 1.6 a puff still holds 80 % at 60 % of its life. The delay that leaves the body solid long enough for the contraction to be what you see.")]
        [ZUILabel("Hold")] [ZUIGroup("Death", Tooltip = "How the body dies: contracting from the outside in, going solid, then holding until it fades.")]
        [Range(0f, 3f)] public float hold = 0f;
        [Tooltip("Full name: \"Shrink\". Fraction of its radius a puff loses by the end of its life (`shrink`), weighted by lead^1.4 so the front loses all of it and the slowest third does not contract at all: the blast collapses from the OUTSIDE IN and never hollows. Shed gas is exempt (it swells).")]
        [ZUILabel("Shrink")] [ZUIGroup("Death")]
        [Range(0f, 1f)] public float shrink = 0f;
        [Tooltip("Full name: \"Shrink at\". The age the contraction starts at (`shrink_at`).")]
        [ZUILabel("Shrink at")] [ZUIGroup("Death")]
        [Range(0f, 1f)] public float shrinkAt = 0.50f;
        [Tooltip("Full name: \"Lead dies first\". The FAST gas dies first (`lead_die`: life × (1 − lead_die·lead), shed gas takes half of it) — the outer shell expires while the slow middle still burns, so the lit silhouette contracts.")]
        [ZUILabel("Lead die")] [ZUIGroup("Death")]
        [Range(0f, 1f)] public float leadDie = 0f;
        [Tooltip("Full name: \"Solidity\". Exponent on the ramp's opacity ceiling (`opaq`): < 1 pushes the mid-ramp ceilings towards solid (0.62^0.55 = 0.77) while the coldest skin stays see-through — a solid body WITHOUT losing the soft edge. 1 = the ramp as authored.")]
        [ZUILabel("Solidity")] [ZUIGroup("Death")]
        [Range(0.2f, 1.5f)] public float opaq = 1f;
        [Tooltip("Full name: \"Shed swell\". Shed gas dies the OTHER way: radius × (1 + shed_swell·s) — it thins and spreads while the body contracts, which is what separates the lumps from the body they came off.")]
        [ZUILabel("Shed swell")] [ZUIGroup("Death")]
        [Range(0f, 2f)] public float shedSwell = 0f;

        // ── fracture (gen 6 / 7) and debris (gen 4 / 5 / 6) ──
        [Tooltip("THE CRACK: past Fracture › At the body is cut into uneven wedges that close towards their own bisectors, slide apart along a constant-width cut, kick, spin and drift — a displacement of the gas already there, never a second emitter, which is what makes it read as one object breaking.")]
        [ZUILabel("Fracture")] public ExplosiveFracture fracture = new ExplosiveFracture();
        [Tooltip("THE SECOND CRACK: every piece is split at its own random seam (0.30..0.70 of its width) and rolls whether that seam opens later, so four pieces become up to eight, staggered.")]
        [ZUILabel("Fracture 2")] public ExplosiveFracture2 fracture2 = new ExplosiveFracture2();
        [Tooltip("THE INSTANT: a hard bright core at the seat of each detonation, gone in a few frames — in the field, under the gas, not an overlay.")]
        [ZUILabel("Flash")] public ExplosiveFlash flash = new ExplosiveFlash();
        [Tooltip("BURNING FRAGMENTS thrown clear of the fireball with trails: they outrun the gas, arc over under gravity and go out on their own against an empty frame.")]
        [ZUILabel("Chunks")] public ExplosiveChunks chunks = new ExplosiveChunks();
        [Tooltip("SHED BURNING MASS: lumps torn off the opening front in the blast's first phase that balloon and thin as they die.")]
        [ZUILabel("Gobs")] public ExplosiveGobs gobs = new ExplosiveGobs();
        [Tooltip("PARTICLES shed off the burning body's own skin, continuously, at the radius it has actually reached.")]
        [ZUILabel("Dust")] public ExplosiveDust dust = new ExplosiveDust();

        public bool HasSchedule => blasts != null && blasts.Count > 0;
    }

    /// The gen-7 stage overrides on the shared engine (see the file header). Stateless, like the base.
    public sealed class ExplosiveJetProgram : JetProgram
    {
        public new static readonly ExplosiveJetProgram Default = new ExplosiveJetProgram();

        static ExplosiveJetSettings E(JetSettings s) => s as ExplosiveJetSettings ?? throw new ArgumentException("ExplosiveJetProgram needs ExplosiveJetSettings");

        /// The slot table with the detonation's extra per-slot constants (the `tab` dict of `_slot_table`).
        public sealed class Slots : JetSlots
        {
            public double[] birth, ampk, lead, ox, oy;
            // the first crack: membership weight, angular pull, kick, drift, cut, crack age
            public double[] fx, fdt, fkick, fdx, fdy, fnx, fny, fat;
            // the second crack
            public double[] f2x, f2dt, f2kick, f2dx, f2dy, f2nx, f2ny, f2at;
        }

        /// `_plate_geom`: the pieces themselves, a pure function of the seed (the gas and the ring both break on them).
        sealed class Plates
        {
            public int k; public double half, span;
            public double[] edges, cen, openk, broke, grip, spin, kick, dx, dy, at;
            public bool second;
            public double[] seam, cen2, broke2, openk2, spin2, kick2, dx2, dy2, at2;
        }

        struct Blast { public double at, pw, ox, oy; }

        /// `_blast_list`: (phase, violence, dx_px, dy_px) per detonation — one place so nothing drifts out of step.
        static Blast[] BlastList(ExplosiveJetSettings s)
        {
            var b = new Blast[s.blasts.Count];
            for (int m = 0; m < b.Length; m++) { var e = s.blasts[m]; b[m] = new Blast { at = e.at, pw = e.pow, ox = e.offX * s.w, oy = e.offY * s.h }; }
            return b;
        }

        // ── stage: the slot table — WHEN each puff is born is the whole of generation 4 ──
        public override JetSlots BuildSlots(JetSettings bs, int seed)
        {
            bs.EnsureLive();
            var s = E(bs);
            var rng = new PyreNumpyRng(unchecked((uint)(seed * 7919 + 13)));
            int n = s.slots;
            var t = new Slots
            {
                phase = new double[n], da = new double[n], vs = new double[n], rs = new double[n], amp = new double[n], ls = new double[n], drift = new double[n], shed = new bool[n],
                birth = new double[n], ampk = new double[n], lead = null, ox = new double[n], oy = new double[n],
            };
            double j = s.live.jitter, spread = s.live.spread * Math.PI / 180.0, bias = s.bias;
            for (int i = 0; i < n; i++) t.phase[i] = i / (double)n;

            // `_blast_groups`: slot → detonation as contiguous blocks sized by share (np.round half-even of the cumsum)
            int k = s.HasSchedule ? s.blasts.Count : 0;
            int[] grp = null; var edges = new int[k + 1];
            if (k > 0)
            {
                double sum = 0; for (int g = 0; g < k; g++) sum += s.blasts[g].share;
                double cum = 0;
                for (int g = 0; g < k; g++) { cum += s.blasts[g].share / sum; edges[g + 1] = RoundHalfEven(cum * n); }
                edges[k] = n;
                grp = new int[n];
                for (int g = 0; g < k; g++) for (int i = edges[g]; i < edges[g + 1]; i++) grp[i] = g;
            }
            // the angles, stratified PER BLOCK for wide arcs (a blast drawing a random third of a globally stratified set
            // gets the bald patches back)
            var baseA = new double[n];
            int blocks = k > 0 ? k : 1;
            for (int g = 0; g < blocks; g++)
            {
                int a = k > 0 ? edges[g] : 0, b = k > 0 ? edges[g + 1] : n, m = b - a;
                if (m <= 0) continue;
                if (s.live.spread >= 60f) { var v = StratifiedPermuted(rng, m); Array.Copy(v, 0, baseA, a, m); }
                else for (int i = a; i < b; i++) baseA[i] = rng.Uniform(-1.0, 1.0);
            }
            for (int i = 0; i < n; i++) t.da[i] = Math.Sign(baseA[i]) * Math.Pow(Math.Abs(baseA[i]), bias) * spread;
            if (s.lobes != 0 && s.lobeDepth != 0f)
                for (int i = 0; i < n; i++) { double phi = t.da[i] * s.lobes; t.da[i] = (phi - s.lobeDepth * Math.Sin(phi)) / s.lobes; }
            for (int i = 0; i < n; i++)
            {
                double lf = s.lobes != 0 ? 0.5 + 0.5 * Math.Cos(s.lobes * t.da[i]) : 1.0;
                t.vs[i] = (1.0 + j * rng.Uniform(-0.42, 0.42)) * (1.0 - s.lobeKick * (1.0 - lf));
            }
            for (int i = 0; i < n; i++) t.rs[i] = 1.0 + j * rng.Uniform(-0.34, 0.50);
            for (int i = 0; i < n; i++) t.amp[i] = 1.0 + j * rng.Uniform(-0.30, 0.30);
            for (int i = 0; i < n; i++) t.ls[i] = 1.0 + j * rng.Uniform(-0.25, 0.25);
            for (int i = 0; i < n; i++) t.drift[i] = rng.Uniform(-1.0, 1.0);
            for (int i = 0; i < n; i++) t.shed[i] = rng.NextDouble() < s.live.shed;
            BuildPlates(s, seed, t, grp, n);

            if (k == 0)
            {
                // generation 3's steady stream: slot i born at i/N
                for (int i = 0; i < n; i++) { t.birth[i] = t.phase[i]; t.ampk[i] = 1.0; }
                t.lead = LeadRank(t.vs);
                return t;
            }
            // births piled into the instant: at + span·u^skew; the front runs away; a velocity tail fills the middle
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = Math.Pow(rng.NextDouble(), s.blastSkew);
            for (int i = 0; i < n; i++)
            {
                var bl = s.blasts[grp[i]];
                t.birth[i] = Mod1(bl.at + s.blastSpan * frac[i]);
                t.vs[i] = t.vs[i] * bl.pow * (1.0 + s.blastFront * (1.0 - frac[i]));
            }
            if (s.velSpread != 0f)
                for (int i = 0; i < n; i++) t.vs[i] *= 1.0 - s.velSpread * Math.Pow(rng.NextDouble(), 1.4);
            for (int i = 0; i < n; i++) t.ampk[i] = s.blasts[grp[i]].pow;
            t.lead = LeadRank(t.vs);
            for (int i = 0; i < n; i++) { var bl = s.blasts[grp[i]]; t.ox[i] = bl.offX * s.w; t.oy[i] = bl.offY * s.h; }
            return t;
        }

        /// `_plate_geom`: the uneven wedges, which blasts break, every puff's grip, each piece's opening / kick / spin /
        /// drift / delay, and the second seam inside each piece — all from default_rng(seed·15013 + 401).
        static Plates PlateGeom(ExplosiveJetSettings s, int seed)
        {
            var f = s.fracture; var f2 = s.fracture2;
            var rng = new PyreNumpyRng(unchecked((uint)(seed * 15013 + 401)));
            int k = f.pieces, n = s.slots;
            var p = new Plates { k = k };
            var wid = new double[k]; double wsum = 0;
            for (int i = 0; i < k; i++) { wid[i] = rng.Uniform(0.55, 1.45); wsum += wid[i]; }
            p.half = Math.Min(s.live.spread, 180f) * Math.PI / 180.0; p.span = 2.0 * p.half;
            p.edges = new double[k + 1]; double cum = 0;
            for (int i = 0; i < k; i++) { cum += wid[i] / wsum; p.edges[i + 1] = cum * p.span; }
            p.edges[k] = p.span;
            int nb = Math.Max(s.HasSchedule ? s.blasts.Count : 0, 1);
            p.broke = new double[nb]; double bsum = 0;
            for (int i = 0; i < nb; i++) { p.broke[i] = rng.NextDouble() < f.chance ? 1.0 : 0.0; bsum += p.broke[i]; }
            if (bsum == 0 && f.chance >= 0.999) for (int i = 0; i < nb; i++) p.broke[i] = 1.0;
            // grip: a Bernoulli per puff, THEN a uniform per puff (two passes on the stream, as numpy evaluates them)
            var hold = new bool[n]; for (int i = 0; i < n; i++) hold[i] = rng.NextDouble() < f.grip;
            p.grip = new double[n]; for (int i = 0; i < n; i++) { double u = rng.Uniform(0.45, 1.25); p.grip[i] = hold[i] ? u : 0.0; }
            p.spin = new double[k]; double fs = f.spin * Math.PI / 180.0; for (int i = 0; i < k; i++) p.spin[i] = fs * rng.Uniform(-1.0, 1.0);
            p.kick = new double[k]; for (int i = 0; i < k; i++) p.kick[i] = f.kick * rng.Uniform(-0.85, 0.85);
            p.openk = new double[k]; for (int i = 0; i < k; i++) p.openk[i] = rng.Uniform(0.30, 1.70);
            var ang = new double[k]; for (int i = 0; i < k; i++) ang[i] = rng.Uniform(0.0, TAU);
            var mag = new double[k]; for (int i = 0; i < k; i++) mag[i] = f.drift * rng.Uniform(0.35, 1.35) * s.w;
            p.cen = new double[k]; for (int i = 0; i < k; i++) p.cen[i] = 0.5 * (p.edges[i] + p.edges[i + 1]);
            p.dx = new double[k]; p.dy = new double[k]; for (int i = 0; i < k; i++) { p.dx[i] = mag[i] * Math.Cos(ang[i]); p.dy[i] = mag[i] * Math.Sin(ang[i]); }
            p.at = new double[k]; for (int i = 0; i < k; i++) p.at[i] = f.at + f.stagger * rng.Uniform(0.0, 1.0);
            if (f2.chance != 0f && f2.open + f2.cut + f2.kick + f2.drift > 0f)
            {
                p.second = true;
                p.seam = new double[k]; for (int i = 0; i < k; i++) p.seam[i] = p.edges[i] + (p.edges[i + 1] - p.edges[i]) * rng.Uniform(0.30, 0.70);
                var e2 = new double[2 * k + 1];
                for (int i = 0; i < k; i++) { e2[2 * i] = p.edges[i]; e2[2 * i + 1] = p.seam[i]; }
                e2[2 * k] = p.span;
                p.broke2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) p.broke2[i] = rng.NextDouble() < f2.chance ? 1.0 : 0.0;
                p.openk2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) p.openk2[i] = rng.Uniform(0.30, 1.70);
                var ang2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) ang2[i] = rng.Uniform(0.0, TAU);
                var mag2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) mag2[i] = f2.drift * rng.Uniform(0.40, 1.40) * s.w;
                p.cen2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) p.cen2[i] = 0.5 * (e2[i] + e2[i + 1]);
                p.spin2 = new double[2 * k]; double fs2 = f2.spin * Math.PI / 180.0; for (int i = 0; i < 2 * k; i++) p.spin2[i] = fs2 * rng.Uniform(-1.0, 1.0);
                p.kick2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) p.kick2[i] = f2.kick * rng.Uniform(-0.80, 0.95);
                p.dx2 = new double[2 * k]; p.dy2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) { p.dx2[i] = mag2[i] * Math.Cos(ang2[i]); p.dy2[i] = mag2[i] * Math.Sin(ang2[i]); }
                p.at2 = new double[2 * k]; for (int i = 0; i < 2 * k; i++) p.at2[i] = f2.at + f2.stagger * rng.Uniform(0.0, 1.0);
            }
            return p;
        }

        /// Python's `x % m` for doubles (never negative).
        static double PyMod(double x, double m) { double r = x - m * Math.Floor(x / m); return r >= m ? r - m : r; }

        /// np.searchsorted(edges, phi, side="right") − 1, clipped to a piece index.
        static int PieceOf(double[] edges, double phi, int k)
        {
            int cnt = 0; while (cnt < edges.Length && edges[cnt] <= phi) cnt++;
            int idx = cnt - 1; return idx < 0 ? 0 : idx > k - 1 ? k - 1 : idx;
        }

        /// `_rep`: the piece bisector nearest THIS puff and the puff's signed offset from it (the wrap matters on a
        /// turned crack pattern — a bisector a whole span away points the opposite way).
        ///
        /// T-0167: `span` is `2 * p.half`, and `p.half` is `Min(s.live.spread, 180) * PI/180` (PlateGeom) — spread
        /// is an animatable ZUIValue, so an authored Curve that passes through (or starts at) 0 makes span exactly
        /// 0 here at that frame. The un-guarded `(da - cenDa) / span` then divides by zero — Infinity, or NaN when
        /// da == cenDa — and `Math.Round`/`Math.Sign` on that NaN downstream throws `ArithmeticException` ("Function
        /// does not accept floating point Not-a-Number values."). Guarded the same way every other division in this
        /// file already guards its denominator (Math.Max(x, epsilon)); at span ~ 0 the puffs collapse onto one
        /// bisector (da ≈ bis, dphi ≈ 0), which is the sane degenerate answer for a needle-thin (spread ≈ 0) burst.
        static void Rep(double da, double cenDa, double span, out double dphi, out double bis)
        {
            double safeSpan = Math.Max(span, 1e-6);
            bis = cenDa + safeSpan * Math.Round((da - cenDa) / safeSpan, MidpointRounding.ToEven);
            dphi = da - bis;
        }

        /// `_plates`: which piece each puff belongs to and how it moves when the crack opens — frozen into the table.
        static void BuildPlates(ExplosiveJetSettings s, int seed, Slots t, int[] grp, int n)
        {
            var f = s.fracture; var f2 = s.fracture2;
            t.fx = new double[n]; t.fdt = new double[n]; t.fkick = new double[n]; t.fdx = new double[n]; t.fdy = new double[n]; t.fnx = new double[n]; t.fny = new double[n]; t.fat = new double[n];
            t.f2x = new double[n]; t.f2dt = new double[n]; t.f2kick = new double[n]; t.f2dx = new double[n]; t.f2dy = new double[n]; t.f2nx = new double[n]; t.f2ny = new double[n]; t.f2at = new double[n];
            for (int i = 0; i < n; i++) { t.fat[i] = 1.0; t.f2at[i] = 1.0; }
            if (f.chance == 0f || f.pieces < 2) return;
            var g = PlateGeom(s, seed);
            double rotSpan = f.rot * g.span;
            double cutPx = f.cut * s.w, cut2Px = f2.cut * s.w;
            for (int i = 0; i < n; i++)
            {
                double phi = PyMod(t.da[i] + g.half + rotSpan, g.span);
                int idx = PieceOf(g.edges, phi, g.k);
                double per = g.broke[grp != null ? grp[i] : 0];
                // gen 7: the shed gas travels with its piece in proportion to `body` (exempting it welded the pieces together)
                t.fx[i] = per * g.grip[i] * (1.0 - (0.85 - 0.70 * f.body) * (t.shed[i] ? 1.0 : 0.0));
                Rep(t.da[i], g.cen[idx] - g.half - rotSpan, g.span, out double dphi, out double bis);
                double ok = g.openk[idx];
                t.fdt[i] = -dphi * f.open * ok + g.spin[idx];
                // `_cut`: a constant-width displacement ACROSS the bisector, away from it (np.sign(0) = 0; all-zero px → zeros)
                if (cutPx != 0.0) { double c = Math.Sign(dphi) * cutPx * ok; t.fnx[i] = c * -Math.Sin(bis); t.fny[i] = c * Math.Cos(bis); }
                t.fkick[i] = g.kick[idx]; t.fdx[i] = g.dx[idx]; t.fdy[i] = g.dy[idx]; t.fat[i] = g.at[idx];
                if (!g.second) continue;
                int sub = 2 * idx + (phi >= g.seam[idx] ? 1 : 0);
                Rep(t.da[i], g.cen2[sub] - g.half - rotSpan, g.span, out double dphi2, out double bis2);
                t.f2x[i] = t.fx[i] * g.broke2[sub];
                double ok2 = g.openk2[sub];
                t.f2dt[i] = -dphi2 * f2.open * ok2 + g.spin2[sub];
                if (cut2Px != 0.0) { double c = Math.Sign(dphi2) * cut2Px * ok2; t.f2nx[i] = c * -Math.Sin(bis2); t.f2ny[i] = c * Math.Cos(bis2); }
                t.f2kick[i] = g.kick2[sub]; t.f2dx[i] = g.dx2[sub]; t.f2dy[i] = g.dy2[sub];
                t.f2at[i] = Math.Max(g.at2[sub], g.at[idx] + 0.02);
            }
        }

        // ── the frame: the shared stages with the detonation's own inserted between them ──
        public override void Frame(JetSettings bs, JetFrame fr, double phase, JetScratch sc)
        {
            bs.EnsureLive();
            var s = E(bs);
            fr.NozzleX = s.nozzleX * s.w; fr.NozzleY = s.nozzleY * s.h;
            if (!BeginInstance(s, in fr, sc)) return;
            var slots = BuildSlots(s, fr.seed);
            Warp(s, in fr, phase, sc);
            Root(s, in fr, phase, sc);
            Flash(s, in fr, phase, sc);
            Emit(s, in fr, slots, phase, sc);
            Rings(s, in fr, phase, sc);
            Chunks(s, in fr, phase, sc);
            Gobs(s, in fr, phase, sc);
            Dust(s, in fr, phase, sc);
            Sparks(s, in fr, phase, sc);
        }

        // ── stage: the polar domain warp, scrolling OUTWARD in every direction (`_warp_out`) ──
        public override void Warp(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = E(bs);
            const int pu = 24, pv = 16, pw = 4;   // angle, radius, time
            double cell = s.warpCell;
            double reachPx = Math.Max(s.live.reach * s.w, 1.0);
            double pxPerLoop = reachPx / Math.Max(s.life, 0.05);
            int kv = Math.Max(1, RoundHalfEven(pxPerLoop / (pv * cell)));
            double offV = phase * kv * pv, offU = s.warpSpin != 0 ? phase * s.warpSpin * pu : 0.0;
            double ww = (float)(phase * pw);
            double nx = fr.NozzleX, ny = fr.NozzleY, invReach = 1.0 / reachPx;
            double warpMax = 0.0;
            int W = fr.W;
            for (int y = sc.by0; y < sc.by1; y++)
                for (int x = sc.bx0; x < sc.bx1; x++)
                {
                    int i = y * W + x;
                    if (!sc.inside[i]) continue;
                    double px = sc.sx[i], py = sc.sy[i];
                    double dx0 = px - nx, dy0 = py - ny;
                    double rad = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                    double th = Math.Atan2(dy0, dx0);
                    double u = th / TAU * pu - offU, v = rad / cell - offV;
                    double dr = JetNoise.Sample(sc, fr.seed, u, v, ww, pu, pv, pw, s.warpOct);
                    double dt = JetNoise.Sample(sc, fr.seed + 4409, u, v, ww, pu, pv, pw, s.warpOct);
                    double along = rad * invReach; if (along > 1.35) along = 1.35;
                    double fade = rad / 7.0; if (fade > 1.0) fade = 1.0;
                    double amp = (s.live.warp0 + s.live.warp1 * along) * fade;
                    double ct = Math.Cos(th), st = Math.Sin(th);
                    double dx = (dr * ct - dt * st) * amp, dy = (dr * st + dt * ct) * amp;
                    sc.sx[i] = px + dx; sc.sy[i] = py + dy;
                    double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (m > warpMax) warpMax = m;
                }
            sc.warpMax = warpMax;
        }

        // ── stage: the seat — under the blast envelope, or the radial source ──
        public override void Root(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = E(bs);
            if (s.live.rootR <= 0) return;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double b = 1.0 + 0.14 * Math.Sin(TAU * 3.0 * phase);
            double a = s.live.aim * Math.PI / 180.0;
            if (s.HasSchedule)
            {
                // a detonation has no standing source: the lump burns out with each blast, or every frame between bangs
                // carries a bright dot waiting for the next one
                double env = 0.0;
                foreach (var bl in BlastList(s))
                {
                    double sAge = Mod1(phase - bl.at) / Math.Max(s.life * 0.85, 1e-3);
                    if (sAge < 1.0) env = Math.Max(env, bl.pw * Fade(sAge, 0.04) * (1.0 - sAge) * (1.0 - sAge));
                }
                if (env <= 0.002) return;
                JetField.Blob(sc, in fr, nx, ny, s.live.rootR * (s.live.spread >= 60f ? 1.0 : 1.7) * b, s.live.rootR * b, a, s.live.rootAmp * env);
                return;
            }
            if (s.srcR > 0f && s.rootK > 0)
            {
                double rr = s.srcR * s.w;
                for (int j = 0; j < s.rootK; j++)
                {
                    double th = TAU * j / s.rootK;
                    double bb = b * (1.0 + 0.10 * Math.Cos(3.0 * th + TAU * phase));
                    JetField.Blob(sc, in fr, nx + rr * Math.Cos(th), ny + rr * Math.Sin(th), s.live.rootR * 1.5 * bb, s.live.rootR * bb, th + Math.PI / 2, s.live.rootAmp);
                }
                return;
            }
            double ex = s.live.spread >= 60f ? 1.0 : 1.7;
            JetField.Blob(sc, in fr, nx, ny, s.live.rootR * ex * b, s.live.rootR * b, a, s.live.rootAmp);
        }

        // ── stage: the instant itself ──
        public void Flash(ExplosiveJetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            var f = s.flash;
            if (f.radius <= 0f || !s.HasSchedule) return;
            double nx = fr.NozzleX, ny = fr.NozzleY, a = s.live.aim * Math.PI / 180.0;
            foreach (var bl in BlastList(s))
            {
                double sAge = Mod1(phase - bl.at) / Math.Max(f.life, 1e-3);
                if (sAge >= 1.0) continue;
                double r = f.radius * bl.pw * (1.0 + f.grow * sAge);
                double amp = f.amp * bl.pw * Fade(sAge, 0.05) * Math.Pow(1.0 - sAge, 3.0);
                JetField.Blob(sc, in fr, nx + bl.ox, ny + bl.oy, r * f.elong, r, a, amp);
            }
        }

        // ── stage: every live puff at its own age — born in an instant, dying from the outside in, breaking ──
        public override void Emit(JetSettings bs, in JetFrame fr, JetSlots tab0, double phase, JetScratch sc)
        {
            var s = E(bs);
            var tab = (Slots)tab0;
            int n = tab.N;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.live.reach * s.w, srcPx = s.srcR * s.w;
            double aim0 = s.live.aim * Math.PI / 180.0, sweep = s.live.sweep * Math.PI / 180.0, sw = s.swirl * Math.PI / 180.0;
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            bool fullCircle = s.live.spread >= 180f;
            double lim = Math.Min(s.live.spread + 15.0, 180.0) * Math.PI / 180.0;
            bool fracture = s.fracture.chance != 0f && s.fracture.pieces >= 2, second = fracture && s.fracture2.chance != 0f;
            double invShrink = 1.0 / Math.Max(1.0 - s.shrinkAt, 1e-3);
            for (int i = 0; i < n; i++)
            {
                double life = s.life * tab.ls[i] * (tab.shed[i] ? s.shedLife : 1.0);
                // the fast gas dies first (shed gas takes half of it), so the lit silhouette closes inward
                if (s.leadDie != 0f) life *= 1.0 - s.leadDie * tab.lead[i] * (tab.shed[i] ? 0.5 : 1.0);
                double sAge = Mod1(phase - tab.birth[i]) / Math.Max(life, 1e-3);
                if (sAge >= 1.0) continue;
                // the sweep, the spin and the pulse are frozen into the puff at its BIRTH phase
                double ep = tab.birth[i];
                double aim = aim0;
                if (s.live.sweep != 0f) aim += sweep * Math.Sin(TAU * s.sweepN * ep);
                if (s.spin != 0) aim += TAU * s.spin * ep;
                double pulse = 1.0;
                if (s.pulseN != 0 && s.live.pulseDepth != 0f)
                {
                    pulse = 1.0 + s.live.pulseDepth * Math.Cos(TAU * s.pulseN * ep);
                    if (pulse < 0.05) pulse = 0.05; else if (pulse > 2.5) pulse = 2.5;
                }
                double d = reachPx * tab.vs[i] * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double da = tab.da[i], theta;
                if (fullCircle || !tab.shed[i]) theta = aim + da;
                else
                {
                    double room = Math.Max(lim - Math.Abs(da), 0.0);
                    theta = aim + da + Math.Sign(da) * Math.Min(Math.Abs(da) * s.live.shedKick, room * 0.85);
                }
                double thetaP = s.swirl != 0f ? theta + sw * sAge : theta;
                // the crack: smoothstepped and raised to 1.6 so the first frames of the break are almost nothing, weighted
                // by lead (a crack runs from the surface inward) blended towards 1 by `body` (a piece LEAVES whole)
                double fu = 0.0, fu2 = 0.0;
                if (fracture)
                {
                    double fat = tab.fat[i];
                    fu = (sAge - fat) / Math.Max(1.0 - fat, 1e-3); if (fu < 0) fu = 0; else if (fu > 1) fu = 1;
                    double lw = 0.22 + 0.78 * tab.lead[i];
                    lw += s.fracture.body * (1.0 - lw);
                    fu = Math.Pow(fu * fu * (3.0 - 2.0 * fu), 1.6) * tab.fx[i] * lw;
                    thetaP += tab.fdt[i] * fu;
                    d *= 1.0 + tab.fkick[i] * fu;
                    if (second)
                    {
                        double f2at = tab.f2at[i];
                        fu2 = (sAge - f2at) / Math.Max(1.0 - f2at, 1e-3); if (fu2 < 0) fu2 = 0; else if (fu2 > 1) fu2 = 1;
                        fu2 = Math.Pow(fu2 * fu2 * (3.0 - 2.0 * fu2), 1.6) * tab.f2x[i] * lw;
                        thetaP += tab.f2dt[i] * fu2;
                        d *= 1.0 + tab.f2kick[i] * fu2;
                    }
                }
                double rr = srcPx + d;
                double px = nx + tab.ox[i] + rr * Math.Cos(thetaP);
                double py = ny + tab.oy[i] + rr * Math.Sin(thetaP);
                if (fracture)
                {
                    px += tab.fdx[i] * fu + tab.fnx[i] * fu;
                    py += tab.fdy[i] * fu + tab.fny[i] * fu;
                    if (second)
                    {
                        px += tab.f2dx[i] * fu2 + tab.f2nx[i] * fu2;
                        py += tab.f2dy[i] * fu2 + tab.f2ny[i] * fu2;
                    }
                }
                py += s.grav * reachPx * sAge * sAge - s.buoy * reachPx * Math.Pow(sAge, 2.4);
                if (tab.shed[i]) py += tab.drift[i] * s.live.shedKick * 3.0 * sAge * sAge;
                // entrainment per px travelled + swell per unit age (gas that goes nowhere still expands)
                double r = s.live.r0 * tab.rs[i] + s.growth * d + s.swell * reachPx * sAge;
                if (s.shrink != 0f && !tab.shed[i])
                {
                    // it dies by getting SMALLER, from the outside in: the front loses `shrink` of itself, the slowest
                    // third nothing (lead^1.4, not a floor — a floor reopened the centre)
                    double u = (sAge - s.shrinkAt) * invShrink; if (u < 0) u = 0; else if (u > 1) u = 1;
                    r *= 1.0 - s.shrink * Math.Pow(tab.lead[i], 1.4) * (u * u * (3.0 - 2.0 * u));
                }
                if (s.shedSwell != 0f && tab.shed[i]) r *= 1.0 + s.shedSwell * sAge;
                double aspect = 1.0 + s.live.elong * Math.Exp(-sAge / Math.Max(s.roundAt, 0.02));
                double ori = thetaP;
                if (s.swirl != 0f)
                {
                    double drds = reachPx * tab.vs[i] * kd * Math.Exp(-kd * sAge) / denom;
                    ori = thetaP + Math.Atan2(rr * sw, Math.Max(drds, 1e-3));
                }
                // the transparency starts LATER: the decay pushed to the back of the life by `hold`
                double decay = s.hold != 0f ? 1.0 - Math.Pow(sAge, 1.0 + s.hold) : 1.0 - sAge;
                if (decay < 0) decay = 0; else if (decay > 1) decay = 1;
                double amp = s.live.strength * tab.amp[i] * tab.ampk[i] * pulse * Fade(sAge) * Math.Pow(decay, s.live.cool);
                if (s.shockN != 0f && s.live.shockDepth != 0f)
                {
                    amp *= 1.0 + s.live.shockDepth * Math.Cos(TAU * s.shockN * d / Math.Max(reachPx, 1.0));
                    if (amp < 0) amp = 0;
                }
                if (tab.shed[i]) amp *= 0.72;
                double tint = s.live.soot != 0f ? Math.Min(Math.Max(s.live.soot * sAge, 0.0), 1.0) : 0.0;
                JetField.Blob(sc, in fr, px, py, r * aspect, r, ori, amp, tint);
            }
        }

        // ── stage: rings — launched at the bang; flat fronts that break on the same seams as the gas ──
        public override void Rings(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = E(bs);
            if (s.ringN <= 0) return;
            // without a schedule the edge-on branch is the base's, verbatim
            if (!s.HasSchedule && !s.ringFlat) { base.Rings(bs, in fr, phase, sc); return; }
            double reachPx = s.live.reach * s.w, srcPx = s.srcR * s.w;
            double a = s.live.aim * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            var bl = s.HasSchedule ? BlastList(s) : null;
            bool fracture = s.fracture.chance != 0f && s.fracture.pieces >= 2 && s.ringFlat;
            var fg = fracture ? PlateGeom(s, fr.seed) : null;
            double rotSpan = fg != null ? s.fracture.rot * fg.span : 0.0;
            for (int m = 0; m < s.ringN; m++)
            {
                double at, pw, ox, oy;
                if (bl != null) { var b = bl[m % bl.Length]; at = b.at; pw = b.pw; ox = b.ox; oy = b.oy; }
                else { at = m / (double)s.ringN; pw = 1.0; ox = 0.0; oy = 0.0; }
                double sAge = Mod1(phase - at) / Math.Max(s.life * s.ringLife, 1e-3);
                if (sAge >= 1.0) continue;
                double d = reachPx * s.live.ringReach * pw * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double amp0 = s.live.strength * s.live.ringAmp * pw * Fade(sAge, 0.10) * Math.Pow(Math.Max(0.0, 1.0 - sAge), s.live.cool * (s.ringFlat ? 0.50 : 0.30));
                double rot = 1.31 * m;
                double wob = 0.17 + 0.05 * ((m * 7) % 3);
                double nx = fr.NozzleX + ox, ny = fr.NozzleY + oy;
                double rise = s.buoy * reachPx * Math.Pow(sAge, 2.4);
                if (s.ringFlat)
                {
                    double rr = srcPx + d;
                    double pr = s.live.ringR0 + s.ringGrow * d * 0.42;
                    double arc = Math.Min(s.ringArc, 180f) * Math.PI / 180.0;
                    bool full = arc >= Math.PI - 1e-6;
                    double span = full ? TAU : 2.0 * arc;
                    int k = (int)Math.Max(s.ringK, Math.Min(140, RoundHalfEven(span * rr / Math.Max(pr * 0.80, 1e-3))));
                    bool broken = fg != null && fg.broke[m % fg.broke.Length] != 0.0;
                    for (int j = 0; j < k; j++)
                    {
                        double th, taper;
                        if (full) { th = TAU * j / k + rot; taper = 1.0; }
                        else
                        {
                            // a FRONT: only the part of the circle within the arc, tapered over the last 30 % of each end
                            double u = (j + 0.5) / k;
                            th = a + (-arc + span * u);
                            double e = Math.Min(u, 1.0 - u) / 0.30;
                            taper = Math.Pow(Math.Min(1.0, e), 1.5);
                        }
                        double rj = rr * (1.0 + wob * (Math.Cos(3 * th + rot) * 0.6 + Math.Cos(5 * th - 1.7 * rot) * 0.4));
                        double bx = 0.0, by = 0.0;
                        if (broken)
                        {
                            // the blob's own angle decides its piece, the same bisector arithmetic the gas gets
                            double ph = PyMod(th - a + fg.half + rotSpan, fg.span);
                            int ii = PieceOf(fg.edges, ph, fg.k);
                            double fs = (sAge - fg.at[ii]) / Math.Max(1.0 - fg.at[ii], 1e-3); if (fs < 0) fs = 0; else if (fs > 1) fs = 1;
                            fs = Math.Pow(fs * fs * (3.0 - 2.0 * fs), 1.6);
                            double dp = ph - fg.cen[ii];
                            double bsA = th - a - dp;
                            double ok = fg.openk[ii];
                            th += (-dp * s.fracture.open * ok + fg.spin[ii]) * fs;
                            rj *= 1.0 + fg.kick[ii] * fs;
                            double cu = CopySign(s.fracture.cut * s.w * ok, dp) * fs;
                            bx = fg.dx[ii] * fs - cu * Math.Sin(bsA + a);
                            by = fg.dy[ii] * fs + cu * Math.Cos(bsA + a);
                        }
                        JetField.Blob(sc, in fr, nx + rj * Math.Cos(th) + bx, ny + rj * Math.Sin(th) - rise + by, pr * 1.45, pr, th + Math.PI / 2, amp0 * taper);
                    }
                    continue;
                }
                double rr2 = s.live.ringR0 + s.ringGrow * d;
                double pr2 = s.live.r0 * 0.85 + s.growth * d * 0.34;
                int k2 = (int)Math.Max(s.ringK, Math.Min(48, RoundHalfEven(TAU * rr2 / Math.Max(pr2 * 1.05, 1e-3))));
                for (int j = 0; j < k2; j++)
                {
                    double th = TAU * j / k2;
                    double rj = rr2 * (1.0 + wob * (Math.Cos(2 * th + rot) * 0.6 + Math.Cos(3 * th - 1.7 * rot) * 0.4));
                    double perp = Math.Cos(th) * rj, along = Math.Sin(th) * rj * 0.35;
                    double x = nx + (d + along) * ca - perp * sa;
                    double y = ny + (d + along) * sa + perp * ca - rise;
                    double depth = 0.62 + 0.38 * Math.Cos(th);
                    JetField.Blob(sc, in fr, x, y, pr2 * 1.25, pr2, a, amp0 * depth);
                }
            }
        }

        /// math.copysign: |x| with the sign of y (y = +0.0 gives +|x|).
        static double CopySign(double x, double y) => (y < 0 || (y == 0 && 1.0 / y < 0)) ? -Math.Abs(x) : Math.Abs(x);

        // ── stage: burning fragments thrown clear, each dragging a trail ──
        public void Chunks(ExplosiveJetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            var c = s.chunks;
            if (c.count <= 0 || !s.HasSchedule) return;
            var rng = new PyreNumpyRng(unchecked((uint)(fr.seed * 6367 + 29)));
            double nx = fr.NozzleX, ny = fr.NozzleY, reachPx = s.live.reach * s.w, a = s.live.aim * Math.PI / 180.0;
            var bl = BlastList(s);
            double arc = Math.Min(s.live.spread * c.wide, 180.0) * Math.PI / 180.0;
            int n = c.count;
            var da = new double[n]; var vs = new double[n]; var rs = new double[n]; var sag = new double[n]; var lag = new double[n];
            for (int i = 0; i < n; i++) da[i] = rng.Uniform(-1.0, 1.0) * arc;
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.62, 1.35);
            for (int i = 0; i < n; i++) rs[i] = rng.Uniform(0.70, 1.45);
            for (int i = 0; i < n; i++) sag[i] = rng.Uniform(0.45, 1.35);
            for (int i = 0; i < n; i++) { double u = rng.Uniform(0.0, 0.9); lag[i] = u * u; }   // most leave with the front
            double kd = Math.Max(c.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            double invShrink = 1.0 / Math.Max(1.0 - s.shrinkAt, 1e-3);
            int trail = Math.Max(1, c.trail);
            for (int i = 0; i < n; i++)
            {
                var b = bl[i % bl.Length];
                double sAge = Mod1(phase - b.at - lag[i] * s.blastSpan) / Math.Max(s.life * c.life, 1e-3);
                if (sAge >= 1.0) continue;
                double th = a + da[i];
                double baseD = reachPx * c.reach * vs[i] * b.pw;
                for (int j = 0; j < trail; j++)
                {
                    double sj = sAge * (1.0 - 0.11 * j);
                    if (sj <= 0.0) break;
                    double d = baseD * (1.0 - Math.Exp(-kd * sj)) / denom;
                    double x = nx + b.ox + d * Math.Cos(th);
                    double y = ny + b.oy + d * Math.Sin(th) + c.sag * sag[i] * reachPx * sj * sj - s.buoy * reachPx * 0.35 * Math.Pow(sj, 2.4);
                    double f = 1.0 - 0.26 * j;
                    // one death model in the file: a fragment gets small and goes, the same two terms as the body
                    double cr = c.radius * rs[i] * f;
                    if (s.shrink != 0f)
                    {
                        double us = (sj - s.shrinkAt) * invShrink; if (us < 0) us = 0; else if (us > 1) us = 1;
                        cr *= 1.0 - 0.80 * s.shrink * (us * us * (3.0 - 2.0 * us));
                    }
                    double dec = s.hold != 0f ? 1.0 - Math.Pow(sj, 1.0 + s.hold) : 1.0 - sj;
                    JetField.Blob(sc, in fr, x, y, cr * (j == 0 ? 1.35 : 1.0), cr, th, c.amp * b.pw * f * Fade(sj, 0.05) * Math.Pow(Math.Max(dec, 0.0), 1.5));
                }
            }
        }

        // ── stage: shed burning mass that dissipates ──
        public void Gobs(ExplosiveJetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            var g = s.gobs;
            if (g.count <= 0 || !s.HasSchedule) return;
            var rng = new PyreNumpyRng(unchecked((uint)(fr.seed * 3571 + 91)));
            double nx = fr.NozzleX, ny = fr.NozzleY, reachPx = s.live.reach * s.w, a = s.live.aim * Math.PI / 180.0;
            var bl = BlastList(s);
            double arc = Math.Min(s.live.spread * g.wide, 180.0) * Math.PI / 180.0;
            int n = g.count;
            var da = new double[n]; var vs = new double[n]; var rs = new double[n]; var sag = new double[n]; var born = new double[n];
            for (int i = 0; i < n; i++) da[i] = rng.Uniform(-1.0, 1.0) * arc;
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.42, 1.25);
            for (int i = 0; i < n; i++) rs[i] = rng.Uniform(0.62, 1.55);
            for (int i = 0; i < n; i++) sag[i] = rng.Uniform(0.35, 1.40);
            for (int i = 0; i < n; i++) born[i] = Math.Pow(rng.NextDouble(), 1.7) * g.early;
            double kd = Math.Max(g.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            double sw = s.swirl * Math.PI / 180.0 * 0.6;
            int trail = Math.Max(1, g.trail);
            for (int i = 0; i < n; i++)
            {
                var b = bl[i % bl.Length];
                double sAge = Mod1(phase - b.at - born[i] * s.blastSpan) / Math.Max(s.life * g.life, 1e-3);
                if (sAge >= 1.0) continue;
                double th = a + da[i] + sw * sAge;
                double baseD = reachPx * g.reach * vs[i] * b.pw;
                double r = g.radius * rs[i] * (1.0 + g.swell * sAge);
                double aspect = 1.0 + 1.30 * Math.Exp(-sAge / 0.34);   // stretched while moving, round once stalled
                double tint = s.live.soot != 0f ? Math.Min(s.live.soot * sAge * 1.15, 1.0) : 0.0;
                for (int j = 0; j < trail; j++)
                {
                    double sj = sAge * (1.0 - 0.13 * j);
                    if (sj <= 0.0) break;
                    double d = baseD * (1.0 - Math.Exp(-kd * sj)) / denom;
                    double x = nx + b.ox + d * Math.Cos(th);
                    double y = ny + b.oy + d * Math.Sin(th) + g.sag * sag[i] * reachPx * sj * sj - s.buoy * reachPx * 0.55 * Math.Pow(sj, 2.4);
                    double f = 1.0 - 0.30 * j;
                    JetField.Blob(sc, in fr, x, y, r * f * aspect, r * f, th, g.amp * b.pw * f * Fade(sj, 0.07) * (1.0 - sj) * (1.0 - sj), tint);
                }
            }
        }

        // ── stage: particles shed off the body's own skin ──
        public void Dust(ExplosiveJetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            var du = s.dust;
            if (du.count <= 0) return;
            var rng = new PyreNumpyRng(unchecked((uint)(fr.seed * 2749 + 617)));
            double nx = fr.NozzleX, ny = fr.NozzleY, reachPx = s.live.reach * s.w, srcPx = s.srcR * s.w, a = s.live.aim * Math.PI / 180.0;
            int n = du.count;
            double arc = Math.Min(s.live.spread * du.wide, 180.0) * Math.PI / 180.0;
            var th0 = new double[n]; var born = new double[n]; var onr = new double[n]; var vs = new double[n]; var rs = new double[n]; var sag = new double[n]; var scat = new double[n];
            for (int i = 0; i < n; i++) th0[i] = a + rng.Uniform(-1.0, 1.0) * arc;
            for (int i = 0; i < n; i++) born[i] = du.from + (du.to - du.from) * Math.Pow(rng.NextDouble(), du.bias);
            for (int i = 0; i < n; i++) onr[i] = rng.Uniform(0.88, 1.18);
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.30, 1.00);
            for (int i = 0; i < n; i++) rs[i] = rng.Uniform(0.60, 1.55);
            for (int i = 0; i < n; i++) sag[i] = rng.Uniform(0.25, 1.45);
            for (int i = 0; i < n; i++) scat[i] = rng.Uniform(-1.0, 1.0) * du.scatter;
            double kg = Math.Max(s.drag, 1e-3), dg = 1.0 - Math.Exp(-kg);   // the GAS's drag: where the body is
            double kp = Math.Max(du.drag, 1e-3), dp = 1.0 - Math.Exp(-kp);  // the particle's own
            var bl = s.HasSchedule ? BlastList(s) : new[] { new Blast { at = 0.0, pw = 1.0, ox = 0.0, oy = 0.0 } };
            int trail = Math.Max(1, du.trail);
            for (int i = 0; i < n; i++)
            {
                var b = bl[i % bl.Length];
                double sAge = Mod1(phase - b.at - born[i] * s.life) / Math.Max(s.life * du.life, 1e-3);
                if (sAge >= 1.0) continue;
                // where the body's own front is at the age this one came off it
                double rb = srcPx + reachPx * du.where * b.pw * (1.0 - Math.Exp(-kg * born[i])) / dg * onr[i];
                double th = th0[i] + scat[i];
                double baseD = reachPx * du.reach * vs[i] * b.pw;
                double tint = s.live.soot != 0f ? Math.Min(s.live.soot * (born[i] + sAge * 0.5), 1.0) : 0.0;
                double c0 = Math.Cos(th0[i]), s0 = Math.Sin(th0[i]), ct = Math.Cos(th), st = Math.Sin(th);
                for (int j = 0; j < trail; j++)
                {
                    double sj = sAge * (1.0 - 0.18 * j);
                    if (sj <= 0.0) break;
                    double d = baseD * (1.0 - Math.Exp(-kp * sj)) / dp;
                    double x = nx + b.ox + rb * c0 + d * ct;
                    double y = ny + b.oy + rb * s0 + d * st + du.sag * sag[i] * reachPx * sj * sj - s.buoy * reachPx * 0.45 * Math.Pow(sj, 2.4);
                    double f = 1.0 - 0.34 * j;
                    double r = du.radius * rs[i] * f * (1.0 - 0.42 * sj);
                    JetField.Blob(sc, in fr, x, y, r * 1.25, r, th, du.amp * b.pw * f * Fade(sj, 0.10) * Math.Pow(Math.Max(1.0 - sj, 0.0), 1.7), tint);
                }
            }
        }

        // ── stage: sparks, leaving WITH the gas ──
        public override void Sparks(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = E(bs);
            if (s.sparks <= 0) return;
            var rng = new PyreNumpyRng(unchecked((uint)(fr.seed * 104729 + 77)));
            int n = s.sparks;
            double nx = fr.NozzleX, ny = fr.NozzleY, reachPx = s.live.reach * s.w, srcPx = s.srcR * s.w;
            double a = s.live.aim * Math.PI / 180.0, sw = s.swirl * Math.PI / 180.0;
            double arc = Math.Min(Math.Min(s.live.spread * 1.7, s.live.spread + 15.0), 180.0) * Math.PI / 180.0;
            // a spark rises because the gas around it does: its lift is tied to buoy, not a fixed 0.10..0.55
            double lift = s.buoy / 0.18; if (lift < 0.35) lift = 0.35; else if (lift > 2.0) lift = 2.0;
            var ph = new double[n]; var da = new double[n]; var vs = new double[n]; var rise = new double[n];
            for (int i = 0; i < n; i++) ph[i] = rng.NextDouble();
            for (int i = 0; i < n; i++) da[i] = rng.Uniform(-1.0, 1.0) * arc;
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.55, 1.22);
            for (int i = 0; i < n; i++) rise[i] = rng.Uniform(0.10, 0.55) * lift;
            var bl = s.HasSchedule ? BlastList(s) : null;
            for (int i = 0; i < n; i++)
            {
                double pw = 1.0, ox = 0.0, oy = 0.0, born;
                if (bl != null) { var b = bl[i % bl.Length]; pw = b.pw; ox = b.ox; oy = b.oy; born = b.at + ph[i] * ph[i] * s.blastSpan; }
                else born = ph[i];
                double sAge = Mod1(phase - born) / Math.Max(s.life * 1.1, 1e-3);
                if (sAge >= 1.0) continue;
                double d = srcPx + reachPx * vs[i] * pw * sAge;
                double th = a + da[i] + sw * sAge;
                double x = nx + ox + d * Math.Cos(th);
                double y = ny + oy + d * Math.Sin(th) - rise[i] * reachPx * sAge * sAge;
                JetField.Blob(sc, in fr, x, y, s.live.sparkR * 1.4, s.live.sparkR, th, 1.5 * pw * Fade(sAge, 0.08) * Math.Pow(1.0 - sAge, 1.2));
            }
        }
    }

    /// The shared shade pass with the explosive `opaq`: an exponent on the ramp's opacity ceiling.
    public sealed class ExplosiveJetShade : JetShade
    {
        public new static readonly ExplosiveJetShade Default = new ExplosiveJetShade();
        protected override double CeilingExponent(JetSettings s) => s is ExplosiveJetSettings e ? e.opaq : 1.0;
    }
}
