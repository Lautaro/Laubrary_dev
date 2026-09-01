using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// Hypothesis: the slab loop re-seeds mPrev = Member(a0) at every slab, and never emits a flip
// for a membership change that happens exactly AT a shared slab boundary. For a Stepped profile
// the crossings live ON the breakpoints, so the coincidence is systematic rather than rare.
public static class V3f
{
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

    public static void Run()
    {
        Console.WriteLine("=== V3f - is the lost crossing a membership flip sitting exactly ON a slab boundary? ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        int n = 8;
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(290f), steps = new ZUIValue(n) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        float z = op.baseZ + op.body * (6f / (n - 1)) + 3e-4f;
        float dzf = 0.3f, dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);

        var bps = new float[512];
        int nb = ShaperHeight.Breakpoints(op, bps);
        Console.WriteLine("  the ray meets each slab boundary at ray parameter s = (oz - (baseZ + body*zeta)) / |dz|:");
        Console.WriteLine(string.Format("{0,6} {1,10} {2,12} {3,14} {4,16} {5,16}",
            "k", "zeta", "z", "s at boundary", "Member(s - 1e-3)", "Member(s + 1e-3)"));
        int flips = 0;
        for (int k = 0; k < nb; k++)
        {
            float zb = op.baseZ + op.body * bps[k];
            float s = (z - zb) / dzf;
            if (s < 0 || s > 1200) continue;
            bool a = InsideAmended(prog, stack, op, -400f, 0f, z, dx, 0f, dz, s - 1e-3f);
            bool b = InsideAmended(prog, stack, op, -400f, 0f, z, dx, 0f, dz, s + 1e-3f);
            if (a != b) flips++;
            Console.WriteLine(string.Format("{0,6} {1,10:F6} {2,12:F4} {3,14:F4} {4,16} {5,16}{6}",
                k, bps[k], zb, s, a, b, a != b ? "   <- MEMBERSHIP FLIPS AT THE SLAB BOUNDARY" : ""));
        }
        Console.WriteLine("  boundaries at which membership flips: " + flips);

        // Now: does the march emit a crossing at any of those?
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var buf = new ShaperCrossing[256];
        var rr = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
        Console.Write("  emitted: "); for (int i = 0; i < rr.count; i++) Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I " : "O ")); Console.WriteLine();

        // ── breadth over profiles: is this Stepped-only? ───────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  breadth: 42 combinations, rays aimed so a crossing lands on a slab boundary");
        Console.WriteLine(string.Format("{0,-10} {1,-9} {2,8} {3,10} {4,12}", "profile", "bevel", "rays", "lost", "worst gap"));
        int totalRays = 0, totalLost = 0;
        foreach (var tech in Fixtures.Techs)
        {
            foreach (var bev in Fixtures.Bevels)
            {
                var d2 = new ShaperHeightDef { technique = tech, bevel = bev, depth = new ZUIValue(290f),
                    steps = new ZUIValue(8f), curve = new ZUIValue(1f), taper = new ZUIValue(1f),
                    bevelAmount = new ZUIValue(0.4f), bevelSteps = new ZUIValue(4f), angle = new ZUIValue(45f) };
                var o2 = ShaperHeightCompiler.Compile(d2, prog, 1f, 0f, 0f, 0u);
                var sc = new ShaperResolveScene(); sc.Add(prog, o2);
                var b2 = new ShaperCrossing[256];
                int nb2 = ShaperHeight.Breakpoints(o2, bps);
                int rays = 0, lost = 0; double worst = 0;
                for (int k = 1; k + 1 < nb2; k++)
                    foreach (float dd in new float[] { 0.1f, 0.3f, 0.6f })
                        foreach (float off in new float[] { -3e-4f, 0f, 3e-4f })
                        {
                            float zz = o2.baseZ + o2.body * bps[k] + off;
                            float ddz = -dd, ddx = Mathf.Sqrt(1f - dd * dd);
                            var r2 = ShaperResolve.Query(sc, -400f, 0f, zz, ddx, 0f, ddz, b2);
                            var tr = Truth(prog, stack, o2, -400f, 0f, zz, ddx, 0f, ddz, 1200f, 400000);
                            rays++; totalRays++;
                            double gap = 0;
                            foreach (double tt in tr)
                            { double b = 1e30; for (int i = 0; i < r2.count; i++) { double e = Math.Abs(b2[i].rayT - tt); if (e < b) b = e; } if (b > gap) gap = b; }
                            if (gap > 0.05) { lost++; totalLost++; if (gap > worst && gap < 1e29) worst = gap; }
                        }
                if (lost > 0)
                    Console.WriteLine(string.Format("{0,-10} {1,-9} {2,8} {3,10} {4,12:F4}", tech, bev, rays, lost, worst));
            }
        }
        Console.WriteLine("  total rays = " + totalRays + ",  rays losing a crossing = " + totalLost
                          + "   (only rows with a loss printed)");
    }

    static List<double> Truth(ShaperProgram f, float[] st, in ShaperHeightOp op,
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
