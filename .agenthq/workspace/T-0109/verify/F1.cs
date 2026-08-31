using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// T-0109 FIXER — F1 verification. The verifier's own reproduction (March2) is re-run separately; this is the
// BROADER attack the verifier said the audit lacked: a NON-CONVEX fixture with a feature STRICTLY INSIDE the
// support box, many rays, all 42 profile x bevel combinations, and the emitted crossing COUNT asserted
// against an independently computed ground-truth count rather than only the residual of what was emitted.
public static class F1
{
    // A plate with two thin slots strictly inside the support box: 4 solid runs along a horizontal ray.
    public static ShaperNode TwinSlotPlate(float halfW, float halfH, float slotHalfW, float c0, float c1)
    {
        var outer = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH };
        var s0 = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = slotHalfW, rectHalfH = halfH * 0.6f };
        var s1 = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = slotHalfW, rectHalfH = halfH * 0.6f };
        var a = ShaperNode.Primitive(outer, "plate", ShaperCombineMode.Add);
        var b = ShaperNode.Primitive(s0, "slot0", ShaperCombineMode.Subtract);
        var c = ShaperNode.Primitive(s1, "slot1", ShaperCombineMode.Subtract);
        b.transform.translate = new Vector2(c0, 0f);
        c.transform.translate = new Vector2(c1, 0f);
        return ShaperNode.Bag("twinslot", ShaperCombineMode.Add, a, b, c);
    }

    static readonly ShaperExtrusionTechnique[] Techs = {
        ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Linear, ShaperExtrusionTechnique.Stepped,
        ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round, ShaperExtrusionTechnique.Taper,
        ShaperExtrusionTechnique.Pyramid };
    static readonly ShaperBevelTechnique[] Bevels = {
        ShaperBevelTechnique.None, ShaperBevelTechnique.Linear, ShaperBevelTechnique.Rounded,
        ShaperBevelTechnique.Cove, ShaperBevelTechnique.Ogee, ShaperBevelTechnique.Stepped };

    public static void Run()
    {
        Console.WriteLine("=== F1 - general march vs INDEPENDENT ground-truth crossing COUNT ===");
        Console.WriteLine("fixture: 400x400 plate minus TWO 24px slots strictly inside the support box (non-convex).");
        Console.WriteLine("all 42 profile x bevel combos x 25 rays; truth = 600k-sample scan of the exact predicate.");
        Console.WriteLine();

        var root = TwinSlotPlate(200f, 200f, 12f, -60f, 70f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        int totalRays = 0, countMismatch = 0, worstMissing = 0, raysWithSolid = 0, shown = 0;
        double worstResidual = 0;
        int capped = 0, exhausted = 0, omissions = 0, sliversVerified = 0, spurious = 0;
        string worstWhere = "";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var tech in Techs)
        foreach (var bev in Bevels)
        {
            var def = new ShaperHeightDef
            {
                technique = tech, bevel = bev, depth = new ZUIValue(300f),
                angle = new ZUIValue(45f), steps = new ZUIValue(4f), curve = new ZUIValue(1f),
                taper = new ZUIValue(1f), bevelAmount = new ZUIValue(0.25f), bevelSteps = new ZUIValue(3f)
            };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[128];

            for (int r = 0; r < 25; r++)
            {
                // A fan of tilts, all off the -Z axis so the GENERAL branch is taken; heights spread over
                // the layer's Z extent so some rays sit under the bevel band and some over the cap.
                double tilt = (r % 5) * 12.0 + 1.0;               // 1 .. 49 degrees
                float zf = 0.06f + 0.18f * (r / 5);                // 0.06 .. 0.60 of the extent
                double rad = tilt * Math.PI / 180.0;
                float dx = (float)Math.Cos(rad), dy = 0f, dz = (float)(-Math.Sin(rad));
                float ox = -420f, oy = (r % 3 - 1) * 40f;
                // place the ray so that at x = -200 (the plate's left edge) it sits at zf of the Z extent
                float oz = op.baseZ + zf * op.body * op.supG + (float)(220.0 * Math.Tan(rad));

                var res = ShaperResolve.Query(scene, ox, oy, oz, dx, dy, dz, buf);
                if (res.branch != ShaperResolveBranch.General) throw new Exception("not general");
                capped += res.bracketCapped;
                if (res.stepsExhausted) exhausted++;

                var truth = March.TrueCrossings(prog, stack, op, ox, oy, oz, dx, dy, dz, 1400f, 600000);
                totalRays++;
                if (truth.Count > 0) raysWithSolid++;

                // THE OMISSION TEST — the thing H6 structurally cannot do. Every crossing the independent
                // scan found must have an emitted crossing near it. This is what defect F1 broke.
                for (int j = 0; j < truth.Count; j++)
                {
                    double best = double.MaxValue;
                    for (int i = 0; i < res.count; i++) best = Math.Min(best, Math.Abs(truth[j] - buf[i].rayT));
                    if (best > worstResidual) worstResidual = best;
                    if (best > 0.05)
                    {
                        omissions++;
                        if (shown < 6)
                        {
                            shown++;
                            Console.WriteLine("  OMISSION " + tech + "+" + bev + " ray " + r + " tilt " + tilt.ToString("F0") +
                                              "  true crossing at " + truth[j].ToString("F4") + " has no emitted crossing within 0.05 px" +
                                              "  (true=" + truth.Count + " got=" + res.count + ")");
                        }
                    }
                }

                // EXTRA emissions. The scan is a uniform 600k-sample sweep, so a solid sliver thinner than
                // 0.0023 px along the ray is invisible to it and visible to the march. Rather than call that
                // a mismatch, VERIFY it against the exact predicate: an emitted entering/exiting pair whose
                // interior really is inside is a crossing the SCAN missed, not one the march invented.
                if (res.count != truth.Count)
                {
                    countMismatch++;
                    int miss = truth.Count - res.count;
                    if (miss > worstMissing) { worstMissing = miss; worstWhere = tech + "+" + bev + " ray " + r + " true=" + truth.Count + " got=" + res.count; }
                    for (int i = 0; i + 1 < res.count; i++)
                    {
                        if (!buf[i].entering || buf[i + 1].entering) continue;
                        double mid = 0.5 * (buf[i].rayT + buf[i + 1].rayT);
                        bool nearTruth = false;
                        for (int j = 0; j < truth.Count; j++) if (Math.Abs(truth[j] - buf[i].rayT) < 0.05) nearTruth = true;
                        if (nearTruth) continue;
                        if (March.Inside(prog, stack, op, ox, oy, oz, dx, dy, dz, (float)mid)) sliversVerified++;
                        else spurious++;
                    }
                }
            }
        }
        sw.Stop();

        Console.WriteLine("rays                : " + totalRays + "   (rays that actually hit solid: " + raysWithSolid + ")");
        Console.WriteLine("COUNT mismatches    : " + countMismatch + (countMismatch > 0 ? "   worst: " + worstWhere : ""));
        Console.WriteLine("OMISSIONS (a true crossing with no emitted crossing within 0.05 px): " + omissions);
        Console.WriteLine("worst |rayT| gap, true crossing -> nearest emitted: " + worstResidual.ToString("E4") + " canvas px");
        Console.WriteLine("extra emitted pairs VERIFIED real by the exact predicate (scan too coarse): " + sliversVerified);
        Console.WriteLine("extra emitted pairs that are SPURIOUS (interior not inside): " + spurious);
        Console.WriteLine("bracketCapped total : " + capped);
        Console.WriteLine("stepsExhausted rays : " + exhausted);
        Console.WriteLine("elapsed             : " + sw.ElapsedMilliseconds + " ms  (includes the 600k-sample truth scan per ray)");
        Console.WriteLine();

        // Timing of the march ALONE, so the fix's cost is a number rather than a worry.
        {
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Dome, bevel = ShaperBevelTechnique.Cove,
                                            depth = new ZUIValue(300f), bevelAmount = new ZUIValue(0.25f) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[128];
            var t2 = System.Diagnostics.Stopwatch.StartNew();
            int n = 2000;
            for (int i = 0; i < n; i++)
                ShaperResolve.Query(scene, -420f, (i % 61) - 30f, op.baseZ + 400f + (i % 37), 0.9f, 0.05f, -0.43f, buf);
            t2.Stop();
            Console.WriteLine("march cost (Dome+Cove, 300px body, tilted): " + (t2.Elapsed.TotalMilliseconds * 1000.0 / n).ToString("F1") + " us/ray");
        }
    }
}
