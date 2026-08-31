using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

public static class V2Last
{
    // ── how broadly does the breakpoint-parallel duplication fire? ─────────────────────────────
    public static void Breakpoints()
    {
        Console.WriteLine("=== V2B-2 - the breakpoint-parallel duplication, over all 42 combinations ===");
        Console.WriteLine("A HORIZONTAL ray (|dz| < 1e-9) at z exactly equal to an INTERIOR slab breakpoint is accepted");
        Console.WriteLine("by the slab below AND the slab above, because ClipSlab's parallel branch is inclusive at both");
        Console.WriteLine("ends. Both slabs march the same range and emit the same flips.");
        Console.WriteLine();
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var bps = new float[512];
        int combos = 0, badCombos = 0, badRays = 0, rays = 0;
        double worstDepthErr = 0; string worstAt = "";
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var def = new ShaperHeightDef {
                    technique = tech, bevel = bev, depth = new ZUIValue(40f),
                    bevelAmount = new ZUIValue(0.5f), bevelSteps = new ZUIValue(4f),
                    steps = new ZUIValue(5f), curve = new ZUIValue(1f), taper = new ZUIValue(1f), angle = new ZUIValue(45f) };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                int nb = ShaperHeight.Breakpoints(op, bps);
                combos++;
                bool comboBad = false;
                for (int i = 1; i + 1 < nb; i++)
                {
                    float z = op.baseZ + op.body * bps[i];
                    var scene = new ShaperResolveScene(); scene.Add(prog, op);
                    var buf = new ShaperCrossing[64];
                    var r = ShaperResolve.Query(scene, -400f, 0f, z, 1f, 0f, 0f, buf);
                    var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, 1f, 0f, 0f, 1200f, 300000);
                    rays++;
                    float depth = ShaperResolve.Depth(buf, r.count, 0);
                    double trueDepth = 0;
                    for (int j = 0; j + 1 < truth.Count; j += 2) trueDepth += truth[j + 1] - truth[j];
                    if (r.count != truth.Count || Math.Abs(depth - trueDepth) > 0.05)
                    {
                        badRays++; comboBad = true;
                        double e = Math.Abs(depth - trueDepth);
                        if (e > worstDepthErr) { worstDepthErr = e; worstAt = tech + "+" + bev + " bp[" + i + "] zeta=" + bps[i].ToString("F5") + " z=" + z + "  got " + r.count + " true " + truth.Count + "  Depth " + depth.ToString("F3") + " vs " + trueDepth.ToString("F3"); }
                    }
                }
                if (comboBad) badCombos++;
            }
        Console.WriteLine("  combinations = " + combos + ",  interior-breakpoint rays fired = " + rays);
        Console.WriteLine("  combinations with at least one wrong ray = " + badCombos + ",  wrong rays = " + badRays);
        Console.WriteLine("  worst |Depth error| = " + worstDepthErr.ToString("F3") + " canvas px");
        Console.WriteLine("  " + worstAt);
    }

    // ── the rim cut in ShaperHeight.FillTile, now that the sheet is read (verifier defect 8) ────
    public static void RimCut()
    {
        Console.WriteLine();
        Console.WriteLine("=== V2-RIM - ShaperHeight.FillTile's hard cut at d = 0, now that the sheet IS read ===");
        int W = 64, H = 16; float px = 1f;
        var grid = ShaperSampleGrid.Centred(W, H, px, 1.5f);
        var root = March.SolidPlate(20f, 6f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Flat, bevel = ShaperBevelTechnique.None, depth = new ZUIValue(24f) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        int n = W * H;
        var dist = new float[n]; var cov = new float[n]; var hgt = new float[n];
        ShaperEvaluator.FillTile(prog, grid, 0, 0, W, H, dist, cov, 0, W, stack);
        ShaperHeight.FillTile(op, grid, 0, 0, W, H, dist, hgt, 0, W, 0, W);
        int row = H / 2;
        Console.WriteLine("  a row across the rim (Flat, body 24, halfBand from the grid):");
        Console.WriteLine(string.Format("{0,10} {1,12} {2,12} {3,12}", "d", "coverage", "height", "cov*body"));
        float worstGap = 0f;
        for (int x = 0; x < W; x++)
        {
            int i = row * W + x;
            if (dist[i] > -3f && dist[i] < 3f)
            {
                Console.WriteLine(string.Format("{0,10:F4} {1,12:F5} {2,12:F5} {3,12:F5}", dist[i], cov[i], hgt[i], cov[i] * op.body));
                if (dist[i] > 0f && cov[i] > 0f)
                    worstGap = Mathf.Max(worstGap, cov[i] * op.body - hgt[i]);
            }
        }
        Console.WriteLine("  worst (coverage*body - height) at a PARTIALLY covered sample outside the silhouette: "
                          + worstGap.ToString("F5") + " canvas px");
    }

    public static void Run() { Breakpoints(); RimCut(); }
}
