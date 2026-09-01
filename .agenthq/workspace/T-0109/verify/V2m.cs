using System;
using UnityEngine;
using Laubrary.Shaper;

// LocalNormalised clamps nx and ny INDEPENDENTLY. linearEGrad is the gradient of the UNCLAMPED E.
// Where one axis is clamped its term drops out of the vector sum - and if the two terms partially
// CANCEL, the surviving single term can be LARGER than the sum. The compiler's comment says the
// clamp "can only reduce the variation"; this measures whether that is true.
public static class V2Clamp
{
    public static void Run()
    {
        Console.WriteLine("=== V2-CLAMP - is linearEGrad an UPPER bound on |grad E| once LocalNormalised's clamp is active? ===");
        Console.WriteLine("Four clamp states: neither axis clamped, nx clamped, ny clamped, both. linearEGrad covers only the first.");
        Console.WriteLine();
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(40f), angle = new ZUIValue(45f) };
        float worstRatio = 1f; string worstAt = "";
        int cases = 0, bad = 0;
        foreach (float ang in new float[] { -180f, -135f, -90f, -45f, 0f, 30f, 45f, 60f, 90f, 135f, 180f })
        foreach (float rot in new float[] { 0f, 17f, 45f, 73f, 90f })
        foreach (float skew in new float[] { 0f, 12f, 24f, 45f, 60f })
        foreach (float sx in new float[] { 1f, 0.4f, 2.3f })
        foreach (float hh in new float[] { 40f, 12f })
        {
            var r2 = ShaperNode.Bag("p", ShaperCombineMode.Add,
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = hh }, "o", ShaperCombineMode.Add));
            r2.transform.rotation = rot; r2.transform.scale = new Vector2(sx, 1f);
            r2.transform.skewDegrees = new Vector2(skew, 0f);
            var p2 = ShaperCompiler.Compile(r2, 0f, 0u);
            def.angle = new ZUIValue(ang);
            var op = ShaperHeightCompiler.Compile(def, p2, 1f, 0f, 0f, 0u);
            cases++;

            float gxnx = op.m00 * op.invLocalHalfW, gynx = op.m01 * op.invLocalHalfW;   // grad nx
            float gxny = op.m10 * op.invLocalHalfH, gyny = op.m11 * op.invLocalHalfH;   // grad ny
            float c = op.cosAngle, s = op.sinAngle;
            float full = Mag(0.6f * (c * gxnx + s * gxny), 0.6f * (c * gynx + s * gyny));
            float nyClamped = Mag(0.6f * c * gxnx, 0.6f * c * gynx);
            float nxClamped = Mag(0.6f * s * gxny, 0.6f * s * gyny);
            float trueSup = Mathf.Max(full, Mathf.Max(nyClamped, nxClamped));
            float ratio = trueSup / Mathf.Max(1e-30f, op.linearEGrad);
            if (ratio > 1f + 1e-4f) bad++;
            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worstAt = "angle=" + ang + " rot=" + rot + " skew=" + skew + " sx=" + sx + " halfH=" + hh
                        + "   unclamped=" + full.ToString("E5") + "  nx-clamped=" + nxClamped.ToString("E5")
                        + "  ny-clamped=" + nyClamped.ToString("E5") + "  baked=" + op.linearEGrad.ToString("E5");
            }
        }
        Console.WriteLine("  configurations = " + cases);
        Console.WriteLine("  configurations where the TRUE sup|grad E| EXCEEDS the baked linearEGrad: " + bad);
        Console.WriteLine("  worst ratio trueSup / baked = " + worstRatio.ToString("F4"));
        Console.WriteLine("  " + worstAt);
    }
    static float Mag(float a, float b) => Mathf.Sqrt(a * a + b * b);
}
