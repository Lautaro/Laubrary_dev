using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// ATTACK 1, take two. A SOLID plate with a thin slot subtracted through its middle.
// The slot is an air gap strictly INSIDE the support box, so neither of the two exact
// skips (empty-space outside the containing prism, solid-space inside the contained
// prism) can carry the marcher across it — the fixed resStep floor has to.
public static class March2
{
    public static ShaperNode SlottedPlate(float halfW, float halfH, float slotHalfW, float slotCx)
    {
        var outer = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH };
        var slot = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = slotHalfW, rectHalfH = halfH * 0.5f };
        var a = ShaperNode.Primitive(outer, "plate", ShaperCombineMode.Add);
        var b = ShaperNode.Primitive(slot, "slot", ShaperCombineMode.Subtract);
        b.transform.translate = new Vector2(slotCx, 0f);
        return ShaperNode.Bag("slotted", ShaperCombineMode.Add, a, b);
    }

    public static void Run()
    {
        Console.WriteLine("=== ATTACK 1 (take 2) — a thin SLOT strictly inside the support box ===");
        Console.WriteLine("shape: solid plate 400x400 canvas px, minus a slot of width `slot` at x = +40");
        Console.WriteLine("layer: Flat profile, no bevel, depth `body`. One slab (HS-5.5), so resStep = (a1-a0)/8.");
        Console.WriteLine("ray:   +X, tilted `tilt` deg downward; ground truth = 800k-sample scan of the exact predicate");
        Console.WriteLine();
        Console.WriteLine(string.Format("{0,7} {1,7} {2,7} {3,7} {4,7} {5,9} {6,9} {7}",
            "slot", "tilt", "body", "true", "got", "resStep", "branch", "verdict"));

        float[] slots = { 60f, 40f, 30f, 20f, 12f, 8f, 4f, 1f };
        var rows = new List<string>();
        int misses = 0;
        foreach (float slot in slots)
        {
            foreach (float tilt in new float[] { 0f, 10f, 30f, 60f })
            {
                float body = tilt < 1f ? 40f : 400f;   // a thick body keeps a tilted ray in the slab
                var root = SlottedPlate(200f, 200f, slot * 0.5f, 40f);
                var prog = ShaperCompiler.Compile(root, 0f, 0u);
                var stack = prog.NewStack();
                var def = new ShaperHeightDef
                {
                    technique = ShaperExtrusionTechnique.Flat,
                    bevel = ShaperBevelTechnique.None,
                    depth = new ZUIValue(body)
                };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);

                double rad = tilt * Math.PI / 180.0;
                float dx = (float)Math.Cos(rad), dy = 0f, dz = (float)(-Math.Sin(rad));
                // start well outside on the -X side, entering at the solid's mid-height
                float startX = -400f;
                float sIn = (startX - (-200f)) / dx;              // negative; used only to place z
                float ox = startX, oy = 0f;
                float oz = op.baseZ + 0.5f * op.body - dz * ((-200f - startX) / dx);

                var scene = new ShaperResolveScene(); scene.Add(prog, op);
                var buf = new ShaperCrossing[64];
                var r = ShaperResolve.Query(scene, ox, oy, oz, dx, dy, dz, buf);
                var truth = March.TrueCrossings(prog, stack, op, ox, oy, oz, dx, dy, dz, 1200f, 1200000);

                float resStep = 400f / dx / ShaperResolve.SlabSamples;
                string verdict = r.count == truth.Count ? "ok" : "MISS (" + (truth.Count - r.count) + " crossings lost)";
                if (r.count != truth.Count) misses++;
                rows.Add(string.Format("{0,7} {1,7} {2,7} {3,7} {4,7} {5,9:F2} {6,9} {7}",
                    slot, tilt, body, truth.Count, r.count, resStep, r.branch, verdict));

                if (r.count != truth.Count && misses <= 3)
                {
                    rows.Add("        truth rayT: " + Join(truth));
                    var got = new List<double>();
                    for (int i = 0; i < r.count; i++) got.Add(buf[i].rayT);
                    rows.Add("        got   rayT: " + Join(got));
                }
            }
        }
        foreach (var s in rows) Console.WriteLine(s);
        Console.WriteLine();
        Console.WriteLine("total configurations that lost crossings: " + misses);
    }

    static string Join(List<double> l)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(l[i].ToString("F3")); }
        return sb.ToString();
    }
}
