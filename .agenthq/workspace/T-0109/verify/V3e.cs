using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// Mechanism of the 26 remaining lost-crossing rays, replicating the march's own slab-wide skip
// decision from the PUBLIC API so the jump can be watched happening.
public static class V3e
{
    static float SteppedE(in ShaperHeightOp op, float t)
    { float e = Mathf.Floor(t * op.n) * op.invN1; return e > 1f ? 1f : e; }

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
        float g = ShaperHeight.Composed(op, t, 0f, 0f);
        if (!(g > 0f)) return false;
        return above <= op.body * g;
    }

    public static void Run()
    {
        Console.WriteLine("=== V3e - mechanism of the remaining lost crossings ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        int n = 8;
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(290f), steps = new ZUIValue(n) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        float zeta0 = 6f / (n - 1);
        float z = op.baseZ + op.body * zeta0 + 3e-4f;
        float dzf = 0.3f, dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var buf = new ShaperCrossing[256];
        var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);

        Console.WriteLine("  n=" + n + " body=" + op.body + " span=" + op.span + " baseZ=" + op.baseZ);
        Console.WriteLine("  ray z=" + z.ToString("F6") + " dir=(" + dx.ToString("F5") + ",0," + dz + ")");
        Console.Write("  emitted: "); for (int i = 0; i < rr.count; i++) Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I " : "O ")); Console.WriteLine();

        // true crossings under the AMENDED convention
        var tr = new List<double>();
        bool prev = InsideAmended(prog, stack, op, -400f, 0f, z, dx, 0f, dz, 0f);
        for (int i = 1; i <= 1200000; i++)
        {
            float s = 1200f * i / 1200000f;
            bool m = InsideAmended(prog, stack, op, -400f, 0f, z, dx, 0f, dz, s);
            if (m != prev)
            {
                double lo = 1200.0 * (i - 1) / 1200000.0, hi = 1200.0 * i / 1200000.0;
                for (int k = 0; k < 40; k++) { double mid = 0.5 * (lo + hi); if (InsideAmended(prog, stack, op, -400f, 0f, z, dx, 0f, dz, (float)mid) == prev) lo = mid; else hi = mid; }
                tr.Add(hi); prev = m;
            }
        }
        Console.Write("  truth  : "); foreach (double t in tr) Console.Write(t.ToString("F4") + " "); Console.WriteLine();

        var bps = new float[512];
        int nb = ShaperHeight.Breakpoints(op, bps);
        Console.WriteLine("  breakpoints: " + nb + " -> " + (nb - 1) + " slabs");

        // find the missed span and watch the skip that jumps it
        for (int j = 0; j + 1 < tr.Count; j += 2)
        {
            double lo = tr[j], hi = tr[j + 1];
            double best = 1e30;
            for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - lo));
            if (best <= 0.05) continue;
            Console.WriteLine();
            Console.WriteLine("  MISSED span [" + lo.ToString("F4") + ", " + hi.ToString("F4") + "]  width " + (hi - lo).ToString("F4") + " px");
            float bound = prog.bound > 0f ? prog.bound : 1f;
            float dxy = dx;
            Console.WriteLine("  walking the slab-wide skip test backwards from the span:");
            Console.WriteLine(string.Format("{0,10} {1,9} {2,9} {3,10} {4,9} {5,5} {6,10} {7,10} {8,11} {9,9}",
                "rayT", "x", "z", "d", "zeta", "slab", "tauMinSlab", "tauMaxSlab", "gapIn", "skip"));
            for (double s = lo - 40; s < hi + 2; s += 2.0)
            {
                float ss = (float)s;
                float px = -400f + ss * dx, pz = z + ss * dz;
                float dd = ShaperEvaluator.Distance(prog, px, 0f, stack);
                float ze = (pz - op.baseZ) / op.body;
                int slab = -1;
                for (int q = 0; q + 1 < nb; q++) if (ze >= bps[q] && ze < bps[q + 1]) slab = q;
                if (slab < 0) { Console.WriteLine(string.Format("{0,10:F4} {1,9:F3} {2,9:F4} {3,10:F4} {4,9:F6} {5,5} (outside every slab)", s, px, pz, dd, ze, slab)); continue; }
                float tmin = ShaperHeight.InverseLowerBound(op, bps[slab]);
                float tmax = ShaperHeight.InverseUpperBound(op, bps[slab + 1]);
                float gOut = dd + (ShaperHeight.IsNoCrossSection(tmin) ? 0f : tmin) * op.span;
                float gIn = ShaperHeight.IsNoCrossSection(tmax) ? float.NaN : -dd - tmax * op.span;
                string skip = gOut > 0f ? "AIR " + (gOut / (bound * dxy)).ToString("F3")
                             : (!float.IsNaN(gIn) && gIn > 0f ? "SOLID " + (gIn / (bound * dxy)).ToString("F3") : "-");
                Console.WriteLine(string.Format("{0,10:F4} {1,9:F3} {2,9:F4} {3,10:F4} {4,9:F6} {5,5} {6,10:F6} {7,10:F6} {8,11:F4} {9,9}",
                    s, px, pz, dd, ze, slab, tmin, tmax, gIn, skip));
            }
            break;
        }

        // Is the SOLID skip's claim actually true over its own step?
        Console.WriteLine();
        Console.WriteLine("  the soundness question: inside slab k, does t >= tauMax(zetaHi) really imply solid?");
        Console.WriteLine("  For Stepped, G is a STAIRCASE in t, so G(t) >= zetaHi holds — but the point's OWN zeta");
        Console.WriteLine("  must also be <= G(t), and the skip only bounds zeta by the SLAB's top, not by G(t).");
        for (int q = 0; q + 1 < nb; q++)
        {
            float tmax = ShaperHeight.InverseUpperBound(op, bps[q + 1]);
            if (ShaperHeight.IsNoCrossSection(tmax)) continue;
            float gAtTmax = ShaperHeight.Composed(op, tmax, 0f, 0f);
            Console.WriteLine("    slab " + q + " zeta in [" + bps[q].ToString("F6") + ", " + bps[q + 1].ToString("F6")
                              + "]  tauMax=" + tmax.ToString("F6") + "  G(tauMax)=" + gAtTmax.ToString("F6")
                              + (gAtTmax + 1e-7f < bps[q + 1] ? "   <- G(tauMax) < zetaHi, the contained prism is NOT contained"
                                                              : "   ok"));
        }
    }
}
