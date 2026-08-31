using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// THIRD-PASS probes. Everything here is new: it either re-points a second-pass probe at the
// function the march NOW calls, or attacks something the second fix round introduced.
public static class V3
{
    static ShaperNode Plate(float hw, float hh, float[] cx, float[] halfW)
    {
        var kids = new List<ShaperNode> {
            ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, "o", ShaperCombineMode.Add) };
        if (cx != null)
            for (int i = 0; i < cx.Length; i++)
            {
                var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW[i], rectHalfH = hh * 2f }, "s" + i, ShaperCombineMode.Subtract);
                n.transform.translate = new Vector2(cx[i], 0f);
                kids.Add(n);
            }
        return ShaperNode.Bag("p", ShaperCombineMode.Add, kids.ToArray());
    }

    static ShaperHeightOp H(ShaperProgram prog, ShaperExtrusionTechnique tech, ShaperBevelTechnique bev,
                            float body, float amount, float steps, float curve, float angle, float bevelSteps)
    {
        var def = new ShaperHeightDef {
            technique = tech, bevel = bev, depth = new ZUIValue(body), bevelAmount = new ZUIValue(amount),
            steps = new ZUIValue(steps), curve = new ZUIValue(curve), angle = new ZUIValue(angle),
            bevelSteps = new ZUIValue(bevelSteps), taper = new ZUIValue(1f) };
        return ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
    }

    static float LowerG(in ShaperHeightOp op, float t, float e)
        => op.technique == ShaperExtrusionTechnique.Linear ? e * ShaperHeight.Bevel(op, t)
                                                           : ShaperHeight.Composed(op, t, 0f, 0f);

    // N4-AFTER - the function the march NOW calls, on my own V2A grid plus t == tau
    public static void N4()
    {
        Console.WriteLine("=== V3-N4 - InverseAtEUpperBound (what ShaperResolve.cs:615 now calls) as an UPPER bound ===");
        Console.WriteLine("Same sweep as my V2A, but the t grid now INCLUDES t == tau exactly, which is where my");
        Console.WriteLine("second-pass worst case sat. Both functions measured side by side.");
        for (int pass = 0; pass < 2; pass++)
        {
            bool guarded = pass == 1;
            long checks = 0, viol = 0; double worst = 0; string at = "";
            foreach (var tech in Fixtures.Techs)
                foreach (var bev in Fixtures.Bevels)
                    foreach (float ang in new float[] { -180f, -135f, -90f, -45f, 0f, 45f, 90f, 135f, 180f })
                        foreach (float am in new float[] { 0.05f, 0.25f, 0.6f, 1f })
                        {
                            if (tech != ShaperExtrusionTechnique.Linear && ang != 45f) continue;
                            var op = Fixtures.Make(tech, bev, 4f, 1f, 1f, am, 3f, 1f, ang);
                            float e = tech == ShaperExtrusionTechnique.Linear ? op.infE : 1f;
                            for (int i = 1; i <= 400; i++)
                            {
                                float zeta = (i / 400f) * op.supG;
                                float tau = guarded ? ShaperHeight.InverseAtEUpperBound(op, zeta, e)
                                                    : ShaperHeight.InverseAtE(op, zeta, e);
                                if (ShaperHeight.IsNoCrossSection(tau)) continue;
                                for (int k = 0; k <= 40; k++)
                                {
                                    float t = k == 0 ? tau : tau + (1f - tau) * (k / 40f);
                                    if (t < tau) continue;
                                    float g = LowerG(op, t, e);
                                    checks++;
                                    if (g < zeta)
                                    {
                                        viol++; double m = zeta - g;
                                        if (m > worst) { worst = m; at = tech + "+" + bev + " ang=" + ang + " a=" + am + " zeta=" + zeta + " tau=" + tau + " t=" + t + " LowerG=" + g; }
                                    }
                                }
                            }
                        }
            Console.WriteLine((guarded ? "  InverseAtEUpperBound (SHIPPED per-step): " : "  InverseAtE           (pre-fix per-step): ")
                + "checks=" + checks + "  violations=" + viol + "  worst shortfall in zeta = " + worst.ToString("E4"));
            if (at != "") Console.WriteLine("      worst: " + at);
        }
    }

    // N5-NUMERIC - |grad E| measured from LocalNormalised/Profile themselves, including clamped regions
    public static void N5()
    {
        Console.WriteLine();
        Console.WriteLine("=== V3-N5 - is linearEGrad an upper bound MEASURED, not re-derived from the same four regimes? ===");
        Console.WriteLine("|grad E| sampled by central difference on the SHIPPED LocalNormalised+Profile over a dense");
        Console.WriteLine("grid that straddles every clamp boundary. The difference is formed in DOUBLE so the");
        Console.WriteLine("subtraction of two near-1 values stays honest.");
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Linear, bevel = ShaperBevelTechnique.None,
                                        depth = new ZUIValue(40f) };
        int cases = 0, bad = 0; double worstRatio = 0; string worstAt = "";
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
            double measured = 0; string mAt = "";
            const float h = 0.25f;
            for (float x = -300f; x <= 300f; x += 3.7f)
                for (float y = -300f; y <= 300f; y += 3.7f)
                {
                    float ax, ay, bx, by, cx, cy, dxn, dyn;
                    ShaperHeight.LocalNormalised(op, x - h, y, out ax, out ay);
                    ShaperHeight.LocalNormalised(op, x + h, y, out bx, out by);
                    ShaperHeight.LocalNormalised(op, x, y - h, out cx, out cy);
                    ShaperHeight.LocalNormalised(op, x, y + h, out dxn, out dyn);
                    double ex = ((double)ShaperHeight.Profile(op, 0f, bx, by) - ShaperHeight.Profile(op, 0f, ax, ay)) / (2.0 * h);
                    double ey = ((double)ShaperHeight.Profile(op, 0f, dxn, dyn) - ShaperHeight.Profile(op, 0f, cx, cy)) / (2.0 * h);
                    double g = Math.Sqrt(ex * ex + ey * ey);
                    if (g > measured) { measured = g; mAt = "(" + x.ToString("F1") + "," + y.ToString("F1") + ")"; }
                }
            double ratio = measured / Math.Max(1e-30, op.linearEGrad);
            if (ratio > 1.02) bad++;
            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worstAt = "angle=" + ang + " rot=" + rot + " skew=" + skew + " sx=" + sx + " halfH=" + hh
                        + "  measured=" + measured.ToString("E5") + " at " + mAt + "  baked=" + op.linearEGrad.ToString("E5");
            }
        }
        Console.WriteLine("  configurations = " + cases);
        Console.WriteLine("  configurations where the MEASURED |grad E| exceeds the baked bound by more than 2 percent: " + bad);
        Console.WriteLine("  worst measured/baked = " + worstRatio.ToString("F4"));
        Console.WriteLine("  " + worstAt);
        Console.WriteLine("  (a central difference straddling a clamp edge under-reads, so a ratio slightly BELOW 1 is expected)");
    }

    // N1-RESIDUAL - the one ray in 552 the fixer calls unrelated
    public static void N1Residual()
    {
        Console.WriteLine();
        Console.WriteLine("=== V3-N1R - the residual ray in the 552-ray breakpoint sweep, examined ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var bps = new float[512];
        int printed = 0;
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var op = H(prog, tech, bev, 40f, 0.5f, 5f, 1f, 45f, 4f);
                int nb = ShaperHeight.Breakpoints(op, bps);
                for (int i = 1; i + 1 < nb; i++)
                {
                    float z = op.baseZ + op.body * bps[i];
                    var scene = new ShaperResolveScene(); scene.Add(prog, op);
                    var buf = new ShaperCrossing[64];
                    var r = ShaperResolve.Query(scene, -400f, 0f, z, 1f, 0f, 0f, buf);
                    var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, 1f, 0f, 0f, 1200f, 300000);
                    float depth = ShaperResolve.Depth(buf, r.count, 0);
                    double trueDepth = 0;
                    for (int j = 0; j + 1 < truth.Count; j += 2) trueDepth += truth[j + 1] - truth[j];
                    if (r.count != truth.Count || Math.Abs(depth - trueDepth) > 0.05)
                    {
                        printed++;
                        Console.WriteLine("  " + tech + "+" + bev + "  bp[" + i + "] zeta=" + bps[i].ToString("F6") + " z=" + z.ToString("F6"));
                        Console.WriteLine("     got " + r.count + " crossings, true " + truth.Count
                                          + "   Depth " + depth.ToString("F4") + " vs true " + trueDepth.ToString("F4"));
                        Console.Write("     emitted: ");
                        for (int q = 0; q < r.count; q++) Console.Write(buf[q].rayT.ToString("F4") + (buf[q].entering ? "I" : "O") + " ");
                        Console.WriteLine();
                        Console.Write("     truth  : ");
                        foreach (double tt in truth) Console.Write(tt.ToString("F4") + " ");
                        Console.WriteLine();
                        for (int j = 1; j + 1 < truth.Count; j += 2)
                        {
                            double gap = truth[j + 1] - truth[j];
                            Console.WriteLine("     air gap between span " + (j / 2) + " and " + (j / 2 + 1) + " = " + gap.ToString("E4")
                                              + " px   (SurfaceResolution = " + ShaperResolve.SurfaceResolution + ")");
                        }
                        bool dup = false;
                        for (int q = 0; q + 1 < r.count; q++) if (Math.Abs(buf[q].rayT - buf[q + 1].rayT) < 1e-4f) dup = true;
                        Console.WriteLine("     duplicated rayT present (the N1 signature)? " + dup);
                        float zOff = V2.NextUp(z, 4);
                        var r2 = ShaperResolve.Query(scene, -400f, 0f, zOff, 1f, 0f, 0f, buf);
                        var truth2 = March.TrueCrossings(prog, stack, op, -400f, 0f, zOff, 1f, 0f, 0f, 1200f, 300000);
                        Console.WriteLine("     the same ray 4 ulps OFF the breakpoint (where N1 structurally cannot fire): got "
                                          + r2.count + ", true " + truth2.Count
                                          + (r2.count != truth2.Count ? "  -> SAME disagreement off the breakpoint, so NOT N1"
                                                                      : "  -> agrees off the breakpoint"));
                    }
                }
            }
        Console.WriteLine("  residual rays printed: " + printed);
    }

    // N3-INVISIBLE - is the class GONE, or newly unobservable?
    public static void N3Invisible()
    {
        Console.WriteLine();
        Console.WriteLine("=== V3-N3 - zero-width emitted pairs, hunted by MY OWN detector, not by H6 ===");
        Console.WriteLine("Two questions: (1) does the resolve still emit any pair of width 0 anywhere; (2) did the");
        Console.WriteLine("G > 0 convention LOSE anything the old closed reading contained (measured against a truth");
        Console.WriteLine("scan that still carries the OLD convention).");
        var root = Plate(60f, 60f, new float[] { -18f, 22f }, new float[] { 3.5f, 5f });
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int rays = 0, zeroWidth = 0, omissions = 0, depthLoss = 0;
        double worstGap = 0, worstDepthLoss = 0; string worstAt = "";
        var buf = new ShaperCrossing[256];
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                var op = H(prog, tech, bev, 90f, 0.25f, 4f, 1f, 45f, 3f);
                var scene = new ShaperResolveScene(); scene.Add(prog, op);
                for (int r = 0; r < 24; r++)
                {
                    float tilt = (r % 8) * 6f + 2f;
                    float zf = 0.05f + 0.28f * (r / 8);
                    float rad = tilt * Mathf.Deg2Rad;
                    float ddx = Mathf.Cos(rad), ddz = -Mathf.Sin(rad);
                    float ox = -130f, oy = (r % 3 - 1) * 14f;
                    float oz = op.baseZ + zf * op.body * op.supG + 70f * Mathf.Tan(rad);
                    var rr = ShaperResolve.Query(scene, ox, oy, oz, ddx, 0f, ddz, buf);
                    rays++;
                    for (int i = 0; i + 1 < rr.count; i++)
                        if (buf[i].entering && !buf[i + 1].entering && buf[i + 1].rayT - buf[i].rayT <= 1e-5f) zeroWidth++;
                    var truth = March.TrueCrossings(prog, stack, op, ox, oy, oz, ddx, 0f, ddz, 400f, 400000);
                    foreach (double tt in truth)
                    {
                        double best = 1e30;
                        for (int i = 0; i < rr.count; i++) { double e = Math.Abs(buf[i].rayT - tt); if (e < best) best = e; }
                        if (best > worstGap) { worstGap = best; worstAt = tech + "+" + bev + " tilt=" + tilt; }
                        if (best > 0.05) omissions++;
                    }
                    double trueDepth = 0;
                    for (int j = 0; j + 1 < truth.Count; j += 2) trueDepth += truth[j + 1] - truth[j];
                    double gotDepth = ShaperResolve.Depth(buf, rr.count, 0);
                    if (trueDepth - gotDepth > 0.05) { depthLoss++; if (trueDepth - gotDepth > worstDepthLoss) worstDepthLoss = trueDepth - gotDepth; }
                }
            }
        Console.WriteLine("  rays = " + rays);
        Console.WriteLine("  emitted pairs of width <= 1e-5 px (the N3 signature): " + zeroWidth);
        Console.WriteLine("  true crossings (OLD closed convention) with no emitted one within 0.05 px: " + omissions
                          + "   worst gap " + worstGap.ToString("E4") + " px  (" + worstAt + ")");
        Console.WriteLine("  rays where Depth LOST solid vs the OLD convention: " + depthLoss
                          + "   worst loss " + worstDepthLoss.ToString("E4") + " px");
    }

    // N6 - exhaustive re-sweep, including the four latent (n,k) the fixer claims
    public static void N6()
    {
        Console.WriteLine();
        Console.WriteLine("=== V3-N6 - the Stepped inverses, exhaustively ===");
        Console.WriteLine("  (a) does floor(fl(k/n)*n) map back to k for every authored n and k?");
        int failN = 0; var list = new List<string>();
        for (int n = 2; n <= 32; n++)
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n;
                int back = Mathf.FloorToInt(t * n);
                if (back < k) { failN++; list.Add("n=" + n + ",k=" + k); }
            }
        Console.WriteLine("      indices where floor(fl(k/n)*n) < k : " + failN + "   " + string.Join("  ", list));
        int failM = 0; var listM = new List<string>();
        for (int m = 2; m <= 16; m++)
            for (int k = 0; k <= m; k++)
            {
                float u = k * (1f / m);
                int back = Mathf.FloorToInt(u * m);
                if (back < k) { failM++; listM.Add("m=" + m + ",k=" + k); }
            }
        Console.WriteLine("      bevel, k*(1/m) round trip failures : " + failM + "   " + string.Join("  ", listM));

        Console.WriteLine("  (b) |Ginv - Bisect| and reach shortfall over EVERY tread of EVERY step count, plus/minus 4 ulps");
        double worstDt = 0, worstReach = 0; string atDt = "", atReach = "";
        long probes = 0;
        for (int n = 2; n <= 32; n++)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int k = 0; k <= n; k++)
            {
                float tread = Mathf.Min(1f, k / (float)(n - 1));
                for (int u = -4; u <= 4; u++)
                {
                    float zeta = u == 0 ? tread : (u > 0 ? V2.NextUp(tread, u) : NextDown(tread, -u));
                    if (zeta <= 0f || zeta > 1f) continue;
                    probes++;
                    float ginv = ShaperHeight.ProfileInverse(op, zeta, 0f, 0f);
                    if (ShaperHeight.IsNoCrossSection(ginv)) continue;
                    float bis = ShaperHeight.Bisect(op, zeta, 0f, 0f, 0f, 1f);
                    double dt = Math.Abs(ginv - bis);
                    double reach = Math.Max(0, zeta - ShaperHeight.Composed(op, ginv, 0f, 0f));
                    if (dt > worstDt) { worstDt = dt; atDt = "n=" + n + " k=" + k + " zeta=" + zeta.ToString("R"); }
                    if (reach > worstReach) { worstReach = reach; atReach = "n=" + n + " k=" + k + " zeta=" + zeta.ToString("R"); }
                }
            }
        }
        Console.WriteLine("      ProfileInverse(Stepped): probes=" + probes + "  worst |dt| = " + worstDt.ToString("E3")
                          + (atDt != "" ? " at " + atDt : "") + "   worst reach shortfall = " + worstReach.ToString("E3")
                          + (atReach != "" ? " at " + atReach : ""));
        double wDt2 = 0, wR2 = 0; string at2 = ""; long p2 = 0;
        for (int m = 2; m <= 16; m++)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Stepped, 4f, 1f, 1f, 1f, m, 1f, 45f);
            for (int k = 0; k <= m; k++)
            {
                float lvl = Mathf.Min(1f, k / (float)m);
                for (int u = -4; u <= 4; u++)
                {
                    float zeta = u == 0 ? lvl : (u > 0 ? V2.NextUp(lvl, u) : NextDown(lvl, -u));
                    if (zeta <= 0f || zeta > 1f) continue;
                    p2++;
                    float uu = ShaperHeight.BevelInverseU(op, zeta);
                    if (uu < 0f) continue;
                    float bis = ShaperHeight.Bisect(op, zeta, 0f, 0f, 0f, 1f);
                    double dt = Math.Abs(uu * op.a - bis);
                    double reach = Math.Max(0, zeta - ShaperHeight.Composed(op, uu * op.a, 0f, 0f));
                    if (dt > wDt2) { wDt2 = dt; at2 = "m=" + m + " k=" + k + " zeta=" + zeta.ToString("R"); }
                    if (reach > wR2) wR2 = reach;
                }
            }
        }
        Console.WriteLine("      BevelInverseU(Stepped): probes=" + p2 + "  worst |dt| = " + wDt2.ToString("E3")
                          + (at2 != "" ? " at " + at2 : "") + "   worst reach shortfall = " + wR2.ToString("E3"));

        long c1 = 0, v1 = 0, c2 = 0, v2 = 0;
        for (int n = 2; n <= 32; n++)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int k = 0; k <= n; k++)
            {
                float tread = Mathf.Min(1f, k / (float)(n - 1));
                for (int u = -2; u <= 2; u++)
                {
                    float zeta = u == 0 ? tread : (u > 0 ? V2.NextUp(tread, u) : NextDown(tread, -u));
                    if (zeta <= 0f || zeta > 1f) continue;
                    float lo = ShaperHeight.InverseLowerBound(op, zeta);
                    if (!ShaperHeight.IsNoCrossSection(lo))
                        for (int j = 0; j < 200; j++)
                        { float t = lo * (j / 200f); if (t >= lo) continue; c1++; if (ShaperHeight.Composed(op, t, 0f, 0f) >= zeta) v1++; }
                    float hi = ShaperHeight.InverseUpperBound(op, zeta);
                    if (!ShaperHeight.IsNoCrossSection(hi))
                        for (int j = 0; j <= 200; j++)
                        { float t = hi + (1f - hi) * (j / 200f); c2++; if (ShaperHeight.Composed(op, t, 0f, 0f) < zeta) v2++; }
                }
            }
        }
        Console.WriteLine("  (c) containing prism: " + c1 + " checks, " + v1 + " violations;  contained prism: " + c2 + " checks, " + v2 + " violations");
    }

    static float NextDown(float x, int n)
    {
        for (int i = 0; i < n; i++) { int b = BitConverter.SingleToInt32Bits(x); x = BitConverter.Int32BitsToSingle(x > 0 ? b - 1 : b + 1); }
        return x;
    }

    public static void Run() { N4(); N5(); N1Residual(); N3Invisible(); N6(); }
}
