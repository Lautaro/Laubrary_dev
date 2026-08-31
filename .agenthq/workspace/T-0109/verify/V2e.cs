using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// V2 - ADVERSARIAL attacks on the rebuilt QueryGeneral. Every fixture here is one the fixer did
// not build. Ground truth is an independent dense scan of the membership predicate (March.Inside).
public static class V2March
{
    static ShaperNode PlateMinusSlots(float hw, float hh, float[] cx, float[] halfW)
    {
        var kids = new List<ShaperNode> {
            ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, "o", ShaperCombineMode.Add) };
        for (int i = 0; i < cx.Length; i++)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW[i], rectHalfH = hh * 2f }, "s" + i, ShaperCombineMode.Subtract);
            n.transform.translate = new Vector2(cx[i], 0f);
            kids.Add(n);
        }
        return ShaperNode.Bag("p", ShaperCombineMode.Add, kids.ToArray());
    }

    static ShaperHeightOp H(ShaperProgram prog, ShaperExtrusionTechnique tech, ShaperBevelTechnique bev,
                            float body, float amount, float steps, float curve, float angle)
    {
        var def = new ShaperHeightDef {
            technique = tech, bevel = bev,
            depth = new ZUIValue(body), bevelAmount = new ZUIValue(amount),
            steps = new ZUIValue(steps), curve = new ZUIValue(curve), angle = new ZUIValue(angle) };
        return ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
    }

    struct Res { public int got, want; public double worstGap; public int spurious; public bool capped, exhausted; }

    static Res Fire(ShaperProgram prog, float[] stack, in ShaperHeightOp op,
                    float ox, float oy, float oz, float dx, float dy, float dz, float sMax, int samples)
    {
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var buf = new ShaperCrossing[512];
        var r = ShaperResolve.Query(scene, ox, oy, oz, dx, dy, dz, buf);
        var truth = March.TrueCrossings(prog, stack, op, ox, oy, oz, dx, dy, dz, sMax, samples);
        var res = new Res { got = r.count, want = truth.Count, capped = r.bracketCapped > 0, exhausted = r.stepsExhausted };
        foreach (double t in truth)
        {
            double best = 1e30;
            for (int i = 0; i < r.count; i++) { double e = Math.Abs(buf[i].rayT - t); if (e < best) best = e; }
            if (best > res.worstGap) res.worstGap = best;
        }
        for (int i = 0; i < r.count; i++)
        {
            double best = 1e30;
            foreach (double t in truth) { double e = Math.Abs(buf[i].rayT - t); if (e < best) best = e; }
            if (best > 0.05) res.spurious++;
        }
        return res;
    }

    public static string SEL = "all";
    static bool On(string k) => SEL == "all" || SEL == k;
    public static void Run()
    {
        Console.WriteLine("=== V2-MARCH - adversarial attacks on the rebuilt QueryGeneral ===");
        Console.WriteLine();

        if (On("1")) {
        Console.WriteLine("-- 1. thin slot, HORIZONTAL +X ray, widths straddling SurfaceResolution = "
                          + ShaperResolve.SurfaceResolution + " canvas px --");
        Console.WriteLine(string.Format("{0,12} {1,7} {2,7} {3,14} {4,9} {5,9} {6}", "slot px", "true", "got", "worst gap px", "spurious", "capped", ""));
        float[] widths = { 5f, 1f, 0.5f, 0.2f, 0.1f, 0.05f, 0.03f, 0.021f, 0.02f, 0.019f, 0.01f, 0.005f, 0.002f, 0.001f };
        float smallest = -1;
        foreach (float w in widths)
        {
            var root = PlateMinusSlots(200f, 200f, new float[] { 40f }, new float[] { w * 0.5f });
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            var op = H(prog, ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 40f, 0.25f, 4f, 1f, 45f);
            var res = Fire(prog, st, op, -400f, 0f, op.baseZ + 20f, 1f, 0f, 0f, 1200f, 4000000);
            bool ok = res.got == res.want;
            if (ok && res.want == 4 && (smallest < 0 || w < smallest)) smallest = w;
            Console.WriteLine(string.Format("{0,12} {1,7} {2,7} {3,14:E3} {4,9} {5,9} {6}",
                w, res.want, res.got, res.worstGap, res.spurious, res.capped, ok ? "ok" : "MISS"));
        }
        Console.WriteLine("  smallest slot resolved as 4 crossings: " + smallest + " canvas px");
        Console.WriteLine(); }

        if (On("2")) {
        // 2. the ray sits at / around an interior slab breakpoint, TILTED (so the parallel clip is not used)
        Console.WriteLine("-- 2. tilted rays at, just below and just above every interior slab breakpoint --");
        {
            var root = PlateMinusSlots(200f, 200f, new float[] { 40f }, new float[] { 10f });
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            var op = H(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.Stepped, 300f, 0.5f, 5f, 1f, 45f);
            var bps = new float[64]; int nb = ShaperHeight.Breakpoints(op, bps);
            Console.WriteLine("      breakpoints: " + nb + "  ->  " + (nb - 1) + " slabs");
            int bad = 0, cases = 0;
            for (int i = 1; i + 1 < nb; i++)
                foreach (float eps in new float[] { -1e-3f, 0f, 1e-3f })
                    foreach (float tilt in new float[] { 0.5f, 3f, 12f })
                    {
                        double rad = tilt * Math.PI / 180.0;
                        float dx = (float)Math.Cos(rad), dz = (float)(-Math.Sin(rad));
                        float z = op.baseZ + op.body * bps[i] + eps;
                        var res = Fire(prog, st, op, -400f, 0f, z, dx, 0f, dz, 1400f, 1400000);
                        cases++;
                        if (res.got != res.want || res.spurious > 0)
                        {
                            bad++;
                            Console.WriteLine(string.Format("      bp[{0}] zeta={1:F5} z={2:F4} eps={3} tilt={4}  true={5} got={6} spurious={7}",
                                i, bps[i], z, eps, tilt, res.want, res.got, res.spurious));
                        }
                    }
            Console.WriteLine("      cases=" + cases + "  wrong=" + bad);
        } }
        Console.WriteLine();

        if (On("3")) {
        // 3. all 42 combinations on a two-slot plate, three tilts
        Console.WriteLine("-- 3. all 42 combinations, two 12 px slots strictly inside, three tilts --");
        {
            int bad = 0, cases = 0; double worstGap = 0; string worstAt = "";
            var root = PlateMinusSlots(200f, 200f, new float[] { -60f, 60f }, new float[] { 6f, 6f });
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            foreach (var tech in Fixtures.Techs)
                foreach (var bev in Fixtures.Bevels)
                    foreach (float tilt in new float[] { 0f, 7f, 23f })
                    {
                        var op = H(prog, tech, bev, 300f, 0.35f, 5f, 1f, 45f);
                        double rad = tilt * Math.PI / 180.0;
                        float dx = (float)Math.Cos(rad), dz = (float)(-Math.Sin(rad));
                        float z = op.baseZ + 0.31f * op.body * op.supG;
                        var res = Fire(prog, st, op, -400f, 0f, z, dx, 0f, dz, 1400f, 1400000);
                        cases++;
                        if (res.worstGap > worstGap) { worstGap = res.worstGap; worstAt = tech + "+" + bev + " tilt=" + tilt; }
                        if (res.got != res.want || res.spurious > 0)
                        {
                            bad++;
                            Console.WriteLine(string.Format("      {0,-9}+{1,-8} tilt={2,4}  true={3} got={4} spurious={5} gap={6:E3} capped={7} exhausted={8}",
                                tech, bev, tilt, res.want, res.got, res.spurious, res.worstGap, res.capped, res.exhausted));
                        }
                    }
            Console.WriteLine("      " + cases + " rays,  wrong = " + bad
                              + ",  worst gap true->emitted = " + worstGap.ToString("E3") + " px  (" + worstAt + ")");
        } }
        Console.WriteLine();

        if (On("4")) {
        // 4. rays nearly parallel to a vertical silhouette wall
        Console.WriteLine("-- 4. rays nearly parallel to a vertical silhouette wall (hugging a slot face) --");
        {
            var root = PlateMinusSlots(200f, 200f, new float[] { 0f }, new float[] { 50f });
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            var op = H(prog, ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 40f, 0.25f, 4f, 1f, 45f);
            int bad = 0, cases = 0;
            foreach (float slope in new float[] { 1e-1f, 1e-2f, 1e-3f, 1e-4f, 1e-5f, 0f })
                foreach (float off in new float[] { -50.5f, -50.01f, -50f, -49.99f, -49.5f })
                {
                    var res = Fire(prog, st, op, off, -400f, op.baseZ + 20f, slope, 1f, 0f, 1200f, 2000000);
                    cases++;
                    bool ok = res.got == res.want && res.spurious == 0;
                    if (!ok)
                    {
                        bad++;
                        Console.WriteLine(string.Format("      slope={0,-8} x={1,-8} true={2} got={3} gap={4:E3} spurious={5}", slope, off, res.want, res.got, res.worstGap, res.spurious));
                    }
                }
            Console.WriteLine("      " + cases + " rays, mismatches = " + bad);
        } }
        Console.WriteLine();

        if (On("5")) {
        // 5. tangent to a circular silhouette
        Console.WriteLine("-- 5. rays tangent to a circular silhouette --");
        {
            var circle = ShaperNode.Bag("c", ShaperCombineMode.Add,
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 100f, ellipseRy = 100f }, "c", ShaperCombineMode.Add));
            var prog = ShaperCompiler.Compile(circle, 0f, 0u); var st = prog.NewStack();
            int bad = 0, cases = 0;
            foreach (var tech in new[] { ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round })
                foreach (float off in new float[] { 100.5f, 100.01f, 100f, 99.999f, 99.99f, 99.9f, 99f, 90f })
                {
                    var op = H(prog, tech, ShaperBevelTechnique.Cove, 60f, 0.3f, 4f, 1f, 45f);
                    var res = Fire(prog, st, op, -400f, off, op.baseZ + 1f, 1f, 0f, 0f, 900f, 3000000);
                    cases++;
                    bool ok = res.got == res.want && res.spurious == 0;
                    if (!ok)
                    {
                        bad++;
                        Console.WriteLine(string.Format("      {0,-8} y={1,-9} true={2} got={3} gap={4:E3} spurious={5}", tech, off, res.want, res.got, res.worstGap, res.spurious));
                    }
                }
            Console.WriteLine("      " + cases + " rays, mismatches = " + bad + "  (a tangent ray's true count is scan-resolution dependent; read the gaps)");
        } }
        Console.WriteLine();

        if (On("6")) {
        // 6. six nested subtractions of shrinking width
        Console.WriteLine("-- 6. six subtractions of shrinking width (8, 4, 2, 1, 0.5, 0.25 px half-width) --");
        {
            var cx = new float[] { -150f, -90f, -40f, 20f, 80f, 150f };
            var hw = new float[] { 8f, 4f, 2f, 1f, 0.5f, 0.25f };
            var root = PlateMinusSlots(200f, 200f, cx, hw);
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            int bad = 0, cases = 0;
            foreach (var tech in Fixtures.Techs)
                foreach (var bev in Fixtures.Bevels)
                {
                    var op = H(prog, tech, bev, 200f, 0.3f, 4f, 1f, 45f);
                    var res = Fire(prog, st, op, -400f, 0f, op.baseZ + 0.13f * op.body, 1f, 0f, 0f, 1200f, 6000000);
                    cases++;
                    if (res.got != res.want)
                    {
                        bad++;
                        Console.WriteLine(string.Format("      {0,-9}+{1,-8} true={2} got={3} gap={4:E3} spurious={5}", tech, bev, res.want, res.got, res.worstGap, res.spurious));
                    }
                }
            Console.WriteLine("      " + cases + " rays, mismatches = " + bad);
        } }
        Console.WriteLine();

        if (On("7")) {
        // 7. support box much larger than the solid
        Console.WriteLine("-- 7. support box 800x800, solid is a 2 px sliver at the centre --");
        {
            var kids = new List<ShaperNode> {
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 400f, rectHalfH = 400f }, "big", ShaperCombineMode.Add),
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 400f, rectHalfH = 400f }, "cutL", ShaperCombineMode.Subtract),
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 400f, rectHalfH = 400f }, "cutR", ShaperCombineMode.Subtract) };
            kids[1].transform.translate = new Vector2(-401f, 0f);
            kids[2].transform.translate = new Vector2(401f, 0f);
            var root = ShaperNode.Bag("sliver", ShaperCombineMode.Add, kids.ToArray());
            var prog = ShaperCompiler.Compile(root, 0f, 0u); var st = prog.NewStack();
            Console.WriteLine("      support box halfW=" + prog.supportHalfW + " halfH=" + prog.supportHalfH + "  span=" + Math.Min(prog.supportHalfW, prog.supportHalfH));
            int bad = 0, cases = 0;
            foreach (var tech in Fixtures.Techs)
                foreach (var bev in new[] { ShaperBevelTechnique.None, ShaperBevelTechnique.Cove })
                {
                    var op = H(prog, tech, bev, 100f, 0.4f, 4f, 1f, 45f);
                    var res = Fire(prog, st, op, -900f, 0f, op.baseZ + 0.02f * op.body, 1f, 0f, 0f, 2200f, 8000000);
                    cases++;
                    if (res.got != res.want)
                    {
                        bad++;
                        Console.WriteLine(string.Format("      {0,-9}+{1,-6} true={2} got={3} gap={4:E3} exhausted={5} capped={6}", tech, bev, res.want, res.got, res.worstGap, res.exhausted, res.capped));
                    }
                }
            Console.WriteLine("      " + cases + " rays, mismatches = " + bad);
        } }
    }
}