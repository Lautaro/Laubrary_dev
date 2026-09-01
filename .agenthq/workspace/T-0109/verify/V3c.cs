using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// The 9.1 px omission V3b found. Attribute it: is it the over-reported containing prism, or
// something else? And how broadly does it fire?
public static class V3c
{
    static float SteppedE(in ShaperHeightOp op, float t)
    {
        float e = Mathf.Floor(t * op.n) * op.invN1;
        return e > 1f ? 1f : e;
    }

    // the exact smallest t reaching zeta, found by walking the risers - independent of ProfileInverse
    static float TrueInf(in ShaperHeightOp op, float zeta)
    {
        for (int j = 0; j <= op.n; j++)
        {
            float t = j / (float)op.n;
            for (int q = 0; q < 8 && SteppedE(op, t) < zeta; q++) t *= 1.0000001f;
            if (SteppedE(op, t) >= zeta) return t;
        }
        return float.PositiveInfinity;
    }

    public static void Run()
    {
        Console.WriteLine("=== V3c - attributing the 9.1 px omission, and measuring how broadly it fires ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        // --- the exact failing ray ---
        {
            int n = 23;
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                            depth = new ZUIValue(290f), steps = new ZUIValue(n) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            float zeta = 15f / (n - 1);
            float z = op.baseZ + op.body * zeta - 3e-4f;
            float dzf = 0.3f, dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[256];
            var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
            var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, dx, 0f, dz, 1200f, 1200000);
            Console.WriteLine("  the failing ray: n=" + n + " body=" + op.body + " span=" + op.span
                              + "  origin=(-400,0," + z.ToString("F5") + ") dir=(" + dx.ToString("F5") + ",0," + dz + ")");
            Console.WriteLine("    got " + rr.count + ", true " + truth.Count
                              + "   Depth " + ShaperResolve.Depth(buf, rr.count, 0).ToString("F4"));
            Console.Write("    emitted: "); for (int i = 0; i < rr.count; i++) Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I " : "O ")); Console.WriteLine();
            Console.Write("    truth  : "); foreach (double t in truth) Console.Write(t.ToString("F4") + " "); Console.WriteLine();
            Console.WriteLine("    bracketCapped=" + rr.bracketCapped + " stepsExhausted=" + rr.stepsExhausted + " branch=" + rr.branch);

            // which slab does the missed span live in, and is that slab's tauMin over-reported?
            for (int j = 0; j < truth.Count; j++)
            {
                double best = 1e30;
                for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - truth[j]));
                if (best <= 0.05) continue;
                float s = (float)truth[j];
                float px = -400f + s * dx, pz = z + s * dz;
                float dd = ShaperEvaluator.Distance(prog, px, 0f, stack);
                float tt = ShaperHeight.T(op, dd);
                float zHere = (pz - op.baseZ) / op.body;
                Console.WriteLine("    MISSED true crossing at rayT=" + s.ToString("F4") + "  x=" + px.ToString("F4")
                                  + " z=" + pz.ToString("F5") + "  d=" + dd.ToString("F4") + " t=" + tt.ToString("F6")
                                  + "  zeta=" + zHere.ToString("F7") + "  E(t)=" + SteppedE(op, tt).ToString("R"));
                float tauMin = ShaperHeight.InverseLowerBound(op, zHere);
                float trueInf = TrueInf(op, zHere);
                Console.WriteLine("      InverseLowerBound(zeta) = " + tauMin.ToString("R")
                                  + "   TRUE infimum = " + trueInf.ToString("R")
                                  + "   over-report = " + (tauMin - trueInf).ToString("E3")
                                  + " t-units" + (tauMin > trueInf + 1e-6f ? "   <- THE PRISM DOES NOT CONTAIN THIS POINT" : "   (prism sound here)"));
                var bps = new float[512];
                int nb = ShaperHeight.Breakpoints(op, bps);
                for (int q = 0; q + 1 < nb; q++)
                    if (zHere >= bps[q] && zHere <= bps[q + 1])
                    {
                        float tms = ShaperHeight.InverseLowerBound(op, bps[q]);
                        float ti = TrueInf(op, bps[q]);
                        Console.WriteLine("      slab " + q + " zetaLo=" + bps[q].ToString("R") + "  tauMinSlab=" + tms.ToString("R")
                                          + "  true=" + ti.ToString("R") + (tms > ti + 1e-6f ? "   <- SLAB PRISM UNSOUND" : "   (slab prism sound)"));
                    }
            }
        }

        // --- breadth: every step count, many ray angles and offsets ---
        Console.WriteLine();
        Console.WriteLine("  breadth: every n in [2,32], every tread, 5 tilts x 3 offsets");
        int rays = 0, wrong = 0; double worstGap = 0; string worstAt = "";
        var seen = new SortedSet<int>();
        for (int n = 2; n <= 32; n++)
        {
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                            depth = new ZUIValue(290f), steps = new ZUIValue(n) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[256];
            for (int k = 1; k < n; k++)
            {
                float zeta = k / (float)(n - 1);
                if (zeta > 1f) continue;
                foreach (float dzf in new float[] { 0.02f, 0.1f, 0.3f, 0.6f, 0.85f })
                    foreach (float off in new float[] { -3e-4f, 0f, 3e-4f })
                    {
                        float z = op.baseZ + op.body * zeta + off;
                        float dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
                        var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
                        var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, dx, 0f, dz, 1200f, 400000);
                        rays++;
                        double gap = 0;
                        foreach (double tt in truth)
                        { double b = 1e30; for (int i = 0; i < rr.count; i++) { double e = Math.Abs(buf[i].rayT - tt); if (e < b) b = e; } if (b > gap) gap = b; }
                        if (gap > 0.05)
                        {
                            wrong++; seen.Add(n);
                            if (gap > worstGap) { worstGap = gap; worstAt = "n=" + n + " k=" + k + " tilt-dz=" + dzf + " off=" + off + " true=" + truth.Count + " got=" + rr.count; }
                        }
                    }
            }
        }
        Console.WriteLine("    rays = " + rays + ",  rays losing a crossing = " + wrong
                          + ",  worst gap = " + worstGap.ToString("E4") + " px");
        Console.WriteLine("    " + worstAt);
        Console.Write("    step counts affected: "); foreach (int n in seen) Console.Write(n + " "); Console.WriteLine();

        // --- is the containment property broken for step counts OTHER than the four latent ones? ---
        Console.WriteLine();
        Console.WriteLine("  containment of InverseLowerBound over a DENSE zeta grid, every n in [2,32]:");
        long checks = 0, viol = 0; double worst = 0; var vn = new SortedSet<int>();
        for (int n = 2; n <= 32; n++)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int i = 1; i <= 4000; i++)
            {
                float zeta = i / 4000f;
                float tauMin = ShaperHeight.InverseLowerBound(op, zeta);
                if (ShaperHeight.IsNoCrossSection(tauMin)) continue;
                float ti = TrueInf(op, zeta);
                checks++;
                if (tauMin > ti + 1e-6f) { viol++; vn.Add(n); if (tauMin - ti > worst) worst = tauMin - ti; }
            }
        }
        Console.WriteLine("    zeta probes = " + checks + "   over-reports = " + viol + "   worst = " + worst.ToString("E3") + " t-units");
        Console.Write("    step counts affected: "); foreach (int n in vn) Console.Write(n + " "); Console.WriteLine();
    }
}
