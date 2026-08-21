// PlusOrb — the algorithm of Kiln "Energy Projectile / agent2" THE ORB, generation 2 (orbcanvas.py + gen.py), as pure
// functions over float planes. OrbForm.cs holds the dials and the PlusForm glue; this file is the port proper.
//
// Source: D:/CODEZ/Kiln/projects/Energy Projectile/agents/agent2 (MANIFEST "generation 2 — the slider came back: 4").
// Five archetypes = five PROGRAMS sharing one canvas model: an additive energy field E built from compact-support
// stamps (`kern`), plane-wave turbulence that loops exactly and advects in −x (`turb`), a whole-pixel −x smear
// (`smear`), one binomial soften pass, then tone = 1 − exp(−E·gain) read through a 256-entry sRGB-interpolated LUT
// with a power-curve ALPHA WINDOW on the tone (a0..a1, acurve, amax), a floor cut and a despeckle.
//
// Ported / approximated / dropped, per components.json of each draw:
//   emberdrift  wake_turbulence, smear_wake, core_boiling, embers, glow, soften, rasterise, despeckle ........ PORTED
//   wisp        trail_tube, smear_trail, cloud, nucleus, veils, glow, soften, rasterise, despeckle ........... PORTED
//   coronal     prominences, smear_prominences, star_body, limb, glow, soften, rasterise, despeckle .......... PORTED
//   membrane    shed_shells, smear_shells, motes, bubble_window, bubble_skin, ripples, nucleus, glow, soften,
//               rasterise, despeckle ....................................................................... PORTED
//   voltcore    filaments, smear_filaments, plasma_tail, envelope, charge_noise, bead, ball, glow, soften,
//               rasterise, despeckle ....................................................................... PORTED
//   RNG: numpy default_rng streams (turb octaves, embers, prominences, motes, filaments) ............... PORTED EXACT
//   APPROXIMATED: float32 arithmetic (numpy) vs double here — ±1 specks at the LUT / alpha rounding boundaries;
//                 the LUT itself is baked with numpy's float32 positions (np.linspace / np.interp replicated).
//   APPROXIMATED: `emit` requires the period to divide the frame count (the source asserts it) — here the piece ids
//                 wrap modulo ⌊N / period⌋, so a frame count the period does not divide still renders (the loop seam
//                 is then imperfect, which is what the source refuses to do).
//   DROPPED: the export path (sprite sheet / WebP / contact sheet) and the censuses (alpha / colour / seam) — analysis,
//            not rendering.
//
// Units: every program runs in the SOURCE's pixel frame (the archetype's own R, nose x and axis y in source px) on a
// grid whose spacing is 1/u source px, u = canvas R / source R. So the picture is the source's, magnified by u and
// placed at the form's nose/axis — the same orb at PyrePlus's 64 px and at the contract's 192 × 96, and a dial moves
// the whole figure instead of one term. Pixel-grain passes (smear taps, soften, despeckle) act on CANVAS pixels:
// the smear's taps and per-pixel decay are rescaled so the trail's LENGTH in source px is kept.
//
// Threading: no statics but readonly tables; every RNG is instantiated inside the draw; scratch planes belong to the
// calling form instance (one clone per worker thread).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// The archetype's own frame: its source canvas, core radius, nose x and axis y in source px. The programs below
    /// are written against these constants exactly as gen.py is; the form's dials only place and scale the result.
    public readonly struct OrbSource
    {
        public readonly float W, H, R, nx, cy;
        public OrbSource(float w, float h, float r, float nx, float cy) { W = w; H = h; R = r; this.nx = nx; this.cy = cy; }
        public static readonly OrbSource Emberdrift = new OrbSource(192, 96, 16.0f, 192 * 0.755f, 96 * 0.52f);
        public static readonly OrbSource Wisp = new OrbSource(200, 80, 15.0f, 200 * 0.775f, 80 * 0.5f);
        public static readonly OrbSource Coronal = new OrbSource(184, 108, 17.0f, 184 * 0.760f, 108 * 0.5f);
        public static readonly OrbSource Membrane = new OrbSource(180, 100, 18.5f, 180 * 0.745f, 100 * 0.5f);
        public static readonly OrbSource Voltcore = new OrbSource(184, 104, 16.5f, 184 * 0.735f, 104 * 0.5f);
    }

    /// One orb's placement on the canvas: canvas pixel (x, y) ↦ source px (Xs, Ys) = ((x + ½ − ox) / u + nx, (y + ½ − oy) / u + cy),
    /// y-down like the source. `amp` multiplies every deposit (the swarm's brightness shading).
    public struct OrbFrame
    {
        public int W, H;
        public double u, ox, oy, nx, cy, R, L, amp;
        public double Xs(int x) => (x + 0.5 - ox) / u + nx;
        public double Ys(int y) => (y + 0.5 - oy) / u + cy;
        /// Canvas x range (inclusive) covering source x ∈ [xa, xb].
        public void XRange(double xa, double xb, out int x0, out int x1)
        {
            x0 = Math.Max(0, (int)Math.Floor((xa - nx) * u + ox - 0.5));
            x1 = Math.Min(W - 1, (int)Math.Ceiling((xb - nx) * u + ox - 0.5));
        }
        public void YRange(double ya, double yb, out int y0, out int y1)
        {
            y0 = Math.Max(0, (int)Math.Floor((ya - cy) * u + oy - 0.5));
            y1 = Math.Min(H - 1, (int)Math.Ceiling((yb - cy) * u + oy - 0.5));
        }
    }

    /// orbcanvas.turb: a sum of plane waves sin(fx·x + fy·y + m·tph + ph) with integer temporal harmonics, so it is
    /// exactly periodic in tph and, with fx > 0, advects in −x. The octave table is drawn from numpy's default_rng
    /// stream in the source's order (fx, fy, sign, m, ph per octave).
    public sealed class OrbTurb
    {
        readonly double[] _fx, _fy, _ph, _amp;
        readonly int[] _m;
        readonly double _invTot;
        public OrbTurb(uint seed, int octaves, double scale, double aniso, int drift = 1)
        {
            octaves = Math.Max(1, octaves);
            _fx = new double[octaves]; _fy = new double[octaves]; _ph = new double[octaves]; _amp = new double[octaves]; _m = new int[octaves];
            var rng = new PlusNumpyRng(seed);
            double amp = 1.0, tot = 0.0, f = scale;
            for (int o = 0; o < octaves; o++)
            {
                _fx[o] = f * rng.Uniform(0.65, 1.45) / Math.Max(aniso, 1e-6);
                _fy[o] = f * rng.Uniform(0.65, 1.45) * rng.ChoiceSign();
                _m[o] = (int)rng.Integers(1, 4) * drift;
                _ph[o] = rng.Uniform(0.0, 2.0 * Math.PI);
                _amp[o] = amp; tot += amp; amp *= 0.55; f *= 2.0;
            }
            _invTot = 1.0 / Math.Max(tot, 1e-6);
        }
        public double Eval(double x, double y, double tph)
        {
            double s = 0.0;
            for (int o = 0; o < _fx.Length; o++) s += _amp[o] * Math.Sin(_fx[o] * x + _fy[o] * y + _m[o] * tph + _ph[o]);
            return s * _invTot;
        }
    }

    /// The tone → pixel stage of orbcanvas.rasterise: gain, the alpha window, the floor, and the LUT (256 × rgb).
    public struct OrbStyle
    {
        public double gain, a0, a1, acurve, amax;
        public int floor;
        public double[] lut;   // 256 × 3, unrounded (orbcanvas.lut), baked by PlusOrb.BakeLut
        public double Tone(double e) => 1.0 - Math.Exp(-Math.Max(e, 0.0) * gain);
        public double Alpha(double tone)
        {
            double a = (tone - a0) / Math.Max(a1 - a0, 1e-6);
            a = a < 0.0 ? 0.0 : a > 1.0 ? 1.0 : a;
            return Math.Pow(a, acurve) * amax;
        }
    }

    public static class PlusOrb
    {
        public const double TAU = 2.0 * Math.PI;

        // ── primitives ──────────────────────────────────────────────────────────────────────────────────────

        /// orbcanvas.kern: 1 for d ≤ flat, (1 − d)/(1 − flat) raised to p down to EXACTLY 0 at d = 1 (compact support).
        public static double Kern(double d, double flat, double p)
        {
            double t = (1.0 - d) / Math.Max(1e-6, 1.0 - flat);
            t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
            return p == 1.0 ? t : Math.Pow(t, p);
        }

        /// gen._nose: compress the leading half along the travel axis (u > 0 is ahead of the centre).
        public static double Nose(double u, double squash) => u > 0.0 ? u / squash : u / (2.0 - squash);

        static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
        static double Clip01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

        /// orbcanvas.emit: the (age, stable id) of every live shed piece at frame t.
        public static int Emit(int t, int n, int period, int life, List<(int age, int bid)> into)
        {
            into.Clear();
            period = Math.Max(1, period);
            int slots = Math.Max(1, n / period);
            for (int age = 0; age < life; age++)
            {
                int k = t - age;
                if (((k % period) + period) % period != 0) continue;
                int q = (int)Math.Floor(k / (double)period);
                into.Add((age, ((q % slots) + slots) % slots));
            }
            return into.Count;
        }

        /// A soft ellipse kern(hyp((X−x)/rx, (Y−y)/ry), flat, p) × amp added into E over its support only.
        public static void AddEllipse(float[] E, in OrbFrame fr, double x, double y, double rx, double ry, double flat, double p, double amp)
        {
            amp *= fr.amp;
            if (amp == 0.0 || rx <= 0.0 || ry <= 0.0) return;
            fr.XRange(x - rx, x + rx, out int x0, out int x1);
            fr.YRange(y - ry, y + ry, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double dy = (fr.Ys(py) - y) / ry;
                int row = py * fr.W;
                for (int px = x0; px <= x1; px++)
                {
                    double dx = (fr.Xs(px) - x) / rx;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d >= 1.0) continue;
                    E[row + px] += (float)(amp * Kern(d, flat, p));
                }
            }
        }

        /// orbcanvas.streak: a stamp stretched `elong` times along its own velocity (p = 0.75), added into E.
        public static void AddStreak(float[] E, in OrbFrame fr, double x, double y, double vx, double vy, double r, double elong, double amp)
        {
            amp *= fr.amp;
            if (amp == 0.0 || r <= 0.0) return;
            double sp = Hyp(vx, vy), ax, ay;
            if (sp < 1e-6) { ax = 1.0; ay = 0.0; } else { ax = vx / sp; ay = vy / sp; }
            double reach = r * Math.Max(elong, 1.0);
            fr.XRange(x - reach, x + reach, out int x0, out int x1);
            fr.YRange(y - reach, y + reach, out int y0, out int y1);
            double re = r * elong;
            for (int py = y0; py <= y1; py++)
            {
                double dy = fr.Ys(py) - y;
                int row = py * fr.W;
                for (int px = x0; px <= x1; px++)
                {
                    double dx = fr.Xs(px) - x;
                    double par = (dx * ax + dy * ay) / re, per = (-dx * ay + dy * ax) / r;
                    double d = Math.Sqrt(par * par + per * per);
                    if (d >= 1.0) continue;
                    E[row + px] += (float)(amp * Kern(d, 0.0, 0.75));
                }
            }
        }

        /// gen._glow: the light the orb sits in, a low-amplitude wide term of the same field plus a tail lobe.
        public static void AddGlow(float[] E, in OrbFrame fr, double wide, double tail, double amp)
        {
            AddEllipse(E, fr, fr.nx, fr.cy, fr.R * wide, fr.R * wide * 0.82, 0.0, 2.0, amp);
            AddEllipse(E, fr, fr.nx - fr.L * 0.34, fr.cy, fr.L * 0.46, fr.R * 1.7, 0.0, 2.0, amp * tail);
        }

        /// orbcanvas.smear on CANVAS pixels: `taps` and `decay` are per SOURCE px, rescaled by u so the trail keeps its length.
        public static void Smear(float[] F, in OrbFrame fr, int taps, double decay, bool norm, float[] scratch)
        {
            int tapsC = (int)Math.Round(taps * fr.u);
            if (tapsC <= 0) return;
            double decayC = fr.u == 1.0 ? decay : Math.Pow(Math.Max(decay, 1e-9), 1.0 / fr.u);
            PlusFieldOps.SmearShiftX(F, fr.W, fr.H, tapsC, (float)decayC, norm, scratch);
        }

        static void AddScaled(float[] E, float[] F, double k, int n)
        {
            if (k == 1.0) for (int i = 0; i < n; i++) E[i] += F[i];
            else for (int i = 0; i < n; i++) E[i] += (float)(F[i] * k);
        }

        /// The per-piece / per-octave seed the source derives from the draw seed (kept in 32 bits for SeedSequence).
        public static uint Seed(long v) => unchecked((uint)v);

        // ── the five programs: each ADDS one frame's field into E (temp planes A / B are scratch, cleared here) ──

        public static void Emberdrift(OrbForm.EmberdriftSettings s, in OrbFrame fr, int t, int n, long seed, float[] E, float[] A, float[] scratch, List<(int, int)> emit)
        {
            double tph = TAU * t / n, nx = fr.nx, cy = fr.cy, R = fr.R, L = fr.L;
            int W = fr.W, H = fr.H;
            // --- the wake, as a torn field: envelope × travelling turbulence, clipped at zero, then SMEARED backward.
            Array.Clear(A, 0, W * H);
            var turbA = new OrbTurb(Seed(seed + s.turbSeedOff), s.turbOct, s.turbScale, s.turbAniso);
            var turbB = new OrbTurb(Seed(seed + s.turbSeedOff2), s.turbOct2, s.turbScale2, s.turbAniso2);
            fr.XRange(nx - L, nx + R * 0.30, out int x0, out int x1);
            fr.YRange(cy - R * 1.02, cy + R * 1.02, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    double X = fr.Xs(px);
                    double sx = Math.Max((nx - X) / L, 0.0), sxc = Math.Min(sx, 1.0);
                    double halfw = Math.Max(R * (1.02 - 0.46 * sxc), 1e-6);
                    double env = Math.Pow(Clip01(1.0 - sx), 1.05) * Math.Pow(Clip01(1.0 - Math.Abs(v) / halfw), 0.70);
                    double n2 = s.own.turbW * turbA.Eval(X, Y, tph) + s.own.turbW2 * turbB.Eval(X, Y, tph);
                    double wake = Math.Max(env * (1.0 - 0.30 * sxc + (0.50 + 1.15 * sxc) * n2), 0.0) * 1.60;
                    // the smooth root underneath, so the torn tongues read as coming off the ball
                    wake += 0.60 * Math.Pow(Clip01(1.0 - sx), 1.30) * Math.Pow(Clip01(1.0 - Math.Abs(v) / (halfw * 0.60)), 0.80);
                    // faded over 0.9R past the nose (a hard cut here drew the nose as a wall)
                    wake *= Clip01((nx + R * 0.30 - X) / (R * 0.90));
                    A[row + px] = (float)(wake * fr.amp);
                }
            }
            Smear(A, fr, s.smearTaps, s.own.smearDecay, s.smearNorm, scratch);
            AddScaled(E, A, 1.0, W * H);
            // --- the core: the warp is applied to the DISTANCE so the whole body boils.
            var turbC = new OrbTurb(Seed(seed + s.turbSeedOffBody), s.turbOctBody, s.turbScaleBody, s.turbAnisoBody);
            double reach = 1.0 + s.own.coreWarp;
            fr.XRange(nx - reach * R * (2.0 - s.live.noseSquash), nx + reach * R, out x0, out x1);
            fr.YRange(cy - reach * R, cy + reach * R, out y0, out y1);
            for (int py = y0; py <= y1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    double X = fr.Xs(px), u = X - nx;
                    double dw = Hyp(Nose(u, s.live.noseSquash) / R, v / R) - s.own.coreWarp * turbC.Eval(X, Y, tph);
                    if (dw >= 1.0) continue;
                    E[row + px] += (float)(fr.amp * s.own.coreAmp * Kern(Math.Max(dw, 0.0), 0.0, s.own.coreP));
                }
            }
            // --- embers dispersing: fine, streaked along their own velocity, gone in `emitLife` frames.
            Emit(t, n, s.emitPeriod, s.emitLife, emit);
            foreach (var (age, bid) in emit)
            {
                double f = age / (double)s.emitLife;
                var rng = new PlusNumpyRng(Seed(seed * 131 + bid * 7919));
                for (int q = 0; q < s.embersPerPiece; q++)
                {
                    double vy = rng.Uniform(-1.5, 1.5);
                    double vx = -rng.Uniform(5.5, 9.0);
                    double x = nx - R * 0.5 + vx * age + rng.Uniform(-3, 3);
                    double y = cy + vy * age * 1.5 + rng.Uniform(-R * 0.55, R * 0.55);
                    double r = (1.1 + 0.9 * rng.NextDouble()) * (1.0 - 0.4 * f);
                    AddStreak(E, fr, x, y, vx, vy, r, s.own.emberElong, s.own.emberAmp * Math.Pow(1.0 - f, s.emberFadeP));
                }
            }
            AddGlow(E, fr, s.live.glowWide, s.live.glowTail, s.live.glowAmp);
        }

        public static void Wisp(OrbForm.WispSettings s, in OrbFrame fr, int t, int n, long seed, float[] E, float[] A, float[] scratch)
        {
            double tph = TAU * t / n, nx = fr.nx, cy = fr.cy, R = fr.R, L = fr.L;
            int W = fr.W, H = fr.H;
            // --- the trail as a snaking tube: a sum of overlapping stamps down a curve (only the tone map lets it).
            Array.Clear(A, 0, W * H);
            int NS = Math.Max(1, s.tubeStamps);
            for (int i = 0; i < NS; i++)
            {
                double sv = (i + 0.5) / NS;
                double x = nx - R * 0.95 - sv * L;
                double y = cy + 5.6 * Math.Pow(sv, 1.15) * Math.Sin(TAU * 1.35 * sv - 2.0 * tph);
                double r = R * (0.46 - 0.40 * Math.Pow(sv, 0.80));   // 0.46R at the root closing to almost nothing
                AddEllipse(A, fr, x, y, r * s.own.tubeXstretch, Math.Max(r, 0.5), 0.0, s.own.tubeP, 0.150 * Math.Pow(1.0 - sv, 1.60));
            }
            Smear(A, fr, s.smearTaps, s.own.smearDecay, s.smearNorm, scratch);
            AddScaled(E, A, s.own.smearPostScale, W * H);
            // --- the cloud, the nucleus (breathing) and a second one riding behind it.
            AddNosed(E, fr, nx, cy, R * 1.62, R * 1.50, s.live.noseSquash, 0.0, s.own.cloudP, s.own.cloudAmp);
            double pulse = 1.0 + 0.10 * Math.Sin(2.0 * tph);
            AddNosed(E, fr, nx, cy, R * 0.98, R * 0.92, 0.88, s.own.nucleusFlat, s.own.nucleusP, s.own.nucleusAmp * pulse);
            AddEllipse(E, fr, nx - R * 0.62, cy, R * 0.92, R * 0.66, 0.0, 1.70, s.own.nucleus2Amp);
            // --- veils: two faint sheets drifting back through the trail.
            var turb = new OrbTurb(Seed(seed + s.turbSeedOffVeil), s.turbOctVeil, s.turbScaleVeil, s.turbAnisoVeil);
            fr.XRange(nx - L, nx, out int x0, out int x1);
            fr.YRange(cy - R * 1.5, cy + R * 1.5, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                double vt = Math.Pow(Clip01(1.0 - Math.Abs(v) / (R * 1.5)), 0.8);
                if (vt <= 0.0) continue;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    double X = fr.Xs(px);
                    double sx = Clip01((nx - X) / L);
                    if (sx <= 0.0 || sx >= 1.0) continue;
                    double n1 = Math.Max(turb.Eval(X, Y, tph), 0.0);
                    if (n1 <= 0.0) continue;
                    E[row + px] += (float)(fr.amp * s.own.veilAmp * n1 * Math.Pow(sx, 0.55) * Math.Pow(1.0 - sx, 1.2) * vt);
                }
            }
            AddGlow(E, fr, s.live.glowWide, s.live.glowTail, s.live.glowAmp);
        }

        public static void Coronal(OrbForm.CoronalSettings s, in OrbFrame fr, int t, int n, long seed, float[] E, float[] A, float[] scratch)
        {
            double tph = TAU * t / n, nx = fr.nx, cy = fr.cy, R = fr.R;
            int W = fr.W, H = fr.H;
            // --- prominences: chains of stamps leaving the surface radially and swept into −x as they climb.
            Array.Clear(A, 0, W * H);
            int NP = Math.Max(1, s.prominences), NSEG = Math.Max(1, s.prominenceSegments);
            for (int k = 0; k < NP; k++)
            {
                var rng = new PlusNumpyRng(Seed(seed + k * 977));
                int m = 1 + (k % 3);   // its own integer temporal harmonic: the set repeats on the loop, no two move together
                double a0 = TAU * k / NP + 0.55 * Math.Sin(m * tph + k);
                double th = a0 - 1.15 * Math.Sin(tph * 2.0 + k * 1.7) * 0.18;
                double length = R * (0.95 + 0.75 * (0.5 + 0.5 * Math.Sin(3.0 * tph + k * 2.1)));
                length *= 0.42 + 0.58 * (0.5 - 0.5 * Math.Cos(th));   // stubs into the airstream, long with it
                for (int i = 0; i < NSEG; i++)
                {
                    double sv = (i + 0.5) / NSEG;
                    double rr = R * 0.80 + length * sv;
                    double px = nx + rr * Math.Cos(th) - Math.Pow(sv, 1.7) * length * 1.30;
                    double py = cy + rr * Math.Sin(th) * (1.0 - 0.28 * sv);
                    double r = R * (0.26 - 0.14 * sv) * (0.8 + 0.4 * rng.NextDouble());
                    AddEllipse(A, fr, px, py, r * 1.7, Math.Max(r, 0.4), 0.0, s.own.prominenceP, s.own.prominenceAmp * Math.Pow(1.0 - sv, 1.15));
                }
            }
            Smear(A, fr, s.smearTaps, s.own.smearDecay, s.smearNorm, scratch);
            AddScaled(E, A, 1.0, W * H);
            // --- the star: granulated interior (the noise MULTIPLIES the body, the limb stays clean) and a bright limb.
            var turb = new OrbTurb(Seed(seed + s.turbSeedOffBody), s.turbOctBody, s.turbScaleBody, s.turbAnisoBody);
            double reach = s.own.limbR + s.own.limbW;
            fr.XRange(nx - reach * R * (2.0 - s.live.noseSquash), nx + reach * R, out int x0, out int x1);
            fr.YRange(cy - reach * R, cy + reach * R, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    double X = fr.Xs(px), u = X - nx;
                    double d = Hyp(Nose(u, s.live.noseSquash) / R, v / R);
                    double e = 0.0;
                    if (d < 1.0) e += s.own.coreAmp * Kern(d, 0.0, s.own.coreP) * (1.0 + s.own.coreGranulation * turb.Eval(X, Y, tph));
                    double dl = Math.Abs(d - s.own.limbR) / s.own.limbW;
                    if (dl < 1.0) e += s.own.limbAmp * Kern(dl, 0.0, s.own.limbP);
                    if (e != 0.0) E[row + px] += (float)(fr.amp * e);
                }
            }
            AddGlow(E, fr, s.live.glowWide, s.live.glowTail, s.live.glowAmp);
        }

        public static void Membrane(OrbForm.MembraneSettings s, in OrbFrame fr, int t, int n, long seed, float[] E, float[] A, float[] scratch, List<(int, int)> emit)
        {
            double tph = TAU * t / n, nx = fr.nx, cy = fr.cy, R = fr.R, L = fr.L;
            int W = fr.W, H = fr.H;
            // --- the dissolving veil: filled soft shells shed backward, each thinner and more broken than the last.
            Array.Clear(A, 0, W * H);
            var turb = new OrbTurb(Seed(seed + s.turbSeedOffShell), s.turbOctShell, s.turbScaleShell, s.turbAnisoShell);
            Emit(t, n, s.emitPeriod, s.emitLife, emit);
            foreach (var (age, bid) in emit)
            {
                double f = age / (double)s.emitLife;
                double x = nx - R * 0.55 - f * L * 0.92;
                double rr = R * (0.92 - 0.42 * f), rx = rr * (0.80 + 0.20 * f);
                double fade = Math.Pow(1.0 - f, 0.75);
                fr.XRange(x - rx, x + rx, out int x0, out int x1);
                fr.YRange(cy - rr, cy + rr, out int y0, out int y1);
                for (int py = y0; py <= y1; py++)
                {
                    double Y = fr.Ys(py), dy = (Y - cy) / rr;
                    int row = py * W;
                    for (int px = x0; px <= x1; px++)
                    {
                        double X = fr.Xs(px), dx = (X - x) / rx;
                        double dsh = Math.Sqrt(dx * dx + dy * dy);
                        if (dsh >= 1.0) continue;
                        // the break only ever SCALES (never clips), so it cannot draw an edge the shape did not have
                        double brk = fade * (0.62 + 0.38 * turb.Eval(X, Y, tph + f * 1.7));
                        A[row + px] += (float)(fr.amp * s.own.shellAmp * Math.Max(brk, 0.0) * Kern(dsh, 0.0, s.own.shellP));
                    }
                }
            }
            Smear(A, fr, s.smearTaps, s.own.smearDecay, s.smearNorm, scratch);
            AddScaled(E, A, 1.0, W * H);
            // --- motes: what the skin comes apart INTO — tiny, streaked, scattering laterally.
            Emit(t, n, s.emitPeriodMotes, s.emitLifeMotes, emit);
            foreach (var (age, bid) in emit)
            {
                double f = age / (double)s.emitLifeMotes;
                var rng = new PlusNumpyRng(Seed(seed * 17 + bid * 6151));
                for (int q = 0; q < s.motesPerPiece; q++)
                {
                    double ang = rng.Uniform(0.0, TAU);
                    double vx = -rng.Uniform(4.0, 7.5);
                    double vy = rng.Uniform(-1.3, 1.3);
                    double x = nx - R * 0.7 + vx * age + R * 0.8 * Math.Cos(ang);
                    double y = cy + vy * age + R * 0.85 * Math.Sin(ang);
                    AddStreak(E, fr, x, y, vx, vy, 1.6 + 1.2 * rng.NextDouble(), s.own.moteElong, s.own.moteAmp * Math.Pow(1.0 - f, 1.4));
                }
            }
            // --- the bubble: an interior fill you can see through, a skin that is a THICKENING not an outline, ripples.
            double reach = Math.Max(s.own.skinR + s.own.skinW, s.own.ripR + s.own.ripW);
            fr.XRange(nx - reach * R * (2.0 - s.live.noseSquash), nx + reach * R, out int bx0, out int bx1);
            fr.YRange(cy - reach * R * 0.97, cy + reach * R * 0.97, out int by0, out int by1);
            for (int py = by0; py <= by1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                int row = py * W;
                for (int px = bx0; px <= bx1; px++)
                {
                    double X = fr.Xs(px), u = X - nx;
                    double d = Hyp(Nose(u, s.live.noseSquash) / R, v / (R * 0.97));
                    double e = 0.0;
                    if (d < 1.0) e += s.own.windowAmp * Kern(d, 0.0, s.own.windowP);
                    double ds = Math.Abs(d - s.own.skinR) / s.own.skinW;
                    if (ds < 1.0) e += s.own.skinAmp * Kern(ds, 0.0, s.own.skinP);
                    double dr = Math.Abs(d - s.own.ripR) / s.own.ripW;
                    if (dr < 1.0)
                    {
                        double rip = 1.0 + s.own.ripDepth * Math.Sin(s.ripOrder * Math.Atan2(v, u) - 3.0 * tph);
                        e += s.own.ripAmp * rip * Kern(dr, 0.0, s.own.ripP);
                    }
                    if (e != 0.0) E[row + px] += (float)(fr.amp * e);
                }
            }
            // --- the nucleus on a figure-eight inside the bubble, biased forward.
            double nxp = nx + R * 0.20 + R * 0.26 * Math.Cos(tph);
            double nyp = cy + R * 0.24 * Math.Sin(2.0 * tph);
            AddEllipse(E, fr, nxp, nyp, R * 0.40, R * 0.36, s.own.nucleusFlat, s.own.nucleusP, s.own.nucleusAmp);
            AddGlow(E, fr, s.live.glowWide, s.live.glowTail, s.live.glowAmp);
        }

        public static void Voltcore(OrbForm.VoltcoreSettings s, in OrbFrame fr, int t, int n, long seed, float[] E, float[] A, float[] scratch, List<(int, int)> emit)
        {
            double tph = TAU * t / n, nx = fr.nx, cy = fr.cy, R = fr.R, L = fr.L;
            int W = fr.W, H = fr.H;
            // --- filaments: random walks launched off the ball, dragged backward more the older they are.
            Array.Clear(A, 0, W * H);
            Emit(t, n, s.emitPeriod, s.emitLife, emit);
            int steps = Math.Max(1, s.filamentSteps);
            foreach (var (age, bid) in emit)
            {
                double f = age / (double)s.emitLife;
                var rng = new PlusNumpyRng(Seed(seed * 31 + bid * 5077));
                for (int b = 0; b < s.branchesPerPiece; b++)
                {
                    double th = rng.Uniform(0.0, TAU);
                    double x = nx + R * 0.55 * Math.Cos(th), y = cy + R * 0.50 * Math.Sin(th);
                    double dx = Math.Cos(th), dy = Math.Sin(th);
                    for (int i = 0; i < steps; i++)
                    {
                        double step = 3.4 * (1.0 - 0.35 * i / 9.0);
                        double ang = Math.Atan2(dy, dx) + rng.Uniform(-0.85, 0.85);
                        dx = Math.Cos(ang); dy = Math.Sin(ang);
                        x += dx * step - (5.0 + 2.5 * i / 9.0) * age * 0.62;
                        y += dy * step;
                        double r = (1.7 - 0.9 * i / 9.0) * (1.0 - 0.35 * f);
                        AddStreak(A, fr, x, y, dx * step - 4.0 * age, dy * step, Math.Max(r, 0.55), s.own.filamentElong,
                                  s.own.filamentAmp * Math.Pow(1.0 - f, 1.4) * (1.0 - 0.55 * i / 9.0));
                    }
                }
            }
            Smear(A, fr, s.smearTaps, s.own.smearDecay, s.smearNorm, scratch);
            AddScaled(E, A, 1.0, W * H);
            // --- the plasma tail: a tapering tube of charged haze off the back, stamped then smeared unnormalised.
            Array.Clear(A, 0, W * H);
            int NT = Math.Max(1, s.tailStamps);
            for (int i = 0; i < NT; i++)
            {
                double sv = (i + 0.5) / NT;
                double x = nx - R * 0.6 - sv * L;
                double y = cy + R * 0.42 * Math.Pow(sv, 1.2) * Math.Sin(TAU * 1.15 * sv - 2.0 * tph);
                double r = R * (0.70 - 0.54 * Math.Pow(sv, 0.9));
                AddEllipse(A, fr, x, y, r * 1.8, Math.Max(r, 0.5), 0.0, s.own.tailP, 0.095 * Math.Pow(1.0 - sv, 1.35));
            }
            Smear(A, fr, s.tailSmearTaps, s.own.tailSmearDecay, s.tailSmearNorm, scratch);
            AddScaled(E, A, s.own.tailPostScale, W * H);
            // --- the envelope (a halo the ball wears, not a region it sits in) with charge crawling through it.
            var turb = new OrbTurb(Seed(seed + s.turbSeedOffCharge), s.turbOctCharge, s.turbScaleCharge, s.turbAnisoCharge);
            double rx = R * 1.50, ry = R * 1.26;
            fr.XRange(nx - rx * (2.0 - 0.94) - R * 0.08, nx + rx, out int x0, out int x1);
            fr.YRange(cy - ry, cy + ry, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double Y = fr.Ys(py), v = Y - cy;
                int row = py * W;
                for (int px = x0; px <= x1; px++)
                {
                    double X = fr.Xs(px), u = X - nx;
                    double de = Hyp((Nose(u, 0.94) + R * 0.08) / rx, v / ry);
                    if (de >= 1.0) continue;
                    double e = s.own.envelopeAmp * Kern(de, 0.0, s.own.envelopeP);
                    double n1 = turb.Eval(X, Y, tph);
                    if (n1 > 0.0) e += s.own.chargeAmp * n1 * Kern(de, 0.0, 1.9);
                    E[row + px] += (float)(fr.amp * e);
                }
            }
            // --- the bead (the one near-opaque thing), its soft halo, and the ball around them.
            AddNosed(E, fr, nx, cy, R * 0.34, R * 0.32, s.live.noseSquash, s.own.beadFlat, s.own.beadP, s.own.beadAmp);
            AddNosed(E, fr, nx, cy, R * 0.70, R * 0.64, s.live.noseSquash, 0.0, 1.70, s.own.bead2Amp);
            AddNosed(E, fr, nx, cy, R * 1.08, R * 1.00, s.live.noseSquash, 0.0, s.own.ballP, s.own.ballAmp);
            AddGlow(E, fr, s.live.glowWide, s.live.glowTail, s.live.glowAmp);
        }

        /// A nose-squashed ellipse kern(hyp(nose(u)/rx, v/ry), flat, p) × amp centred on (cx, cy).
        static void AddNosed(float[] E, in OrbFrame fr, double cx, double cyy, double rx, double ry, double squash, double flat, double p, double amp)
        {
            amp *= fr.amp;
            if (amp == 0.0) return;
            fr.XRange(cx - rx * (2.0 - squash), cx + rx * squash, out int x0, out int x1);
            fr.YRange(cyy - ry, cyy + ry, out int y0, out int y1);
            for (int py = y0; py <= y1; py++)
            {
                double v = (fr.Ys(py) - cyy) / ry;
                int row = py * fr.W;
                for (int px = x0; px <= x1; px++)
                {
                    double u = Nose(fr.Xs(px) - cx, squash) / rx;
                    double d = Math.Sqrt(u * u + v * v);
                    if (d >= 1.0) continue;
                    E[row + px] += (float)(amp * Kern(d, flat, p));
                }
            }
        }

        // ── shade ───────────────────────────────────────────────────────────────────────────────────────────

        /// orbcanvas.lut: the ramp's control points → 256 × rgb, np.interp over float32 positions and colours at the
        /// float32 np.linspace(0, 1, 256) samples, unrounded (the pixel stage adds 0.5 and truncates).
        public static double[] BakeLut(PlusRamp ramp)
        {
            var stops = new List<PlusRampStop>(ramp != null && ramp.stops != null ? ramp.stops : new List<PlusRampStop>());
            if (stops.Count == 0) stops.Add(new PlusRampStop(0f, Color.white));
            stops.Sort((a, b) => a.pos.CompareTo(b.pos));
            int m = stops.Count;
            var xs = new double[m]; var cs = new double[m, 3];
            for (int i = 0; i < m; i++)
            {
                xs[i] = (float)Mathf.Clamp01(stops[i].pos);
                cs[i, 0] = (float)Mathf.Round(Mathf.Clamp01(stops[i].color.r) * 255f);
                cs[i, 1] = (float)Mathf.Round(Mathf.Clamp01(stops[i].color.g) * 255f);
                cs[i, 2] = (float)Mathf.Round(Mathf.Clamp01(stops[i].color.b) * 255f);
            }
            var lut = new double[256 * 3];
            for (int i = 0; i < 256; i++)
            {
                double t = i == 255 ? 1.0 : (float)(i * (1.0 / 255.0));   // np.linspace(0, 1, 256, dtype=float32)
                int j;
                if (t <= xs[0]) j = -1;
                else if (t >= xs[m - 1]) j = m - 1;
                else { j = 0; while (j + 1 < m && xs[j + 1] <= t) j++; }
                for (int c = 0; c < 3; c++)
                {
                    double v;
                    if (j < 0) v = cs[0, c];
                    else if (j >= m - 1) v = cs[m - 1, c];
                    else if (xs[j] == t) v = cs[j, c];
                    else
                    {
                        double slope = (cs[j + 1, c] - cs[j, c]) / (xs[j + 1] - xs[j]);
                        v = slope * (t - xs[j]) + cs[j, c];
                    }
                    lut[i * 3 + c] = v;
                }
            }
            return lut;
        }

        /// orbcanvas.rasterise + despeckle on a y-DOWN field: straight RGBA into `target` (y-UP, the renderer's rows),
        /// the tone / alpha planes for the parity dump when asked. `layerAlpha` multiplies the window alpha.
        public static void Rasterise(float[] E, int W, int H, in OrbStyle st, double layerAlpha, bool despeckle, int despeckleBelow,
                                     Color32[] target, float[] toneOut, float[] alphaOut, byte[] aScratch)
        {
            int n = W * H;
            for (int y = 0; y < H; y++)
            {
                int row = y * W, trow = (H - 1 - y) * W;
                for (int x = 0; x < W; x++)
                {
                    double tone = st.Tone(E[row + x]);
                    double a = st.Alpha(tone);
                    if (toneOut != null) toneOut[row + x] = (float)tone;
                    if (alphaOut != null) alphaOut[row + x] = (float)a;
                    double ti = tone * 255.0;
                    int idx = ti >= 255.0 ? 255 : ti <= 0.0 ? 0 : (int)ti;   // astype(int32) truncates
                    double av = a * layerAlpha * 255.0 + 0.5;
                    int A = av >= 255.0 ? 255 : av <= 0.0 ? 0 : (int)av;
                    if (A < st.floor) { target[trow + x] = default; aScratch[row + x] = 0; continue; }
                    byte r = Chan(st.lut[idx * 3]), g = Chan(st.lut[idx * 3 + 1]), b = Chan(st.lut[idx * 3 + 2]);
                    target[trow + x] = new Color32(r, g, b, (byte)A);
                    aScratch[row + x] = (byte)A;
                }
            }
            if (!despeckle) return;
            // drop lit pixels that are BOTH faint and isolated (4-neighbour count over a WRAPPED plane, as np.roll does)
            for (int y = 0; y < H; y++)
            {
                int yu = (y - 1 + H) % H, yd = (y + 1) % H;
                for (int x = 0; x < W; x++)
                {
                    byte a = aScratch[y * W + x];
                    if (a == 0 || a >= despeckleBelow) continue;
                    int xl = (x - 1 + W) % W, xr = (x + 1) % W;
                    int cnt = (aScratch[yu * W + x] > 0 ? 1 : 0) + (aScratch[yd * W + x] > 0 ? 1 : 0)
                            + (aScratch[y * W + xl] > 0 ? 1 : 0) + (aScratch[y * W + xr] > 0 ? 1 : 0);
                    if (cnt < 2) target[(H - 1 - y) * W + x] = default;
                }
            }
        }

        static byte Chan(double v) { double r = v + 0.5; return (byte)(r >= 255.0 ? 255 : r <= 0.0 ? 0 : (int)r); }
    }
}
