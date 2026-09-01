using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// W5 — third-pass verifier: independent re-implementation of two ShaperHeightAudit checks (H4, H8),
// the near-zero-G march probe FIX-REPORT-2 left open, and a Depth/branch-agreement cross-check.
public static class W5
{
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W5A — H4 re-implemented. H4 claims, per combination: |closed - bisected| small, and
    //        G(Ginv(zeta)) >= zeta. I bisect myself, on my own log+linear grid.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void A()
    {
        Console.WriteLine("=== W5A — H4 re-implemented independently (closed Ginv vs MY bisection, and reach) ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));

        Console.WriteLine(string.Format("  {0,-22} {1,14} {2,14} {3,10}", "combination", "worst |dt|", "worst reach", "H4 says"));
        var h4 = new Dictionary<string, string>();
        long total = 0;
        double globalDt = 0, globalReach = 0;
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = W3.MakeOp(prog, te, be, 40f, 4, 0.25f, 1f, 1f, 3);
            if (!op.present) continue;
            double worstDt = 0, worstReach = 0;
            for (int i = 0; i <= 600; i++)
            {
                // MY grid: linear over [0, supG] plus log-spaced down to 1e-9, both inclusive
                float zeta = i <= 300 ? (i / 300f) * op.supG
                                      : (float)(Math.Pow(10, -9 + (i - 300) * 9.0 / 300) * op.supG);
                if (zeta <= 0f || zeta > op.supG) continue;
                float closed = ShaperHeight.Inverse(op, zeta, 0f, 0f);
                if (ShaperHeight.IsNoCrossSection(closed)) continue;
                total++;
                // MY bisection on the shipped forward G
                float lo = 0f, hi = 1f;
                if (ShaperHeight.Composed(op, hi, 0f, 0f) < zeta) continue;
                for (int q = 0; q < 60; q++) { float mid = 0.5f * (lo + hi); if (ShaperHeight.Composed(op, mid, 0f, 0f) >= zeta) hi = mid; else lo = mid; }
                double dt = Math.Abs((double)closed - hi);
                if (dt > worstDt) worstDt = dt;
                double reach = (double)zeta - ShaperHeight.Composed(op, closed, 0f, 0f);
                if (reach > worstReach) worstReach = reach;
            }
            if (worstDt > globalDt) globalDt = worstDt;
            if (worstReach > globalReach) globalReach = worstReach;
            h4[te + "+" + be] = worstDt.ToString("E3") + " / " + worstReach.ToString("E3");
            Console.WriteLine(string.Format("  {0,-22} {1,14} {2,14}", te + " + " + be, worstDt.ToString("E3"), worstReach.ToString("E3")));
        }
        Console.WriteLine("  round-trips = " + total + "   worst |dt| overall = " + globalDt.ToString("E3") + "   worst reach shortfall = " + globalReach.ToString("E3"));
        Console.WriteLine("  H4 (editor) reports worst |dt| 4.567E-004 (Dome) and worst reach 3.397E-006 (Flat+Cove).");
        Console.WriteLine("  INJECTION — can MY probe see a broken Ginv? multiply it by 0.95 and re-measure reach:");
        {
            var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.Rounded, 40f, 4, 0.25f, 1f, 1f, 3);
            double worst = 0;
            for (int i = 1; i <= 300; i++)
            {
                float zeta = (i / 300f) * op.supG;
                float closed = ShaperHeight.Inverse(op, zeta, 0f, 0f) * 0.95f;
                double reach = (double)zeta - ShaperHeight.Composed(op, closed, 0f, 0f);
                if (reach > worst) worst = reach;
            }
            Console.WriteLine("    perturbed reach shortfall = " + worst.ToString("E4") + "  (must be >> the baseline above)");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W5B — near-zero G in the march (FIX-REPORT-2 open question 3, with a config that actually
    //        produces it: Dome/Round curve 0.2 reach G ≈ 1.4e-45 at small t, per W4D).
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void B()
    {
        Console.WriteLine();
        Console.WriteLine("=== W5B — 'G nearly zero': does the march emit a sub-resolution pair on Dome/Round c=0.2? ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];
        foreach (var te in new[] { ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round, ShaperExtrusionTechnique.Taper, ShaperExtrusionTechnique.Pyramid })
            foreach (float c in new[] { 0.2f, 1f })
            {
                var op = W3.MakeOp(prog, te, ShaperBevelTechnique.None, 300f, 4, 0.5f, c, 1f);
                var scene = new ShaperResolveScene(); scene.Add(prog, op);
                int narrow = 0, rays = 0, capped = 0, exh = 0; double narrowest = 1e30;
                for (int a = 0; a < 24; a++)
                {
                    float ang = 2f + a * 3.5f, rad = ang * Mathf.Deg2Rad;
                    float dz = -Mathf.Sin(rad), dx = Mathf.Cos(rad);
                    float oz = op.baseZ + op.body * op.supG + 20f;
                    var rr = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, buf);
                    rays++; capped += rr.bracketCapped; if (rr.stepsExhausted) exh++;
                    for (int i = 1; i < rr.count; i++)
                        if (buf[i - 1].entering && !buf[i].entering)
                        {
                            double w = buf[i].rayT - buf[i - 1].rayT;
                            if (w < ShaperResolve.SurfaceResolution) { narrow++; if (w < narrowest) narrowest = w; }
                        }
                }
                Console.WriteLine("  " + te + " c=" + c + ": rays=" + rays + "  emitted pairs narrower than SurfaceResolution(0.02) = " + narrow
                                + (narrow > 0 ? "  narrowest " + narrowest.ToString("E4") + " px" : "")
                                + "   bracketCapped=" + capped + "  stepsExhausted rays=" + exh);
            }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W5C — allocation on the hot path, measured by ME (not by H8).
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void C()
    {
        Console.WriteLine();
        Console.WriteLine("=== W5C — hot-path allocation, measured with GC.GetAllocatedBytesForCurrentThread ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var buf = new ShaperCrossing[512];
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));
        var scenes = new List<ShaperResolveScene>();
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = W3.MakeOp(prog, te, be, 300f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var sc = new ShaperResolveScene(); sc.Add(prog, op); scenes.Add(sc);
        }
        // warm up (JIT, any first-call statics)
        foreach (var sc in scenes) { ShaperResolve.Query(sc, -400f, 0f, 400f, 0.9f, 0f, -0.4f, buf); ShaperResolve.Query(sc, 0f, 0f, 400f, 0f, 0f, -1f, buf); }

        long gen0Before = GC.CollectionCount(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int calls = 0;
        for (int rep = 0; rep < 40; rep++)
            foreach (var sc in scenes)
                for (int a = 0; a < 10; a++)
                {
                    float ang = 5f + a * 8f, rad = ang * Mathf.Deg2Rad;
                    ShaperResolve.Query(sc, -400f, 0f, 400f, Mathf.Cos(rad), 0f, -Mathf.Sin(rad), buf); calls++;
                    ShaperResolve.Query(sc, 0f, 0f, 400f, 0f, 0f, -1f, buf); calls++;   // the straight-down branch too
                }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long gen0After = GC.CollectionCount(0);
        Console.WriteLine("  ShaperResolve.Query calls = " + calls + " (both branches)");
        Console.WriteLine("  managed heap bytes allocated = " + (after - before) + "   gen-0 collections = " + (gen0After - gen0Before));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W5D — BC-2.3: the two branches must agree. My own comparison, at the tolerance boundary.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void D()
    {
        Console.WriteLine();
        Console.WriteLine("=== W5D — BC-2.3: straight-down vs general at the branch tolerance, and Depth agreement ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var a = new ShaperCrossing[512]; var b = new ShaperCrossing[512];
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));
        int pairs = 0, countMismatch = 0; double worstT = 0, worstDepth = 0; string at = "";
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = W3.MakeOp(prog, te, be, 300f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int i = 0; i < 24; i++)
            {
                float x = -190f + i * 15f;
                var rd = ShaperResolve.Query(scene, x, 0f, 400f, 0f, 0f, -1f, a);            // straight down
                // just OUTSIDE the tolerance, so the general branch is taken on an almost-vertical ray
                float eps = 2e-5f;
                var rg = ShaperResolve.Query(scene, x, 0f, 400f, eps, 0f, -1f, b);
                if (rd.branch != ShaperResolveBranch.StraightDown || rg.branch != ShaperResolveBranch.General) continue;
                pairs++;
                if (rd.count != rg.count) { countMismatch++; continue; }
                double da = 0, db = 0; bool ia = false, ib = false; double la = 0, lb = 0;
                for (int j = 0; j < rd.count; j++)
                {
                    double e = Math.Abs(a[j].rayT - b[j].rayT); if (e > worstT) { worstT = e; at = te + "+" + be + " x=" + x; }
                    if (a[j].entering) { ia = true; la = a[j].rayT; } else if (ia) { da += a[j].rayT - la; ia = false; }
                    if (b[j].entering) { ib = true; lb = b[j].rayT; } else if (ib) { db += b[j].rayT - lb; ib = false; }
                }
                if (Math.Abs(da - db) > worstDepth) worstDepth = Math.Abs(da - db);
            }
        }
        Console.WriteLine("  branch-paired rays = " + pairs + "   crossing-COUNT mismatches = " + countMismatch);
        Console.WriteLine("  worst |d rayT| = " + worstT.ToString("E4") + " px   worst |d Depth| = " + worstDepth.ToString("E4") + " px   " + at);
    }

    public static void Run(string sel)
    {
        if (sel == "all" || sel == "A") A();
        if (sel == "all" || sel == "B") B();
        if (sel == "all" || sel == "C") C();
        if (sel == "all" || sel == "D") D();
    }
}
