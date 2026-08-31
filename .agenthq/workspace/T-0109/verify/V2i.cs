using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// V2 - H6's OMISSION check, replicated EXACTLY (same fixture, same 504 rays, same predicate), so the
// 12 "extra emitted pairs VERIFIED real by the predicate" can be inspected one by one. The claim under
// test is that those 12 are genuine solid slivers finer than the truth scan's spacing.
public static class V2H6
{
    static ShaperNode SlotAt(float cx, float halfW, float halfH)
    {
        var n = ShaperNode.Primitive(new ShaperPrimitiveDef
        { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH }, "slot", ShaperCombineMode.Subtract);
        n.transform.translate = new Vector2(cx, 0f);
        return n;
    }

    static bool TruthInside(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                            float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float z = oz + s * dz;
        float above = z - op.baseZ;
        if (above < 0f || above > op.body * op.supG) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float d = ShaperEvaluator.Distance(field, px, py, stack);
        if (d > 0f || ShaperField.IsEmpty(d)) return false;
        float t = ShaperHeight.T(op, d);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        return above <= op.body * ShaperHeight.Composed(op, t, nx, ny);
    }

    static List<float> TruthCrossings(ShaperProgram f, float[] st, in ShaperHeightOp op,
                                      float ox, float oy, float oz, float dx, float dy, float dz, float sMax, int samples)
    {
        var res = new List<float>();
        bool prev = TruthInside(f, st, op, ox, oy, oz, dx, dy, dz, 0f);
        if (prev) res.Add(0f);
        for (int i = 1; i <= samples; i++)
        {
            float s = sMax * i / samples;
            bool m = TruthInside(f, st, op, ox, oy, oz, dx, dy, dz, s);
            if (m != prev)
            {
                float lo = sMax * (i - 1) / samples, hi = s;
                for (int k = 0; k < 40; k++)
                { float mid = 0.5f * (lo + hi); if (TruthInside(f, st, op, ox, oy, oz, dx, dy, dz, mid) == prev) lo = mid; else hi = mid; }
                res.Add(hi); prev = m;
            }
        }
        return res;
    }

    static ShaperHeightOp OpFor(ShaperProgram prog, ShaperExtrusionTechnique tech, ShaperBevelTechnique bev,
                                float depth, float amount, float baseZ)
    {
        var def = new ShaperHeightDef
        {
            technique = tech, bevel = bev,
            depth = new ZUIValue(depth), angle = new ZUIValue(45f), steps = new ZUIValue(4f),
            curve = new ZUIValue(1f), taper = new ZUIValue(1f),
            bevelAmount = new ZUIValue(amount), bevelSteps = new ZUIValue(3f)
        };
        return ShaperHeightCompiler.Compile(def, prog, 1f, baseZ, 0f, 0u);
    }

    public static void Run()
    {
        Console.WriteLine("=== V2-H6 - H6's omission fixture replicated, and its 12 'verified real' extras inspected ===");
        var plate = ShaperNode.Bag("twinslot", ShaperCombineMode.Add,
            ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 60f }, "plate", ShaperCombineMode.Add),
            SlotAt(-18f, 3.5f, 36f), SlotAt(22f, 5.0f, 36f));
        var pprog = ShaperCompiler.Compile(plate, 0f, 0u);
        var pstack = pprog.NewStack();
        var pbuf = new ShaperCrossing[128];

        int rays = 0, omissions = 0, extraVerified = 0, extraSpurious = 0;
        int zeroWidth = 0, atBase = 0;
        float worstGap = 0f;

        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var php = OpFor(pprog, tech, bev, 90f, 0.25f, 0f);
                var ps = new ShaperResolveScene(); ps.Add(pprog, php);
                for (int r = 0; r < 12; r++)
                {
                    float tilt = (r % 4) * 14f + 2f;
                    float zf = 0.08f + 0.22f * (r / 4);
                    float rad = tilt * Mathf.Deg2Rad;
                    float ddx = Mathf.Cos(rad), ddy = 0f, ddz = -Mathf.Sin(rad);
                    float ox = -130f, oy = (r % 3 - 1) * 14f;
                    float oz = php.baseZ + zf * php.body * php.supG + 70f * Mathf.Tan(rad);

                    var rr = ShaperResolve.Query(ps, ox, oy, oz, ddx, ddy, ddz, pbuf);
                    rays++;
                    var truth = TruthCrossings(pprog, pstack, php, ox, oy, oz, ddx, ddy, ddz, 400f, 120000);
                    for (int j = 0; j < truth.Count; j++)
                    {
                        float best = float.MaxValue;
                        for (int i = 0; i < rr.count; i++) best = Mathf.Min(best, Mathf.Abs(truth[j] - pbuf[i].rayT));
                        if (best > worstGap) worstGap = best;
                        if (best > 0.05f) omissions++;
                    }
                    if (rr.count != truth.Count)
                        for (int i = 0; i + 1 < rr.count; i++)
                        {
                            if (!pbuf[i].entering || pbuf[i + 1].entering) continue;
                            bool near = false;
                            for (int j = 0; j < truth.Count; j++) if (Mathf.Abs(truth[j] - pbuf[i].rayT) < 0.05f) near = true;
                            if (near) continue;
                            float mid = 0.5f * (pbuf[i].rayT + pbuf[i + 1].rayT);
                            bool inside = TruthInside(pprog, pstack, php, ox, oy, oz, ddx, ddy, ddz, mid);
                            if (inside) extraVerified++; else extraSpurious++;

                            float width = pbuf[i + 1].rayT - pbuf[i].rayT;
                            float pz = oz + mid * ddz;
                            float px = ox + mid * ddx, py = oy + mid * ddy;
                            float dd = ShaperEvaluator.Distance(pprog, px, py, pstack);
                            float tt = ShaperHeight.T(php, dd);
                            float G = ShaperHeight.Composed(php, tt, 0f, 0f);
                            // Is it a genuine sliver, or a ZERO-WIDTH pair sitting on the base plane where
                            // G = 0 and the closed set degenerates to a single plane?
                            bool zw = width <= 1e-4f;
                            bool onBase = Mathf.Abs(pz - php.baseZ) <= 1e-4f;
                            if (zw) zeroWidth++;
                            if (onBase) atBase++;
                            Console.WriteLine(string.Format(
                                "   extra: {0,-9}+{1,-8} tilt={2,4:F0}  rayT {3,10:F4}->{4,10:F4}  width={5:E3}  z={6,9:F5}  d={7,9:F4}  t={8:F5}  G={9:F5}  body*G={10:F5}  inside={11}{12}{13}",
                                tech, bev, tilt, pbuf[i].rayT, pbuf[i + 1].rayT, width, pz, dd, tt, G, php.body * G, inside,
                                zw ? "  ZERO-WIDTH" : "", onBase ? "  ON-BASE-PLANE" : ""));
                        }
                }
            }
        Console.WriteLine();
        Console.WriteLine("  rays=" + rays + "  omissions=" + omissions + "  worst gap=" + worstGap.ToString("E4"));
        Console.WriteLine("  extra VERIFIED=" + extraVerified + "  extra SPURIOUS=" + extraSpurious);
        Console.WriteLine("  of the 'verified' extras: ZERO-WIDTH " + zeroWidth + ", sitting exactly ON the base plane " + atBase);
    }
}
