using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// The 12 containing-prism violations V3-N6(c) found. My second pass measured 0 on the same
// property, so if these are real they are a REGRESSION introduced by the N6 rewrite.
public static class V3b
{
    static float SteppedE(in ShaperHeightOp op, float t)
    {
        float e = Mathf.Floor(t * op.n) * op.invN1;
        return e > 1f ? 1f : e;
    }

    public static void Run()
    {
        Console.WriteLine("=== V3b - the containing-prism violations, isolated ===");
        Console.WriteLine("HS-5.2 needs {G >= zeta} subset of {t >= tauMin}. A violation means some t BELOW tauMin");
        Console.WriteLine("already reaches zeta, i.e. the containing prism does NOT contain, and the march can step");
        Console.WriteLine("clean through solid. This is the one direction HS-5.4 explicitly does not cover.");
        Console.WriteLine();

        var bad = new List<string>();
        long checks = 0, viol = 0;
        double worstT = 0;
        for (int n = 2; n <= 32; n++)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int k = 0; k <= n; k++)
            {
                float tread = Mathf.Min(1f, k / (float)(n - 1));
                for (int u = -2; u <= 2; u++)
                {
                    float zeta = u == 0 ? tread : (u > 0 ? V2.NextUp(tread, u) : NextDown(tread, -u));
                    if (zeta <= 0f || zeta > 1f) continue;
                    float tauMin = ShaperHeight.InverseLowerBound(op, zeta);
                    if (ShaperHeight.IsNoCrossSection(tauMin)) continue;
                    // the exact smallest t reaching zeta, by scanning the risers themselves
                    float trueInf = float.PositiveInfinity;
                    for (int j = 0; j <= n; j++)
                    {
                        float t = j / (float)n;
                        for (int q = 0; q < 8 && SteppedE(op, t) < zeta; q++) t *= 1.0000001f;
                        if (SteppedE(op, t) >= zeta) { trueInf = t; break; }
                    }
                    checks++;
                    if (tauMin > trueInf + 1e-6f)
                    {
                        viol++;
                        double over = tauMin - trueInf;
                        if (over > worstT) worstT = over;
                        bad.Add("n=" + n + " k=" + k + " ulp=" + u + " zeta=" + zeta.ToString("R")
                                + "  tauMin=" + tauMin.ToString("R") + "  trueInf=" + trueInf.ToString("R")
                                + "  over-report=" + over.ToString("E3") + " t-units (1 tread = " + (1f / n).ToString("E3") + ")"
                                + "  E(trueInf)=" + SteppedE(op, trueInf).ToString("R") + " >= zeta");
                    }
                }
            }
        }
        Console.WriteLine("  zeta probes = " + checks + "   Ginv OVER-reports the true infimum in " + viol + " of them");
        Console.WriteLine("  worst over-report = " + worstT.ToString("E3") + " t-units");
        foreach (string s in bad) Console.WriteLine("    " + s);

        Console.WriteLine();
        Console.WriteLine("  WHY: the DOWN walk asks SteppedE at the UN-NUDGED (k-1)/n, which is exactly the float");
        Console.WriteLine("  that needed the UP nudge at the four latent indices, so it breaks immediately.");
        foreach (int n in new int[] { 22, 23, 29 })
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            foreach (int k in new int[] { 7, 13, 14, 15 })
            {
                if (k > n) continue;
                float raw = k / (float)n;
                float nudged = raw;
                for (int q = 0; q < 8 && Mathf.Floor(nudged * op.n) < k; q++) nudged *= 1.0000001f;
                if (Mathf.FloorToInt(raw * n) >= k) continue;
                Console.WriteLine("    n=" + n + " k=" + k + ": fl(k/n)=" + raw.ToString("R")
                                  + "  floor(fl(k/n)*n)=" + Mathf.FloorToInt(raw * n) + " (want " + k + ")"
                                  + "  SteppedE(raw)=" + SteppedE(op, raw).ToString("R")
                                  + "  SteppedE(nudged)=" + SteppedE(op, nudged).ToString("R"));
            }
        }

        // Does it produce a real march failure?
        Console.WriteLine();
        Console.WriteLine("=== does the un-contained prism lose a crossing in the actual march? ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int rays = 0, wrong = 0; double worstGap = 0; string worstAt = "";
        foreach (int n in new int[] { 22, 23, 29 })
        {
            var def = new ShaperHeightDef {
                technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                depth = new ZUIValue(290f), steps = new ZUIValue(n) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[256];
            // aim rays at the z of the affected risers
            foreach (int k in new int[] { 7, 13, 14, 15 })
            {
                if (k >= n) continue;
                float zeta = k / (float)(n - 1);
                foreach (float dzf in new float[] { 0f, 0.02f, 0.1f, 0.3f, 0.6f })
                    foreach (float off in new float[] { -3e-4f, 0f, 3e-4f })
                    {
                        float z = op.baseZ + op.body * zeta + off;
                        float dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
                        var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
                        var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, dx, 0f, dz, 1200f, 600000);
                        rays++;
                        double gap = 0;
                        foreach (double tt in truth)
                        { double best = 1e30; for (int i = 0; i < rr.count; i++) { double e = Math.Abs(buf[i].rayT - tt); if (e < best) best = e; } if (best > gap) gap = best; }
                        if (gap > 0.05)
                        {
                            wrong++;
                            if (gap > worstGap) { worstGap = gap; worstAt = "n=" + n + " k=" + k + " dz=" + dzf + " off=" + off + " true=" + truth.Count + " got=" + rr.count; }
                        }
                    }
            }
        }
        Console.WriteLine("  rays = " + rays + ",  rays with a true crossing unmatched within 0.05 px = " + wrong);
        Console.WriteLine("  worst gap = " + worstGap.ToString("E4") + " px   " + worstAt);
    }

    static float NextDown(float x, int n)
    {
        for (int i = 0; i < n; i++) { int b = BitConverter.SingleToInt32Bits(x); x = BitConverter.Int32BitsToSingle(x > 0 ? b - 1 : b + 1); }
        return x;
    }
}
