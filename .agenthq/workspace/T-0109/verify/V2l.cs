using System;
using UnityEngine;
using Laubrary.Shaper;
public static class V2Grad
{
    public static void Run()
    {
        Console.WriteLine("=== V2-GRAD - is op.linearEGrad an upper bound on |grad E| for a general root transform? ===");
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(40f), angle = new ZUIValue(45f) };
        foreach (float rot in new float[] { 0f, 17f, 45f })
          foreach (float skew in new float[] { 0f, 24f, 45f })
            foreach (float sx in new float[] { 1f, 2.3f })
            {
                var r2 = ShaperNode.Bag("p", ShaperCombineMode.Add,
                    ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, "o", ShaperCombineMode.Add));
                r2.transform.rotation = rot; r2.transform.scale = new Vector2(sx, 1f);
                r2.transform.skewDegrees = new Vector2(skew, 0f);
                var p2 = ShaperCompiler.Compile(r2, 0f, 0u);
                var op = ShaperHeightCompiler.Compile(def, p2, 1f, 0f, 0f, 0u);

                // analytic partials of E, from LocalNormalised's own affine map, measured by central
                // difference on nx/ny at a point WELL inside the box (so the clamp is not active).
                float x = 0f, y = 0f, h = 0.01f;
                float ax, ay, bx, by, cx2, cy2;
                ShaperHeight.LocalNormalised(op, x - h, y, out ax, out ay);
                ShaperHeight.LocalNormalised(op, x + h, y, out bx, out by);
                ShaperHeight.LocalNormalised(op, x, y - h, out cx2, out cy2);
                float dx2, dy2; ShaperHeight.LocalNormalised(op, x, y + h, out dx2, out dy2);
                float dnxdx = (bx - ax) / (2 * h), dnydx = (by - ay) / (2 * h);
                float dnxdy = (dx2 - cx2) / (2 * h), dnydy = (dy2 - cy2) / (2 * h);
                float exM = 0.6f * (op.cosAngle * dnxdx + op.sinAngle * dnydx);
                float eyM = 0.6f * (op.cosAngle * dnxdy + op.sinAngle * dnydy);
                float trueG = Mathf.Sqrt(exM * exM + eyM * eyM);
                float exB = 0.6f * (op.cosAngle * op.m00 * op.invLocalHalfW + op.sinAngle * op.m10 * op.invLocalHalfH);
                float eyB = 0.6f * (op.cosAngle * op.m01 * op.invLocalHalfW + op.sinAngle * op.m11 * op.invLocalHalfH);
                Console.WriteLine(string.Format("rot={0,4} skew={1,4} sx={2,4}  invertible={3}  m=[{4:F4} {5:F4}; {6:F4} {7:F4}]  iw={8:F5} ih={9:F5}",
                    rot, skew, sx, p2.rootInvertible, op.m00, op.m01, op.m10, op.m11, op.invLocalHalfW, op.invLocalHalfH));
                Console.WriteLine(string.Format("    dnx/dx={0:F6} dnx/dy={1:F6} dny/dx={2:F6} dny/dy={3:F6}", dnxdx, dnxdy, dnydx, dnydy));
                Console.WriteLine(string.Format("    TRUE |grad E| = {0:E6}   baked linearEGrad = {1:E6}   ratio true/baked = {2:F4}  {3}",
                    trueG, op.linearEGrad, trueG / Mathf.Max(1e-30f, op.linearEGrad),
                    trueG <= op.linearEGrad * (1f + 1e-4f) ? "ok" : "*** BAKED BOUND TOO SMALL ***"));
            }
    }
}
