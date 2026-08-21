// PlusJetEngine — the Kiln "Flame / agent3" family's `flame3` core (jet.py + field.py + noise.py + grad.py + lut.py),
// ported ONCE as the shared JET ENGINE: Port 07 (agent3, generation 2 — the directional flamethrower stream, this
// file's first consumer: JetForm) and the two forks that grew out of the same package, Port 06 (agent3_fork_radial,
// generation 3) and Port 05 (agent3_fork_explosive, generation 7), which extend it instead of re-porting it.
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3/flame3 (MANIFEST "generation 2 — THE FLAMETHROWER STREAM,
// REGRADED"). The algorithm: a per-pixel DOMAIN WARP (tileable value noise scrolled downstream, amplitude ramped with
// the distance along the aim) displaces the sampling grid; then N anisotropic compact-support blobs — puffs that leave
// the nozzle fast, small and stretched along their velocity, slow under drag, fatten by entrainment and only lift under
// buoyancy once they are slow (s^2.4) — are summed into a heat plane H and a soot plane T; then ONE shade pass turns H
// into RGBA through a 1024-entry LUT interpolated in LINEAR LIGHT with a per-stop opacity ceiling, a soot crossfade
// between two whole ramps, an edge smoothstep on the raw heat, and a despeckle. Every term is a closed-form function of
// the loop phase (slot ages (phase − i/N) mod 1, noise scrolled by whole lattice periods, integer-frequency sinusoids),
// so any frame renders standalone and the loop is exact by construction.
//
// Component list (contract components.json), status in this port:
//   warp       PORTED   cartesian value noise (pu, pv, pw) = (24, 12, 4), 2 octaves, seeds seed / seed + 4409, scroll ku / kv
//                       snapped per axis, amplitude warp0 + warp1·clip(along / reach, 0, 1.35) — `Warp`.
//   root       PORTED   one lump at the nozzle, 1.7 : 1 along the aim, breathing 1 + 0.14·sin(6πφ) — `Root`.
//   emit       PORTED   the slot table from default_rng(seed·7919 + 13) (bit-exact via PlusNumpyRng), exponential drag
//                       normalised to `reach`, buoyancy s^2.4, sag s², elongation decaying with round_at, fade-in, cool,
//                       pulse / sweep frozen at birth, shock diamonds by distance, shed puffs (kick, drift, 0.72 amp,
//                       shed_life), soot tint — `Emit`.
//   rings      PORTED   vortex rings seen from the side (perp full, along × 0.35, far half dimmed), puff count grown with the
//                       circumference, radius wobbled by two harmonics of the ring index — `Rings`.
//   sparks     PORTED   motes from default_rng(seed·104729 + 77) — `Sparks`.
//   shade      PORTED   t = clip((H·gain − lo)/(hi − lo))^curve, optional `steps` floor-quantisation of t (never of alpha),
//                       idx = int(t·1023 + 0.5), linear-light LUT baked through uint8 sRGB exactly as lut.build_lut,
//                       soot = smoothstep((T/(H + 1e−6) − soot_lo)/(soot_hi − soot_lo)) crossfade of both ramps and both
//                       ceilings, alpha = smoothstep((H − lo)/soft)·ceiling, sRGB re-encode, +0.5 truncation — `JetShade`.
//   despeckle  PORTED   lit px with alpha < 70 and fewer than 2 lit 4-neighbours dropped — `JetShade.Despeckle`.
//   Nothing approximated, nothing dropped. `lo` / `hi` / `curve` are the contract's FITTED values, frozen per draw.
//
// Frame of reference: everything runs in the draw's SOURCE px frame (w × h, y-DOWN, +aim = downward, exactly the
// contract's units: `reach` / `buoy` / `grav` in canvas WIDTHS of that frame, radii and warp in its px) and is sampled
// at canvas pixels through `JetFrame` (canvas = anchor + u·R(rot)·(src − nozzle)). At u = 1, rot = 0 and the frame
// letterboxed on a square canvas the sample set IS the contract's, so parity is exact; at any other u / rot the same
// picture is scaled / turned (the field is scale-invariant: kernels are normalised by their radii). The field is only
// evaluated inside the frame rect (what the source rendered), which also bounds a swarm instance's cost.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// ENGINE HAND-OFF NOTE for Ports 06 / 05 (how to extend without rewriting):
//
//   DATA — `JetSettings` is NOT sealed. A fork declares `sealed class RadialJetSettings : JetSettings` (or explosive)
//   adding only its own fields; ZuiReflect draws inherited public fields, `JetForm.SetContractParam`'s reflection finds
//   them by name, and every base stage reads the base fields through the base type. Ranges wide enough for the family
//   are already on the base fields where a fork only changes a VALUE (`spread` up to 180, `reach` down to 0.05).
//
//   CODE — `JetProgram` is the stage pipeline; every stage is `virtual`. The base `Frame` runs
//       Warp → Root → Emit → Rings → Sparks,
//   and `JetShade.Shade` is the shade pass (its `CeilingExponent` virtual is the explosive `opaq` hook). A fork
//   subclasses `JetProgram`, overrides what it changes and keeps the rest:
//     • radial (gen 3): `Warp` → polar (`_warp_out`: u = θ/2π·pu, v = r/cell − phase·kv·pv, (pu, pv, pw) = (24, 16, 4),
//       displacement in the radial / tangential basis, amplitude × clip(r/7, 0, 1)); `BuildSlots` → `bias` power,
//       stratified + permuted angles for spread ≥ 60 (`StratifiedPermuted`, ready), lobes remap, lobe_kick on vs;
//       `Emit` → `src_r` birth radius, `spin` (turns per loop added to the birth aim), `swirl` (θ + sw·s, orientation =
//       the lead angle θp + atan2(rr·sw, dr/ds)), spread ≥ 180 skips the shed-kick clamp; `Root` → a ring of root_k lumps
//       when src_r > 0; `Rings` → `ring_flat` face-on rings (k ≤ 140, cool × 0.50); `Sparks` → src_r offset, swirl, the
//       vs range 0.55..1.22 and the angle clamp min(spread·1.7, spread + 15, 180).
//     • explosive (gen 7): `BuildSlots` → blast groups (`blast_at` / `blast_pow` / `blast_share` / `blast_off`, a per-
//       group stratified table), births `at + span·u^skew`, `blast_front`, `vel_spread`, per-puff `lead` = `LeadRank(vs)`
//       (rank-based, ready), plates / fracture membership (`_plates`); `Emit` → hold / shrink / shrink_at / lead_die /
//       swell, the fracture displacement (`_plate_geom`, `_cut`, `_rep`), shed_swell; `Frame` → insert `Flash` after
//       Root and `Chunks` / `Gobs` / `Dust` between Rings and Sparks (each is a new virtual on the subclass, seeded from
//       its own stream: plates seed·15013 + 401, dust seed·2749 + 617, chunks seed·6367 + 29, gobs seed·3571 + 91);
//       `JetShade.CeilingExponent` → `opaq`; `Rings` → `ring_arc` + breaking along the plate geometry.
//   The field primitives (`JetField.Blob`, the warp application, `JetNoise`) and the shade pass are the family's
//   byte-identical shared code (field.py / noise.py / lut.py are identical in all three dirs; grad.py differs only by
//   `opaq`) and should not need touching.
//
//   DELIBERATELY LEFT OUT of the base (so the base stays the gen-2 contract): the polar warp, the stratified angle
//   path, blast schedules, the death model, fracture, dust / gobs / chunks / flash, `opaq`. Their RNG streams are
//   documented above so a fork's draws land on the contract's own numbers.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// The gen-2 `JetSpec` minus canvas / timing / tags: every contract parameter key as a dial (camelCase of the key),
    /// plus the draw's source frame (w, h, nozzle) and its ramps. Field initialisers are the JetSpec CLASS defaults
    /// (jet.py), which no published draw uses as-is — a form always holds one of `JetDraws`' five factories.
    [Serializable]
    public class JetSettings
    {
        // ── the source frame ──
        [Tooltip("Width of the draw's SOURCE frame in px — the reference every px dial and every canvas-width dial here is relative to (Size × canvas width / this = the scale).")]
        [Range(16, 400)] public int w = 160;
        [Tooltip("Height of the draw's source frame in px. Nothing is drawn outside the frame.")]
        [Range(16, 400)] public int h = 80;
        [Tooltip("Where the nozzle sits across the source frame, as a fraction of its width (contract `nozzle` x).")]
        [Range(0f, 1f)] public float nozzleX = 0.08f;
        [Tooltip("Where the nozzle sits down the source frame, as a fraction of its height from the TOP (contract `nozzle` y; the source is y-down).")]
        [Range(0f, 1f)] public float nozzleY = 0.55f;

        // ── where it goes ──
        [Tooltip("Aim in degrees: 0 = straight right, + = downward (the source's y-down frame).")]
        [Range(-180f, 180f)] public float aim = 0f;
        [Tooltip("Travel of a puff over its whole life, in canvas WIDTHS of the source frame.")]
        [Range(0.05f, 1.5f)] public float reach = 0.80f;
        [Tooltip("Half-angle of the emission cone, degrees. The cone is dense on the axis (angles biased by |x|^1.7), so the stream has a spine and a ragged fringe rather than a paper fan.")]
        [Range(0f, 180f)] public float spread = 10f;

        // ── the argument between the push and the air ──
        [Tooltip("Exponential drag: > 0 decelerates, the higher the sooner the puff stalls. Travel = reach·(1 − e^(−drag·s))/(1 − e^(−drag)).")]
        [Range(0.01f, 6f)] public float drag = 2.2f;
        [Tooltip("Upward rise by the end of a puff's life, canvas widths — applied as s^2.4, so the root runs flat and only the slowed tip rolls over.")]
        [Range(0f, 1f)] public float buoy = 0.10f;
        [Tooltip("Downward sag by the end of life, canvas widths — applied as s² (unburnt fuel is heavy).")]
        [Range(0f, 1f)] public float grav = 0f;
        [Tooltip("Puff radius at the nozzle, source px.")]
        [Range(0.5f, 10f)] public float r0 = 2.6f;
        [Tooltip("Radius gained per px travelled (entrainment): the cone fattens as it slows.")]
        [Range(0f, 0.3f)] public float growth = 0.075f;
        [Tooltip("Extra length-to-width of a puff at birth (a streak along its velocity); decays as it slows.")]
        [Range(0f, 6f)] public float elong = 2.4f;
        [Tooltip("Age (fraction of life) by which a puff is round again: aspect = 1 + elong·e^(−s/round_at).")]
        [Range(0.02f, 1f)] public float roundAt = 0.30f;

        // ── emission ──
        [Tooltip("Puff slots. Slot i is born at phase i/slots every loop, so the set of live puffs at phase 1 is the set at phase 0.")]
        [Range(1, 400)] public int slots = 64;
        [Tooltip("A puff's life as a fraction of the loop.")]
        [Range(0.05f, 1f)] public float life = 0.55f;
        [Tooltip("Per-slot variation of speed / size / amplitude / life, 0..1 (scales the uniform jitters of the slot table).")]
        [Range(0f, 1f)] public float jitter = 0.55f;
        [Tooltip("Peak heat a puff deposits (the kernel's height); overlap sums.")]
        [Range(0.05f, 3f)] public float strength = 1.15f;
        [Tooltip("Amplitude falloff exponent over a puff's life: amp ∝ (1 − s)^cool.")]
        [Range(0.1f, 4f)] public float cool = 1.6f;
        [Tooltip("Soot tint gained by the end of a puff's life (tint = clip(soot·s)); drives the crossfade into the second ramp. 0 = the second ramp is never used.")]
        [Range(0f, 2f)] public float soot = 0f;
        [Tooltip("Surges per loop frozen into each puff at birth; 0 = a steady jet.")]
        [Range(0, 6)] public int pulseN = 0;
        [Tooltip("Depth of the surges: amp × clip(1 + depth·cos(2π·pulse_n·birth), 0.05, 2.5).")]
        [Range(0f, 1f)] public float pulseDepth = 0f;
        [Tooltip("Degrees the aim swings either side, frozen into each puff at birth — a swept stream CURVES because its tail still points where the nozzle was.")]
        [Range(0f, 90f)] public float sweep = 0f;
        [Tooltip("Sweeps per loop (integer, so the loop stays exact).")]
        [Range(1, 4)] public int sweepN = 1;
        [Tooltip("Shock diamonds: standing bright nodes down the axis, this many per reach; the gas travels through them. 0 = none.")]
        [Range(0f, 8f)] public float shockN = 0f;
        [Tooltip("Depth of the shock modulation: amp × (1 + depth·cos(2π·shock_n·d/reach)).")]
        [Range(0f, 1f)] public float shockDepth = 0f;

        // ── the root ──
        [Tooltip("Radius of the dense hot lump at the nozzle, source px; 0 = none. It is what makes the stream read as THROWN from a source rather than drifting.")]
        [Range(0f, 10f)] public float rootR = 0f;
        [Tooltip("Heat of the root lump.")]
        [Range(0f, 4f)] public float rootAmp = 1.5f;

        // ── things that leave the stream ──
        [Tooltip("Share of slots that detach: extra lateral throw, sideways drift, 0.72 amplitude and a longer life — fireballs tumbling off the end.")]
        [Range(0f, 1f)] public float shed = 0f;
        [Tooltip("A shed puff's extra angular throw (× its own cone angle) and drift scale.")]
        [Range(0f, 3f)] public float shedKick = 1.6f;
        [Tooltip("A shed puff's life as a multiple of Life.")]
        [Range(0.5f, 3f)] public float shedLife = 1.5f;
        [Tooltip("Tiny fast bright motes torn off the stream (own stream seed·104729 + 77).")]
        [Range(0, 40)] public int sparks = 0;
        [Tooltip("Spark radius, source px (never one pixel: 1.4 rasterises to a 9 px lump, the floor the reference sheets keep).")]
        [Range(0.5f, 4f)] public float sparkR = 1.5f;

        // ── vortex rings ──
        [Tooltip("Vortex rings shed per loop, seen from the side as flattened O's travelling away; 0 = none.")]
        [Range(0, 6)] public int ringN = 0;
        [Tooltip("Minimum puffs per ring; the count grows with the circumference so the ring stays closed.")]
        [Range(3, 48)] public int ringK = 8;
        [Tooltip("Ring radius at birth, source px.")]
        [Range(0.5f, 10f)] public float ringR0 = 2f;
        [Tooltip("Ring radius gained per px travelled.")]
        [Range(0f, 0.5f)] public float ringGrow = 0.16f;
        [Tooltip("Ring life as a multiple of Life — a ring has to OUTLIVE the stream to get clear of it.")]
        [Range(0.5f, 4f)] public float ringLife = 1.9f;
        [Tooltip("Ring travel as a multiple of Reach — and OUTRUN it.")]
        [Range(0.5f, 3f)] public float ringReach = 1.45f;
        [Tooltip("Ring heat relative to Strength (rings cool slower than the stream: (1 − s)^(0.3·cool)).")]
        [Range(0f, 3f)] public float ringAmp = 0.95f;

        // ── turbulence ──
        [Tooltip("Domain-warp amplitude at the nozzle, source px (a jet is laminar while it is fast).")]
        [Range(0f, 4f)] public float warp0 = 0.6f;
        [Tooltip("Warp amplitude added by the end of the reach, source px (it breaks up once it has slowed).")]
        [Range(0f, 12f)] public float warp1 = 5.5f;
        [Tooltip("Source px per noise lattice cell — the texture scale. The downstream scroll is snapped to whole lattice periods per loop so the loop stays exact.")]
        [Range(2f, 24f)] public float warpCell = 9f;
        [Tooltip("Noise octaves (lacunarity 2, gain 0.5).")]
        [Range(1, 3)] public int warpOct = 2;

        // ── look ──
        [Tooltip("Field value at the silhouette's outer edge (lit = H·gain > lo).")]
        [Range(0f, 1f)] public float lo = 0.20f;
        [Tooltip("Field value at which the ramp tops out. FITTED per draw by the source's tune2 solver against two style targets (average ramp position, share of lit area in the top tenth) — not a guess; the shipped values are the contract's.")]
        [Range(0.3f, 8f)] public float hi = 1.20f;
        [Tooltip("Bends where the gradient is spent: t^curve, < 1 pushes area up the ramp (a hotter, fully developed flame). FITTED together with Hi.")]
        [Range(0.2f, 2f)] public float curve = 1f;
        [Tooltip("Shades in the ramp: 0 = continuous; N quantises the ramp coordinate to N shades (floor, so the darkest shade reaches the edge). Never quantises the alpha.")]
        [Range(0, 64)] public int steps = 0;
        [Tooltip("Width of the edge falloff in field units: alpha = smoothstep((H − lo)/soft) × the ramp's opacity ceiling.")]
        [Range(0.01f, 1f)] public float soft = 0.55f;
        [Tooltip("Multiplier on the heat before exposure.")]
        [Range(0.1f, 3f)] public float gain = 1f;

        [Tooltip("The heat ramp: pos 0 = the cold outer edge, pos 1 = the hottest core; each stop's alpha is its OPACITY CEILING. Interpolated in linear light through a 1024-entry table.")]
        public PlusRamp ramp = PlusRampPresets.Ember();
        [Tooltip("The second ramp the gas crosses into as its soot tint rises (gout / sputter: greasy soot; whip: a HOT orange — the head of the swung stream). Empty = no crossfade.")]
        public PlusRamp sootRamp = new PlusRamp();
        [Tooltip("Tint (T/H) at which the crossfade into the second ramp starts — a WIDTH with Soot Hi, not a threshold.")]
        [Range(0f, 1f)] public float sootLo = 0.35f;
        [Tooltip("Tint at which the crossfade is complete.")]
        [Range(0f, 1f)] public float sootHi = 0.95f;

        public bool HasSoot => sootRamp != null && !sootRamp.IsEmpty;
    }

    /// One jet to accumulate: where the source frame lands on the canvas. canvas(y-down) = anchor + u·R(rot)·(src − nozzle);
    /// `amp` multiplies every blob (swarm brightness), `seed` is the Kiln seed of this instance's streams.
    public struct JetFrame
    {
        public int W, H;                 // canvas size
        public double ax, ay;            // the nozzle on the canvas, px, y-DOWN
        public double u;                 // canvas px per source px
        public double rot;               // extra rotation of the whole jet in the y-down frame, radians
        public double amp;
        public int seed;
        public double NozzleX, NozzleY;  // the nozzle in source px, set by the program from the settings before a frame

        public static JetFrame Solo(int W, int H, double ax, double ay, double u, int seed) =>
            new JetFrame { W = W, H = H, ax = ax, ay = ay, u = u, rot = 0, amp = 1, seed = seed };
    }

    /// Per-form scratch: the accumulated planes and the warped sample coordinates of the current instance. Owned by one
    /// form instance (workers render on clones), so no locking.
    public sealed class JetScratch
    {
        public float[] H, T;             // accumulated heat / soot, canvas size
        public double[] sx, sy;          // the current instance's WARPED source-frame sample coords per canvas pixel
        public bool[] inside;            // the pixel samples inside the instance's frame rect
        public int bx0, by0, bx1, by1;   // the instance's canvas bbox (exclusive max)
        public double warpMax;
        readonly Dictionary<long, float[]> _lattices = new Dictionary<long, float[]>();

        public void Ensure(int n)
        {
            if (H != null && H.Length == n) return;
            H = new float[n]; T = new float[n]; sx = new double[n]; sy = new double[n]; inside = new bool[n];
        }

        public void Clear() { Array.Clear(H, 0, H.Length); Array.Clear(T, 0, T.Length); }

        /// numpy `default_rng(seed).uniform(−1, 1, (pw, pv, pu)).astype(float32)`, C order — cached per seed and shape.
        public float[] Lattice(int seed, int pu, int pv, int pw)
        {
            long key = ((long)seed << 24) ^ ((long)pu << 16) ^ ((long)pv << 8) ^ pw;
            if (_lattices.TryGetValue(key, out var a)) return a;
            var rng = new PlusNumpyRng(unchecked((uint)seed));
            a = new float[pu * pv * pw];
            for (int i = 0; i < a.Length; i++) a[i] = (float)(-1.0 + 2.0 * rng.NextDouble());
            if (_lattices.Count > 64) _lattices.Clear();
            _lattices[key] = a;
            return a;
        }
    }

    /// The slot table: per-slot constants, a pure function of the seed and the emission dials (jet.py `_slot_table`).
    public sealed class JetSlots
    {
        public double[] phase, da, vs, rs, amp, ls, drift;
        public bool[] shed;
        public int N => phase.Length;
    }

    /// flame3/noise.py `value_noise_3d`: fractal value noise on a WRAPPING integer lattice, quintic fade, exactly periodic
    /// in every axis with the given cell counts (the loop depends on it). Coordinates in lattice cells.
    public static class JetNoise
    {
        public static double Sample(JetScratch sc, int seed, double u, double v, double w, int pu, int pv, int pw, int octaves)
        {
            double result = 0.0, total = 0.0;
            for (int k = 0; k < octaves; k++)
            {
                int scale = 1 << k;
                int Pu = pu * scale, Pv = pv * scale, Pw = pw * scale;
                var lat = sc.Lattice(seed * 1000 + k, Pu, Pv, Pw);
                double uk = u * scale, vk = v * scale, wk = w * scale;
                double fu0 = Math.Floor(uk), fv0 = Math.Floor(vk), fw0 = Math.Floor(wk);
                long iu = (long)fu0, iv = (long)fv0, iw = (long)fw0;
                double fu = uk - fu0, fv = vk - fv0, fw = wk - fw0;
                double gu = fu * fu * fu * (fu * (fu * 6 - 15) + 10);
                double gv = fv * fv * fv * (fv * (fv * 6 - 15) + 10);
                double gw = fw * fw * fw * (fw * (fw * 6 - 15) + 10);
                int u0 = Mod(iu, Pu), u1 = Mod(iu + 1, Pu), v0 = Mod(iv, Pv), v1 = Mod(iv + 1, Pv), w0 = Mod(iw, Pw), w1 = Mod(iw + 1, Pw);
                int s0 = w0 * Pv * Pu, s1 = w1 * Pv * Pu;
                double c000 = lat[s0 + v0 * Pu + u0], c001 = lat[s0 + v0 * Pu + u1];
                double c010 = lat[s0 + v1 * Pu + u0], c011 = lat[s0 + v1 * Pu + u1];
                double c100 = lat[s1 + v0 * Pu + u0], c101 = lat[s1 + v0 * Pu + u1];
                double c110 = lat[s1 + v1 * Pu + u0], c111 = lat[s1 + v1 * Pu + u1];
                double c00 = c000 * (1 - gu) + c001 * gu, c01 = c010 * (1 - gu) + c011 * gu;
                double c10 = c100 * (1 - gu) + c101 * gu, c11 = c110 * (1 - gu) + c111 * gu;
                double c0 = c00 * (1 - gv) + c01 * gv, c1 = c10 * (1 - gv) + c11 * gv;
                double g = Math.Pow(0.5, k);
                result += (c0 * (1 - gw) + c1 * gw) * g;
                total += g;
            }
            return result / total;
        }

        static int Mod(long a, int m) { long r = a % m; return (int)(r < 0 ? r + m : r); }
    }

    /// field.py `HeatField`: the blob kernel over the warped sample grid of the current instance.
    public static class JetField
    {
        /// One anisotropic soft lump (k = clip(1 − u² − v²)², rx along `ang`, ry across), blitted over its bbox only;
        /// source-frame coordinates. `tint` feeds the soot plane.
        public static void Blob(JetScratch sc, in JetFrame fr, double cx, double cy, double rx, double ry, double ang, double amp, double tint = 0.0)
        {
            amp *= fr.amp;
            if (amp <= 0.0 || rx <= 0.0 || ry <= 0.0) return;
            double reach = Math.Max(rx, ry) + sc.warpMax + 1.0;
            // the source bbox → canvas envelope
            Bounds(in fr, cx - reach, cy - reach, cx + reach, cy + reach, out int x0, out int y0, out int x1, out int y1);
            x0 = Math.Max(x0, sc.bx0); y0 = Math.Max(y0, sc.by0); x1 = Math.Min(x1, sc.bx1); y1 = Math.Min(y1, sc.by1);
            if (x1 <= x0 || y1 <= y0) return;
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            double irx = 1.0 / rx, iry = 1.0 / ry;
            int W = fr.W;
            for (int y = y0; y < y1; y++)
            {
                int row = y * W;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x;
                    if (!sc.inside[i]) continue;
                    double dx = sc.sx[i] - cx, dy = sc.sy[i] - cy;
                    double u = (dx * ca + dy * sa) * irx;
                    double v = (-dx * sa + dy * ca) * iry;
                    double k = 1.0 - (u * u + v * v);
                    if (k <= 0.0) continue;
                    if (k > 1.0) k = 1.0;
                    k *= k;
                    k *= amp;
                    sc.H[i] += (float)k;
                    if (tint != 0.0) sc.T[i] += (float)(k * tint);
                }
            }
        }

        /// Canvas px envelope (y-down, exclusive max) of a source-frame rect through the instance transform.
        public static void Bounds(in JetFrame fr, double sx0, double sy0, double sx1, double sy1, out int x0, out int y0, out int x1, out int y1)
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            double c = Math.Cos(fr.rot), s = Math.Sin(fr.rot);
            for (int k = 0; k < 4; k++)
            {
                double sx = (k & 1) == 0 ? sx0 : sx1, sy = (k & 2) == 0 ? sy0 : sy1;
                double dx = (sx - fr.NozzleX) * fr.u, dy = (sy - fr.NozzleY) * fr.u;
                double cx = fr.ax + dx * c - dy * s, cy = fr.ay + dx * s + dy * c;
                if (cx < minx) minx = cx; if (cx > maxx) maxx = cx; if (cy < miny) miny = cy; if (cy > maxy) maxy = cy;
            }
            x0 = Math.Max(0, (int)Math.Floor(minx)); y0 = Math.Max(0, (int)Math.Floor(miny));
            x1 = Math.Min(fr.W, (int)Math.Floor(maxx) + 1); y1 = Math.Min(fr.H, (int)Math.Floor(maxy) + 1);
        }
    }

    /// The stage pipeline (jet.py `frame`): Warp → Root → Emit → Rings → Sparks, every stage virtual (see the hand-off
    /// note in the file header). Stateless — one shared instance serves every thread; all scratch is the caller's.
    public class JetProgram
    {
        public static readonly JetProgram Default = new JetProgram();
        public const double TAU = 2.0 * Math.PI;

        /// Accumulate one jet instance into `sc.H` / `sc.T` at loop phase `phase` (0..1).
        public virtual void Frame(JetSettings s, JetFrame fr, double phase, JetScratch sc)
        {
            fr.NozzleX = s.nozzleX * s.w; fr.NozzleY = s.nozzleY * s.h;
            if (!BeginInstance(s, in fr, sc)) return;
            var slots = BuildSlots(s, fr.seed);
            Warp(s, in fr, phase, sc);
            Root(s, in fr, phase, sc);
            Emit(s, in fr, slots, phase, sc);
            Rings(s, in fr, phase, sc);
            Sparks(s, in fr, phase, sc);
        }

        /// Map every canvas pixel of the instance's envelope back into the source frame (unwarped for now) and mark the
        /// ones inside it. False when the frame misses the canvas entirely.
        protected bool BeginInstance(JetSettings s, in JetFrame fr, JetScratch sc)
        {
            JetField.Bounds(in fr, 0, 0, s.w, s.h, out sc.bx0, out sc.by0, out sc.bx1, out sc.by1);
            if (sc.bx1 <= sc.bx0 || sc.by1 <= sc.by0) return false;
            double c = Math.Cos(fr.rot), sn = Math.Sin(fr.rot), iu = 1.0 / fr.u;
            for (int y = sc.by0; y < sc.by1; y++)
                for (int x = sc.bx0; x < sc.bx1; x++)
                {
                    int i = y * fr.W + x;
                    double dx = x - fr.ax, dy = y - fr.ay;
                    // inverse rotation, then back to source px from the nozzle
                    double px = (dx * c + dy * sn) * iu + fr.NozzleX;
                    double py = (-dx * sn + dy * c) * iu + fr.NozzleY;
                    sc.sx[i] = px; sc.sy[i] = py;
                    // the source samples its integer grid [0, w) × [0, h); round-to-nearest keeps the letterboxed u = 1
                    // case exact against the 1e−15 of the transform
                    sc.inside[i] = px > -0.5 && px < s.w - 0.5 && py > -0.5 && py < s.h - 0.5;
                }
            return true;
        }

        // ── stage: the slot table ──
        public virtual JetSlots BuildSlots(JetSettings s, int seed)
        {
            var rng = new PlusNumpyRng(unchecked((uint)(seed * 7919 + 13)));
            int n = s.slots;
            var t = new JetSlots
            {
                phase = new double[n], da = new double[n], vs = new double[n], rs = new double[n], amp = new double[n], ls = new double[n], drift = new double[n], shed = new bool[n],
            };
            double j = s.jitter, spread = s.spread * Math.PI / 180.0;
            for (int i = 0; i < n; i++) t.phase[i] = i / (double)n;
            for (int i = 0; i < n; i++) { double a = rng.Uniform(-1.0, 1.0); t.da[i] = Math.Sign(a) * Math.Pow(Math.Abs(a), 1.7) * spread; }
            for (int i = 0; i < n; i++) t.vs[i] = 1.0 + j * rng.Uniform(-0.42, 0.42);
            for (int i = 0; i < n; i++) t.rs[i] = 1.0 + j * rng.Uniform(-0.34, 0.50);
            for (int i = 0; i < n; i++) t.amp[i] = 1.0 + j * rng.Uniform(-0.30, 0.30);
            for (int i = 0; i < n; i++) t.ls[i] = 1.0 + j * rng.Uniform(-0.25, 0.25);
            for (int i = 0; i < n; i++) t.drift[i] = rng.Uniform(-1.0, 1.0);
            for (int i = 0; i < n; i++) t.shed[i] = rng.NextDouble() < s.shed;
            return t;
        }

        /// Smoothstep in over the first `k` of life, so a puff grows into existence instead of popping on.
        public static double Fade(double x, double k = 0.06)
        {
            double t = x / k; if (t < 0) t = 0; else if (t > 1) t = 1;
            return t * t * (3.0 - 2.0 * t);
        }

        // ── stage: the domain warp (cartesian, scrolled downstream along the aim) ──
        public virtual void Warp(JetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            const int pu = 24, pv = 12, pw = 4;
            double cell = s.warpCell;
            double reachPx = s.reach * s.w;
            double pxPerLoop = reachPx / Math.Max(s.life, 0.05);
            double a = s.aim * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
            // both axes snapped to whole lattice periods per loop, separately — it is what makes the wrap exact
            int ku = Math.Max(1, RoundHalfEven(pxPerLoop * ca / (pu * cell)));
            int kv = RoundHalfEven(pxPerLoop * sa / (pv * cell));
            double offU = phase * ku * pu, offV = phase * kv * pv;
            double ww = (float)(phase * pw);
            double nx = fr.NozzleX, ny = fr.NozzleY, invReach = 1.0 / Math.Max(reachPx, 1.0);
            double warpMax = 0.0;
            int W = fr.W;
            for (int y = sc.by0; y < sc.by1; y++)
                for (int x = sc.bx0; x < sc.bx1; x++)
                {
                    int i = y * W + x;
                    if (!sc.inside[i]) continue;
                    double px = sc.sx[i], py = sc.sy[i];
                    double u = px / cell - offU, v = py / cell - offV;
                    double dx = JetNoise.Sample(sc, fr.seed, u, v, ww, pu, pv, pw, s.warpOct);
                    double dy = JetNoise.Sample(sc, fr.seed + 4409, u, v, ww, pu, pv, pw, s.warpOct);
                    double along = ((px - nx) * ca + (py - ny) * sa) * invReach;
                    if (along < 0) along = 0; else if (along > 1.35) along = 1.35;
                    double amp = s.warp0 + s.warp1 * along;
                    dx *= amp; dy *= amp;
                    sc.sx[i] = px + dx; sc.sy[i] = py + dy;
                    double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (m > warpMax) warpMax = m;
                }
            sc.warpMax = warpMax;
        }

        // ── stage: the root lump ──
        public virtual void Root(JetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            if (s.rootR <= 0) return;
            double b = 1.0 + 0.14 * Math.Sin(TAU * 3.0 * phase);
            double a = s.aim * Math.PI / 180.0;
            JetField.Blob(sc, in fr, fr.NozzleX, fr.NozzleY, s.rootR * 1.7 * b, s.rootR * b, a, s.rootAmp);
        }

        // ── stage: every live puff at its own age ──
        public virtual void Emit(JetSettings s, in JetFrame fr, JetSlots tab, double phase, JetScratch sc)
        {
            int n = tab.N;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w;
            double aim0 = s.aim * Math.PI / 180.0, sweep = s.sweep * Math.PI / 180.0;
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            for (int i = 0; i < n; i++)
            {
                double life = s.life * tab.ls[i] * (tab.shed[i] ? s.shedLife : 1.0);
                double age = Mod1(phase - tab.phase[i]);
                double sAge = age / Math.Max(life, 1e-3);
                if (sAge >= 1.0) continue;
                double ep = tab.phase[i];
                double aim = s.sweep != 0f ? aim0 + sweep * Math.Sin(TAU * s.sweepN * ep) : aim0;
                double pulse = 1.0;
                if (s.pulseN != 0 && s.pulseDepth != 0f)
                {
                    pulse = 1.0 + s.pulseDepth * Math.Cos(TAU * s.pulseN * ep);
                    if (pulse < 0.05) pulse = 0.05; else if (pulse > 2.5) pulse = 2.5;
                }
                double d = reachPx * tab.vs[i] * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double theta = aim + tab.da[i] + (tab.shed[i] ? tab.da[i] * s.shedKick : 0.0);
                double px = nx + d * Math.Cos(theta);
                double py = ny + d * Math.Sin(theta);
                py += s.grav * reachPx * sAge * sAge - s.buoy * reachPx * Math.Pow(sAge, 2.4);
                if (tab.shed[i]) py += tab.drift[i] * s.shedKick * 3.0 * sAge * sAge;
                double r = s.r0 * tab.rs[i] + s.growth * d;
                double aspect = 1.0 + s.elong * Math.Exp(-sAge / Math.Max(s.roundAt, 0.02));
                double amp = s.strength * tab.amp[i] * pulse * Fade(sAge) * Math.Pow(Math.Max(1.0 - sAge, 0.0), s.cool);
                if (s.shockN != 0f && s.shockDepth != 0f)
                {
                    amp *= 1.0 + s.shockDepth * Math.Cos(TAU * s.shockN * d / Math.Max(reachPx, 1.0));
                    if (amp < 0) amp = 0;
                }
                if (tab.shed[i]) amp *= 0.72;
                double tint = s.soot != 0f ? Math.Min(Math.Max(s.soot * sAge, 0.0), 1.0) : 0.0;
                JetField.Blob(sc, in fr, px, py, r * aspect, r, theta, amp, tint);
            }
        }

        // ── stage: vortex rings seen from the side ──
        public virtual void Rings(JetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            if (s.ringN <= 0) return;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w;
            double a = s.aim * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            for (int m = 0; m < s.ringN; m++)
            {
                double sAge = Mod1(phase - m / (double)s.ringN) / Math.Max(s.life * s.ringLife, 1e-3);
                if (sAge >= 1.0) continue;
                double d = reachPx * s.ringReach * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double rr = s.ringR0 + s.ringGrow * d;
                double amp0 = s.strength * s.ringAmp * Fade(sAge, 0.10) * Math.Pow(Math.Max(0.0, 1.0 - sAge), s.cool * 0.30);
                double pr = s.r0 * 0.85 + s.growth * d * 0.34;
                int k = (int)Math.Max(s.ringK, Math.Min(48, RoundHalfEven(TAU * rr / Math.Max(pr * 1.05, 1e-3))));
                double rot = 1.31 * m;
                double wob = 0.17 + 0.05 * ((m * 7) % 3);
                double rise = s.buoy * reachPx * Math.Pow(sAge, 2.4);
                for (int j = 0; j < k; j++)
                {
                    double th = TAU * j / k;
                    double rj = rr * (1.0 + wob * (Math.Cos(2 * th + rot) * 0.6 + Math.Cos(3 * th - 1.7 * rot) * 0.4));
                    double perp = Math.Cos(th) * rj, along = Math.Sin(th) * rj * 0.35;
                    double x = nx + (d + along) * ca - perp * sa;
                    double y = ny + (d + along) * sa + perp * ca - rise;
                    double depth = 0.62 + 0.38 * Math.Cos(th);
                    JetField.Blob(sc, in fr, x, y, pr * 1.25, pr, a, amp0 * depth);
                }
            }
        }

        // ── stage: sparks ──
        public virtual void Sparks(JetSettings s, in JetFrame fr, double phase, JetScratch sc)
        {
            if (s.sparks <= 0) return;
            var rng = new PlusNumpyRng(unchecked((uint)(fr.seed * 104729 + 77)));
            int n = s.sparks;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w;
            double a = s.aim * Math.PI / 180.0;
            var ph = new double[n]; var da = new double[n]; var vs = new double[n]; var rise = new double[n];
            double spreadR = s.spread * 1.7 * Math.PI / 180.0;
            for (int i = 0; i < n; i++) ph[i] = rng.NextDouble();
            for (int i = 0; i < n; i++) da[i] = rng.Uniform(-1.0, 1.0) * spreadR;
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.55, 1.45);
            for (int i = 0; i < n; i++) rise[i] = rng.Uniform(0.10, 0.55);
            for (int i = 0; i < n; i++)
            {
                double sAge = Mod1(phase - ph[i]) / Math.Max(s.life * 1.1, 1e-3);
                if (sAge >= 1.0) continue;
                double d = reachPx * vs[i] * sAge;
                double th = a + da[i];
                double x = nx + d * Math.Cos(th);
                double y = ny + d * Math.Sin(th) - rise[i] * reachPx * sAge * sAge;
                JetField.Blob(sc, in fr, x, y, s.sparkR * 1.4, s.sparkR, th, 1.5 * Fade(sAge, 0.08) * Math.Pow(1.0 - sAge, 1.2));
            }
        }

        // ── helpers the forks need (unused by the base; tested) ──

        /// numpy `round()` — half to even — as an int.
        public static int RoundHalfEven(double x) => (int)Math.Round(x, MidpointRounding.ToEven);

        /// Python's `x % 1.0` (never negative).
        public static double Mod1(double x) { double r = x - Math.Floor(x); return r >= 1.0 ? r - 1.0 : r; }

        /// The explosive fork's `_lead`: each value's RANK in a stable ascending sort, scaled to 0..1 (0 = slowest,
        /// 1 = fastest). Rank-based on purpose — a linear remap of a skewed distribution compresses the range.
        public static double[] LeadRank(double[] vs)
        {
            int n = vs.Length;
            var r = new double[n];
            if (n < 2) return r;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, (p, q) => { int c = vs[p].CompareTo(vs[q]); return c != 0 ? c : p.CompareTo(q); });
            for (int k = 0; k < n; k++) r[idx[k]] = k / (n - 1.0);
            return r;
        }

        /// The forks' stratified angle base for wide arcs: `(i + 0.5)/n·2 − 1 + uniform(−0.5, 0.5)·2/n`, then
        /// `rng.permutation` — `n` uniform draws followed by a Fisher-Yates shuffle on the same stream, as numpy does it.
        public static double[] StratifiedPermuted(PlusNumpyRng rng, int n)
        {
            var b = new double[n];
            for (int i = 0; i < n; i++) b[i] = (i + 0.5) / n * 2.0 - 1.0;
            for (int i = 0; i < n; i++) b[i] += rng.Uniform(-0.5, 0.5) * (2.0 / n);
            rng.Shuffle(b);
            return b;
        }
    }

    /// grad.py + lut.py: the ONE place a field becomes pixels.
    public class JetShade
    {
        public const int N_LUT = 1024;
        public static readonly JetShade Default = new JetShade();

        /// A baked ramp: linear-light RGB per entry (decoded from the uint8 sRGB the source stores — `build_lut` rounds
        /// to bytes, then `Ramp.luts` decodes them) and the opacity ceiling.
        public sealed class Lut
        {
            public readonly double[] r = new double[N_LUT], g = new double[N_LUT], b = new double[N_LUT], a = new double[N_LUT];
            public readonly byte[] sr = new byte[N_LUT], sg = new byte[N_LUT], sb = new byte[N_LUT];
        }

        public static double SrgbToLinear(double c) => c > 0.04045 ? Math.Pow((Math.Abs(c) + 0.055) / 1.055, 2.4) : c / 12.92;
        public static double LinearToSrgb(double c) => c > 0.0031308 ? 1.055 * Math.Pow(Math.Abs(c), 1.0 / 2.4) - 0.055 : 12.92 * c;

        /// lut.build_lut: stops (pos, sRGB bytes, alpha) → N entries at i/(N−1), linear interpolation of the LINEARISED
        /// colours (np.interp: clamps to the end stops, a repeated position is a hard step), re-encoded, +0.5 truncated
        /// to uint8; alpha interpolated as-is (coverage is already linear).
        public static Lut Bake(PlusRamp ramp)
        {
            var lut = new Lut();
            var stops = ramp?.stops;
            int m = stops?.Count ?? 0;
            if (m == 0) { for (int i = 0; i < N_LUT; i++) { lut.r[i] = lut.g[i] = lut.b[i] = 1; lut.a[i] = 1; lut.sr[i] = lut.sg[i] = lut.sb[i] = 255; } return lut; }
            var sorted = new List<PlusRampStop>(stops);
            sorted.Sort((p, q) => p.pos.CompareTo(q.pos));   // stable: equal positions keep authoring order (a step)
            var pos = new double[m]; var lr = new double[m]; var lg = new double[m]; var lb = new double[m]; var al = new double[m];
            for (int i = 0; i < m; i++)
            {
                var st = sorted[i]; pos[i] = st.pos;
                lr[i] = SrgbToLinear(Byte(st.color.r) / 255.0); lg[i] = SrgbToLinear(Byte(st.color.g) / 255.0); lb[i] = SrgbToLinear(Byte(st.color.b) / 255.0);
                al[i] = st.color.a;
            }
            double step = 1.0 / (N_LUT - 1);
            for (int i = 0; i < N_LUT; i++)
            {
                double x = i * step;
                double R = Interp(x, pos, lr), G = Interp(x, pos, lg), B = Interp(x, pos, lb), A = Interp(x, pos, al);
                lut.sr[i] = ToByte(LinearToSrgb(R)); lut.sg[i] = ToByte(LinearToSrgb(G)); lut.sb[i] = ToByte(LinearToSrgb(B));
                // Ramp.luts decodes `rgb.astype(float32) / 255.0` — a float32 quotient — so the blend input is that value exactly
                // (the explicit (float) cast forces the float32 rounding C# may otherwise skip)
                lut.r[i] = SrgbToLinear((float)(lut.sr[i] / 255f)); lut.g[i] = SrgbToLinear((float)(lut.sg[i] / 255f)); lut.b[i] = SrgbToLinear((float)(lut.sb[i] / 255f));
                lut.a[i] = (float)A;
            }
            return lut;
        }

        static int Byte(float c) => Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);
        static byte ToByte(double srgb) { double v = (srgb < 0 ? 0 : srgb > 1 ? 1 : srgb) * 255.0 + 0.5; return (byte)(v > 255 ? 255 : v); }

        /// np.interp on sorted xp (clamped ends; a zero-width segment is a step).
        static double Interp(double x, double[] xp, double[] fp)
        {
            int n = xp.Length;
            if (x <= xp[0]) return fp[0];
            if (x >= xp[n - 1]) return fp[n - 1];
            int j = 0;
            while (j < n - 2 && x >= xp[j + 1]) j++;
            double dx = xp[j + 1] - xp[j];
            if (dx <= 0) return fp[j + 1];
            return fp[j] + (x - xp[j]) * (fp[j + 1] - fp[j]) / dx;
        }

        /// The explosive fork's `opaq`: an exponent on the ramp's opacity ceiling (1 = the gen-2 contract).
        protected virtual double CeilingExponent(JetSettings s) => 1.0;

        /// grad.shade over the canvas: H / T y-DOWN planes → `target` y-UP Color32 (straight alpha), then despeckle.
        /// `rampT` (optional) receives the ramp coordinate before `steps` (the contract's `ramp_t`).
        public void Shade(JetSettings s, Lut hot, Lut soot, float[] H, float[] T, int W, int Hh, Color32[] target, float[] rampT = null)
        {
            double lo = s.lo, hi = s.hi, curve = s.curve, invSpan = 1.0 / Math.Max(hi - lo, 1e-4), invSoft = 1.0 / Math.Max(s.soft, 1e-4);
            double gain = s.gain;
            bool quant = s.steps > 0; int steps = s.steps;
            bool useSoot = soot != null && s.HasSoot;
            double sLo = s.sootLo, invSootSpan = 1.0 / Math.Max(s.sootHi - s.sootLo, 1e-4);
            double opaq = CeilingExponent(s);
            int n = W * Hh;
            for (int i = 0; i < n; i++)
            {
                int y = i / W, x = i - y * W;
                int o = (Hh - 1 - y) * W + x;
                double h = gain == 1.0 ? H[i] : H[i] * gain;
                if (rampT != null) rampT[o] = 0f;
                if (!(h > lo)) { target[o] = default; continue; }
                double t = (h - lo) * invSpan;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                if (curve != 1.0) t = Math.Pow(t, curve);
                if (rampT != null) rampT[o] = (float)t;
                if (quant) t = Math.Min(Math.Floor(t * steps), steps - 1) / (steps - 1);
                int idx = (int)(t * (N_LUT - 1) + 0.5); if (idx > N_LUT - 1) idx = N_LUT - 1;
                double lr = hot.r[idx], lg = hot.g[idx], lb = hot.b[idx], ac = hot.a[idx];
                if (useSoot)
                {
                    double tint = T[i] / (h + 1e-6);
                    double w = (tint - sLo) * invSootSpan;
                    if (w < 0) w = 0; else if (w > 1) w = 1;
                    w = w * w * (3.0 - 2.0 * w);
                    if (w > 0)
                    {
                        lr = lr * (1.0 - w) + soot.r[idx] * w; lg = lg * (1.0 - w) + soot.g[idx] * w; lb = lb * (1.0 - w) + soot.b[idx] * w;
                        ac = ac * (1.0 - w) + soot.a[idx] * w;
                    }
                }
                if (opaq != 1.0) ac = Math.Pow(ac, opaq);
                double e = (h - lo) * invSoft; if (e < 0) e = 0; else if (e > 1) e = 1;
                e = e * e * (3.0 - 2.0 * e);
                byte a = ToByte255(e * ac);
                if (a == 0) { target[o] = default; continue; }
                target[o] = new Color32(ToByte(LinearToSrgb(Clamp01(lr))), ToByte(LinearToSrgb(Clamp01(lg))), ToByte(LinearToSrgb(Clamp01(lb))), a);
            }
            Despeckle(target, W, Hh, 70);
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static byte ToByte255(double v) { double q = v * 255.0 + 0.5; return (byte)(q < 0 ? 0 : q > 255 ? 255 : q); }

        /// grad._despeckle: drop lit pixels that are faint (alpha < below) AND isolated (fewer than 2 lit 4-neighbours).
        /// Counted against the state before any kill, as the source does.
        public static void Despeckle(Color32[] buf, int W, int H, int below)
        {
            int n = W * H;
            var lit = new bool[n];
            bool any = false;
            for (int i = 0; i < n; i++) { lit[i] = buf[i].a > 0; any |= lit[i]; }
            if (!any) return;
            var kill = new List<int>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (!lit[i] || buf[i].a >= below) continue;
                    int cnt = 0;
                    if (y > 0 && lit[i - W]) cnt++;
                    if (y < H - 1 && lit[i + W]) cnt++;
                    if (x > 0 && lit[i - 1]) cnt++;
                    if (x < W - 1 && lit[i + 1]) cnt++;
                    if (cnt < 2) kill.Add(i);
                }
            foreach (int i in kill) buf[i] = default;
        }
    }
}
