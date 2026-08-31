using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// T-0109 SECOND FIX PASS — the probes that measure the FIXED functions, written by the fixer.
// Every one of these mirrors a probe the second verifier wrote, but points at what the march
// actually calls after the fix rather than at what it called before.
public static class V2Fix
{
    // ── N4: the per-step contained prism, through the GUARDED function ────────────────────────
    public static void N4()
    {
        Console.WriteLine("=== N4-AFTER - InverseAtEUpperBound used as an upper bound (what the per-step prism NOW calls) ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);

        long checksRaw = 0, violRaw = 0, checksFix = 0, violFix = 0;
        double worstRaw = 0, worstFix = 0;
        string atRaw = "", atFix = "";

        float[] angles = { -135f, -90f, -45f, 0f, 45f, 90f, 135f, 180f, 22.5f };
        float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };

        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (var ang in angles)
                    foreach (var amt in amounts)
                    {
                        var def = new ShaperHeightDef {
                            technique = tech, bevel = bev, depth = new ZUIValue(40f),
                            bevelAmount = new ZUIValue(amt), bevelSteps = new ZUIValue(4f),
                            steps = new ZUIValue(5f), curve = new ZUIValue(1f),
                            taper = new ZUIValue(1f), angle = new ZUIValue(ang) };
                        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                        float eLo = op.technique == ShaperExtrusionTechnique.Linear ? op.infE : 1f;

                        for (int zi = 1; zi <= 400; zi++)
                        {
                            float zeta = (zi / 400f) * op.supG;

                            float tauRaw = ShaperHeight.InverseAtE(op, zeta, eLo);
                            float tauFix = ShaperHeight.InverseAtEUpperBound(op, zeta, eLo);

                            // The t grid MUST include tau itself: the verifier's worst violation sat at
                            // t == tau exactly (Flat+Cove, a=0.05, zeta=0.995), which a uniform grid misses.
                            for (int ti = 0; ti <= 42; ti++)
                            {
                                float t = ti <= 40 ? ti / 40f
                                        : (ti == 41 ? tauRaw : tauFix);
                                if (ShaperHeight.IsNoCrossSection(t)) continue;

                                if (!ShaperHeight.IsNoCrossSection(tauRaw) && t >= tauRaw)
                                {
                                    checksRaw++;
                                    float g = LowerG(op, t, eLo);
                                    if (g < zeta) { violRaw++; double e = zeta - g;
                                        if (e > worstRaw) { worstRaw = e; atRaw = tech + "+" + bev + " a=" + amt + " zeta=" + zeta; } }
                                }
                                if (!ShaperHeight.IsNoCrossSection(tauFix) && t >= tauFix)
                                {
                                    checksFix++;
                                    float g = LowerG(op, t, eLo);
                                    if (g < zeta) { violFix++; double e = zeta - g;
                                        if (e > worstFix) { worstFix = e; atFix = tech + "+" + bev + " a=" + amt + " zeta=" + zeta; } }
                                }
                            }
                        }
                    }

        Console.WriteLine("  InverseAtE        (RAW, what it used to call):  checks=" + checksRaw +
                          "  violations=" + violRaw + "  worst shortfall in zeta = " + worstRaw.ToString("E4") + "   " + atRaw);
        Console.WriteLine("  InverseAtEUpperBound (what it calls NOW):       checks=" + checksFix +
                          "  violations=" + violFix + "  worst shortfall in zeta = " + worstFix.ToString("E4") + "   " + atFix);
    }

    static float LowerG(in ShaperHeightOp op, float t, float e)
        => op.technique == ShaperExtrusionTechnique.Linear ? e * ShaperHeight.Bevel(op, t)
                                                           : ShaperHeight.Composed(op, t, 0f, 0f);

