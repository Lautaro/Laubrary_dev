using System;
using UnityEngine;
using Laubrary.Shaper;
public static class V2F7
{
    public static void Run()
    {
        Console.WriteLine("=== V2-F7 - the Linear frame flip, and whether all four sites agree ===");
        var root = ShaperNode.Bag("p", ShaperCombineMode.Add,
            ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, "o", ShaperCombineMode.Add));
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(40f), angle = new ZUIValue(45f) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        float gx, gy; ShaperHeight.LinearGradient(op, out gx, out gy);
        Console.WriteLine("  LinearGradient = (" + gx.ToString("F5") + ", " + gy.ToString("F5") + ")");
        Console.WriteLine("  corner (nx,ny)=( 1, 1) height = " + ShaperHeight.Height(op, 1f, 1f, 1f).ToString("F4"));
        Console.WriteLine("  corner (nx,ny)=( 1,-1) height = " + ShaperHeight.Height(op, 1f, 1f, -1f).ToString("F4"));
        Console.WriteLine("  supE = " + op.supE + "   infE = " + op.infE + "   linearEGrad = " + op.linearEGrad.ToString("E5"));
        // highest canvas point by scan
        float best = -1e30f, bx = 0, by = 0;
        for (float x = -60f; x <= 60f; x += 0.5f)
            for (float y = -40f; y <= 40f; y += 0.5f)
            {
                float nx, ny; ShaperHeight.LocalNormalised(op, x, y, out nx, out ny);
                float h = ShaperHeight.Height(op, 1f, nx, ny);
                if (h > best) { best = h; bx = x; by = y; }
            }
        Console.WriteLine("  highest canvas point = (" + bx + ", " + by + ") at height " + best.ToString("F4"));

        // is linearEGrad a genuine upper bound on |grad E| for a SHEARED root transform?
        Console.WriteLine();
        Console.WriteLine("  |grad E| vs the baked linearEGrad, for ROTATED and SHEARED root transforms:");
        foreach (float rot in new float[] { 0f, 17f, 45f, 90f })
            foreach (float sk in new float[] { 0f, 0.3f, 0.8f, 1.5f })
                foreach (float sx in new float[] { 1f, 0.4f, 2.3f })
                {
                    var r2 = ShaperNode.Bag("p", ShaperCombineMode.Add,
                        ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, "o", ShaperCombineMode.Add));
                    r2.transform.rotation = rot;
                    r2.transform.scale = new Vector2(sx, 1f);
                    r2.transform.skewDegrees = new Vector2(sk * 30f, 0f);
                    var p2 = ShaperCompiler.Compile(r2, 0f, 0u);
                    var o2 = ShaperHeightCompiler.Compile(def, p2, 1f, 0f, 0f, 0u);
                    float worst = 0f;
                    for (float x = -200f; x <= 200f; x += 2f)
                        for (float y = -200f; y <= 200f; y += 2f)
                        {
                            const float h = 0.05f;
                            float n0x, n0y, n1x, n1y, n2x, n2y;
                            ShaperHeight.LocalNormalised(o2, x, y, out n0x, out n0y);
                            ShaperHeight.LocalNormalised(o2, x + h, y, out n1x, out n1y);
                            ShaperHeight.LocalNormalised(o2, x, y + h, out n2x, out n2y);
                            float e0 = ShaperHeight.Profile(o2, 0f, n0x, n0y);
                            float ex = (ShaperHeight.Profile(o2, 0f, n1x, n1y) - e0) / h;
                            float ey = (ShaperHeight.Profile(o2, 0f, n2x, n2y) - e0) / h;
                            float g = Mathf.Sqrt(ex * ex + ey * ey);
                            if (g > worst) worst = g;
                        }
                    bool ok = worst <= o2.linearEGrad * 1.001f + 1e-7f;
                    if (!ok || (rot == 45f && sk == 0.8f))
                        Console.WriteLine(string.Format("    rot={0,5} skew={1,4} sx={2,4}  measured |grad E| = {3:E5}  baked = {4:E5}  {5}",
                            rot, sk, sx, worst, o2.linearEGrad, ok ? "ok" : "*** BAKED BOUND IS TOO SMALL ***"));
                }
        Console.WriteLine("  (only failures and one sample row printed)");
    }
}
