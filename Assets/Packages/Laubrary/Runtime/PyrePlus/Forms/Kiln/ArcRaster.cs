// ArcRaster — the Burst rasteriser behind ArcField: every stroke (`_seg`) and body (`blob`) an Arc Burst draw
// deposits is collected as a blittable record, and one native kernel sweeps them into the E / A planes with
// MAXIMUM compositing. The geometry stays managed and untouched; only the per-pixel arithmetic moved here.
//
// This kernel is the ONLY path. Burst's exp / pow are not bit-identical to Mono's C-runtime calls (and Mono even
// promotes float sub-expressions to double unrounded where Burst rounds every op), so a managed twin selectable
// at runtime would make the preview, the bake and the tests disagree by a last bit. Instead: Strict float mode
// (no FMA / reassociation), High precision, synchronous compilation (the first call blocks on the compile rather
// than running a frame on the managed fallback). With "Jobs ▸ Burst ▸ Enable Compilation" off the editor runs
// this same source under Mono — correct, many times slower, and a one-time warning says so.
//
// Off the main thread: forms render on worker threads with spec clones (T-0060), and the job system's Schedule
// is main-thread-only — so the kernel is a `[BurstCompile]` static method reached by Burst direct call on the
// calling thread, with no JobHandle involved. Each worker owns its frame; the outer parallelism is across frames.
using System;
using Unity.Burst;
using UnityEngine;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    /// One `_seg` deposit: endpoints already in plane pixels, bbox already clipped to the plane.
    public struct ArcSegRec
    {
        public float x0, y0, x1, y1, invW, amp, opa;
        public int xs, xe, ys, ye;
    }

    /// One `blob` deposit: centre in plane pixels, radius table in `ArcRaster` profile pool units, bbox clipped.
    public struct ArcBlobRec
    {
        public float pcx, pcy, opa;
        public double rs, step, amp, invSoft, edge, inner, invHole;
        public int profOff, profN;          // profN = 0 ⇒ a plain disc of radius rs
        public int xs, xe, ys, ye;
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public static unsafe class ArcRaster
    {
        // ── the skip test's bounds (see ArcField.Seg for the reasoning) ──
        public const int FalloffN = 4096; public const float FalloffXMax = 64f, FalloffScale = FalloffN / FalloffXMax;
        public const int GammaN = 1024;
        const float BoundSlack = 1.00001f;

        /// exp(−x^1.7) sampled at j / FalloffScale, widened; read at floor(x · scale) it bounds the falloff above.
        public static readonly float[] FalloffUpper = BuildFalloffUpper();
        static float[] BuildFalloffUpper()
        {
            var t = new float[FalloffN + 1];
            for (int j = 0; j <= FalloffN; j++) t[j] = (float)Math.Exp(-Math.Pow(j / FalloffScale, 1.7)) * BoundSlack;
            return t;
        }

        /// pow(j / GammaN, agamma), widened; read one entry above floor(a · GammaN) it bounds the gamma curve above.
        public static void BuildGammaUpper(float[] dst, float agamma)
        {
            for (int j = 0; j <= GammaN; j++) dst[j] = Mathf.Pow(j / (float)GammaN, agamma) * BoundSlack;
        }

        static bool _warned;
        /// One-time notice when the kernel will run under Mono — same picture, a fraction of the speed.
        public static void CheckEnabled()
        {
            if (_warned || BurstCompiler.IsEnabled) return;
            _warned = true;
            Debug.LogWarning("PyrePlus Arc Burst: Burst compilation is disabled (Jobs ▸ Burst ▸ Enable Compilation), so the stroke rasteriser runs on the managed fallback — correct, but several times slower.");
        }

        /// Deposit every record into the planes. `falloff` / `gamma` are the two bound tables above.
        [BurstCompile]
        public static void Run(float* E, float* A, int S, float aref, float agamma,
                               ArcSegRec* segs, int nSeg, ArcBlobRec* blobs, int nBlob, double* profiles,
                               float* falloff, float* gamma)
        {
            float invAref = 1f / aref;
            for (int r = 0; r < nSeg; r++)
            {
                var g = segs[r];
                float x0 = g.x0, y0 = g.y0, dx = g.x1 - g.x0, dy = g.y1 - g.y0;
                float L2 = dx * dx + dy * dy;
                float invW = g.invW, fa = g.amp, fo = g.opa;
                bool point = L2 < 1e-9f;
                for (int y = g.ys; y < g.ye; y++)
                {
                    float Y = y;
                    int row = y * S;
                    for (int x = g.xs; x < g.xe; x++)
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
                        int i = row + x;
                        int fj = (int)(xw * FalloffScale); if (fj > FalloffN) fj = FalloffN;
                        float eUpper = fa * falloff[fj];
                        if (eUpper <= E[i])
                        {
                            float au = eUpper * invAref; if (au > 1f) au = 1f;
                            int gj = (int)(au * GammaN) + 1; if (gj > GammaN) gj = GammaN;
                            if (gamma[gj] * fo <= A[i]) continue;
                        }
                        float e = fa * (float)Math.Exp(-Math.Pow(xw, 1.7));
                        Put(E, A, i, e, fo, aref, agamma);
                    }
                }
            }

            const float tauF = 6.2831855f;
            for (int r = 0; r < nBlob; r++)
            {
                var b = blobs[r];
                double* prof = profiles + b.profOff;
                int n = b.profN;
                for (int y = b.ys; y < b.ye; y++)
                    for (int x = b.xs; x < b.xe; x++)
                    {
                        float fx = x - b.pcx, fy = y - b.pcy;
                        float ang = Mathf.Atan2(fy, fx) % tauF; if (ang < 0f) ang += tauF;
                        double R;
                        if (n == 0) R = b.rs;
                        else
                        {
                            double q = ang / b.step; int i0 = (int)Math.Floor(q); double fr = q - i0;
                            if (i0 >= n) { i0 = n - 1; fr = (ang - i0 * b.step) / b.step; }
                            int i1 = i0 + 1 >= n ? 0 : i0 + 1;
                            R = b.rs * (prof[i0] + (prof[i1] - prof[i0]) * fr);
                        }
                        float d = Hypot(fx, fy);
                        double u = d / Math.Max(R, 1e-3);
                        double e = (1.0 - u) * b.invSoft; if (e < 0) e = 0; else if (e > 1) e = 1;
                        e = b.amp * Math.Pow(e, b.edge);
                        if (b.inner > 0.0)
                        {
                            double h = (u - b.inner) * b.invHole; if (h < 0) h = 0; else if (h > 1) h = 1;
                            e *= h;
                        }
                        if (e > 0) Put(E, A, y * S + x, (float)e, b.opa, aref, agamma);
                    }
            }
        }

        // `a = clip(e / aref)^agamma · opa`, maximum into both planes — arclib4.Field._put.
        static void Put(float* E, float* A, int i, float e, float opa, float aref, float agamma)
        {
            if (e > E[i]) E[i] = e;
            float a = e / aref; if (a > 1f) a = 1f; else if (a < 0f) a = 0f;
            a = Mathf.Pow(a, agamma);
            if (opa != 1f) a *= opa;
            if (a > A[i]) A[i] = a;
        }

        static float Hypot(float a, float b) => (float)Math.Sqrt((double)a * a + (double)b * b);
    }
}
