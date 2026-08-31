using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// The 28 rays that lose a crossing. Attribute the mechanism, and decide whether it is new.
public static class V3d
{
    static float SteppedE(in ShaperHeightOp op, float t)
    {
        float e = Mathf.Floor(t * op.n) * op.invN1;
        return e > 1f ? 1f : e;
    }

    public static void Run()
    {
        Console.WriteLine("=== V3d - the 28 lost-crossing rays: what is actually happening ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        // the worst one: n=10 k=5 dz=0.6 off=-3e-4, true=2 got=0
        int n = 10;
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(290f), steps = new ZUIValue(n) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        float zeta = 5f / (n - 1);
        float z = op.baseZ + op.body * zeta - 3e-4f;
        float dzf = 0.6f, dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var buf = new ShaperCrossing[256];
        var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
        var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, dx, 0f, dz, 1200f, 2000000);
        Console.WriteLine("  n=" + n + " body=" + op.body + " supG=" + op.supG + " zHi=" + (op.baseZ + op.body * op.supG));
        Console.WriteLine("  ray origin z = " + z.ToString("F6") + "  dir=(" + dx.ToString("F5") + ",0," + dz + ")");
        Console.WriteLine("  got " + rr.count + ", true " + truth.Count + "  branch=" + rr.branch
                          + " capped=" + rr.bracketCapped + " exhausted=" + rr.stepsExhausted + " truncated=" + rr.truncated);
        Console.Write("  emitted: "); for (int i = 0; i < rr.count; i++) Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I " : "O ")); Console.WriteLine();
        Console.Write("  truth  : "); foreach (double t in truth) Console.Write(t.ToString("F4") + " "); Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("  the true span, sampled by BOTH predicates:");
        Console.WriteLine(string.Format("{0,12} {1,10} {2,10} {3,12} {4,10} {5,10} {6,12} {7,10}",
            "rayT", "x", "z", "d", "t", "E(t)", "body*E", "OLD-inside"));
        if (truth.Count >= 2)
        {
            double lo = truth[0], hi = truth[truth.Count - 1];
            for (int i = 0; i <= 12; i++)
            {
                float s = (float)(lo + (hi - lo) * i / 12.0);
                float px = -400f + s * dx, pz = z + s * dz;
                float dd = ShaperEvaluator.Distance(prog, px, 0f, stack);
                float tt = ShaperHeight.T(op, dd);
                float e = SteppedE(op, tt);
                bool oldIn = March.Inside(prog, stack, op, -400f, 0f, z, dx, 0f, dz, s);
                Console.WriteLine(string.Format("{0,12:F4} {1,10:F4} {2,10:F5} {3,12:F5} {4,10:F6} {5,10:R} {6,12:F5} {7,10}",
                    s, px, pz, dd, tt, e, op.body * e, oldIn));
            }
            Console.WriteLine("  span width = " + (hi - lo).ToString("F5") + " canvas px   (SurfaceResolution = " + ShaperResolve.SurfaceResolution + ")");
            // is the span ENTIRELY at G == 0? then the OLD truth is the zero-measure membrane and the
            // march is RIGHT to drop it; the disagreement is my truth predicate, not the march.
            bool allZero = true, anyPositive = false;
            for (int i = 0; i <= 400; i++)
            {
                float s = (float)(lo + (hi - lo) * i / 400.0);
                float px = -400f + s * dx;
                float dd = ShaperEvaluator.Distance(prog, px, 0f, stack);
                float e = SteppedE(op, ShaperHeight.T(op, dd));
                if (e > 0f) { allZero = false; anyPositive = true; }
            }
            Console.WriteLine("  every sample in the span has E(t) == 0 : " + allZero
                              + "    any sample has E(t) > 0 : " + anyPositive);
            Console.WriteLine("  -> " + (allZero
                ? "the span exists ONLY under the OLD closed convention (the zero-height membrane N3 removed);"
                  + " the march is CORRECT and my truth predicate is the stale one"
                : "the span has real thickness; this is a genuine march omission"));
        }

        // Re-run the whole breadth sweep with a truth predicate carrying the AMENDED HS-1.1 (G > 0)
        Console.WriteLine();
        Console.WriteLine("  the same 7440-ray sweep, judged against a truth predicate carrying the AMENDED HS-1.1:");
        int rays = 0, wrong = 0; double worstGap = 0; string worstAt = "";
        var seen = new SortedSet<int>();
        for (int nn = 2; nn <= 32; nn++)
        {
            var d2 = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                           depth = new ZUIValue(290f), steps = new ZUIValue(nn) };
            var o2 = ShaperHeightCompiler.Compile(d2, prog, 1f, 0f, 0f, 0u);
            var sc = new ShaperResolveScene(); sc.Add(prog, o2);
            var b2 = new ShaperCrossing[256];
            for (int k = 1; k < nn; k++)
            {
                float zt = k / (float)(nn - 1);
                if (zt > 1f) continue;
                foreach (float dd2 in new float[] { 0.02f, 0.1f, 0.3f, 0.6f, 0.85f })
                    foreach (float off in new float[] { -3e-4f, 0f, 3e-4f })
                    {
                        float zz = o2.baseZ + o2.body * zt + off;
                        float ddz = -dd2, ddx = Mathf.Sqrt(1f - dd2 * dd2);
                        var r2 = ShaperResolve.Query(sc, -400f, 0f, zz, ddx, 0f, ddz, b2);
                        var tr = TrueCrossingsAmended(prog, stack, o2, -400f, 0f, zz, ddx, 0f, ddz, 1200f, 400000);
                        rays++;
                        double gap = 0;
                        foreach (double tt in tr)
                        { double b = 1e30; for (int i = 0; i < r2.count; i++) { double e = Math.Abs(b2[i].rayT - tt); if (e < b) b = e; } if (b > gap) gap = b; }
                        if (gap > 0.05)
                        {
                            wrong++; seen.Add(nn);
                            if (gap > worstGap) { worstGap = gap; worstAt = "n=" + nn + " k=" + k + " dz=" + dd2 + " off=" + off + " true=" + tr.Count + " got=" + r2.count; }
                        }
                    }
            }
        }
        Console.WriteLine("    rays = " + rays + ",  rays losing a crossing = " + wrong
                          + ",  worst gap = " + worstGap.ToString("E4") + " px");
        if (worstAt != "") Console.WriteLine("    " + worstAt);
        Console.Write("    step counts affected: "); foreach (int q in seen) Console.Write(q + " "); Console.WriteLine();
    }

    // HS-1.1 as amended by N3: inside the silhouette, below base + body*G, and G > 0.
    static bool InsideAmended(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                              float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float zz = oz + s * dz;
        float above = zz - op.baseZ;
        if (above < 0f) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float d = ShaperEvaluator.Distance(field, px, py, stack);
        if (d > 0f || ShaperField.IsEmpty(d)) return false;
        float t = ShaperHeight.T(op, d);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        float g = ShaperHeight.Composed(op, t, nx, ny);
        if (!(g > 0f)) return false;
        return above <= op.body * g;
    }

    static List<double> TrueCrossingsAmended(ShaperProgram f, float[] st, in ShaperHeightOp op,
                                             float ox, float oy, float oz, float dx, float dy, float dz, float sMax, int samples)
    {
        var res = new List<double>();
        bool prev = InsideAmended(f, st, op, ox, oy, oz, dx, dy, dz, 0f);
        if (prev) res.Add(0.0);
        double step = (double)sMax / samples;
        for (int i = 1; i <= samples; i++)
        {
            float s = (float)(i * step);
            bool m = InsideAmended(f, st, op, ox, oy, oz, dx, dy, dz, s);
            if (m != prev)
            {
                double lo = (i - 1) * step, hi = i * step;
                for (int k = 0; k < 40; k++)
                { double mid = 0.5 * (lo + hi); if (InsideAmended(f, st, op, ox, oy, oz, dx, dy, dz, (float)mid) == prev) lo = mid; else hi = mid; }
                res.Add(hi); prev = m;
            }
        }
        return res;
    }
}
