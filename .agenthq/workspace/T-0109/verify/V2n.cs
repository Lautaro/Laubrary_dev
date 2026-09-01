using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// Does the unsound linearEGrad actually make the march LOSE something end to end?
public static class V2LinAttack
{
    public static void Run()
    {
        Console.WriteLine("=== V2-LIN - can the under-reported linearEGrad be turned into a real march failure? ===");
        int rays = 0, wrong = 0; double worstGap = 0; string worstAt = "";
        foreach (float ang in new float[] { -135f, -45f, 45f, 135f })
        foreach (float rot in new float[] { 0f, 17f, 45f })
        foreach (float skew in new float[] { 0f, 24f, 60f })
        {
            var kids = new List<ShaperNode> {
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, "o", ShaperCombineMode.Add) };
            var slot = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 4f, rectHalfH = 80f }, "s", ShaperCombineMode.Subtract);
            slot.transform.translate = new Vector2(14f, 0f);
            kids.Add(slot);
            var root = ShaperNode.Bag("p", ShaperCombineMode.Add, kids.ToArray());
            root.transform.rotation = rot;
            root.transform.scale = new Vector2(2.3f, 1f);
            root.transform.skewDegrees = new Vector2(skew, 0f);
            var prog = ShaperCompiler.Compile(root, 0f, 0u);
            var st = prog.NewStack();
            foreach (var bev in Fixtures.Bevels)
            {
                var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = bev,
                    depth = new ZUIValue(120f), angle = new ZUIValue(ang), bevelAmount = new ZUIValue(0.4f), bevelSteps = new ZUIValue(3f) };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                var scene = new ShaperResolveScene(); scene.Add(prog, op);
                var buf = new ShaperCrossing[256];
                for (int r = 0; r < 24; r++)
                {
                    float tilt = 1f + (r % 8) * 7f;
                    float rad = tilt * Mathf.Deg2Rad;
                    float dx = Mathf.Cos(rad), dz = -Mathf.Sin(rad);
                    float oy = (r / 8 - 1) * 22f;
                    float oz = op.baseZ + (0.1f + 0.3f * (r % 3)) * op.body * op.supG + 220f * Mathf.Tan(rad);
                    var rr = ShaperResolve.Query(scene, -260f, oy, oz, dx, 0f, dz, buf);
                    var truth = March.TrueCrossings(prog, st, op, -260f, oy, oz, dx, 0f, dz, 700f, 700000);
                    rays++;
                    double gap = 0;
                    foreach (double tt in truth)
                    { double best = 1e30; for (int i = 0; i < rr.count; i++) { double e = Math.Abs(buf[i].rayT - tt); if (e < best) best = e; } if (best > gap) gap = best; }
                    if (gap > 0.05)
                    {
                        wrong++;
                        if (gap > worstGap) { worstGap = gap; worstAt = "angle=" + ang + " rot=" + rot + " skew=" + skew + " bevel=" + bev + " tilt=" + tilt + " true=" + truth.Count + " got=" + rr.count; }
                    }
                }
            }
        }
        Console.WriteLine("  rays = " + rays + ",  rays with a true crossing NOT matched within 0.05 px = " + wrong);
        Console.WriteLine("  worst gap = " + worstGap.ToString("E4") + " canvas px");
        if (worstAt != "") Console.WriteLine("  " + worstAt);
    }
}