    // ── N5: what does the corrected bound COST? ───────────────────────────────────────────────
    public static void N5Cost()
    {
        Console.WriteLine();
        Console.WriteLine("=== N5-COST - how much wider is the corrected |grad E| bound than the old (unsound) one? ===");
        Console.WriteLine("The old bake was the exact unclamped Jacobian |sum of the two axis terms|. The new one is the");
        Console.WriteLine("MAX over the four clamp regimes. Where the two terms do NOT cancel the max IS the old value.");

        // V2Clamp's own 1650-configuration fixture, verbatim, so the cost is measured where the defect was.
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(40f), angle = new ZUIValue(45f) };
        int n = 0, same = 0; double sumRatio = 0, worst = 1; string atWorst = "";

        foreach (float ang in new float[] { -180f, -135f, -90f, -45f, 0f, 30f, 45f, 60f, 90f, 135f, 180f })
        foreach (float rot in new float[] { 0f, 17f, 45f, 73f, 90f })
        foreach (float skew in new float[] { 0f, 12f, 24f, 45f, 60f })
        foreach (float sx0 in new float[] { 1f, 0.4f, 2.3f })
        foreach (float hh in new float[] { 40f, 12f })
        {
            var r2 = ShaperNode.Bag("p", ShaperCombineMode.Add,
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = hh }, "o", ShaperCombineMode.Add));
            r2.transform.rotation = rot; r2.transform.scale = new Vector2(sx0, 1f);
            r2.transform.skewDegrees = new Vector2(skew, 0f);
            var p2 = ShaperCompiler.Compile(r2, 0f, 0u);
            def.angle = new ZUIValue(ang);
            var op = ShaperHeightCompiler.Compile(def, p2, 1f, 0f, 0f, 0u);

            float ax = 0.6f * op.cosAngle * op.m00 * op.invLocalHalfW;
            float ay = 0.6f * op.cosAngle * op.m01 * op.invLocalHalfW;
            float bx = 0.6f * op.sinAngle * op.m10 * op.invLocalHalfH;
            float by = 0.6f * op.sinAngle * op.m11 * op.invLocalHalfH;
            float sx = ax + bx, sy = ay + by;
            float oldB = Mathf.Sqrt(sx * sx + sy * sy);      // the OLD bake: the unclamped Jacobian
            float newB = op.linearEGrad;                     // the NEW bake: the max over the clamp regimes

            n++;
            double ratio = oldB > 1e-30f ? newB / (double)oldB : 1.0;
            sumRatio += ratio; if (ratio <= 1.0000001) same++;
            if (ratio > worst) { worst = ratio; atWorst = "angle=" + ang + " rot=" + rot + " skew=" + skew + " sx=" + sx0 + " halfH=" + hh
                                                        + "  old=" + oldB.ToString("E5") + " new=" + newB.ToString("E5"); }
        }
        Console.WriteLine("  configurations = " + n + "   (V2-CLAMP's own fixture)");
        Console.WriteLine("  configurations where the new bound EQUALS the old (no cost) = " + same);
        Console.WriteLine("  mean new/old = " + (sumRatio / n).ToString("F4") + "   worst new/old = " + worst.ToString("F4"));
        Console.WriteLine("  " + atWorst);
    }

    // ── N1 residual: is the one remaining 'wrong ray' a real defect or a reference artefact? ──
    public static void N1Residual()
    {
        Console.WriteLine();
        Console.WriteLine("=== N1-RESIDUAL - Stepped+Rounded, bp[8], zeta=0.5: got 2 crossings, reference says 6 ===");
        Console.WriteLine("Depth agreed exactly. If the reference's extra pairs are ZERO-WIDTH they are the N3 class,");
        Console.WriteLine("i.e. the reference still carries the OLD closed convention, not a march defect.");

        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var bps = new float[512];

        var def = new ShaperHeightDef {
            technique = ShaperExtrusionTechnique.Stepped, bevel = ShaperBevelTechnique.Rounded,
            depth = new ZUIValue(40f), bevelAmount = new ZUIValue(0.5f), bevelSteps = new ZUIValue(4f),
            steps = new ZUIValue(5f), curve = new ZUIValue(1f), taper = new ZUIValue(1f), angle = new ZUIValue(45f) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        int nb = ShaperHeight.Breakpoints(op, bps);

        for (int i = 1; i + 1 < nb; i++)
        {
            float z = op.baseZ + op.body * bps[i];
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[64];
            var r = ShaperResolve.Query(scene, -400f, 0f, z, 1f, 0f, 0f, buf);
            var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, 1f, 0f, 0f, 1200f, 300000);
            if (r.count == truth.Count) continue;

            Console.WriteLine("  bp[" + i + "] zeta=" + bps[i].ToString("F5") + " z=" + z.ToString("F4") +
                              "  got=" + r.count + " reference=" + truth.Count);
            Console.Write("    emitted spans: ");
            for (int j = 0; j + 1 < r.count; j += 2)
                Console.Write("[" + buf[j].rayT.ToString("F4") + "," + buf[j + 1].rayT.ToString("F4") + "] w=" +
                              (buf[j + 1].rayT - buf[j].rayT).ToString("E3") + "  ");
            Console.WriteLine();
            Console.Write("    reference spans: ");
            for (int j = 0; j + 1 < truth.Count; j += 2)
                Console.Write("[" + truth[j].ToString("F4") + "," + truth[j + 1].ToString("F4") + "] w=" +
                              (truth[j + 1] - truth[j]).ToString("E3") + "  ");
            Console.WriteLine();
        }
    }

    // ── the whole N1 sweep re-run against a reference carrying the AMENDED HS-1.1 ─────────────
    public static void N1AmendedRef()
    {
        Console.WriteLine();
        Console.WriteLine("=== N1-AMENDED-REF - the same 552-ray sweep, reference updated to the amended HS-1.1 (G > 0) ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var bps = new float[512];
        int combos = 0, badCombos = 0, badRays = 0, rays = 0;
        double worstDepthErr = 0; string worstAt = "";
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var def = new ShaperHeightDef {
                    technique = tech, bevel = bev, depth = new ZUIValue(40f),
                    bevelAmount = new ZUIValue(0.5f), bevelSteps = new ZUIValue(4f),
                    steps = new ZUIValue(5f), curve = new ZUIValue(1f), taper = new ZUIValue(1f), angle = new ZUIValue(45f) };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                int nb = ShaperHeight.Breakpoints(op, bps);
                combos++;
                bool comboBad = false;
                for (int i = 1; i + 1 < nb; i++)
                {
                    float z = op.baseZ + op.body * bps[i];
                    var scene = new ShaperResolveScene(); scene.Add(prog, op);
                    var buf = new ShaperCrossing[64];
                    var r = ShaperResolve.Query(scene, -400f, 0f, z, 1f, 0f, 0f, buf);
                    var truth = AmendedCrossings(prog, stack, op, -400f, 0f, z, 1f, 0f, 0f, 1200f, 300000);
                    rays++;
                    float depth = ShaperResolve.Depth(buf, r.count, 0);
                    double trueDepth = 0;
                    for (int j = 0; j + 1 < truth.Count; j += 2) trueDepth += truth[j + 1] - truth[j];
                    if (r.count != truth.Count || Math.Abs(depth - trueDepth) > 0.05)
                    {
                        badRays++; comboBad = true;
                        double e = Math.Abs(depth - trueDepth);
                        if (e > worstDepthErr) { worstDepthErr = e; worstAt = tech + "+" + bev + " bp[" + i + "]  got " + r.count + " true " + truth.Count + "  Depth " + depth.ToString("F3") + " vs " + trueDepth.ToString("F3"); }
                    }
                }
                if (comboBad) badCombos++;
            }
        Console.WriteLine("  combinations = " + combos + ",  interior-breakpoint rays fired = " + rays);
        Console.WriteLine("  combinations with at least one wrong ray = " + badCombos + ",  wrong rays = " + badRays);
        Console.WriteLine("  worst |Depth error| = " + worstDepthErr.ToString("F3") + " canvas px   " + worstAt);
    }

    // The amended HS-1.1: inside the silhouette, below base + body*G, AND G > 0.
    public static bool AmendedInside(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                     float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float z = oz + s * dz;
        float above = z - op.baseZ;
        if (above < 0f) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float d = ShaperEvaluator.Distance(field, px, py, stack);
        if (d > 0f || ShaperField.IsEmpty(d)) return false;
        float t = ShaperHeight.T(op, d);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        float g = ShaperHeight.Composed(op, t, nx, ny);
        if (!(g > 0f)) return false;
        return above <= op.body * g;
    }

    static List<double> AmendedCrossings(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                         float ox, float oy, float oz, float dx, float dy, float dz,
                                         float sMax, int samples)
    {
        var res = new List<double>();
        bool prev = AmendedInside(field, stack, op, ox, oy, oz, dx, dy, dz, 0f);
        if (prev) res.Add(0.0);
        double step = (double)sMax / samples;
        for (int i = 1; i <= samples; i++)
        {
            float s = (float)(i * step);
            bool m = AmendedInside(field, stack, op, ox, oy, oz, dx, dy, dz, s);
            if (m != prev)
            {
                double lo = (i - 1) * step, hi = i * step;
                for (int k = 0; k < 40; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (AmendedInside(field, stack, op, ox, oy, oz, dx, dy, dz, (float)mid) == prev) lo = mid; else hi = mid;
                }
                res.Add(hi);
                prev = m;
            }
        }
        if (prev) res.Add(sMax);
        return res;
    }

    // ── N3 breadth: the 42-combination tilted sweep the verifier ran (v2_march3) ──────────────
    public static void N3Breadth()
    {
        Console.WriteLine();
        Console.WriteLine("=== N3-BREADTH - 42 combinations x 3 tilts on a two-slot plate, spurious pairs ===");
        var root = V2.MultiSlotPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int rows = 0, bad = 0, spurTotal = 0, zeroWidth = 0;
        float[] tilts = { 7f, 23f, 41f };
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var def = new ShaperHeightDef {
                    technique = tech, bevel = bev, depth = new ZUIValue(300f),
                    bevelAmount = new ZUIValue(0.3f), bevelSteps = new ZUIValue(4f),
                    steps = new ZUIValue(5f), curve = new ZUIValue(1f), taper = new ZUIValue(1f), angle = new ZUIValue(45f) };
                var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
                var scene = new ShaperResolveScene(); scene.Add(prog, op);
                foreach (var tilt in tilts)
                {
                    float rad = tilt * Mathf.Deg2Rad;
                    float ddx = Mathf.Cos(rad), ddz = -Mathf.Sin(rad);
                    float oz = op.baseZ + 0.33f * op.body * op.supG;
                    var buf = new ShaperCrossing[64];
                    var r = ShaperResolve.Query(scene, -400f, 0f, oz, ddx, 0f, ddz, buf);
                    var truth = AmendedCrossings(prog, stack, op, -400f, 0f, oz, ddx, 0f, ddz, 1400f, 400000);
                    rows++;
                    if (r.count != truth.Count)
                    {
                        bad++; spurTotal += Math.Abs(r.count - truth.Count);
                        for (int j = 0; j + 1 < r.count; j += 2)
                            if (!(buf[j + 1].rayT - buf[j].rayT > 0f)) zeroWidth++;
                        Console.WriteLine("    " + tech + "+" + bev + " tilt=" + tilt + "  got=" + r.count + " true=" + truth.Count);
                    }
                }
            }
        Console.WriteLine("  rows=" + rows + "  rows with a count mismatch=" + bad +
                          "  total surplus/deficit crossings=" + spurTotal + "  ZERO-WIDTH emitted pairs=" + zeroWidth);
    }

    public static void Run() { N4(); N5Cost(); N1Residual(); N1AmendedRef(); N3Breadth(); }
}
