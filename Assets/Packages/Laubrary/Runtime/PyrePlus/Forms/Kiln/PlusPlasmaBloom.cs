// PlusPlasmaBloom — the Kiln "Energy Explosion / agent3_fork" plasma bloom, GENERATION 5 ("the blast has a direction"),
// ported from D:/CODEZ/Kiln/projects/Energy Explosion/agents/agent3_fork/{gen.py, plasma.py} (MANIFEST.md gen 5).
// The dials live on PlasmaBloomForm; this file is the algorithm: the three piece populations, the place/fit
// measurement solve, the scalar energy field, and the field → RGBA shade.
//
// Source components (contract components.json of draw `detonate`) and what happened to each:
//   fit (_place + _fit + border-cap loop)  PORTED   as a cached pre-pass (PlasmaFit) — same 96-px grid, same
//                                                    four-direction extent ratio, same 0.95 shrink loop. The
//                                                    render()-level retry (fill*0.95 on the shipped pixels) is
//                                                    DROPPED: it never fired on any of the 10 published draws.
//   drift                                  PORTED   grid shift by rMax*driftAmt*prog(t, expRate*driftEase, driftLin)
//   shell (+ cosine bias, harmonic K)      PORTED
//   warp (fbm radius displacement)         PORTED
//   lobes (cos / noise gate)               PORTED
//   plume (added tongue shell)             PORTED   (mode == Plume)
//   half_gate (soft half-plane)            PORTED
//   fracture_chunks (body→pieces crossfade)PORTED
//   turbulence (polar fbm)                 PORTED   same kernel (cubic fade, wrapping ku×kv lattice, 2^i per octave)
//   embers / motes (additive blobs)        PORTED
//   core_flash / remnant / ring2 (beaded)  PORTED
//   gain (two-power dissipation)           PORTED
//   shade (E→LUT A/B by radius, alpha)     PORTED   LUT index truncates exactly as the source (int(q*255))
//   downsample (2x, premultiplied box)     PORTED   PlusSupersample's float overload: one quantisation, after the mean
//   trim (drop trailing dim frames)        APPROXIMATED as the `clockEnd` dial (a clip has a fixed frame count)
//   RNG (numpy PCG64 per population)       PORTED   PlusNumpyRng replicates default_rng(seed*7919 + kind) bit-exactly,
//                                                    draws in gen._pop's order — the same piece k everywhere.
//   noise lattices (numpy random)          PORTED   default_rng(seed + 101·octave).random((kv, ku)) likewise.
//
// Coordinates: the source works y-DOWN (PNG rows; orgY/driftY +1 = bottom). Everything here is computed in that
// frame and only the final write maps a y-down row onto the renderer's y-up buffer — so every angle / direction
// dial means what it means in the Kiln source. Lengths are in CANVAS pixels throughout (the source's "field
// units" with SS folded out), except the handful of sub-pixel floors the source applies in grid pixels, which
// stay in grid pixels because that is what they guard (aliasing of a sigma under a pixel).
using System;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// One precomputed piece population (gen._pop): a piece is fixed by these numbers and its position at any t is
    /// a pure function of them — no state, no integration.
    internal sealed class PlasmaPop
    {
        public int n;
        public float[] th, rho, born, spd, lat, siz, amp, spin, strj, tilt, fadj;
    }

    /// A wrapping value-noise lattice for one octave (plasma._lat): `ku` cells around the angle, `kv` along the radius.
    internal sealed class PlasmaLattice
    {
        public int ku, kv;
        public float[] g;
    }

    /// The measurement solve's answer — the SOURCE's placement and radius for this spec. Cached per form by
    /// PlusPrepassCache, rebuilt when any dial / the canvas / the frame count / the seed changes.
    internal sealed class PlasmaFit
    {
        public float rMax;           // canvas px
        public float orgX, orgY;     // fraction of the canvas half-size, 0 = centre, +1 = right / bottom edge
        public PlasmaPop chunks, embers, motes;
        public PlasmaLattice[] body, warp, lobes, beads;
        public PlusLut lutA, lutB;
        public int fitFrames;        // how many clock samples the extents were measured over
        public int shrinkSteps;      // border-cap iterations taken (diagnostic)
    }

    internal static class PlusPlasmaBloom
    {
        public const int SS = 2;                 // the source's supersample factor
        public const int FitGrid = 96;           // gen.FITS — the fit runs on this grid whatever the canvas
        public const float PadFrac = 2f / 128f;  // gen.PAD = 2 px of a 128 canvas, kept as a canvas fraction

        const int KindChunk = 0, KindEmber = 4241, KindMote = 8663;   // gen._KINDSEED

        // ── scalar helpers (plasma.smoothstep, gen._gain/_ease/_prog) ──
        public static float Smooth(float lo, float hi, float x)
        {
            float t = Mathf.Clamp01((x - lo) / Mathf.Max(hi - lo, 1e-6f));
            return t * t * (3f - 2f * t);
        }

        static float Gain(float t, float rise, float hold, float tfast, float tslow, float tmix)
        {
            float up = Mathf.Pow(Mathf.Clamp01(t / Mathf.Max(rise, 1e-6f)), 0.85f);
            if (t <= hold) return up;
            float u = Mathf.Min(1f, (t - hold) / Mathf.Max(1f - hold, 1e-6f));
            float down = tmix * Mathf.Pow(1f - u, tfast) + (1f - tmix) * Mathf.Pow(1f - u, tslow);
            return up * Mathf.Max(down, 0f);
        }

        static float Ease(float t, float k) => (1f - Mathf.Exp(-k * t)) / (1f - Mathf.Exp(-k));

        static float Prog(float t, float k, float lin)
        {
            lin = Mathf.Clamp01(lin);
            return (1f - lin) * Ease(t, k) + lin * t;
        }

        // ── noise (plasma._vnoise / fbm) ──
        static PlasmaLattice[] MakeLattices(int seed, int ku, int kv, int octaves)
        {
            var o = new PlasmaLattice[Mathf.Max(1, octaves)];
            for (int i = 0; i < o.Length; i++)
            {
                int u = ku << i, v = kv << i, s = seed + 101 * i;
                // plasma._lat: default_rng(seed).random((kv, ku)) — the same lattice as the source, row-major
                var g = new PlusNumpyRng(unchecked((uint)s)).Random(u * v);
                o[i] = new PlasmaLattice { ku = u, kv = v, g = g };
            }
            return o;
        }

        static float VNoise(float u, float v, PlasmaLattice L)
        {
            float u0 = Mathf.Floor(u), v0 = Mathf.Floor(v);
            float fu = u - u0, fv = v - v0;
            float su = fu * fu * (3f - 2f * fu), sv = fv * fv * (3f - 2f * fv);
            int i0 = Mod((int)u0, L.ku), i1 = (i0 + 1) % L.ku;
            int j0 = Mod((int)v0, L.kv), j1 = (j0 + 1) % L.kv;
            var g = L.g; int ku = L.ku;
            float a = g[j0 * ku + i0] * (1f - su) + g[j0 * ku + i1] * su;
            float b = g[j1 * ku + i0] * (1f - su) + g[j1 * ku + i1] * su;
            return a * (1f - sv) + b * sv;
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// fbm over the per-octave lattices: octave i samples (u,v)·2^i on a lattice 2^i finer, so every octave wraps
        /// on the same angular period and the seam-free property survives the sum.
        static float Fbm(float u, float v, PlasmaLattice[] octs, float gain = 0.5f, float lac = 2f)
        {
            float tot = 0f, amp = 1f, norm = 0f, s = 1f;
            for (int i = 0; i < octs.Length; i++)
            {
                tot += VNoise(u * s, v * s, octs[i]) * amp;
                norm += amp; amp *= gain; s *= lac;
            }
            return tot / norm;
        }

        // ── populations (gen._strat / _pop) ──
        // The SAME numbers as the source: numpy's default_rng(seed*7919 + kindSeed) replicated bit-for-bit
        // (PlusNumpyRng), draws in gen._pop's order — th, (q, shuffle), (born, shuffle), spd (_strat: q, shuffle),
        // lat, siz, amp, spin, strj, tilt, fadj — so piece k sits where the contract's piece k sits.
        static float[] Strat(PlusNumpyRng rng, int n)
        {
            var u = rng.Random(n);
            var q = new float[n];
            for (int i = 0; i < n; i++) q[i] = (i + u[i]) / Mathf.Max(n, 1);
            rng.Shuffle(q);
            for (int i = 0; i < n; i++) q[i] = q[i] * 2f - 1f;
            return q;
        }

        static PlasmaPop MakePop(PlasmaPopulation p, int seed, int kindSeed)
        {
            int n = Mathf.Max(0, p.n);
            var rng = new PlusNumpyRng(unchecked((uint)(seed * 7919 + kindSeed)));
            var pop = new PlasmaPop { n = n, th = new float[n], rho = new float[n], born = new float[n] };
            int clusters = p.clusters;
            var u = rng.Random(n);
            for (int k = 0; k < n; k++)
            {
                if (clusters >= 2)
                {
                    // Pieces come off in GROUPS: the tongues of a plume do not shed uniformly round the circle.
                    float which = k % clusters;
                    float b = 2f * Mathf.PI * (which + 0.5f) / clusters;
                    pop.th[k] = b + (u[k] - 0.5f) * p.clusterW;
                }
                else pop.th[k] = 2f * Mathf.PI * (k + 0.15f + 0.7f * u[k]) / n;
            }
            u = rng.Random(n);
            var q = new float[n];
            for (int k = 0; k < n; k++) q[k] = (k + u[k]) / n;
            rng.Shuffle(q);                                          // decorrelate radius from angle
            for (int k = 0; k < n; k++) pop.rho[k] = p.rhoLo + (p.rhoHi - p.rhoLo) * Mathf.Sqrt(q[k]);
            u = rng.Random(n);
            for (int k = 0; k < n; k++) pop.born[k] = (k + u[k]) / Mathf.Max(n, 1);
            rng.Shuffle(pop.born);                                   // decorrelate birth from angle
            pop.spd = Strat(rng, n);
            pop.lat = rng.Random(n); for (int k = 0; k < n; k++) pop.lat[k] = pop.lat[k] * 2f - 1f;
            pop.siz = rng.Random(n); for (int k = 0; k < n; k++) pop.siz[k] = 0.62f + 0.85f * pop.siz[k];
            pop.amp = rng.Random(n); for (int k = 0; k < n; k++) pop.amp[k] = 0.65f + 0.70f * pop.amp[k];
            pop.spin = rng.Random(n); for (int k = 0; k < n; k++) pop.spin[k] = pop.spin[k] * 2f - 1f;
            pop.strj = rng.Random(n); for (int k = 0; k < n; k++) pop.strj[k] = 0.60f + 0.95f * pop.strj[k];
            pop.tilt = rng.Random(n); for (int k = 0; k < n; k++) pop.tilt[k] = pop.tilt[k] * 2f - 1f;
            pop.fadj = rng.Random(n); for (int k = 0; k < n; k++) pop.fadj[k] = 0.62f + 0.85f * pop.fadj[k];
            return pop;
        }

        // ── the grid: an S×S sampling of the canvas, origin where the source sits (gen._grid) ──
        // `unit` = canvas px per grid px; (cx, cy) = the source's grid index (y-down).
        public struct Grid
        {
            public int S; public float unit, cx, cy;
            public static Grid Make(int S, float canvasPx, float orgX, float orgY)
            {
                float c = (S - 1) * 0.5f;
                return new Grid { S = S, unit = canvasPx / S, cx = c + orgX * (S * 0.5f), cy = c + orgY * (S * 0.5f) };
            }
        }

        static void DriftOf(PlasmaBloomForm f, float rMax, float t, out float sx, out float sy)
        {
            if (f.live.driftX == 0f && f.live.driftY == 0f) { sx = sy = 0f; return; }
            float d = rMax * f.live.driftAmt * Prog(t, f.expRate * f.driftEase, f.driftLin);
            sx = f.live.driftX * d; sy = f.live.driftY * d;
        }

        /// The scalar radius the coherent mass currently occupies (gen._front) — what the chromatic rim is keyed to.
        static float Front(PlasmaBloomForm f, float rMax, float t, float floorPx)
        {
            float R0 = rMax * Prog(t, f.expRate, f.shellLin);
            float W = Mathf.Max(rMax * (f.live.w0 + f.wGrow * t), floorPx);
            return R0 + W;
        }

        static float Frac(PlasmaBloomForm f, float t) => Smooth(f.fracT0, f.fracT1, t) * f.live.fracMax;

        // ── the piece populations summed into a plane (gen._blobs) ──
        static void Blobs(PlasmaBloomForm f, PlasmaPopulation p, PlasmaPop pop, float rMax, float t, in Grid G,
                          float cx0, float cy0, float dnowX, float dnowY, float[] outPlane, float scale)
        {
            int n = pop.n;
            if (n <= 0 || p.liveAmp <= 0f) return;
            int S = G.S; float unit = G.unit;
            float Pt = Prog(t, f.expRate * p.ease, p.lin);
            float t0 = p.t0, t1 = p.t1, life = p.life, fade = p.fade, ramp = Mathf.Max(p.ramp, 1e-3f);
            float stretch0 = Mathf.Max(p.stretch, 0.05f);
            float bias = f.live.biasAmt, bdir = f.live.biasDir, bk = f.biasK;
            float halfA = f.live.halfAmt, halfD = f.live.halfDir, halfS = Mathf.Max(f.live.halfSoft, 1e-3f), halfK = f.halfK;
            float lag = (f.live.driftX != 0f || f.live.driftY != 0f) ? f.live.driftLag : 0f;
            float ampMul = p.liveAmp * scale;

            for (int k = 0; k < n; k++)
            {
                float tb = t0 + (t1 - t0) * pop.born[k];
                if (t <= tb) continue;
                float u = (t - tb) / Mathf.Max(life * pop.fadj[k], 1e-6f);      // this piece's own age
                if (u >= 1f) continue;
                float alive = Smooth(0f, ramp, u) * Mathf.Pow(1f - u, fade);
                if (alive <= 2e-3f) continue;

                float ang = pop.th[k] + f.swirl * t * p.swirl + pop.spin[k] * p.spin * t;
                float spd = (p.spdBase + p.spread * pop.spd[k]) * (1f + bias * Mathf.Cos(bk * (ang - bdir)));
                // The fastest pieces are the faintest — what makes a wide speed range usable at all.
                alive /= 1f + p.farFade * Mathf.Max(spd - 1f, 0f);
                if (bias > 0f)
                {
                    // More debris one way, not merely faster debris: a fan with a thin side, floored (no pac-man).
                    float w = 0.5f + 0.5f * Mathf.Cos(bk * (ang - bdir));
                    alive *= Mathf.Max(1f - bias + 2f * bias * w, 0.18f);
                }
                if (halfA > 0f)
                {
                    // The gate is what makes a blast directional: it removes the back instead of shrinking it.
                    float pr = Mathf.Cos(halfK * (ang - halfD));
                    float g = Smooth(-halfS, halfS, pr);
                    alive *= 1f - halfA + halfA * g;
                }
                if (alive <= 2e-3f) continue;
                float Pb = Prog(tb, f.expRate * p.ease, p.lin);
                float travel = Mathf.Max(Pt - Pb, 0f);

                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                float latv = p.lat * pop.lat[k] * travel;
                float rr = rMax * (pop.rho[k] * Pb + spd * travel);
                float bx = rr * ca - rMax * latv * sa;
                float by = rr * sa + rMax * latv * ca;
                if (lag > 0f)
                {
                    // THE WAKE: a piece shed at tb was let go where the source was AT tb and does not follow it after.
                    DriftOf(f, rMax, tb, out float dbx, out float dby);
                    bx += lag * (dbx - dnowX);
                    by += lag * (dby - dnowY);
                }
                // Elongation along the piece's REAL velocity, not along the radius.
                float vx = spd * ca - p.lat * pop.lat[k] * sa;
                float vy = spd * sa + p.lat * pop.lat[k] * ca;
                float aa = Mathf.Atan2(vy, vx) + pop.tilt[k] * p.tilt;
                float ux = Mathf.Cos(aa), uy = Mathf.Sin(aa);

                float sg = Mathf.Max(rMax * p.size * pop.siz[k] * (1f + p.grow * u), 0.55f * unit);
                float sr = sg * (1f + (stretch0 * (1f + p.streak * u) - 1f) * pop.strj[k]);
                float rad = 3.4f * Mathf.Max(sr, sg) / unit;
                float px = bx / unit, py = by / unit;
                int i0 = Mathf.Max(0, Mathf.FloorToInt(cy0 + py - rad));
                int i1 = Mathf.Min(S, Mathf.CeilToInt(cy0 + py + rad) + 1);
                int j0 = Mathf.Max(0, Mathf.FloorToInt(cx0 + px - rad));
                int j1 = Mathf.Min(S, Mathf.CeilToInt(cx0 + px + rad) + 1);
                if (i0 >= i1 || j0 >= j1) continue;

                float A = pop.amp[k] * alive * ampMul;
                float invSr = 1f / sr, invSg = 1f / sg;
                for (int i = i0; i < i1; i++)
                {
                    float oy = (i - cy0) * unit - by;
                    int row = i * S;
                    for (int j = j0; j < j1; j++)
                    {
                        float ox = (j - cx0) * unit - bx;
                        float dr = (ox * ux + oy * uy) * invSr;          // along velocity
                        float dt = (-ox * uy + oy * ux) * invSg;         // across it
                        outPlane[row + j] += A * Mathf.Exp(-0.5f * (dr * dr + dt * dt));
                    }
                }
            }
        }

        // ── the field (gen._energy) ──
        /// Fill `E` (S×S, y-down, row-major) with the scalar energy at clock `t`. `scratch` is an S×S plane reused
        /// for the chunk population. `floorPx` is the source's 1.0 sub-pixel floor expressed in canvas px for this grid.
        public static void Energy(PlasmaBloomForm f, PlasmaFit fit, float rMax, float orgX, float orgY, float t,
                                  in Grid G, float floorPx, float[] E, float[] scratch, float scale = 1f, bool accumulate = false)
        {
            int S = G.S; float unit = G.unit;
            DriftOf(f, rMax, t, out float sx, out float sy);
            float cx = G.cx + sx / unit, cy = G.cy + sy / unit;       // the drifted source on this grid

            float R0c = rMax * Prog(t, f.expRate, f.shellLin);
            // THE WIDTH IS A FRACTION OF THE CURRENT RADIUS, NOT OF THE FINAL ONE.
            float W = Mathf.Max(R0c * f.live.w0 + rMax * f.wGrow * t, floorPx);
            bool bias = f.live.biasAmt > 0f;
            bool warp = f.live.warp > 0f;
            float wamp = warp ? f.live.warp * (1f + f.warpGrow * t) * Mathf.Clamp01(t / 0.05f) : 0f;
            bool lobes = f.lobes >= 2;
            bool plume = f.mode == PlasmaBloomForm.Mode.Plume && lobes;
            bool half = f.live.halfAmt > 0f;
            float halfS = Mathf.Max(f.live.halfSoft, 1e-3f);
            float fr = Frac(f, t);
            float amp = f.live.turb * Mathf.Clamp01(t / 0.07f);
            bool core = f.live.coreGain > 0f;
            float cr = core ? Mathf.Max(rMax * f.live.coreR + rMax * Prog(t, f.expRate, f.shellLin) * f.live.coreFollow, 1.2f * floorPx) : 1f;
            float coreK = core ? f.live.coreGain * (1f + f.live.flash * Mathf.Exp(-t / Mathf.Max(f.flashTau, 1e-3f))) * Mathf.Exp(-t / Mathf.Max(f.coreTau, 1e-3f)) : 0f;
            bool remnant = f.live.remnant > 0f;
            float rr = remnant ? Mathf.Max(rMax * f.live.remnantR, 1.5f * floorPx) : 1f;
            float remK = remnant ? f.live.remnant * (fr / Mathf.Max(f.live.fracMax, 1e-3f)) * Mathf.Exp(-t / Mathf.Max(f.remnantTau, 1e-3f)) : 0f;
            bool ring2 = f.live.ring2 > 0f;
            float tl = Mathf.Min(1f, t / Mathf.Max(f.ring2Life, 1e-3f));
            float R2c = rMax * f.live.ring2R * Prog(tl, 5.5f, f.ring2Lin);
            float W2 = Mathf.Max(rMax * f.live.ring2W, floorPx);
            float fade2 = Mathf.Pow(Mathf.Max(0f, 1f - tl), 1.4f);
            float beadK = f.live.ring2Bead > 0f ? f.live.ring2Bead * Smooth(f.beadT0, f.beadT1, t) : 0f;
            float gain = Gain(t, f.rise, f.hold, f.tailFast, f.tailSlow, f.tailMix) * scale;
            float invRmax = 1f / Mathf.Max(rMax, 1e-6f);
            float kuN = f.ku, kvN = f.kv, warpKN = f.warpK, lobesN = f.lobes, ring2KN = f.ring2K;

            // The chunk population is a plane of its own because it is CROSSFADED with the body before the
            // turbulence multiply (a chunk chewed by the same noise as the body is a torn piece of the same material).
            bool chunks = fr > 1e-4f;
            if (chunks)
            {
                Array.Clear(scratch, 0, S * S);
                Blobs(f, f.chunks, fit.chunks, rMax, t, G, cx, cy, sx, sy, scratch, 1f);
            }

            for (int i = 0; i < S; i++)
            {
                float dy = (i - cy) * unit;
                int row = i * S;
                for (int j = 0; j < S; j++)
                {
                    float dx = (j - cx) * unit;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float th = Mathf.Atan2(dy, dx);
                    float R0 = bias ? R0c * (1f + f.live.biasAmt * Mathf.Cos(f.biasK * (th - f.live.biasDir))) : R0c;
                    float R = R0;
                    // turbulence coordinates: the angle wraps, the radius flows OUTWARD with time
                    float tha = th + f.swirl * (r * invRmax) * t;
                    float u = (tha + Mathf.PI) / (2f * Mathf.PI) * kuN;
                    float v = (r * invRmax) * kvN - f.flow * t;
                    float nw = 0.5f;
                    if (warp)
                    {
                        float uw = (tha + Mathf.PI) / (2f * Mathf.PI) * warpKN;
                        nw = Fbm(uw, v * 0.5f, fit.warp);
                        R *= 1f + wamp * (nw - 0.5f) * 2f;
                    }
                    float n = Mathf.Pow(Fbm(u, v, fit.body), f.live.turbPow);

                    float gate = 0f;
                    if (lobes)
                    {
                        if (f.lobeMode == PlasmaBloomForm.LobeMode.Noise)
                        {
                            float ul = (th - f.live.lobePh + Mathf.PI) / (2f * Mathf.PI) * lobesN;
                            gate = Mathf.Pow(Mathf.Clamp01(Fbm(ul, 0.5f, fit.lobes) * f.live.gateGain), f.live.lobePow);
                        }
                        else gate = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(lobesN * (th - f.live.lobePh)), f.live.lobePow);
                    }

                    float dsh = (r - R) / W;
                    float shell = Mathf.Exp(-0.5f * dsh * dsh);
                    if (plume)
                    {
                        // Tongues are an ADDED second shell at a longer radius, gated by angle, fading in and out.
                        float Rt = R0 * f.live.plumeReach;
                        if (warp && f.live.plumeVary > 0f) Rt *= 1f + f.live.plumeVary * (nw - 0.5f) * 2f;
                        float tin = Mathf.Clamp01(r / Mathf.Max(Rt * 0.45f, 1e-6f)); tin = tin * tin * (3f - 2f * tin);
                        float o0 = Rt * 0.55f, o1 = Rt + W * f.live.plumeW;
                        float tout = Mathf.Clamp01((r - o0) / Mathf.Max(o1 - o0, 1e-6f)); tout = tout * tout * (3f - 2f * tout);
                        shell += f.live.plumeAmp * gate * tin * (1f - tout);
                    }
                    else if (lobes) shell *= 1f - f.live.lobeAmp + f.live.lobeAmp * gate;
                    if (half)
                    {
                        float hg = Smooth(-halfS, halfS, Mathf.Cos(f.halfK * (th - f.live.halfDir)));
                        shell *= 1f - f.live.halfAmt + f.live.halfAmt * hg;
                    }

                    float mass = chunks ? shell * (1f - fr) + scratch[row + j] * fr : shell;
                    float field = mass * (1f - amp + amp * (n * 1.85f));
                    if (core) field += coreK * Mathf.Exp(-0.5f * (r / cr) * (r / cr));
                    if (remnant) field += remK * Mathf.Exp(-0.5f * (r / rr) * (r / rr));
                    if (ring2)
                    {
                        float R2 = R2c;
                        if (bias) R2 *= 1f + f.live.biasAmt * 0.7f * Mathf.Cos(f.biasK * (th - f.live.biasDir));
                        if (warp) R2 *= 1f + f.live.warp * 0.5f * (nw - 0.5f) * 2f;
                        float d2 = (r - R2) / W2;
                        float ring = Mathf.Exp(-0.5f * d2 * d2);
                        if (beadK > 0f)
                        {
                            // The ring BEADS as it goes: continuous in the noise, so the beads have soft ends.
                            float ub = (th + Mathf.PI) / (2f * Mathf.PI) * ring2KN;
                            float gb = Fbm(ub, 0.5f, fit.beads);
                            ring *= Mathf.Pow(Mathf.Clamp01(1f - beadK * (1f - gb) * 2.2f), 1.3f);
                        }
                        field += f.live.ring2 * fade2 * ring;
                    }
                    if (accumulate) E[row + j] += field * gain; else E[row + j] = field * gain;
                }
            }
            // embers and motes are ADDITIVE: the spray thrown off the fracture, not the fracture (gain folded in)
            Blobs(f, f.embers, fit.embers, rMax, t, G, cx, cy, sx, sy, E, gain);
            Blobs(f, f.motes, fit.motes, rMax, t, G, cx, cy, sx, sy, E, gain);
        }

        /// The rim coordinate rn = |p − drifted source| / front(t) for every grid sample (what the chromatic rim
        /// crossfade is keyed to). With a swarm the form keeps the MIN over instances, so every bloom owns its own rim.
        public static void RimPlane(PlasmaBloomForm f, float rMax, float t, in Grid G, float floorPx, float[] rn, bool min)
        {
            int S = G.S; float unit = G.unit;
            DriftOf(f, rMax, t, out float sx, out float sy);
            float cx = G.cx + sx / unit, cy = G.cy + sy / unit;
            float invFront = 1f / Mathf.Max(Front(f, rMax, t, floorPx), 1e-3f);
            for (int i = 0; i < S; i++)
            {
                float dy = (i - cy) * unit; int row = i * S;
                for (int j = 0; j < S; j++)
                {
                    float dx = (j - cx) * unit;
                    float v = Mathf.Sqrt(dx * dx + dy * dy) * invFront;
                    if (!min || v < rn[row + j]) rn[row + j] = v;
                }
            }
        }

        static float AlphaOf(PlasmaBloomForm f, float E) => Mathf.Clamp01(Mathf.Pow(Smooth(f.aLo, f.aHi, E), f.aGamma));

        // ── the measurement solve (gen._room / _extents / _place / _fit) ──
        /// The visible extent of the whole clock in each of the four directions from the source: left, right, up, down.
        static void Extents(PlasmaBloomForm f, PlasmaFit fit, float rMax, float orgX, float orgY, int frames, in Grid G,
                            float vis, float[] E, float[] scratch, float[] ext, Action<float> liveAt)
        {
            ext[0] = ext[1] = ext[2] = ext[3] = 0f;
            int S = G.S; float unit = G.unit;
            for (int i = 0; i < frames; i++)
            {
                float t = (i + 1f) / frames;
                liveAt(t);
                Energy(f, fit, rMax, orgX, orgY, t, G, 1f, E, scratch);
                for (int y = 0; y < S; y++)
                {
                    float dy = (y - G.cy) * unit;
                    for (int x = 0; x < S; x++)
                        if (E[y * S + x] > vis)
                        {
                            float dx = (x - G.cx) * unit;
                            if (-dx > ext[0]) ext[0] = -dx;
                            if (dx > ext[1]) ext[1] = dx;
                            if (-dy > ext[2]) ext[2] = -dy;
                            if (dy > ext[3]) ext[3] = dy;
                        }
                }
            }
        }

        /// Solve the source placement and radius for this spec: gen._place (room ratio == extent ratio) then gen._fit
        /// (four-direction size, then the border-cap shrink loop). Pure in (form, canvasPx, frames, seed): the cache key.
        /// `liveAt(t)` resolves the form's envelopes at the clock sample about to be measured (into the form's `live`).
        public static PlasmaFit Solve(PlasmaBloomForm f, float canvasPx, int frames, int seed, Action<float> liveAt)
        {
            var fit = new PlasmaFit
            {
                chunks = MakePop(f.chunks, seed, KindChunk),
                embers = MakePop(f.embers, seed, KindEmber),
                motes = MakePop(f.motes, seed, KindMote),
                body = MakeLattices(seed, Mathf.Max(1, f.ku), 24, Mathf.Max(1, f.oct)),
                warp = MakeLattices(seed + 7717, Mathf.Max(1, f.warpK), 16, 2),
                lobes = MakeLattices(seed + 331, Mathf.Max(1, f.lobes), 8, 2),
                beads = MakeLattices(seed + 991, Mathf.Max(1, f.ring2K), 8, 2),
                lutA = PlusShade.BakeLut(f.hueA, 256),
                lutB = PlusShade.BakeLut(f.hueB, 256),
                fitFrames = frames,
            };
            float h = canvasPx * 0.5f;
            float pad = PadFrac * canvasPx;
            float orgX = f.orgX, orgY = f.orgY;
            float rMax = f.scale * h;
            if (!f.autoFit) { fit.rMax = rMax; fit.orgX = orgX; fit.orgY = orgY; return fit; }

            int S = FitGrid;
            var E = new float[S * S]; var scratch = new float[S * S]; var ext = new float[4];
            float vis = f.aLo + f.fitVis * (f.aHi - f.aLo);

            if (f.autoOrg > 0f)
            {
                // _place: measured on a centred grid at a small trial radius so nothing clips; the ratio is scale-free.
                var G0 = Grid.Make(S, canvasPx, 0f, 0f);
                Extents(f, fit, 0.10f * canvasPx, 0f, 0f, frames, G0, vis, E, scratch, ext, liveAt);
                float room0 = Mathf.Max(h - pad, 1f);
                orgX = orgY = 0f;
                if (ext[0] + ext[1] > 1e-6f) orgX = Mathf.Clamp(room0 * (ext[0] - ext[1]) / (ext[0] + ext[1]) * f.autoOrg / h, -0.55f, 0.55f);
                if (ext[2] + ext[3] > 1e-6f) orgY = Mathf.Clamp(room0 * (ext[2] - ext[3]) / (ext[2] + ext[3]) * f.autoOrg / h, -0.55f, 0.55f);
            }

            // _fit stage 1 — SIZE: the tightest of the four extent/room ratios (exact: the field is scale-invariant
            // in rMax about the source, the second pass only absorbs grid quantisation).
            var G = Grid.Make(S, canvasPx, orgX, orgY);
            rMax = 0.28f * h;
            float ox = orgX * h, oy = orgY * h, fill = f.fill;
            float[] room =
            {
                Mathf.Max((h + ox - pad) * fill, 1f), Mathf.Max((h - ox - pad) * fill, 1f),
                Mathf.Max((h + oy - pad) * fill, 1f), Mathf.Max((h - oy - pad) * fill, 1f),
            };
            for (int pass = 0; pass < 2; pass++)
            {
                Extents(f, fit, rMax, orgX, orgY, frames, G, vis, E, scratch, ext, liveAt);
                float need = 0f;
                for (int k = 0; k < 4; k++) need = Mathf.Max(need, ext[k] / room[k]);
                if (need < 1e-3f) break;
                rMax /= need;
            }

            // _fit stage 2 — CONTAINMENT: shrink 5% at a time until nothing above borderCap touches the frame edge.
            float cap = f.borderCap / 255f;
            for (int iter = 0; iter < 26; iter++)
            {
                float b = 0f;
                for (int i = 0; i < frames && b <= cap; i++)
                {
                    liveAt((i + 1f) / frames);
                    Energy(f, fit, rMax, orgX, orgY, (i + 1f) / frames, G, 1f, E, scratch);
                    for (int x = 0; x < S; x++)
                    {
                        b = Mathf.Max(b, AlphaOf(f, E[x]), AlphaOf(f, E[(S - 1) * S + x]));
                        b = Mathf.Max(b, AlphaOf(f, E[x * S]), AlphaOf(f, E[x * S + S - 1]));
                    }
                }
                if (b <= cap) break;
                rMax *= 0.95f;
                fit.shrinkSteps++;
            }
            fit.rMax = rMax; fit.orgX = orgX; fit.orgY = orgY;
            return fit;
        }

        // ── shade (gen._render_once) ──
        /// Shade one supersampled plane into PREMULTIPLIED float RGB (0..1×alpha) + straight alpha, y-down like the
        /// field; the caller box-averages and quantises ONCE (PlusSupersample float overload) exactly as the source.
        /// Also returns the per-sample ramp coordinate / alpha / rim mix when the caller wants planes for the harness.
        public static void Shade(PlasmaBloomForm f, PlasmaFit fit, int S, float[] E, float[] rn, float layerAlpha,
                                 float[] pr, float[] pg, float[] pb, float[] pa, float[] rampT, float[] alpha, float[] rim)
        {
            float invNorm = 1f / Mathf.Max(f.live.eNorm, 1e-6f);
            var A = fit.lutA.rgba; var B = fit.lutB.rgba;
            int n = S * S;
            for (int i = 0; i < n; i++)
            {
                float e = E[i];
                float q = Mathf.Pow(Mathf.Clamp01(e * invNorm), f.live.cGamma);
                int idx = (int)(q * 255f); if (idx > 255) idx = 255;
                float mix = Smooth(f.live.rimLo, f.live.rimHi, rn[i]) * f.live.rimMix;
                float a = Mathf.Pow(Smooth(f.aLo, f.aHi, e), f.aGamma);
                if (rampT != null) { rampT[i] = q; alpha[i] = a; rim[i] = mix; }
                a *= layerAlpha;
                if (a <= 0f) { pr[i] = pg[i] = pb[i] = pa[i] = 0f; continue; }
                Color ca = A[idx], cb = B[idx];
                pr[i] = (ca.r * (1f - mix) + cb.r * mix) * a;
                pg[i] = (ca.g * (1f - mix) + cb.g * mix) * a;
                pb[i] = (ca.b * (1f - mix) + cb.b * mix) * a;
                pa[i] = a;
            }
        }

        /// k×k box mean of a y-down S×S plane onto a y-down (S/k)×(S/k) plane — the contract's own field export.
        public static float[] BoxDown(float[] big, int S, int k)
        {
            int s = S / k; var o = new float[s * s]; float inv = 1f / (k * k);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float acc = 0f;
                    for (int sy = 0; sy < k; sy++) for (int sx = 0; sx < k; sx++) acc += big[(y * k + sy) * S + x * k + sx];
                    o[y * s + x] = acc * inv;
                }
            return o;
        }
    }
}
