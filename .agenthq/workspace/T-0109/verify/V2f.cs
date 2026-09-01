using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// Drill-down on the section-2 anomaly: a TILTED ray near a slab breakpoint reports 4 crossings
// where the truth is 2, and 2 of the emitted ones are >0.05 px from any true crossing.
public static class V2Drill
{
    public static void Run()
    {
        var kids = new List<ShaperNode> {
            ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 200f, rectHalfH = 200f }, "o", ShaperCombineMode.Add) };
        var n0 = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 10f, rectHalfH = 400f }, "s0", ShaperCombineMode.Subtract);
        n0.transform.translate = new Vector2(40f, 0f);
        kids.Add(n0);
        var root = ShaperNode.Bag("p", ShaperCombineMode.Add, kids.ToArray());
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var st = prog.NewStack();

        var def = new ShaperHeightDef {
            technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.Stepped,
            depth = new ZUIValue(300f), bevelAmount = new ZUIValue(0.5f),
            steps = new ZUIValue(5f), curve = new ZUIValue(1f), angle = new ZUIValue(45f) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        var bps = new float[64]; int nb = ShaperHeight.Breakpoints(op, bps);
        Console.Write("breakpoints: "); for (int i = 0; i < nb; i++) Console.Write(bps[i].ToString("F5") + " "); Console.WriteLine();
        Console.WriteLine("span=" + op.span + " body=" + op.body + " supG=" + op.supG + " baseZ=" + op.baseZ);

        foreach (float tilt in new float[] { 0.5f, 3f, 12f, 20f, 30f })
        {
            double rad = tilt * Math.PI / 180.0;
            float dx = (float)Math.Cos(rad), dz = (float)(-Math.Sin(rad));
            float z = op.baseZ + op.body * bps[2];
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[64];
            var r = ShaperResolve.Query(scene, -400f, 0f, z, dx, 0f, dz, buf);
            var truth = March.TrueCrossings(prog, st, op, -400f, 0f, z, dx, 0f, dz, 1400f, 4000000);
            Console.WriteLine();
            Console.WriteLine("tilt=" + tilt + " z=" + z + " dz=" + dz + "  got=" + r.count + " true=" + truth.Count
                              + " branch=" + r.branch + " capped=" + r.bracketCapped + " exhausted=" + r.stepsExhausted);
            Console.Write("   emitted: ");
            for (int i = 0; i < r.count; i++)
                Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I" : "O") + "/" + buf[i].kind + " ");
            Console.WriteLine();
            Console.Write("   truth  : ");
            foreach (double t in truth) Console.Write(t.ToString("F4") + " ");
            Console.WriteLine();
            // membership around each emitted crossing, by the INDEPENDENT predicate
            for (int i = 0; i < r.count; i++)
            {
                float s = buf[i].rayT;
                bool a = March.Inside(prog, st, op, -400f, 0f, z, dx, 0f, dz, s - 0.05f);
                bool b = March.Inside(prog, st, op, -400f, 0f, z, dx, 0f, dz, s + 0.05f);
                float px = -400f + s * dx, pz = z + s * dz;
                float dd = ShaperEvaluator.Distance(prog, px, 0f, st);
                float tt = ShaperHeight.T(op, dd);
                Console.WriteLine(string.Format("      s={0,10:F4} x={1,9:F4} z={2,9:F4} d={3,10:F4} t={4,8:F5} G={5,8:F5} body*G={6,10:F4} above={7,10:F4}  inside(-)={8} inside(+)={9}",
                    s, px, pz, dd, tt, ShaperHeight.Composed(op, tt, 0f, 0f), op.body * ShaperHeight.Composed(op, tt, 0f, 0f), pz - op.baseZ, a, b));
            }
        }
    }
}
