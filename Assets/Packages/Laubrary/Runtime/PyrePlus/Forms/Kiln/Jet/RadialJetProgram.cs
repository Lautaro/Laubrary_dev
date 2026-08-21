// RadialJetProgram — Kiln "Flame / agent3_fork_radial" generation 3 ("THE DIRECTION IS GONE"): the jet engine's puff
// physics untouched, the DISTRIBUTION OF DIRECTIONS rewritten — a 0–180° half-angle arc (180 = a full disc), an angle
// distribution with a `bias` power (1.0 = uniform across the arc; the base's fixed 1.7 would pile a disc back into a
// beam), stratified + permuted base angles for wide arcs (no bald patches, no rotating arm), a POLAR domain warp that
// scrolls OUTWARD in every direction (the base's downstream scroll combs a disc), and the three ways a disc stops being
// a disc: `swirl` (a puff carried around the source as it travels — a spiral, not a spoke), `spin` (whole turns per
// loop of the emission pattern), `lobes` (the sheet gathered into N tongues by the crossing-free remap φ − depth·sin φ).
// Plus a burner RING (`src_r` birth radius, `root_k` source lumps round it — the middle stays dark) and face-on rings
// (`ring_flat`: an expanding circle in the picture plane, puffs elongated along the tangent).
//
// Source: D:/CODEZ/Kiln/projects/Flame/agents/agent3_fork_radial/flame3/jet.py (`diff -r` against agent3's flame3:
// only jet.py differs — field / grad / lut / noise are byte-identical, so `JetField`, `JetNoise` and `JetShade` are
// reused as they are). Every fork difference lands inside a stage the base already made virtual; the stages below are
// whole-stage overrides where the fork rewrote the stage's arithmetic, and `Rings` delegates its edge-on branch to the
// base because the fork kept that branch verbatim. The base engine is not modified by this fork.
//
// Component list (contract components.json), status in this port:
//   warp_out   PORTED   polar value noise (pu, pv, pw) = (24, 16, 4): u = θ/2π·pu (one turn = one period, no seam at
//                       θ = π), v = r/cell − phase·kv·pv (kv whole radial periods per loop), optional `warp_spin`,
//                       displacement in the radial / tangential basis, amplitude (warp0 + warp1·clip(r/reach, 0, 1.35))
//                       × clip(r/7, 0, 1) (faded at the angular singularity) — `Warp`.
//   root       PORTED   src_r > 0 && root_k > 0: root_k lumps round the birth circle, each 1.5 : 1 along the tangent,
//                       breathing 1 + 0.14·sin(6πφ) × (1 + 0.10·cos(3θ + 2πφ)); else one ROUND lump (1.7 : 1 only below
//                       spread 60) — `Root`.
//   emit       PORTED   bias power, stratified + permuted angles (spread ≥ 60), lobes remap, lobe_kick on vs, spin added
//                       to the birth aim, the room-bounded shed kick (dropped on a full circle), src_r birth radius, swirl
//                       θ + sw·s with orientation = the lead angle θp + atan2(r·sw, dr/ds) — `BuildSlots` + `Emit`.
//   rings      PORTED   ring_flat: radius = src_r + travel, cord pr = ring_r0 + ring_grow·d·0.42, count grown with the
//                       circumference (≤ 140), harmonics 3 / 5 of θ + rot, cool × 0.50, elongated along the tangent;
//                       ring_flat off = the base's edge-on rings — `Rings`.
//   sparks     PORTED   src_r offset, swirl, vs 0.55..1.22, angle clamp min(spread·1.7, spread + 15, 180) — `Sparks`.
//   shade      PORTED   the shared `JetShade` (identical grad.py / lut.py).
//   despeckle  PORTED   the shared `JetShade.Despeckle`.
//   Nothing approximated, nothing dropped. `lo` / `hi` / `curve` are the contract's FITTED values, frozen per draw.
//
// RNG: the same streams as the base (slot table seed·7919 + 13, sparks seed·104729 + 77, lattices seed·1000 + k);
// the stratified path draws n uniforms then a permutation on the SAME stream before vs / rs / as / ls / drift / shed,
// exactly as `_slot_table` does (`JetProgram.StratifiedPermuted`).
//
// HAND-OFF for Port 05 (the explosive fork) from this port's diff: it did not need any new seam on the base either —
// but note (a) `Emit`'s shed-kick differs between the gen-2 base and BOTH forks (the forks bound it by the room left in
// the arc), so the explosive fork must port ITS jet.py's `_emit`, not reuse the base's; (b) the fork's `Rings` kept the
// base's edge-on branch verbatim, so delegating the non-flat / non-arc branch to `base.Rings` is safe; (c) the radial
// fork's `Root` changed the lump's aspect for spread ≥ 60 — check the explosive `_root` for the same rule; (d) the
// explosive `Frame` inserts stages — override `Frame` and call the shared stages in order rather than re-implementing
// `BeginInstance`.
using System;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// The gen-3 `JetSpec`: the base's dials plus the ten radial keys. Field initialisers are the JetSpec CLASS defaults.
    [Serializable]
    public sealed class RadialJetSettings : JetSettings
    {
        // ── the arc ──
        [Tooltip("Angle-distribution power across the arc: 1 = uniform (a disc is actually filled); > 1 biases towards the aim (the base jet's fixed 1.7 gives a beam a spine). Wide arcs (Spread ≥ 60°) also draw their base angles STRATIFIED and permuted, so a thin corona has no bald patch and no rotating arm.")]
        [Range(0.5f, 3f)] public float bias = 1.7f;
        [Tooltip("Birth radius, canvas WIDTHS of the source frame: > 0 = the gas leaves a burner RING rather than a point, and the middle stays dark.")]
        [Range(0f, 0.3f)] public float srcR = 0f;

        // ── the three ways a disc stops being a disc ──
        [Tooltip("Degrees a puff is carried AROUND the source over its life — its track becomes a spiral and it is stretched along that track (a fire whirl). Applied at the puff's age, not to its birth angle.")]
        [Range(-360f, 360f)] public float swirl = 0f;
        [Tooltip("Whole turns per loop the emission pattern rotates (integer, so the loop stays exact); a puff carries the aim it was born under, so a spinning source trails spiral arms.")]
        [Range(-3, 3)] public int spin = 0;
        [Tooltip("Gather the arc into N tongues with real gaps between them (the birth angles are REDISTRIBUTED by φ − depth·sin φ, never resampled, so the sheet keeps its gas). 0 = an even sheet.")]
        [Range(0, 12)] public int lobes = 0;
        [Tooltip("How hard the tongues clump, 0..0.95 (the remap stays crossing-free below 1).")]
        [Range(0f, 0.95f)] public float lobeDepth = 0f;
        [Tooltip("Extra travel on a lobe axis against between them (speed × (1 − kick·(1 − cos-window))), so the tongues have length as well as density.")]
        [Range(0f, 1f)] public float lobeKick = 0f;

        // ── the root ──
        [Tooltip("With Src R > 0: the root is this many lumps laid round the birth circle (each stretched along the tangent); 0 = one lump at the centre.")]
        [Range(0, 48)] public int rootK = 0;

        // ── rings ──
        [Tooltip("Rings seen FACE ON — an expanding circle in the picture plane with its puffs elongated along the ring (a radial source's shockwave) instead of the base jet's edge-on flattened O.")]
        public bool ringFlat = false;

        // ── turbulence ──
        [Tooltip("Whole turns per loop the polar noise texture rotates around the source (integer, so the loop stays exact).")]
        [Range(-3, 3)] public int warpSpin = 0;
    }

    /// The gen-3 stage overrides on the shared engine (see the file header). Stateless, like the base.
    public sealed class RadialJetProgram : JetProgram
    {
        public new static readonly RadialJetProgram Default = new RadialJetProgram();

        static RadialJetSettings R(JetSettings s) => s as RadialJetSettings ?? throw new ArgumentException("RadialJetProgram needs RadialJetSettings");

        // ── stage: the slot table — the angle distribution IS the whole generation ──
        public override JetSlots BuildSlots(JetSettings bs, int seed)
        {
            var s = R(bs);
            var rng = new PlusNumpyRng(unchecked((uint)(seed * 7919 + 13)));
            int n = s.slots;
            var t = new JetSlots
            {
                phase = new double[n], da = new double[n], vs = new double[n], rs = new double[n], amp = new double[n], ls = new double[n], drift = new double[n], shed = new bool[n],
            };
            double j = s.jitter, spread = s.spread * Math.PI / 180.0, bias = s.bias;
            for (int i = 0; i < n; i++) t.phase[i] = i / (double)n;
            double[] b;
            if (s.spread >= 60f) b = StratifiedPermuted(rng, n);
            else { b = new double[n]; for (int i = 0; i < n; i++) b[i] = rng.Uniform(-1.0, 1.0); }
            for (int i = 0; i < n; i++) t.da[i] = Math.Sign(b[i]) * Math.Pow(Math.Abs(b[i]), bias) * spread;
            if (s.lobes != 0 && s.lobeDepth != 0f)
                for (int i = 0; i < n; i++) { double phi = t.da[i] * s.lobes; t.da[i] = (phi - s.lobeDepth * Math.Sin(phi)) / s.lobes; }
            for (int i = 0; i < n; i++)
            {
                // the lobe window: 1 on a lobe axis, 0 between — the on-axis puffs travel further
                double lf = s.lobes != 0 ? 0.5 + 0.5 * Math.Cos(s.lobes * t.da[i]) : 1.0;
                t.vs[i] = (1.0 + j * rng.Uniform(-0.42, 0.42)) * (1.0 - s.lobeKick * (1.0 - lf));
            }
            for (int i = 0; i < n; i++) t.rs[i] = 1.0 + j * rng.Uniform(-0.34, 0.50);
            for (int i = 0; i < n; i++) t.amp[i] = 1.0 + j * rng.Uniform(-0.30, 0.30);
            for (int i = 0; i < n; i++) t.ls[i] = 1.0 + j * rng.Uniform(-0.25, 0.25);
            for (int i = 0; i < n; i++) t.drift[i] = rng.Uniform(-1.0, 1.0);
            for (int i = 0; i < n; i++) t.shed[i] = rng.NextDouble() < s.shed;
            return t;
        }

        // ── stage: the polar domain warp, scrolling OUTWARD in every direction ──
        public override void Warp(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = R(bs);
            const int pu = 24, pv = 16, pw = 4;   // angle, radius, time
            double cell = s.warpCell;
            double reachPx = Math.Max(s.reach * s.w, 1.0);
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
                    double amp = (s.warp0 + s.warp1 * along) * fade;
                    double ct = Math.Cos(th), st = Math.Sin(th);
                    double dx = (dr * ct - dt * st) * amp, dy = (dr * st + dt * ct) * amp;
                    sc.sx[i] = px + dx; sc.sy[i] = py + dy;
                    double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (m > warpMax) warpMax = m;
                }
            sc.warpMax = warpMax;
        }

        // ── stage: the source — a point, or a burner ring ──
        public override void Root(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = R(bs);
            if (s.rootR <= 0) return;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double b = 1.0 + 0.14 * Math.Sin(TAU * 3.0 * phase);
            if (s.srcR > 0f && s.rootK > 0)
            {
                double rr = s.srcR * s.w;
                for (int j = 0; j < s.rootK; j++)
                {
                    double th = TAU * j / s.rootK;
                    double bb = b * (1.0 + 0.10 * Math.Cos(3.0 * th + TAU * phase));
                    JetField.Blob(sc, in fr, nx + rr * Math.Cos(th), ny + rr * Math.Sin(th), s.rootR * 1.5 * bb, s.rootR * bb, th + Math.PI / 2, s.rootAmp);
                }
                return;
            }
            // a radial source has no downstream, so its lump is round rather than a streak
            double ex = s.spread >= 60f ? 1.0 : 1.7;
            JetField.Blob(sc, in fr, nx, ny, s.rootR * ex * b, s.rootR * b, s.aim * Math.PI / 180.0, s.rootAmp);
        }

        // ── stage: every live puff at its own age, thrown into the arc ──
        public override void Emit(JetSettings bs, in JetFrame fr, JetSlots tab, double phase, JetScratch sc)
        {
            var s = R(bs);
            int n = tab.N;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w, srcPx = s.srcR * s.w;
            double aim0 = s.aim * Math.PI / 180.0, sweep = s.sweep * Math.PI / 180.0, sw = s.swirl * Math.PI / 180.0;
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            bool fullCircle = s.spread >= 180f;
            // the shed kick is bounded by the room left in the arc (a clamp would FOLD every over-thrown puff onto the
            // limit — a hard bar through the disc); on a full circle there is no outside to be thrown to
            double lim = Math.Min(s.spread + 15.0, 180.0) * Math.PI / 180.0;
            for (int i = 0; i < n; i++)
            {
                double life = s.life * tab.ls[i] * (tab.shed[i] ? s.shedLife : 1.0);
                double age = Mod1(phase - tab.phase[i]);
                double sAge = age / Math.Max(life, 1e-3);
                if (sAge >= 1.0) continue;
                double ep = tab.phase[i];
                double aim = aim0;
                if (s.sweep != 0f) aim += sweep * Math.Sin(TAU * s.sweepN * ep);
                if (s.spin != 0) aim += TAU * s.spin * ep;
                double pulse = 1.0;
                if (s.pulseN != 0 && s.pulseDepth != 0f)
                {
                    pulse = 1.0 + s.pulseDepth * Math.Cos(TAU * s.pulseN * ep);
                    if (pulse < 0.05) pulse = 0.05; else if (pulse > 2.5) pulse = 2.5;
                }
                double d = reachPx * tab.vs[i] * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double da = tab.da[i], theta;
                if (fullCircle || !tab.shed[i]) theta = aim + da;
                else
                {
                    double room = Math.Max(lim - Math.Abs(da), 0.0);
                    theta = aim + da + Math.Sign(da) * Math.Min(Math.Abs(da) * s.shedKick, room * 0.85);
                }
                double thetaP = s.swirl != 0f ? theta + sw * sAge : theta;
                double rr = srcPx + d;
                double px = nx + rr * Math.Cos(thetaP);
                double py = ny + rr * Math.Sin(thetaP);
                py += s.grav * reachPx * sAge * sAge - s.buoy * reachPx * Math.Pow(sAge, 2.4);
                if (tab.shed[i]) py += tab.drift[i] * s.shedKick * 3.0 * sAge * sAge;
                double r = s.r0 * tab.rs[i] + s.growth * d;
                double aspect = 1.0 + s.elong * Math.Exp(-sAge / Math.Max(s.roundAt, 0.02));
                // stretched along its VELOCITY — on a spiral that is the lead angle, not the radius
                double ori = thetaP;
                if (s.swirl != 0f)
                {
                    double drds = reachPx * tab.vs[i] * kd * Math.Exp(-kd * sAge) / denom;
                    ori = thetaP + Math.Atan2(rr * sw, Math.Max(drds, 1e-3));
                }
                double amp = s.strength * tab.amp[i] * pulse * Fade(sAge) * Math.Pow(Math.Max(1.0 - sAge, 0.0), s.cool);
                if (s.shockN != 0f && s.shockDepth != 0f)
                {
                    // modulated by DISTANCE: on a disc the standing nodes are concentric bright shells
                    amp *= 1.0 + s.shockDepth * Math.Cos(TAU * s.shockN * d / Math.Max(reachPx, 1.0));
                    if (amp < 0) amp = 0;
                }
                if (tab.shed[i]) amp *= 0.72;
                double tint = s.soot != 0f ? Math.Min(Math.Max(s.soot * sAge, 0.0), 1.0) : 0.0;
                JetField.Blob(sc, in fr, px, py, r * aspect, r, ori, amp, tint);
            }
        }

        // ── stage: rings — face on (a shockwave in the picture plane) or the base's edge-on O ──
        public override void Rings(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = R(bs);
            if (s.ringN <= 0) return;
            if (!s.ringFlat) { base.Rings(bs, in fr, phase, sc); return; }
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w, srcPx = s.srcR * s.w;
            double kd = Math.Max(s.drag, 1e-3), denom = 1.0 - Math.Exp(-kd);
            for (int m = 0; m < s.ringN; m++)
            {
                double sAge = Mod1(phase - m / (double)s.ringN) / Math.Max(s.life * s.ringLife, 1e-3);
                if (sAge >= 1.0) continue;
                double d = reachPx * s.ringReach * (1.0 - Math.Exp(-kd * sAge)) / denom;
                double amp0 = s.strength * s.ringAmp * Fade(sAge, 0.10) * Math.Pow(Math.Max(0.0, 1.0 - sAge), s.cool * 0.50);
                double rot = 1.31 * m;
                double wob = 0.17 + 0.05 * ((m * 7) % 3);
                double rr = srcPx + d;                        // face on: the radius IS the travel
                double pr = s.ringR0 + s.ringGrow * d * 0.42;  // the cord stays thin as the ring gets wide
                int k = (int)Math.Max(s.ringK, Math.Min(140, RoundHalfEven(TAU * rr / Math.Max(pr * 0.80, 1e-3))));
                double rise = s.buoy * reachPx * Math.Pow(sAge, 2.4);
                for (int j = 0; j < k; j++)
                {
                    double th = TAU * j / k + rot;
                    double rj = rr * (1.0 + wob * (Math.Cos(3 * th + rot) * 0.6 + Math.Cos(5 * th - 1.7 * rot) * 0.4));
                    // elongated along the TANGENT so the ring closes into a band
                    JetField.Blob(sc, in fr, nx + rj * Math.Cos(th), ny + rj * Math.Sin(th) - rise, pr * 1.45, pr, th + Math.PI / 2, amp0);
                }
            }
        }

        // ── stage: sparks, thrown into the arc (never more than half a turn, never 15° past the gas) ──
        public override void Sparks(JetSettings bs, in JetFrame fr, double phase, JetScratch sc)
        {
            var s = R(bs);
            if (s.sparks <= 0) return;
            var rng = new PlusNumpyRng(unchecked((uint)(fr.seed * 104729 + 77)));
            int n = s.sparks;
            double nx = fr.NozzleX, ny = fr.NozzleY;
            double reachPx = s.reach * s.w, srcPx = s.srcR * s.w;
            double a = s.aim * Math.PI / 180.0, sw = s.swirl * Math.PI / 180.0;
            double arc = Math.Min(Math.Min(s.spread * 1.7, s.spread + 15.0), 180.0) * Math.PI / 180.0;
            var ph = new double[n]; var da = new double[n]; var vs = new double[n]; var rise = new double[n];
            for (int i = 0; i < n; i++) ph[i] = rng.NextDouble();
            for (int i = 0; i < n; i++) da[i] = rng.Uniform(-1.0, 1.0) * arc;
            for (int i = 0; i < n; i++) vs[i] = rng.Uniform(0.55, 1.22);
            for (int i = 0; i < n; i++) rise[i] = rng.Uniform(0.10, 0.55);
            for (int i = 0; i < n; i++)
            {
                double sAge = Mod1(phase - ph[i]) / Math.Max(s.life * 1.1, 1e-3);
                if (sAge >= 1.0) continue;
                double d = srcPx + reachPx * vs[i] * sAge;
                double th = a + da[i] + sw * sAge;
                double x = nx + d * Math.Cos(th);
                double y = ny + d * Math.Sin(th) - rise[i] * reachPx * sAge * sAge;
                JetField.Blob(sc, in fr, x, y, s.sparkR * 1.4, s.sparkR, th, 1.5 * Fade(sAge, 0.08) * Math.Pow(1.0 - sAge, 1.2));
            }
        }
    }
}
