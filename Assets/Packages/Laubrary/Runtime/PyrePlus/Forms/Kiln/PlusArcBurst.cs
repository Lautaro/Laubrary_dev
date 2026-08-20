// PlusArcBurst — the algorithm behind ArcBurstForm: Kiln "Energy Explosion / agent4" ARC BURST, generation 4
// (D:/CODEZ/Kiln/projects/Energy Explosion/agents/agent4: generate.py + arclib4.py + arclib.py, MANIFEST "generation
// 4 — the ARC burst, transparent to the core").
//
// The source draws each frame from scratch: strokes (midpoint-displaced polylines, branching trees, jittered
// circle arcs, filled irregular bodies) are deposited with MAXIMUM compositing into a float32 ENERGY plane E and,
// already normalised (`a = clip(e/aref)^agamma · opa`), into an ALPHA plane A; a 3-pass zero-padded box blur blooms
// both (alpha at 0.34× the strength over 0.8× the radius); E is then posterised into five cel bands by threshold
// and A is the pixel alpha, zeroed under the lowest energy band. Ten draws = ten programs over these primitives;
// every one is ported below as a `layout`, with its literals (counts, radii, stage timings, per-stroke amp / width /
// opacity) either kept verbatim in the code or lifted into that layout's settings class with the literal as the
// default — each default cites its draw_* function.
//
// Components (contract components.json: blob, channel, polyline, bloom, colorize) and the geometry library:
//   PORTED exactly — Field._seg / polyline / channel / dot / blob / bloom / fade_radial / fade_angular / hollow,
//     colorize (band by threshold, alpha = A, truncated to a byte like astype(uint8)), blur (3-pass zero-padded box,
//     axis 0 then axis 1, banker's rounding of the radius), displace, jitter (np.interp over gauss control points),
//     rprofile, tree, veil / veil_deep / veil_at, ghost, radial_opacity, draw_tree_ch, ease_out / ease_in / pulse,
//     ramp, keep_hue, cool, dissolve; the RNG (PlusPyRandom = CPython's random.Random, so the bolts are the SAME
//     bolts, anchor stream and per-frame stream seeded exactly as the source seeds them).
//   APPROXIMATED — float32 arithmetic: the source runs the stroke maths in numpy float32 and the blob / interp maths
//     in float64; this port does the same split but its box blur accumulates in double where numpy's cumsum is
//     float32 (pixel-level ±1 differences, never structural). `radial` (unused by generation 4) is not ported.
//   DROPPED — the export (sprite sheet, WebP, contact/checker sheets) and the partial-alpha census print.
//
// Units: the draws are written in the source's 128 px frame (centre 64, 64); ArcField maps that frame onto the
// canvas with a centre and a scale (canvas/128 × swarm size), so the figure is the same picture at 64 px and 128 px
// and a swarm instance is one burst placed at its particle.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    public struct ArcPt
    {
        public double x, y;
        public ArcPt(double x, double y) { this.x = x; this.y = y; }
        public static ArcPt operator +(ArcPt a, ArcPt b) => new ArcPt(a.x + b.x, a.y + b.y);
        public static ArcPt operator -(ArcPt a, ArcPt b) => new ArcPt(a.x - b.x, a.y - b.y);
        public static ArcPt operator *(ArcPt a, double k) => new ArcPt(a.x * k, a.y * k);
        public double Len => Math.Sqrt(x * x + y * y);
    }

    /// One stroke path of a tree with its generation weight (1 = trunk, × bAmp per generation).
    public struct ArcSeg
    {
        public List<ArcPt> pts; public double w;
    }

    /// arclib4.Field: E (energy → colour band) and A (normalised alpha) float planes over an S×S buffer, y-down.
    /// Draw calls take SOURCE coordinates (the 128 px frame); `ox, oy, s` place that frame on the buffer.
    public sealed class ArcField
    {
        public readonly int S;
        public readonly float[] E, A;
        public float aref, agamma;
        public double ox, oy, s;   // pixel = (src − 64) · s + o

        public ArcField(int size, float[] e, float[] a) { S = size; E = e; A = a; }

        public void Clear() { Array.Clear(E, 0, E.Length); Array.Clear(A, 0, A.Length); }

        double PX(double x) => (x - 64.0) * s + ox;
        double PY(double y) => (y - 64.0) * s + oy;

        // ── deposition (maximum compositing into both planes) ──
        void Put(int i, float e, float opa)
        {
            if (e > E[i]) E[i] = e;
            float a = e / aref; if (a > 1f) a = 1f; else if (a < 0f) a = 0f;
            a = Mathf.Pow(a, agamma);
            if (opa != 1f) a *= opa;
            if (a > A[i]) A[i] = a;
        }

        // ── the skip test: a deposit that cannot raise either plane is never computed ──
        // Because `Put` is a pure maximum, a pixel whose energy and alpha are already at or above anything this
        // stroke could give it is a no-op, and the output is bit-for-bit the same whether the stroke's exp / pow
        // ran there or not. Both bounds come from monotone tables sampled BELOW the true argument (a lower x gives
        // a higher falloff, a higher index gives a higher gamma curve) and widened by a few ULPs, so they stay upper
        // bounds even if the C runtime's exp / pow are not perfectly monotone at the last bit. Overlap is the
        // common case — a channel's core lies inside its own sheath, a tree's branches cross, a swarm stacks
        // bursts — so most covered pixels end here, after a square root and two table reads.
        const int FalloffN = 4096; const float FalloffXMax = 64f, FalloffScale = FalloffN / FalloffXMax;
        const float BoundSlack = 1.00001f;
        static readonly float[] FalloffUpper = BuildFalloffUpper();
        static float[] BuildFalloffUpper()
        {
            var t = new float[FalloffN + 1];
            for (int j = 0; j <= FalloffN; j++) t[j] = (float)Math.Exp(-Math.Pow(j / FalloffScale, 1.7)) * BoundSlack;
            return t;
        }
        const int GammaN = 1024;
        float[] _gammaUpper; float _gammaUpperFor = float.NaN;
        float[] GammaUpper()
        {
            if (_gammaUpperFor == agamma) return _gammaUpper;
            _gammaUpper ??= new float[GammaN + 1];
            for (int j = 0; j <= GammaN; j++) _gammaUpper[j] = Mathf.Pow(j / (float)GammaN, agamma) * BoundSlack;
            _gammaUpperFor = agamma;
            return _gammaUpper;
        }

        /// `_seg`: e = amp · exp(−(d / width)^1.7) over the segment's padded bbox (source units in, pixels inside).
        public void Seg(double sx0, double sy0, double sx1, double sy1, double widthSrc, double amp, double opa)
        {
            float x0 = (float)PX(sx0), y0 = (float)PY(sy0), x1 = (float)PX(sx1), y1 = (float)PY(sy1);
            double width = widthSrc * s;
            double pad = width * 3.0 + 2.0;
            int xs = Math.Max(0, (int)(Math.Min(x0, x1) - pad)), xe = Math.Min(S, (int)(Math.Max(x0, x1) + pad) + 1);
            int ys = Math.Max(0, (int)(Math.Min(y0, y1) - pad)), ye = Math.Min(S, (int)(Math.Max(y0, y1) + pad) + 1);
            if (xs >= xe || ys >= ye) return;
            float dx = x1 - x0, dy = y1 - y0;
            float L2 = dx * dx + dy * dy;
            float invW = 1f / (float)Math.Max(width, 1e-3), fa = (float)amp, fo = (float)opa;
            bool point = L2 < 1e-9f;
            float[] falloff = FalloffUpper, gamma = GammaUpper();
            float invAref = 1f / aref;
            float[] E = this.E, A = this.A;
            for (int y = ys; y < ye; y++)
            {
                float Y = y;
                for (int x = xs; x < xe; x++)
                {
                    float X = x, d;
                    if (point) d = Hypot(X - x0, Y - y0);
                    else
                    {
                        float t = ((X - x0) * dx + (Y - y0) * dy) / L2;
                        if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                        d = Hypot(X - (x0 + t * dx), Y - (y0 + t * dy));
                    }
                    float xw = d * invW;
                    int i = y * S + x;
                    int fj = (int)(xw * FalloffScale); if (fj > FalloffN) fj = FalloffN;
                    float eUpper = fa * falloff[fj];
                    if (eUpper <= E[i])
                    {
                        float au = eUpper * invAref; if (au > 1f) au = 1f;
                        int gj = (int)(au * GammaN) + 1; if (gj > GammaN) gj = GammaN;
                        if (gamma[gj] * fo <= A[i]) continue;
                    }
                    // `d * invW` stays inline here: Mono promotes the unrounded product to double, and rounding it
                    // through a float local first moves the last bit of a few pixels (Crown's hash changes).
                    float e = fa * (float)Math.Exp(-Math.Pow(d * invW, 1.7));
                    Put(i, e, fo);
                }
            }
        }

        static float Hypot(float a, float b) => (float)Math.Sqrt((double)a * a + (double)b * b);

        /// `polyline`: taper shrinks width toward the far end, fade shrinks amplitude; `opaVec` = one opacity per
        /// VERTEX (null ⇒ the scalar `opa`). Segments under the cull (w ≤ 0.05, a ≤ 0.01, o ≤ 0.002) are skipped.
        public void Polyline(List<ArcPt> pts, double width, double amp, double taper, double fade, double opa, double[] opaVec, double[] weights = null)
        {
            int n = pts.Count - 1;
            if (n < 1) return;
            for (int i = 0; i < n; i++)
            {
                double u = i / (double)Math.Max(n - 1, 1);
                double w = width * (1.0 - taper * u);
                double a = amp * (1.0 - fade * u);
                if (weights != null) a *= 0.5 * (weights[i] + weights[i + 1]);
                double o = opaVec != null ? 0.5 * (opaVec[i] + opaVec[i + 1]) : opa;
                if (w <= 0.05 || a <= 0.01 || o <= 0.002) continue;
                Seg(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, w, a, o);
            }
        }

        /// `channel`: a hot core inside a wider dim sheath (sheath first; maximum keeps the core).
        public void Channel(List<ArcPt> pts, double core, double sheath, double amp, double taper = 0.35, double fade = 0.0, double dim = 0.42, double opa = 1.0, double[] opaVec = null)
        {
            Polyline(pts, sheath, amp * dim, taper, fade, opa, opaVec);
            Polyline(pts, core, amp, taper, fade, opa, opaVec);
        }

        public void Dot(double x, double y, double r, double amp, double opa = 1.0) => Seg(x, y, x, y, r, amp, opa);

        /// `blob`: a filled irregular body of light, soft-edged, optionally hollow. `profile` is a periodic radius
        /// table (64 entries unless given), interpolated round the angle like np.interp(period = τ).
        public void Blob(double cx, double cy, double r, double amp, double[] profile, double soft, double edge, double inner, double holeSoft, double opa)
        {
            int n = profile?.Length ?? 64;
            float pcx = (float)PX(cx), pcy = (float)PY(cy);
            double rs = r * s, step = (2.0 * Math.PI) / n;
            const float tauF = 6.2831855f;
            float fo = (float)opa;
            double invSoft = 1.0 / Math.Max(soft, 1e-3), invHole = 1.0 / Math.Max(holeSoft, 1e-3);
            // Outside the profile's largest radius u ≥ 1, so the soft edge clamps e to exactly 0 and nothing is
            // deposited — the scan can stop at that radius (plus a pixel for the rounding of d) and be the same.
            double rMax = rs; if (profile != null) { rMax = 0; for (int i = 0; i < n; i++) rMax = Math.Max(rMax, rs * profile[i]); }
            int xs = Math.Max(0, (int)Math.Floor(pcx - rMax) - 1), xe = Math.Min(S, (int)Math.Ceiling(pcx + rMax) + 2);
            int ys = Math.Max(0, (int)Math.Floor(pcy - rMax) - 1), ye = Math.Min(S, (int)Math.Ceiling(pcy + rMax) + 2);
            for (int y = ys; y < ye; y++)
                for (int x = xs; x < xe; x++)
                {
                    float fx = x - pcx, fy = y - pcy;
                    float ang = Mathf.Atan2(fy, fx) % tauF; if (ang < 0f) ang += tauF;
                    double R;
                    if (profile == null) R = rs;
                    else
                    {
                        double q = ang / step; int i0 = (int)Math.Floor(q); double fr = q - i0;
                        if (i0 >= n) { i0 = n - 1; fr = (ang - i0 * step) / step; }
                        int i1 = i0 + 1 >= n ? 0 : i0 + 1;
                        R = rs * (profile[i0] + (profile[i1] - profile[i0]) * fr);
                    }
                    float d = Hypot(fx, fy);
                    double u = d / Math.Max(R, 1e-3);
                    double e = (1.0 - u) * invSoft; if (e < 0) e = 0; else if (e > 1) e = 1;
                    e = amp * Math.Pow(e, edge);
                    if (inner > 0.0)
                    {
                        double h = (u - inner) * invHole; if (h < 0) h = 0; else if (h > 1) h = 1;
                        e *= h;
                    }
                    if (e > 0) Put(y * S + x, (float)e, fo);
                }
        }

        // ── post ──
        /// `bloom`: E += blur(E, r)·strength, A += blur(A, round(0.8 r))·aStrength — both from the pre-bloom planes.
        public void Bloom(double radiusPx, double strength, double aStrength, float[] scratchA, float[] scratchB)
        {
            if (radiusPx <= 0 || strength <= 0) return;
            int r = (int)Math.Round(radiusPx, MidpointRounding.ToEven);
            int ra = Math.Max(1, (int)Math.Round(radiusPx * 0.8, MidpointRounding.ToEven));
            Array.Copy(E, scratchA, E.Length); BlurZero(scratchA, S, r, scratchB);
            for (int i = 0; i < E.Length; i++) E[i] += scratchA[i] * (float)strength;
            Array.Copy(A, scratchA, A.Length); BlurZero(scratchA, S, ra, scratchB);
            for (int i = 0; i < A.Length; i++) A[i] += scratchA[i] * (float)aStrength;
        }

        /// arclib.blur: three passes of a ZERO-padded box (axis 0 then axis 1), each divided by 2r+1 — the edge
        /// darkens instead of clamping, which is why PlusFieldOps.BoxBlur (edge-clamped) is not used here.
        public static void BlurZero(float[] f, int S, int r, float[] tmp)
        {
            if (r < 1) return;
            double inv = 1.0 / (2 * r + 1);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int x = 0; x < S; x++)   // axis 0: down each column
                {
                    double sum = 0; for (int i = 0; i <= r && i < S; i++) sum += f[i * S + x];
                    for (int y = 0; y < S; y++)
                    {
                        tmp[y * S + x] = (float)(sum * inv);
                        int add = y + r + 1, sub = y - r;
                        if (add < S) sum += f[add * S + x];
                        if (sub >= 0) sum -= f[sub * S + x];
                    }
                }
                for (int y = 0; y < S; y++)   // axis 1: along each row
                {
                    int row = y * S; double sum = 0; for (int i = 0; i <= r && i < S; i++) sum += tmp[row + i];
                    for (int x = 0; x < S; x++)
                    {
                        f[row + x] = (float)(sum * inv);
                        int add = x + r + 1, sub = x - r;
                        if (add < S) sum += tmp[row + add];
                        if (sub >= 0) sum -= tmp[row + sub];
                    }
                }
            }
        }

        /// `fade_angular`: scale ALPHA by angle about (cx, cy) — a transparent sector of half-width `width` rad.
        public void FadeAngular(double cx, double cy, double a0, double width, double depth, double soft)
        {
            float pcx = (float)PX(cx), pcy = (float)PY(cy);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float ang = Mathf.Atan2(y - pcy, x - pcx);
                    double d = ang - a0;
                    d = Math.Atan2(Math.Sin(d), Math.Cos(d));   // np.angle(exp(1j·d))
                    d = Math.Abs(d);
                    double m = 1.0 - (d - width) / Math.Max(soft, 1e-3); if (m < 0) m = 0; else if (m > 1) m = 1;
                    A[y * S + x] *= (float)(1.0 - depth * m);
                }
        }

        /// `hollow`: cut a hole in BOTH planes (radius r0, ramp `soft`, source units).
        public void Hollow(double cx, double cy, double r0, double soft)
        {
            float pcx = (float)PX(cx), pcy = (float)PY(cy);
            double rp = r0 * s, sp = Math.Max(soft * s, 1e-3);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Hypot(x - pcx, y - pcy);
                    double m = (d - rp) / sp; if (m < 0) m = 0; else if (m > 1) m = 1;
                    int i = y * S + x; E[i] *= (float)m; A[i] *= (float)m;
                }
        }

        public void ScaleAlpha(double k) { float f = (float)k; for (int i = 0; i < A.Length; i++) A[i] *= f; }
    }

    public static class PlusArcBurst
    {
        const double TAU = 2.0 * Math.PI;
        const double CX = 64.0, CY = 64.0;

        // ── easing / staging (arclib + generate.py) ──
        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static double Ramp(double t, double a, double b) => Clamp01((t - a) / Math.Max(b - a, 1e-6));
        public static double EaseOut(double t, double k) => 1.0 - Math.Pow(1.0 - Clamp01(t), k);
        public static double EaseIn(double t, double k) => Math.Pow(Clamp01(t), k);
        public static double Pulse(double t, double peak, double rise, double fall)
        {
            t = Clamp01(t);
            if (t <= peak) return Math.Pow(t / Math.Max(peak, 1e-6), 1.0 / rise);
            return Math.Max(0.0, 1.0 - Math.Pow((t - peak) / Math.Max(1.0 - peak, 1e-6), fall));
        }
        /// `keep_hue(o)`: energy multiplier paired with a low opacity so a ghost's core lands in the saturated band.
        public static double KeepHue(double o, double floor) => floor + (1.0 - floor) * o;
        public static double Cool(double t, double a, double k) => Math.Pow(1.0 - Ramp(t, a, 1.0), k);
        public static double Dissolve(double t, double a, double k) => Math.Pow(1.0 - Ramp(t, a, 1.0), k);

        // ── geometry (arclib) ──
        static ArcPt Perp(ArcPt v)
        {
            double nx = -v.y, ny = v.x, L = Math.Sqrt(nx * nx + ny * ny);
            return L > 1e-9 ? new ArcPt(nx / L, ny / L) : new ArcPt(0, 0);
        }

        /// `displace`: midpoint displacement between two points — one lightning filament (2^detail + 1 points).
        public static List<ArcPt> Displace(ArcPt p0, ArcPt p1, PlusPyRandom rng, int detail, double rough, double decay = 0.55)
        {
            var pts = new List<ArcPt> { p0, p1 };
            double amp = (p1 - p0).Len * rough;
            for (int lvl = 0; lvl < detail; lvl++)
            {
                var o = new List<ArcPt>(pts.Count * 2) { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    ArcPt p = pts[i], q = pts[i + 1];
                    ArcPt m = (p + q) * 0.5 + Perp(q - p) * rng.Gauss(0.0, amp * 0.5);
                    o.Add(m); o.Add(q);
                }
                pts = o;
                amp *= decay;
            }
            return pts;
        }

        /// numpy.linspace(a, b, n): a + i·step with the last sample exactly b.
        public static double[] Linspace(double a, double b, int n)
        {
            var r = new double[n];
            if (n == 1) { r[0] = a; return r; }
            double step = (b - a) / (n - 1);
            for (int i = 0; i < n; i++) r[i] = a + i * step;
            r[n - 1] = b;
            return r;
        }

        /// `jitter`: roughen an existing polyline at several scales without moving its anchors (`ends`: 0 = both pinned, 1 = start pinned, 2 = free).
        public static List<ArcPt> Jitter(List<ArcPt> src, PlusPyRandom rng, double amp, double decay, int passes, int ends = 0)
        {
            int n = src.Count;
            var off = new ArcPt[n];
            double a = amp;
            for (int pass = 0; pass < passes; pass++)
            {
                int k = Math.Max(2, n / Math.Max((int)(a * 2), 2));
                int m = n / k + 2;
                var ctrl = new double[m];
                for (int i = 0; i < m; i++) ctrl[i] = rng.Gauss(0, a);
                var xs = Linspace(0, m - 1, n);
                for (int i = 0; i < n; i++)
                {
                    double x = xs[i]; int i0 = (int)Math.Floor(x); if (i0 >= m - 1) i0 = m - 2; if (i0 < 0) i0 = 0;
                    double sv = ctrl[i0] + (ctrl[i0 + 1] - ctrl[i0]) * (x - i0);
                    ArcPt nxt = src[Math.Min(i + 1, n - 1)], prv = src[Math.Max(i - 1, 0)];
                    off[i] = off[i] + Perp(nxt - prv) * sv;
                }
                a *= decay;
            }
            var w = new double[n];
            if (ends == 0) { var ls = Linspace(0, Math.PI, n); for (int i = 0; i < n; i++) w[i] = Math.Sin(ls[i]); }
            else if (ends == 1) { var ls = Linspace(0, Math.PI * 0.5, n); for (int i = 0; i < n; i++) w[i] = Math.Sin(ls[i]); }
            else for (int i = 0; i < n; i++) w[i] = 1.0;
            var o = new List<ArcPt>(n);
            for (int i = 0; i < n; i++) o.Add(src[i] + off[i] * w[i]);
            return o;
        }

        /// `rprofile`: a periodic wobbly radius profile for Blob (sum of 3 octaves of sinusoids).
        public static double[] RProfile(PlusPyRandom rng, int n = 64, double amp = 0.16, int octaves = 3)
        {
            var p = new double[n];
            for (int i = 0; i < n; i++) p[i] = 1.0;
            double a = amp;
            for (int k = 0; k < octaves; k++)
            {
                int m = 3 * (k + 1) + (int)rng.RandRange(0, 3);
                double ph = rng.Uniform(0, TAU);
                for (int i = 0; i < n; i++) p[i] += a * Math.Sin(m * (i * TAU / n) + ph);
                a *= 0.55;
            }
            return p;
        }

        /// `veil`: a per-vertex opacity envelope, mostly `base`, dipping toward `floor` in one to a few places.
        public static double[] Veil(PlusPyRandom rng, int n, double @base, int dipsLo, int dipsHi, double floor, double wLo, double wHi, double dLo, double dHi)
        {
            var u = Linspace(0, 1, n);
            var o = new double[n];
            for (int i = 0; i < n; i++) o[i] = @base;
            int dips = (int)rng.RandInt(dipsLo, dipsHi);
            for (int k = 0; k < dips; k++)
            {
                double c = rng.Uniform(0.10, 0.94);
                double w = Math.Max(rng.Uniform(wLo, wHi), 1e-3);
                double d = rng.Uniform(dLo, dHi) * (1.0 - floor);
                for (int i = 0; i < n; i++) { double q = (u[i] - c) / w; o[i] *= 1.0 - d * Math.Exp(-q * q); }
            }
            for (int i = 0; i < n; i++) o[i] = Clamp01(o[i]);
            return o;
        }

        /// `veil_deep`: generation-4 defaults (floor 0.04, width 0.10..0.30, depth 0.70..1.0).
        public static double[] VeilDeep(PlusPyRandom rng, int n, double @base, int dipsLo = 1, int dipsHi = 3, double floor = 0.04,
                                        double wLo = 0.10, double wHi = 0.30, double dLo = 0.70, double dHi = 1.0)
            => Veil(rng, n, @base, dipsLo, dipsHi, floor, wLo, wHi, dLo, dHi);

        /// `veil_at`: one soft transparent window at a KNOWN position along the arc (the travelling blow-out).
        public static double[] VeilAt(int n, double centre, double width, double depth, double @base, double floor)
        {
            var u = Linspace(0, 1, n);
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double q = (u[i] - centre) / Math.Max(width, 1e-3), g = Math.Exp(-q * q);
                o[i] = Clamp01(@base * (1.0 - depth * g) + floor * g);
            }
            return o;
        }

        /// `ghost`: one arm's base opacity for its whole life — 1 with probability 1−p, else a deep 5–13 % ghost
        /// with probability deepP, else uniform(lo, hi). (The second draw is consumed even when deepP is 0.)
        public static double Ghost(PlusPyRandom rng, double p, double lo, double hi, double deepP, double deepLo, double deepHi)
        {
            if (rng.Random() >= p) return 1.0;
            if (rng.Random() < deepP) return rng.Uniform(deepLo, deepHi);
            return rng.Uniform(lo, hi);
        }

        /// `radial_opacity`: per-vertex opacity from distance — `inside` within r0 ramping to `base` outside.
        public static double[] RadialOpacity(List<ArcPt> pts, double r0, double soft, double @base, double inside = 0.0)
        {
            var o = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                double r = Math.Sqrt((pts[i].x - CX) * (pts[i].x - CX) + (pts[i].y - CY) * (pts[i].y - CY));
                double u = Clamp01((r - r0) / Math.Max(soft, 1e-3));
                o[i] = inside + (@base - inside) * u;
            }
            return o;
        }

        /// `tree`: a branching bolt — trunk weight 1, sub-branches × bAmp per generation.
        public static List<ArcSeg> Tree(PlusPyRandom rng, ArcPt origin, ArcPt tip, int detail, double rough, int depth,
                                        int branchesLo, int branchesHi, double bScale, double bSpread, double bAmp = 0.62)
        {
            var o = new List<ArcSeg>();
            void Build(ArcPt a, ArcPt b, int dep, double w)
            {
                var pts = Displace(a, b, rng, detail, rough);
                o.Add(new ArcSeg { pts = pts, w = w });
                if (dep <= 0) return;
                int n = (int)rng.RandInt(branchesLo, branchesHi);
                for (int k = 0; k < n; k++)
                {
                    int i = (int)rng.RandInt(pts.Count / 5, pts.Count - 2);
                    ArcPt root = pts[i];
                    ArcPt tan = pts[Math.Min(i + 2, pts.Count - 1)] - pts[Math.Max(i - 2, 0)];
                    double L = tan.Len;
                    if (L < 1e-6) continue;
                    tan = tan * (1.0 / L);
                    double ang = rng.Uniform(0.28, bSpread) * rng.Sign();
                    double c = Math.Cos(ang), s = Math.Sin(ang);
                    var d = new ArcPt(tan.x * c - tan.y * s, tan.x * s + tan.y * c);
                    double rem = (b - root).Len;
                    double ln = Math.Max(3.0, rem * bScale * rng.Uniform(0.55, 1.15));
                    Build(root, root + d * ln, dep - 1, w * bAmp);
                }
            }
            Build(origin, tip, depth, 1.0);
            return o;
        }

        /// arclib4.draw_tree_ch: core/sheath per path, twigs thinner in both passes and faded in ALPHA (branchOpa).
        public static void DrawTreeCh(ArcField f, List<ArcSeg> segs, double core, double sheath, double amp, double taper, double dim,
                                      double opa, PlusPyRandom rng, double veilP, double branchOpa)
        {
            foreach (var sg in segs)
            {
                double k = 0.42 + 0.58 * sg.w;
                double o = opa * (branchOpa + (1.0 - branchOpa) * sg.w);
                double[] ov = null;
                if (rng != null && veilP > 0.0 && rng.Random() < veilP) ov = VeilDeep(rng, sg.pts.Count, o, 1, 2);
                f.Channel(sg.pts, core * k, sheath * k, amp * sg.w, taper, 0.25 * (1.0 - sg.w), dim, o, ov);
            }
        }

        static ArcPt Polar(double cx, double cy, double r, double a) => new ArcPt(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        static List<ArcPt> ArcPts(double cx, double cy, double r, double a0, double a1, int n)
        {
            var u = Linspace(a0, a1, n); var o = new List<ArcPt>(n);
            for (int i = 0; i < n; i++) o.Add(Polar(cx, cy, r, u[i]));
            return o;
        }
        static double[] Mul(double[] a, double[] b) { var o = new double[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = a[i] * b[i]; return o; }

        /// Everything one frame of one layout needs: the field, the clock, the two RNG seeds and the shared dials.
        public struct Frame
        {
            public ArcField f;
            public double t;            // i / (NF − 1)
            public long seed;           // the Kiln seed (spec seed + layer salt stride)
            public long i;              // frame index for the per-frame stream (held constant when reroll is off)
            public int frameNo;         // how many frames the source's anchor stream has already been advanced by in-loop draws
            public double widthK, ampK, hueFloor, deepLo, deepHi;
            public PlusPyRandom Anchor(long off) => new PlusPyRandom(seed + off);
            public PlusPyRandom PerFrame(long mult) => new PlusPyRandom(seed * mult + i);
            public PlusPyRandom Veil(long a, long k, long b) => new PlusPyRandom(seed * a + k * b + i / 2);
            public double KH(double o) => KeepHue(o, hueFloor);
        }

        // ═══════════════════════════ 1. CORE — a plasma lump with whips lashing off it ═══════════════════════════
        public static void DrawCore(in Frame F, ArcBurstForm.CoreSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(0);
            var prof = RProfile(anchor, amp: 0.13);
            int n = c.whips;
            var wa = new double[n]; for (int k = 0; k < n; k++) wa[k] = k * TAU / n + anchor.Uniform(-0.13, 0.13);
            var curl = new double[n]; for (int k = 0; k < n; k++) curl[k] = anchor.Uniform(-c.curl, c.curl);
            var reach = new double[n]; for (int k = 0; k < n; k++) reach[k] = anchor.Uniform(c.reachLo, c.reachHi);
            var op = new double[n]; for (int k = 0; k < n; k++) op[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);

            var rng = F.PerFrame(977);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double hollow = Ramp(t, 0.32, 0.80);
            double bR = (8.0 + 15.0 * EaseOut(t, 3.2)) * (1.0 + 0.75 * hollow);
            double bamp = c.bodyAmp * (1.0 - Math.Pow(Ramp(t, 0.30, 0.78), 1.2));
            if (bamp > 0.03)
                f.Blob(CX, CY, bR, bamp * F.ampK, prof, 0.95, 1.45, 0.80 * hollow, 0.45, go * (c.bodyOpa - 0.42 * Ramp(t, 0.15, 0.7)));

            double Rout = 15.0 + 47.0 * EaseOut(t, 2.1);
            double life = (0.40 + 0.60 * Pulse(t, 0.22, 2.4, 1.35)) * Cool(t, c.coolStart, 1.2);
            double sweep = EaseOut(t, 1.5);
            for (int k = 0; k < n; k++)
            {
                double a0 = wa[k], r0 = bR * 0.62;
                var root = Polar(CX, CY, r0, a0);
                double L = Rout * reach[k];
                double a1 = a0 + curl[k] * sweep;
                var tip = Polar(CX, CY, L, a1);
                var pts = Displace(root, tip, rng, c.detail, c.rough, 0.58);
                var vr = F.Veil(31, k, 17);
                var vv = VeilDeep(vr, pts.Count, op[k] * go, 1, 2, 0.04);
                f.Channel(pts, (1.05 - 0.3 * t) * F.widthK, (3.0 - 0.9 * t) * F.widthK, 1.25 * life * F.KH(op[k]) * F.ampK, 0.5, 0.0, 0.32, 1.0, vv);
                if (rng.Random() < c.forkP + 0.3 * t)
                {
                    int j = (int)rng.RandRange(pts.Count / 3, pts.Count - 2);
                    double rr = Math.Sqrt((pts[j].x - CX) * (pts[j].x - CX) + (pts[j].y - CY) * (pts[j].y - CY));
                    double a2 = Math.Atan2(pts[j].y - CY, pts[j].x - CX) + rng.Uniform(-0.8, 0.8);
                    double ln = rng.Uniform(7, 19);
                    var tgt = Polar(CX, CY, rr + ln, a2);
                    f.Channel(Displace(pts[j], tgt, rng, 4, 0.22), 0.7 * F.widthK, 1.9 * F.widthK, 0.8 * life * F.ampK, 0.6, 0.4, 0.42, op[k] * go * 0.85);
                }
            }
            // crawlers — short arcs skating over the body's skin
            for (int q = 0; q < (int)(c.crawlers * (1.0 - 0.6 * t)); q++)
            {
                double a0 = rng.Uniform(0, TAU);
                double a1 = a0 + rng.Uniform(0.35, 1.0) * rng.Sign();
                double rr = bR * rng.Uniform(0.88, 1.06);
                var pts = Jitter(ArcPts(CX, CY, rr, a0, a1, 10), rng, 1.5, 0.5, 3);
                double co = rng.Uniform(0.28, 1.0);
                f.Channel(pts, 0.75 * F.widthK, 1.8 * F.widthK, 1.05 * Cool(t, c.coolStart, 1.2) * F.KH(co) * F.ampK, 0.0, 0.0, 0.31, go * co);
            }
        }

        // ═══════════════════════════ 2. WEAVE — the ring, breaking into dashes under a rotating shutter ═══════════
        public static void DrawWeave(in Frame F, ArcBurstForm.WeaveSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(17);
            int n = c.arcs;
            var span = new double[n]; for (int k = 0; k < n; k++) span[k] = anchor.Uniform(c.spanLo, c.spanHi);
            var off = new double[n]; for (int k = 0; k < n; k++) off[k] = anchor.Uniform(0, TAU);
            var band = new double[n]; for (int k = 0; k < n; k++) band[k] = anchor.Uniform(-1.0, 1.0);
            var op = new double[n]; for (int k = 0; k < n; k++) op[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            double shutter = anchor.Uniform(0, TAU);
            var die = new double[n]; for (int k = 0; k < n; k++) die[k] = anchor.Uniform(c.dieLo, c.dieHi);
            var prof = RProfile(anchor, amp: 0.10);

            var rng = F.PerFrame(281);
            double R = 7.0 + 49.0 * EaseOut(t, 2.0);
            double BW = 4.0 + 5.0 * EaseOut(t, 1.4);
            double heart = 1.0 - Ramp(t, 0.04, 0.30);
            if (heart > 0.02)
                f.Blob(CX, CY, R * 1.02, 2.0 * Math.Pow(heart, 0.8) * F.ampK, prof, 0.90, 1.35, 0.78 * Ramp(t, 0.02, 0.28), 0.30, 0.72);
            double cl = Cool(t, c.coolStart, 1.2);
            for (int k = 0; k < n; k++)
            {
                double oa = Clamp01((die[k] - t) / 0.22) * op[k];
                if (oa <= 0.01) continue;
                double a0 = off[k] + t * 0.30;
                double rr = R + band[k] * BW;
                var pts = Jitter(ArcPts(CX, CY, rr, a0, a0 + span[k], 18), rng, c.jitter + 2.6 * t, 0.48, 4);
                double lit = 0.75 + 0.25 * Math.Abs(band[k]);
                var vr = F.Veil(53, k, 29);
                var vv = VeilDeep(vr, pts.Count, oa, 1, 1 + (int)(2 * t), 0.04, 0.10, 0.30, 0.18 + 0.75 * t, 0.35 + 0.65 * t);
                f.Channel(pts, (0.95 - 0.3 * t) * F.widthK, (2.4 - 0.7 * t) * F.widthK, 1.15 * lit * (1.0 - 0.2 * t) * cl * F.KH(op[k]) * F.ampK, 0.0, 0.0, 0.32, 1.0, vv);
            }
            for (int q = 0; q < (int)(1 + c.chords * (1.0 - t)); q++)   // chords across the interior
            {
                double aa = rng.Uniform(0, TAU), bb = aa + rng.Uniform(1.7, Math.PI);
                var pp = Displace(Polar(CX, CY, R, aa), Polar(CX, CY, R, bb), rng, 6, 0.095, 0.58);
                f.Channel(pp, 0.8 * F.widthK, 2.0 * F.widthK, 0.85 * cl * F.ampK, 0.35, 0.0, 0.30, 1.0, VeilDeep(rng, pp.Count, Dissolve(t, c.dissolveStart, 1.05) * 0.75));
            }
            for (int q = 0; q < (int)(5 + c.spurs * t); q++)   // spurs shot outward off the shell
            {
                double a = rng.Uniform(0, TAU);
                var p0 = Polar(CX, CY, R + BW * 0.8, a);
                double L = rng.Uniform(6, 15) * (0.5 + 0.9 * t);
                var p1 = new ArcPt(p0.x + L * Math.Cos(a), p0.y + L * Math.Sin(a));
                f.Channel(Displace(p0, p1, rng, 3, 0.28), 0.8 * F.widthK, 1.9 * F.widthK, 0.95 * cl * F.ampK, 0.65, 0.5, 0.42, Dissolve(t, 0.58, 1.05) * rng.Uniform(0.25, 1.0));
            }
            double sw = Ramp(t, c.shutterStart, 0.86);   // THE SHUTTER: a widening transparent sector sweeps round the ring
            if (sw > 0.01) f.FadeAngular(CX, CY, shutter + t * 3.4, 0.25 + 0.60 * sw, c.shutterDepth * sw, 1.1);
        }

        // ═══════════════════════════ 3. BOLT — five enormous trunks with a travelling blow-out ═══════════════════
        public static void DrawBolt(in Frame F, ArcBurstForm.BoltSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(5);
            int n = c.trunks;
            var bas = new double[n]; for (int k = 0; k < n; k++) bas[k] = k * TAU / n + anchor.Uniform(-0.18, 0.18);
            var reach = new double[n]; for (int k = 0; k < n; k++) reach[k] = anchor.Uniform(c.reachLo, c.reachHi);
            var op = new double[n]; for (int k = 0; k < n; k++) op[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            var blow = new double[n]; for (int k = 0; k < n; k++) blow[k] = anchor.Uniform(c.blowLo, c.blowHi);
            var prof = RProfile(anchor, amp: 0.20);

            var rng = F.PerFrame(613);
            double grow = EaseOut(t, 3.4);
            double life = (0.3 + 0.7 * Pulse(t, 0.15, 3.0, 1.2)) * Cool(t, c.coolStart, 1.2);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            if (life > 0.02)
                for (int k = 0; k < n; k++)
                {
                    double a = bas[k], rr = reach[k];
                    double L = 6.0 + 55.0 * grow * rr;
                    var tip = Polar(CX, CY, L, a);
                    double r0 = 2.0 + 26.0 * Math.Pow(Ramp(t, 0.45, 1.0), 1.5);
                    var root = Polar(CX, CY, r0, a);
                    var segs = Tree(rng, root, tip, c.detail, c.rough, c.depth, c.branchMin, c.branchMax, c.branchScale, c.branchSpread, c.branchAmp);
                    double u = Ramp(t, blow[k], 1.0) * 1.20 - 0.12;   // THE BLOW-OUT: a see-through gap travels root → tip
                    foreach (var sg in segs)
                    {
                        double kk = 0.42 + 0.58 * sg.w;
                        double bo = op[k] * go * (0.55 + 0.45 * sg.w);
                        double[] oo = null; double os = bo;
                        if (sg.w >= 0.99 && u > -0.10) oo = VeilAt(sg.pts.Count, u, 0.17, 0.55 + 0.42 * Ramp(t, 0.45, 0.92), bo, 0.03);
                        else if (rng.Random() < c.veilP) oo = VeilDeep(rng, sg.pts.Count, bo, 1, 2);
                        f.Channel(sg.pts, (1.25 - 0.45 * t) * kk * F.widthK, (4.0 - 1.6 * t) * kk * F.widthK,
                                  1.3 * life * sg.w * F.KH(op[k]) * F.ampK, 0.45, 0.25 * (1.0 - sg.w), 0.31, os, oo);
                    }
                }
            double flash = 1.0 - Ramp(t, 0.0, 0.40);   // the flash the trunks are rooted in
            if (flash > 0.02)
                f.Blob(CX, CY, 9.0 + 16.0 * EaseOut(t, 2.0), c.flashAmp * Math.Pow(flash, 1.2) * F.ampK, prof, 1.0, 1.5, 0.0, 0.30, 0.82);
        }

        // ═══════════════════════════ 4. CROWN — a gear-toothed heart with fat tapered lobes ═══════════════════════
        public static void DrawCrown(in Frame F, ArcBurstForm.CrownSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(23);
            int n = c.lobes;
            var la = new double[n]; for (int k = 0; k < n; k++) la[k] = k * TAU / n + anchor.Uniform(-0.13, 0.13);
            var lr = new double[n]; for (int k = 0; k < n; k++) lr[k] = anchor.Uniform(c.reachLo, c.reachHi);
            var lw = new double[n]; for (int k = 0; k < n; k++) lw[k] = anchor.Uniform(c.widthLo, c.widthHi);
            var lt = new double[n]; for (int k = 0; k < n; k++) lt[k] = anchor.Uniform(0.0, 0.09);
            var opA = new double[n]; for (int k = 0; k < n; k++) opA[k] = (k % 2 != 0) ? c.ghostLo + c.ghostSpan * anchor.Random() : 1.0;
            var opB = new double[n]; for (int k = 0; k < n; k++) opB[k] = (k % 2 != 0) ? 1.0 : c.ghostLo + c.ghostSpan * anchor.Random();
            var prof = new double[96]; for (int k = 0; k < 96; k++) { double ang = k * TAU / 96; prof[k] = 1.0 + 0.20 * Math.Sin(n * ang + 0.4) + 0.05 * Math.Sin(3 * ang); }

            var rng = F.PerFrame(397);
            double go = Dissolve(t, c.dissolveStart, c.dissolveK);
            double sw = Math.Pow(Ramp(t, c.swapStart, c.swapEnd), 1.3);   // the crossfade between the two ghost sets
            var op = new double[n]; for (int k = 0; k < n; k++) op[k] = opA[k] + (opB[k] - opA[k]) * sw;
            double R = 9.0 + 15.0 * EaseOut(t, 2.4);
            double reach = 13.0 + 44.0 * EaseOut(t, 1.5);
            double hollow = Ramp(t, 0.38, 0.92);
            double camp = 1.95 * (1.0 - 0.45 * Ramp(t, 0.30, 0.92));
            f.Blob(CX, CY, R, camp * F.ampK, prof, 0.92, 1.5, 0.50 * hollow, 0.44, 0.88 - 0.86 * Ramp(t, 0.42, 1.0));
            for (int k = 0; k < n; k++)
            {
                double a = la[k] + 0.10 * EaseOut(t, 1.6);
                double gr = EaseOut(Math.Max(0.0, (t - lt[k]) / Math.Max(1.0 - lt[k], 1e-6)), 1.9);
                double r0 = R * 0.86, r1 = r0 + reach * lr[k] * (0.42 + 0.58 * gr);
                var u = Linspace(0.0, 1.0, 9);
                var pts = new List<ArcPt>(9);
                for (int q = 0; q < 9; q++)
                {
                    double v = u[q], rad = r0 + (r1 - r0) * v, aa = a + 0.05 * Math.Sin(v * 3);
                    pts.Add(Polar(CX, CY, rad, aa));
                }
                double lo = op[k] * go * (1.0 - 0.35 * Ramp(t, 0.30, 1.0));
                f.Polyline(pts, (6.2 - 2.2 * t) * lw[k] * F.widthK, c.lobeAmp * Cool(t, c.coolStart, 1.2) * (0.50 + 0.50 * op[k]) * F.ampK, 0.86, 0.45, lo, null);
                var fr = new PlusPyRandom(F.seed * 71 + k * 13 + F.i);   // the FILAMENT up its spine, re-rolled every frame
                var sp = Displace(pts[0], pts[pts.Count - 1], fr, 4, c.rough, 0.6);
                double fo = (0.45 + 0.55 * op[k]) * Dissolve(t, 0.68, 1.05) * (0.55 + 0.45 * EaseOut(t, 1.4));
                f.Channel(sp, 0.95 * F.widthK, 2.2 * F.widthK, 1.45 * Cool(t, 0.94, 1.2) * F.ampK, 0.35, 0.0, 0.32, 1.0, VeilDeep(fr, sp.Count, fo, 1, 2, 0.05));
                if (rng.Random() < c.crackleP * (1.0 - Ramp(t, 0.5, 0.95)))   // tooth-to-tooth crackle round the core
                {
                    double b = la[(k + 1) % n];
                    f.Channel(Displace(Polar(CX, CY, R * 0.95, a), Polar(CX, CY, R * 0.95, b), rng, 3, 0.22), 0.75 * F.widthK, 1.7 * F.widthK, 1.2 * F.ampK, 0.35, 0.0, 0.31, go * rng.Uniform(0.25, 1.0));
                }
            }
        }

        // ═══════════════════════════ 5. LATTICE — a node net with no hub, woven by depth ═══════════════════════════
        public static void DrawLattice(in Frame F, ArcBurstForm.LatticeSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(29);
            int outer = c.outer, inner = c.inner;
            var nodes = new List<(double a, double rf)>();
            for (int k = 0; k < outer; k++) nodes.Add((k * TAU / outer + anchor.Uniform(-0.10, 0.10), anchor.Uniform(0.90, 1.02)));
            for (int k = 0; k < inner; k++) nodes.Add((k * TAU / inner + anchor.Uniform(-0.4, 0.4), anchor.Uniform(0.34, 0.48)));
            int N = nodes.Count;
            double tip = anchor.Uniform(0, TAU);
            double RimDepth(double k) => 0.5 + 0.5 * Math.Sin(k * TAU / outer + tip);
            double OpOf(double d) => c.ghostFloor + (1.0 - c.ghostFloor) * Math.Pow(d, 1.4);
            double RimOp(double d) => c.rimFloor + (1.0 - c.rimFloor) * Math.Pow(d, 1.3);
            var edges = new List<(int a, int b, double o, double p)>();
            for (int k = 0; k < outer; k++) edges.Add((k, (k + 1) % outer, RimOp(RimDepth(k + 0.5)), 0.97));
            for (int k = 0; k < outer; k++)
            {
                int j = outer + (int)anchor.RandRange(inner);
                edges.Add((k, j, OpOf(anchor.Uniform(0.0, 0.85)), anchor.Uniform(0.35, 0.75)));
            }
            for (int k = 0; k < c.chords; k++)
            {
                int a = (int)anchor.RandRange(outer);
                int b = (a + (int)anchor.RandInt(2, 4)) % outer;
                edges.Add((a, b, OpOf(anchor.Uniform(0.0, 0.6)), anchor.Uniform(0.2, 0.5)));
            }
            for (int k = 0; k < inner; k++) edges.Add((outer + k, outer + (k + 1) % inner, OpOf(anchor.Uniform(0.1, 0.8)), 0.6));
            var nop = new double[N]; for (int k = 0; k < N; k++) nop[k] = k < outer ? RimOp(RimDepth(k)) : OpOf(anchor.Uniform(0.2, 0.9));
            var fly = new double[N]; for (int k = 0; k < N; k++) fly[k] = anchor.Uniform(c.flyLo, c.flyHi);
            // the flash's profile is drawn from the anchor INSIDE the frame loop in the source — once per frame on the
            // same anchor object while the flash is alive — so frame i's profile is the (i+1)th one drawn after fly[]
            double[] flashProf = null;
            for (int q = 0; q <= F.frameNo; q++) flashProf = RProfile(anchor, amp: 0.22);

            var rng = F.PerFrame(811);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double R = 10.0 + 44.0 * EaseOut(t, 2.2);
            double burst = Ramp(t, c.burstStart, 1.0);
            var P = new ArcPt[N];
            for (int k = 0; k < N; k++) { double rr = R * nodes[k].rf * (1.0 + 0.34 * burst * fly[k]); P[k] = Polar(CX, CY, rr, nodes[k].a); }
            double flash = 1.0 - Ramp(t, 0.0, 0.30);
            if (flash > 0.02) f.Blob(CX, CY, 6.0 + 13.0 * EaseOut(t, 2.0), 1.9 * Math.Pow(flash, 1.2) * F.ampK, flashProf, 1.0, 1.5, 0.0, 0.30, 0.80);
            foreach (var (a, b, baseO, pLit) in edges)
            {
                if (rng.Random() > pLit * (1.0 - 0.80 * burst)) continue;
                var pts = Displace(P[a], P[b], rng, c.detail, c.rough, 0.6);
                double oo = baseO * go; double[] ov = null;
                if (rng.Random() < c.veilP) ov = VeilDeep(rng, pts.Count, oo, 1, 2, 0.04);
                f.Channel(pts, 0.9 * F.widthK, 2.2 * F.widthK, 1.25 * Cool(t, c.coolStart, 1.2) * F.KH(baseO) * F.ampK, 0.0, 0.0, 0.31, oo, ov);
            }
            for (int k = 0; k < N; k++)
            {
                var p = P[k];
                double hot = 0.9 + 0.8 * (1.0 - Ramp(t, 0.1, 0.7));
                f.Blob(p.x, p.y, (1.9 + 1.6 * (1.0 - t)) * (1.0 - 0.4 * burst), hot * F.ampK, RProfile(rng, 20, 0.32), 0.8, 1.2, 0.0, 0.30, go * nop[k]);
                if (burst > 0.0)   // a short tail behind each escapee
                {
                    double a = nodes[k].a;
                    var q = new ArcPt(p.x - 7.0 * burst * Math.Cos(a), p.y - 7.0 * burst * Math.Sin(a));
                    f.Channel(Displace(p, q, rng, 3, 0.3), 0.7 * F.widthK, 1.7 * F.widthK, 1.1 * F.ampK, 0.7, 0.6, 0.30, go * 0.85 * nop[k]);
                }
            }
        }

        // ═══════════════════════════ 6. TERMINAL — a hub striking outward to nodes, dying by angle ═══════════════
        public static void DrawTerminal(in Frame F, ArcBurstForm.TerminalSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(11);
            int n = c.nodes;
            var na = new double[n]; for (int k = 0; k < n; k++) na[k] = k * TAU / n + anchor.Uniform(-0.10, 0.10);
            var nr = new double[n]; for (int k = 0; k < n; k++) nr[k] = anchor.Uniform(c.reachLo, c.reachHi);
            var fire = new double[n]; for (int k = 0; k < n; k++) fire[k] = anchor.Uniform(c.fireLo, c.fireHi);
            var op = new double[n]; for (int k = 0; k < n; k++) op[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            double away = anchor.Uniform(0, TAU);
            var prof = RProfile(anchor, amp: 0.16);

            var rng = F.PerFrame(733);
            double R = 12.0 + 42.0 * EaseOut(t, 2.0);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double hub = 1.0 - Ramp(t, 0.28, 0.74);
            if (hub > 0.02) f.Blob(CX, CY, 7.0 + 12.0 * EaseOut(t, 2.0), 1.95 * Math.Pow(hub, 1.1) * F.ampK, prof, 1.0, 1.5, 0.0, 0.30, 0.85);
            for (int k = 0; k < n; k++)
            {
                var p = Polar(CX, CY, R * nr[k], na[k]);
                double age = t - fire[k];
                if (age < 0.0) { f.Dot(p.x, p.y, 1.4 * F.widthK, 0.55 * F.ampK); continue; }
                double flare = Math.Exp(-age * 7.0);
                double hold = Cool(t, c.coolStart, 1.2);
                double spoke = hub * 0.85 + 0.15 * flare;
                if (spoke > 0.04)
                {
                    var pts = Displace(new ArcPt(CX, CY), p, rng, c.detail, c.rough, 0.6);
                    var vr = F.Veil(97, k, 31);
                    f.Channel(pts, 1.15 * F.widthK, 3.2 * F.widthK, 1.3 * (0.45 + 0.55 * flare) * spoke * F.ampK, 0.25, 0.0, 0.32, 1.0, VeilDeep(vr, pts.Count, op[k] * go, 1, 2));
                }
                f.Blob(p.x, p.y, (2.2 + 3.4 * flare) * hold, (1.1 + 0.8 * flare) * hold * F.ampK, RProfile(rng, 24, 0.3), 0.55, 1.1, 0.0, 0.30, go * (0.55 + 0.45 * op[k]));
                for (int q = 0; q < (int)(1 + 2 * flare); q++)
                {
                    double a = na[k] + rng.Uniform(-1.5, 1.5);
                    double L = rng.Uniform(5, 16) * (0.4 + 0.9 * flare) * hold;
                    var qq = new ArcPt(p.x + L * Math.Cos(a), p.y + L * Math.Sin(a));
                    f.Channel(Displace(p, qq, rng, 4, 0.25), 0.7 * F.widthK, 1.8 * F.widthK, 1.0 * hold * F.ampK, 0.6, 0.45, 0.42, go * rng.Uniform(0.4, 1.0));
                }
                int j = (k + 1) % n;   // neighbours talking along the rim
                double pHop = (0.30 + 0.60 * Ramp(t, 0.3, 0.62)) * (1.0 - 0.85 * Ramp(t, 0.62, 0.94));
                if (t > fire[j] && rng.Random() < pHop)
                {
                    var q = Polar(CX, CY, R * nr[j], na[j]);
                    double mid = na[k] + TAU / (2 * n);
                    double bow = R * rng.Uniform(0.97, 1.03);
                    var via = Polar(CX, CY, bow, mid);
                    var seg = Displace(p, via, rng, 3, 0.16 + 0.16 * t);
                    seg.AddRange(Displace(via, q, rng, 3, 0.16 + 0.16 * t));
                    var rv = VeilDeep(rng, seg.Count, Dissolve(t, 0.62, 1.05) * 0.95, 1, 1 + (int)(3 * t), 0.03, 0.10, 0.30, 0.4 + 0.55 * t, 1.0);
                    f.Channel(seg, 0.8 * F.widthK, 2.0 * F.widthK, 0.95 * Cool(t, 0.92, 1.2) * F.ampK, 0.35, 0.0, 0.30, 1.0, rv);
                }
            }
            double d = Ramp(t, c.sweepStart, 1.0);   // THE SWEEP: the dissipation itself is directional
            if (d > 0.01) f.FadeAngular(CX, CY, away, 0.15 + 2.7 * d, c.sweepDepth * d, 1.5);
        }

        // ═══════════════════════════ 7. CAGE — arcs over a sphere, depth carried in alpha ═══════════════════════════
        public static void DrawCage(in Frame F, ArcBurstForm.CageSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(3);
            int NR = c.ribs;
            var ribs = new List<(double[] u, double[] v, double[] th)>();
            for (int r = 0; r < NR; r++)
            {
                double ax0 = anchor.Gauss(0, 1), ax1 = anchor.Gauss(0, 1), ax2 = anchor.Gauss(0, 1);
                double L = Math.Sqrt(ax0 * ax0 + ax1 * ax1 + ax2 * ax2); ax0 /= L; ax1 /= L; ax2 /= L;
                double[] tmp = Math.Abs(ax2) < 0.85 ? new[] { 0.0, 0.0, 1.0 } : new[] { 1.0, 0.0, 0.0 };
                var u = Cross(new[] { ax0, ax1, ax2 }, tmp); double Lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]); u[0] /= Lu; u[1] /= Lu; u[2] /= Lu;
                var v = Cross(new[] { ax0, ax1, ax2 }, u);
                double th0 = anchor.Uniform(0, TAU), span = anchor.Uniform(c.spanLo, c.spanHi);
                ribs.Add((u, v, Linspace(th0, th0 + span, 34)));
            }
            var snapAt = new double[NR]; for (int k = 0; k < NR; k++) snapAt[k] = anchor.Uniform(c.snapLo, c.snapHi);
            var rop = new double[NR]; for (int k = 0; k < NR; k++) rop[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            var prof = RProfile(anchor, amp: 0.07);

            var rng = F.PerFrame(449);
            double R = 8.0 + 45.0 * EaseOut(t, 2.6);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double bo = 1.0 - Ramp(t, 0.10, 0.62);   // the BALL behind the ribs
            if (bo > 0.02)
                f.Blob(CX, CY, R * 0.97, (0.42 + 1.20 * (1.0 - Ramp(t, 0.0, 0.24))) * bo * F.ampK, prof, 1.0, 1.6, 0.60 * Ramp(t, 0.12, 0.55), 0.30, c.ballOpa);
            for (int k = 0; k < NR; k++)
            {
                var (u, v, th) = ribs[k];
                double sn = Ramp(t, snapAt[k], snapAt[k] + 0.20);
                double rr = R * (1.0 + 0.14 * sn);
                int m = th.Length;
                var pts = new List<ArcPt>(m); var zz = new double[m];
                for (int q = 0; q < m; q++)
                {
                    double cth = Math.Cos(th[q]), sth = Math.Sin(th[q]);
                    double sx = cth * u[0] + sth * v[0], sy = cth * u[1] + sth * v[1];
                    zz[q] = cth * u[2] + sth * v[2];
                    pts.Add(new ArcPt(CX + rr * sx, CY + rr * sy));
                }
                pts = Jitter(pts, rng, c.jitter + 5.0 * t + 5.0 * sn, 0.5, 4, ends: 1);
                var oo = new double[m];
                for (int q = 0; q < m; q++) oo[q] = (c.backOpa + (1.0 - c.backOpa) * Math.Pow(zz[q] * 0.5 + 0.5, c.depthGamma)) * go * rop[k];
                if (sn > 0.0)   // a snapped rib opens a widening transparent gap in its middle
                {
                    var xs = Linspace(0, 1, m);
                    for (int q = 0; q < m; q++)
                    {
                        double gap = Clamp01(Math.Abs(xs[q] - 0.5) * 2.0);
                        oo[q] *= Clamp01((gap - sn * 0.85) / 0.15);
                    }
                }
                f.Channel(pts, (0.95 - 0.2 * t) * F.widthK, (2.0 - 0.5 * t) * F.widthK, 1.25 * Cool(t, c.coolStart, c.coolK) * F.KH(rop[k]) * F.ampK, 0.35, 0.0, 0.31, 1.0, oo);
            }
            for (int q = 0; q < (int)(c.breakouts * Ramp(t, 0.36, 0.90)); q++)   // BREAKOUT through the surface
            {
                double a = rng.Uniform(0, TAU);
                var p0 = Polar(CX, CY, R * 0.92, a);
                double L = rng.Uniform(9, 26);
                double a1 = a + rng.Uniform(-0.25, 0.25), a2 = a + rng.Uniform(-0.25, 0.25);
                var p1 = new ArcPt(CX + (R + L) * Math.Cos(a1), CY + (R + L) * Math.Sin(a2));
                f.Channel(Displace(p0, p1, rng, 4, 0.24), 0.9 * F.widthK, 2.2 * F.widthK, 1.2 * Cool(t, 0.90, 1.2) * F.ampK, 0.6, 0.35, 0.42, Dissolve(t, 0.62, 1.05) * rng.Uniform(0.3, 1.0));
            }
            for (int q = 0; q < (int)(c.sparks * Ramp(t, 0.35, 1.0)); q++)
            {
                double a = rng.Uniform(0, TAU), r = R * rng.Uniform(0.98, 1.32);
                f.Dot(CX + r * Math.Cos(a), CY + r * Math.Sin(a), rng.Uniform(0.55, 1.15) * F.widthK, 0.95 * F.ampK, go * rng.Uniform(0.2, 1.0));
            }
        }

        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        // ═══════════════════════════ 8. STIPPLE — a flash that decomposes into dashes and then dots ═══════════════
        public static void DrawStipple(in Frame F, ArcBurstForm.StippleSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(41);
            int m = c.shards;
            var da = new double[m]; for (int k = 0; k < m; k++) da[k] = k * TAU / m + anchor.Uniform(-0.16, 0.16);
            var dv = new double[m]; for (int k = 0; k < m; k++) dv[k] = anchor.Uniform(c.speedLo, c.speedHi);
            var dl = new double[m]; for (int k = 0; k < m; k++) dl[k] = anchor.Uniform(c.lenLo, c.lenHi);
            var dw = new double[m]; for (int k = 0; k < m; k++) dw[k] = anchor.Uniform(0.75, 1.30);
            var dop = new double[m]; for (int k = 0; k < m; k++) dop[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            var ab = new double[m]; for (int k = 0; k < m; k++) ab[k] = anchor.Uniform(c.breakLo, c.breakHi);
            int arcs = c.arcs;
            var aa = new double[arcs]; for (int k = 0; k < arcs; k++) aa[k] = k * TAU / arcs + anchor.Uniform(-0.2, 0.2);
            var prof = RProfile(anchor, amp: 0.17);

            var rng = F.PerFrame(907);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double body = 1.0 - Ramp(t, 0.10, 0.42);
            if (body > 0.02)
                f.Blob(CX, CY, 8.0 + 20.0 * EaseOut(t, 2.2), 1.95 * body * F.ampK, prof, 0.92, 1.4, 0.7 * Ramp(t, 0.16, 0.44), 0.34, 0.80);
            double early = 1.0 - Ramp(t, 0.24, 0.62);   // the discharge, alive only while the body is
            if (early > 0.02)
                for (int k = 0; k < arcs; k++)
                {
                    double L = 12.0 + 40.0 * EaseOut(t, 2.6);
                    var segs = Tree(rng, new ArcPt(CX, CY), Polar(CX, CY, L, aa[k]), 5, c.rough, c.depth, c.branchMin, c.branchMax, 0.5, 1.0);
                    DrawTreeCh(f, segs, 1.0 * F.widthK, 3.0 * F.widthK, 1.3 * early * F.ampK, 0.5, 0.31, go * (0.55 + 0.45 * early), rng, 0.4, 0.6);
                }
            for (int k = 0; k < m; k++)   // the pieces: keep brightness, lose LENGTH, then opacity
            {
                double age = Ramp(t, ab[k], 1.0);
                if (t < ab[k]) continue;
                double r = 10.0 + 52.0 * EaseOut(age, 1.7) * dv[k];
                double ln = (7.0 + 7.0 * dl[k]) * (1.0 - 0.58 * age);
                double a = da[k] + 0.06 * Math.Sin(age * 3.0 + k);
                var p0 = Polar(CX, CY, r, a); var p1 = Polar(CX, CY, r + ln, a);
                double solid = 1.0 - Ramp(t, 0.10, 0.55);
                double oo = (dop[k] + (1.0 - dop[k]) * solid) * go * (1.0 - 0.25 * age);
                var pts = Displace(p0, p1, rng, 2, 0.14);
                f.Channel(pts, (1.05 - 0.30 * age) * dw[k] * F.widthK, (2.35 - 0.85 * age) * dw[k] * F.widthK, 1.5 * Cool(t, c.coolStart, 1.2) * F.KH(dop[k]) * F.ampK, 0.4, 0.0, 0.32, oo);
                if (rng.Random() < c.hairP * (1.0 - age))
                {
                    double b = a + rng.Uniform(-1.2, 1.2);
                    var q = new ArcPt(p1.x + 7 * Math.Cos(b), p1.y + 7 * Math.Sin(b));
                    f.Channel(Displace(p1, q, rng, 3, 0.3), 0.65 * F.widthK, 1.6 * F.widthK, 1.1 * F.ampK, 0.7, 0.6, 0.30, oo * 0.9);
                }
                if (0.18 < t && t < 0.82 && rng.Random() < c.crossP)   // CROSS-TALK to the next piece round the ring
                {
                    int j = ((k + (int)rng.Sign()) % m + m) % m;
                    double rj = 10.0 + 52.0 * EaseOut(Ramp(t, ab[j], 1.0), 1.7) * dv[j];
                    var q = Polar(CX, CY, rj, da[j]);
                    f.Channel(Displace(p0, q, rng, 4, 0.26), 0.8 * F.widthK, 2.0 * F.widthK, 1.35 * Cool(t, c.coolStart, 1.2) * F.ampK, 0.35, 0.0, 0.31, go * rng.Uniform(0.25, 1.0));
                }
            }
        }

        // ═══════════════════════════ 9. PINCH — bipolar: two axial jets and an equatorial ring ═══════════════════════
        public static void DrawPinch(in Frame F, ArcBurstForm.PinchSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(47);
            double tilt = anchor.Uniform(-c.tilt, c.tilt);
            var jets = new List<(double s, double[] rr)>();
            foreach (double sgn in new[] { -1.0, 1.0 }) jets.Add((sgn, new[] { anchor.Uniform(0.85, 1.0), anchor.Uniform(0.85, 1.0) }));
            int nring = c.ring;
            var ra = new double[nring]; for (int k = 0; k < nring; k++) ra[k] = k * TAU / nring + anchor.Uniform(-0.08, 0.08);
            var ro = new double[nring]; for (int k = 0; k < nring; k++) ro[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            var rb = new double[nring]; for (int k = 0; k < nring; k++) rb[k] = anchor.Uniform(-1.0, 1.0);

            var rng = F.PerFrame(653);
            double go = Dissolve(t, c.dissolveStart, 1.05);
            double ct = Math.Cos(tilt), st = Math.Sin(tilt);
            ArcPt Xf(double x, double y) => new ArcPt(CX + x * ct - y * st, CY + x * st + y * ct);
            double lens = 1.0 - Ramp(t, 0.06, 0.56);   // the lens the ring is squeezed out of
            if (lens > 0.02)
            {
                double lw = 7.0 + 26.0 * EaseOut(t, 2.1);
                var pr = new double[72];
                // the lens is squeezed at 0.42 (its own literal), the ring at axisRatio (0.40) — two numbers in the source
                for (int k = 0; k < 72; k++) { double ang = k * TAU / 72, cs = Math.Cos(ang), sn = Math.Sin(ang) / 0.42; pr[k] = 1.0 / Math.Sqrt(cs * cs + sn * sn); }
                f.Blob(CX, CY, lw, 1.95 * lens * F.ampK, pr, 0.95, 1.45, 0.66 * Ramp(t, 0.20, 0.60), 0.36, c.lensOpa - 0.62 * Ramp(t, 0.34, 1.0));
            }
            double jl = 8.0 + 56.0 * EaseOut(t, 2.8);   // the JETS
            foreach (var (sgn, rr) in jets)
                for (int b = 0; b < 2; b++)
                {
                    double x0 = 0.0, y0 = sgn * (3.0 + 5.0 * t);
                    double x1 = rng.Gauss(0, c.jetSpread), y1 = sgn * jl * rr[b];
                    var segs = Tree(rng, Xf(x0, y0), Xf(x1, y1), 5, c.rough, c.depth, c.branchMin, c.branchMax, c.branchScale, c.branchSpread);
                    DrawTreeCh(f, segs, (1.2 - 0.4 * t) * F.widthK, (3.6 - 1.3 * t) * F.widthK, 1.35 * Cool(t, c.coolStart, 1.2) * F.ampK, 0.5, 0.31, go * (0.92 + 0.08 * b), rng, 0.35, 0.45);
                }
            double rgrow = Ramp(t, 0.16, 1.0);   // the RING, arriving late and outliving the jets
            double Rx = 10.0 + 48.0 * EaseOut(rgrow, 1.8), Ry = Rx * c.axisRatio;
            for (int k = 0; k < nring; k++)
            {
                var u = Linspace(ra[k], ra[k] + c.ringSpan, 14);
                double bw = 1.0 + 0.085 * rb[k] * (0.4 + 0.6 * rgrow);
                var pts = new List<ArcPt>(14);
                for (int q = 0; q < 14; q++) pts.Add(Xf(Rx * bw * Math.Cos(u[q]), Ry * bw * Math.Sin(u[q])));
                pts = Jitter(pts, rng, 0.7 + 2.6 * t, 0.5, 4);
                var vr = F.Veil(19, k, 41);
                var vv = VeilDeep(vr, pts.Count, ro[k] * go * rgrow, 1, 1 + (int)(2 * t), 0.04, 0.10, 0.30, 0.35 + 0.5 * t, 1.0);
                f.Channel(pts, 0.9 * F.widthK, (2.3 - 0.6 * t) * F.widthK, 0.98 * Cool(t, c.ringCoolStart, 1.2) * F.KH(ro[k]) * F.ampK, 0.0, 0.0, 0.31, 1.0, vv);
            }
        }

        // ═══════════════════════════ 10. LICHTEN — a fine capillary creep, hollowing out from the inside ═══════════
        public static void DrawLichten(in Frame F, ArcBurstForm.LichtenSettings c)
        {
            var f = F.f; double t = F.t;
            var anchor = F.Anchor(59);
            int roots = c.roots;
            var ra = new double[roots]; for (int k = 0; k < roots; k++) ra[k] = k * TAU / roots + anchor.Uniform(-0.16, 0.16);
            var rr = new double[roots]; for (int k = 0; k < roots; k++) rr[k] = anchor.Uniform(c.reachLo, c.reachHi);
            var op = new double[roots]; for (int k = 0; k < roots; k++) op[k] = Ghost(anchor, c.ghostP, c.ghostLo, c.ghostHi, c.ghostDeepP, F.deepLo, F.deepHi);
            // the seed flash's profile is drawn from the anchor inside the frame loop (frame i gets the (i+1)th profile)
            double[] flashProf = null;
            for (int q = 0; q <= F.frameNo; q++) flashProf = RProfile(anchor, amp: 0.24);

            var rng = F.PerFrame(1013);
            double go = Dissolve(t, c.dissolveStart, 1.0);
            double front = 5.0 + 58.0 * EaseOut(t, 2.3);
            double r0 = 62.0 * Math.Pow(Ramp(t, c.hollowStart, 1.12), 1.35);   // the hollow front
            double seedFlash = 1.0 - Ramp(t, 0.0, 0.34);
            if (seedFlash > 0.02)
                f.Blob(CX, CY, 5.0 + 12.0 * EaseOut(t, 2.0), 1.9 * Math.Pow(seedFlash, 1.3) * F.ampK, flashProf, 1.0, 1.5, 0.0, 0.30, 0.85 * (1.0 - Ramp(t, 0.14, 0.42)));
            for (int k = 0; k < roots; k++)
            {
                var tip = Polar(CX, CY, front * rr[k], ra[k]);
                var segs = Tree(rng, new ArcPt(CX, CY), tip, c.detail, c.rough, c.depth, c.branchMin, c.branchMax, c.branchScale, c.branchSpread, c.branchAmp);
                foreach (var sg in segs)
                {
                    double kk = 0.5 + 0.5 * sg.w;
                    double bs = op[k] * go * (0.55 + 0.45 * sg.w);
                    var oo = RadialOpacity(sg.pts, r0, 13.0, bs);
                    if (rng.Random() < c.veilP) oo = Mul(oo, VeilDeep(rng, sg.pts.Count, 1.0, 1, 2, 0.06));
                    f.Channel(sg.pts, 0.62 * kk * F.widthK, 1.9 * kk * F.widthK, 1.25 * sg.w * Cool(t, c.coolStart, 1.2) * F.ampK, 0.5, 0.2 * (1.0 - sg.w), 0.33, 1.0, oo);
                }
            }
            for (int q = 0; q < (int)(c.sparks * (1.0 - 0.5 * t)); q++)   // the advancing front sparkles
            {
                double a = rng.Uniform(0, TAU), r = front * rng.Uniform(0.80, 1.04);
                f.Dot(CX + r * Math.Cos(a), CY + r * Math.Sin(a), rng.Uniform(0.5, 1.1) * F.widthK, 1.2 * F.ampK, go * rng.Uniform(0.5, 1.0));
            }
            if (r0 > 1.0) f.Hollow(CX, CY, r0 * 0.86, 15.0);   // the hole is cut in ENERGY too, before the bloom
            f.ScaleAlpha(1.0 - c.fadeAmt * Ramp(t, 0.46, 1.0));
        }

        /// The per-layout bloom literal (radius px @128, strength) — the source's `f.bloom(...)` call per draw.
        public static (double radius, double strength) DefaultBloom(ArcBurstForm.Layout l) => l switch
        {
            ArcBurstForm.Layout.Core => (2.8, 0.42), ArcBurstForm.Layout.Weave => (2.5, 0.44), ArcBurstForm.Layout.Bolt => (3.0, 0.48),
            ArcBurstForm.Layout.Crown => (2.6, 0.40), ArcBurstForm.Layout.Lattice => (2.6, 0.42), ArcBurstForm.Layout.Terminal => (2.4, 0.40),
            ArcBurstForm.Layout.Cage => (2.6, 0.40), ArcBurstForm.Layout.Stipple => (2.0, 0.32), ArcBurstForm.Layout.Pinch => (2.7, 0.44),
            _ => (2.3, 0.46),
        };

        /// The per-layout `field(aref, agamma)` literal.
        public static (float aref, float agamma) DefaultField(ArcBurstForm.Layout l) => l switch
        {
            ArcBurstForm.Layout.Core => (0.27f, 0.72f), ArcBurstForm.Layout.Weave => (0.26f, 0.72f), ArcBurstForm.Layout.Bolt => (0.28f, 0.70f),
            ArcBurstForm.Layout.Crown => (0.27f, 0.74f), ArcBurstForm.Layout.Lattice => (0.26f, 0.72f), ArcBurstForm.Layout.Terminal => (0.27f, 0.74f),
            ArcBurstForm.Layout.Cage => (0.26f, 0.72f), ArcBurstForm.Layout.Stipple => (0.26f, 0.72f), ArcBurstForm.Layout.Pinch => (0.26f, 0.72f),
            _ => (0.25f, 0.72f),
        };

        /// Kiln palette name per layout (generate.py's `PAL[...]` in each draw's colorize call).
        public static string DefaultPalette(ArcBurstForm.Layout l) => l switch
        {
            ArcBurstForm.Layout.Core => "ion", ArcBurstForm.Layout.Weave => "plasma", ArcBurstForm.Layout.Bolt => "violet",
            ArcBurstForm.Layout.Crown => "magenta", ArcBurstForm.Layout.Lattice => "chroma", ArcBurstForm.Layout.Terminal => "acid",
            ArcBurstForm.Layout.Cage => "cyan", ArcBurstForm.Layout.Stipple => "steel", ArcBurstForm.Layout.Pinch => "crimson",
            _ => "teal",
        };

        /// Box-average a k× plane down to canvas size (the published planes), like Port 01.
        public static float[] BoxDown(float[] src, int S, int k)
        {
            if (k <= 1) return (float[])src.Clone();
            int W = S / k; var o = new float[W * W]; float inv = 1f / (k * k);
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) sum += src[(y * k + j) * S + x * k + i];
                    o[y * W + x] = sum * inv;
                }
            return o;
        }
    }

    /// The ten Kiln cel palettes (generate.py `PAL`) as PlusRamp step tables: stop position = the energy threshold
    /// at which that band starts, stop colour = the band. Five stops, sRGB, alpha 1 (the under-floor cut is the
    /// form's own rule, not a stop).
    public static class ArcBands
    {
        static readonly float[] Thr = { 0.045f, 0.13f, 0.30f, 0.58f, 0.92f };
        static PlusRamp Make(byte[,] c)
        {
            var r = new PlusRamp { space = PlusRampSpace.Srgb };
            for (int i = 0; i < 5; i++) r.stops.Add(new PlusRampStop(Thr[i], c[i, 0], c[i, 1], c[i, 2], 1f));
            return r;
        }
        public static PlusRamp Ion() => Make(new byte[,] { { 10, 52, 104 }, { 26, 132, 214 }, { 118, 224, 255 }, { 214, 250, 255 }, { 255, 255, 255 } });
        public static PlusRamp Violet() => Make(new byte[,] { { 52, 16, 96 }, { 124, 54, 214 }, { 198, 138, 255 }, { 240, 214, 255 }, { 255, 255, 255 } });
        public static PlusRamp Acid() => Make(new byte[,] { { 16, 74, 24 }, { 56, 184, 58 }, { 150, 255, 118 }, { 226, 255, 198 }, { 255, 255, 255 } });
        public static PlusRamp Plasma() => Make(new byte[,] { { 86, 34, 0 }, { 214, 116, 8 }, { 255, 198, 60 }, { 255, 244, 178 }, { 255, 255, 255 } });
        public static PlusRamp Cyan() => Make(new byte[,] { { 0, 58, 74 }, { 0, 168, 196 }, { 120, 244, 255 }, { 222, 252, 255 }, { 255, 255, 255 } });
        public static PlusRamp Magenta() => Make(new byte[,] { { 74, 6, 54 }, { 196, 26, 132 }, { 255, 110, 196 }, { 255, 206, 240 }, { 255, 255, 255 } });
        public static PlusRamp Chroma() => Make(new byte[,] { { 104, 8, 78 }, { 214, 40, 150 }, { 96, 196, 255 }, { 206, 246, 255 }, { 255, 255, 255 } });
        public static PlusRamp Steel() => Make(new byte[,] { { 16, 26, 62 }, { 46, 84, 186 }, { 116, 178, 255 }, { 214, 236, 255 }, { 255, 255, 255 } });
        public static PlusRamp Crimson() => Make(new byte[,] { { 64, 0, 18 }, { 190, 18, 52 }, { 255, 96, 110 }, { 255, 202, 208 }, { 255, 255, 255 } });
        public static PlusRamp Teal() => Make(new byte[,] { { 0, 58, 48 }, { 0, 160, 132 }, { 96, 255, 214 }, { 214, 255, 244 }, { 255, 255, 255 } });

        /// By Kiln name (`PAL` key); null for an unknown name.
        public static PlusRamp Get(string name) => (name ?? "").ToLowerInvariant() switch
        {
            "ion" => Ion(), "violet" => Violet(), "acid" => Acid(), "plasma" => Plasma(), "cyan" => Cyan(),
            "magenta" => Magenta(), "chroma" => Chroma(), "steel" => Steel(), "crimson" => Crimson(), "teal" => Teal(), _ => null,
        };
    }
}
