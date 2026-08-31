using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// ============================================================================================
// ATTACK 1 — the in-prism / resStep sampling limit of ShaperResolve.QueryGeneral.
// Independent ground truth: a dense scan of the SAME exact O(1) membership predicate HS-5.4
// declares (evaluate d, form t, evaluate G, compare with the height) — written here, not reused
// from ShaperResolve, so the reference and the subject share no code path.
// ============================================================================================
public static class March
{
    // My own membership predicate. Same definition as HS-1.1's implicit solid, my own code.
    public static bool Inside(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                              float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float z = oz + s * dz;
        float above = z - op.baseZ;
        if (above < 0f) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float d = ShaperEvaluator.Distance(field, px, py, stack);
        if (d > 0f || ShaperField.IsEmpty(d)) return false;
        float t = ShaperHeight.T(op, d);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        return above <= op.body * ShaperHeight.Composed(op, t, nx, ny);
    }

    // Ground truth crossing list: a dense uniform scan.
    public static List<double> TrueCrossings(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                             float ox, float oy, float oz, float dx, float dy, float dz,
                                             float sMax, int samples)
    {
        var res = new List<double>();
        bool prev = Inside(field, stack, op, ox, oy, oz, dx, dy, dz, 0f);
        if (prev) res.Add(0.0);
        double step = (double)sMax / samples;
        for (int i = 1; i <= samples; i++)
        {
            float s = (float)(i * step);
            bool m = Inside(field, stack, op, ox, oy, oz, dx, dy, dz, s);
            if (m != prev)
            {
                // bisect to the true crossing
                double lo = (i - 1) * step, hi = i * step;
                for (int k = 0; k < 40; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Inside(field, stack, op, ox, oy, oz, dx, dy, dz, (float)mid) == prev) lo = mid; else hi = mid;
                }
                res.Add(hi);
                prev = m;
            }
        }
        return res;
    }

    public static ShaperNode HollowPlate(float halfW, float halfH, float wall)
    {
        var outer = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH, rectCornerRadius = 0f };
        var inner = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW - wall, rectHalfH = halfH - wall, rectCornerRadius = 0f };
        var a = ShaperNode.Primitive(outer, "outer", ShaperCombineMode.Add);
        var b = ShaperNode.Primitive(inner, "inner", ShaperCombineMode.Subtract);
        return ShaperNode.Bag("ring", ShaperCombineMode.Add, a, b);
    }

    public static ShaperNode SolidPlate(float halfW, float halfH)
    {
        var outer = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH, rectCornerRadius = 0f };
        return ShaperNode.Bag("plate", ShaperCombineMode.Add, ShaperNode.Primitive(outer, "o", ShaperCombineMode.Add));
    }

    public static void Run()
    {
        Console.WriteLine("=== ATTACK 1 — can the slab march MISS a real feature? ===");
        Console.WriteLine();

        // A hollow square plate: 400x400 canvas px outer, WALL px thick. Extruded 40px, Flat profile,
        // no bevel. HS-6.6 itself calls a hollow shell "ordinary".
        float[] walls = { 40f, 25f, 20f, 12f, 8f, 5f, 2f };
        // Tilt angles away from horizontal, in degrees, so the GENERAL branch is taken.
        float[] tilts = { 0.0f, 1f, 5f, 15f, 30f, 45f };

        Console.WriteLine("shape: hollow square plate, outer half-extent 200px, extruded depth 40, Flat/None");
        Console.WriteLine("ray:   travelling +X, tilted `tilt` degrees downward, aimed at the solid's mid-height");
        Console.WriteLine();
        Console.WriteLine(string.Format("{0,6} {1,6} {2,8} {3,8} {4,10} {5,10} {6,9}", "wall", "tilt", "trueX", "gotX", "resStep", "branch", "verdict"));

        foreach (float wall in walls)
        {
            var root = HollowPlate(200f, 200f, wall);
            var prog = ShaperCompiler.Compile(root, 0f, 0u);
            var stack = prog.NewStack();
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 4f, 1f, 1f, 0.25f, 3f, 40f, 45f);
            // span comes from the program in production; force it the same way the compiler would
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Flat, bevel = ShaperBevelTechnique.None, depth = new ZUIValue(40f) };
            op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);

            foreach (float tilt in tilts)
            {
                double rad = tilt * Math.PI / 180.0;
                float dx = (float)Math.Cos(rad), dy = 0f, dz = (float)(-Math.Sin(rad));
                // Aim at mid-height of the solid at the left wall, then back the origin up along -d.
                float targetZ = op.baseZ + 0.5f * op.body * op.supG;
                float back = 600f;
                float ox = -200f - dx * back, oy = 0f, oz = targetZ - dz * back;

                var crossings = new ShaperCrossing[64];
                var r = ShaperResolve.Query(ShaperResolveSceneOf(prog, op), ox, oy, oz, dx, dy, dz, crossings);

                var truth = TrueCrossings(prog, stack, op, ox, oy, oz, dx, dy, dz, 2000f, 800000);

                // resStep the implementation would use: (a1-a0)/8 for the single slab. Recover it
                // from the geometry: the X clip through the 400-wide support box.
                float resStep = 400f / Math.Max(1e-6f, dx) / ShaperResolve.SlabSamples;
                string verdict = (r.count == truth.Count) ? "ok" : "MISS " + (truth.Count - r.count);
                Console.WriteLine(string.Format("{0,6} {1,6} {2,8} {3,8} {4,10:F2} {5,10} {6,9}",
                    wall, tilt, truth.Count, r.count, resStep, r.branch, verdict));
            }
            Console.WriteLine();
        }
    }

    public static ShaperResolveScene ShaperResolveSceneOf(ShaperProgram prog, in ShaperHeightOp op)
    {
        var scene = new ShaperResolveScene();
        scene.Add(prog, op);
        return scene;
    }

    // A second, cleaner attack: a SOLID plate with a thin bar-shaped feature is not needed — the
    // straight-down closed form and the general branch must agree at ZERO tilt on a bevelled shape.
    public static void RunAgreement()
    {
        Console.WriteLine("=== H6 re-run, independently: do the two branches agree at (near) zero tilt? ===");
        var root = SolidPlate(150f, 110f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var techs = Fixtures.Techs; var bevs = Fixtures.Bevels;
        int compared = 0, disagree = 0; double worst = 0; string worstAt = "";
        foreach (var tech in techs)
            foreach (var bev in bevs)
            {
                var def = new ShaperHeightDef { technique = tech, bevel = bev, depth = new ZUIValue(40f), bevelAmount = new ZUIValue(0.35f) };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                var scene = ShaperResolveSceneOf(prog, op);
                var cd = new ShaperCrossing[32];
                var cg = new ShaperCrossing[32];
                for (int i = -6; i <= 6; i++)
                    for (int j = -4; j <= 4; j++)
                    {
                        float px = i * 22f, py = j * 22f;
                        float oz = op.baseZ + op.body * op.supG + 50f;
                        var rd = ShaperResolve.Query(scene, px, py, oz, 0f, 0f, -1f, cd);
                        // just off the -Z axis: takes the general branch, geometry essentially identical
                        float e = 2e-5f;
                        var rg = ShaperResolve.Query(scene, px, py, oz, e, 0f, -1f, cg);
                        if (rd.branch != ShaperResolveBranch.StraightDown || rg.branch != ShaperResolveBranch.General) continue;
                        compared++;
                        if (rd.count != rg.count) { disagree++; if (worst < 1e9) { worst = 1e9; worstAt = tech + "+" + bev + " at (" + px + "," + py + ") " + rd.count + " vs " + rg.count; } continue; }
                        for (int k = 0; k < rd.count; k++)
                        {
                            double e2 = Math.Abs(cd[k].rayT - cg[k].rayT);
                            if (e2 > worst && worst < 1e9) { worst = e2; worstAt = tech + "+" + bev + " at (" + px + "," + py + ") rayT " + cd[k].rayT + " vs " + cg[k].rayT; }
                        }
                    }
            }
        Console.WriteLine("  compared=" + compared + " countDisagreements=" + disagree + " worst=" + worst.ToString("E3") + "  " + worstAt);
    }
}
