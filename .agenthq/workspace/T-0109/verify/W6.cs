using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;
public static class W6
{
    public static void Run()
    {
        Console.WriteLine("=== W6 - drilling the 4.70 px branch disagreement W5D found ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var a = new ShaperCrossing[512]; var b = new ShaperCrossing[512];
        var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Ogee, 300f, 4, 0.5f, 1f, 1f);
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        float x = -190f;
        float d = ShaperEvaluator.Distance(prog, x, 0f, stack);
        float t = ShaperHeight.T(op, d);
        float g = ShaperHeight.Composed(op, t, 0f, 0f);
        Console.WriteLine("  x=" + x + "  d=" + d + "  t=" + t.ToString("F6") + "  G=" + g.ToString("F6") + "  body=" + op.body + "  span=" + op.span + "  a=" + op.a);
        Console.WriteLine("  closed-form surface z = base + body*G = " + (op.baseZ + op.body * g).ToString("F5"));
        var rd = ShaperResolve.Query(scene, x, 0f, 400f, 0f, 0f, -1f, a);
        Console.Write("  STRAIGHT-DOWN (" + rd.branch + "): "); for (int i = 0; i < rd.count; i++) Console.Write(a[i].rayT.ToString("F5") + (a[i].entering ? "I " : "O ")); Console.WriteLine();
        foreach (float eps in new float[] { 2e-5f, 1e-4f, 1e-3f, 1e-2f, 1e-1f })
        {
            var rg = ShaperResolve.Query(scene, x, 0f, 400f, eps, 0f, -1f, b);
            Console.Write("  GENERAL eps=" + eps.ToString("E1") + " (" + rg.branch + " capped=" + rg.bracketCapped + " exh=" + rg.stepsExhausted + "): ");
            for (int i = 0; i < rg.count; i++) Console.Write(b[i].rayT.ToString("F5") + (b[i].entering ? "I " : "O ")); Console.WriteLine();
        }
        // independent truth for the eps=2e-5 ray
        {
            float eps = 2e-5f; float len = Mathf.Sqrt(eps * eps + 1f); float dx = eps / len, dz = -1f / len;
            W3.Pred p = s => W3.Solid(prog, stack, op, x, 0f, 400f, dx, 0f, dz, s);
            var truth = W3.Scan(p, 500.0, 2000000);
            Console.Write("  TRUTH (my scan, eps=2e-5): "); foreach (double v in truth) Console.Write(v.ToString("F5") + " "); Console.WriteLine();
            Console.WriteLine("  true solid length = " + W3.SolidLength(truth, p(0f), 500.0).ToString("F5"));
        }
        // breadth: which combinations/x disagree, and by how much, vs an independent truth
        Console.WriteLine();
        Console.WriteLine("  breadth - for every combination and x, compare BOTH branches to MY OWN scan:");
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));
        int n = 0, sdWrong = 0, genWrong = 0; double wSd = 0, wGen = 0; string atSd = "", atGen = "";
        foreach (var te in techs) foreach (var be in bevs)
        {
            var o2 = W3.MakeOp(prog, te, be, 300f, 4, 0.5f, 1f, 1f);
            if (!o2.present || o2.body <= 0f) continue;
            var sc = new ShaperResolveScene(); sc.Add(prog, o2);
            for (int i = 0; i < 24; i++)
            {
                float xx = -190f + i * 15f;
                float eps = 2e-5f; float len = Mathf.Sqrt(eps * eps + 1f); float dx = eps / len, dz = -1f / len;
                var r1 = ShaperResolve.Query(sc, xx, 0f, 400f, 0f, 0f, -1f, a);
                var r2 = ShaperResolve.Query(sc, xx, 0f, 400f, eps, 0f, -1f, b);
                W3.Pred p = s => W3.Solid(prog, stack, o2, xx, 0f, 400f, dx, 0f, dz, s);
                var truth = W3.Scan(p, 500.0, 300000);
                double tl = W3.SolidLength(truth, p(0f), 500.0);
                double l1 = 0, l2 = 0; bool i1 = false, i2 = false; double p1 = 0, p2 = 0;
                for (int j = 0; j < r1.count; j++) { if (a[j].entering) { i1 = true; p1 = a[j].rayT; } else if (i1) { l1 += a[j].rayT - p1; i1 = false; } }
                for (int j = 0; j < r2.count; j++) { if (b[j].entering) { i2 = true; p2 = b[j].rayT; } else if (i2) { l2 += b[j].rayT - p2; i2 = false; } }
                n++;
                if (Math.Abs(l1 - tl) > 0.05) { sdWrong++; if (Math.Abs(l1 - tl) > wSd) { wSd = Math.Abs(l1 - tl); atSd = te + "+" + be + " x=" + xx + " got " + l1.ToString("F4") + " true " + tl.ToString("F4"); } }
                if (Math.Abs(l2 - tl) > 0.05) { genWrong++; if (Math.Abs(l2 - tl) > wGen) { wGen = Math.Abs(l2 - tl); atGen = te + "+" + be + " x=" + xx + " got " + l2.ToString("F4") + " true " + tl.ToString("F4"); } }
            }
        }
        Console.WriteLine("    samples = " + n);
        Console.WriteLine("    STRAIGHT-DOWN wrong vs my scan = " + sdWrong + "   worst " + wSd.ToString("F4") + " px   " + atSd);
        Console.WriteLine("    GENERAL      wrong vs my scan = " + genWrong + "   worst " + wGen.ToString("F4") + " px   " + atGen);
    }
}
