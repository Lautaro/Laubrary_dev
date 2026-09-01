using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// W4 — third-pass verifier, round 2: reconciling the two contradictory N4/N6 measurements, an
// independent N5 test that does not re-derive the fixer's own four regimes, and a direct
// demonstration of whether H6's omission arm can see the class W3A found.
public static class W4
{
    static float NextUp(float x) { if (x == 0f) return float.Epsilon; int b = BitConverter.SingleToInt32Bits(x); return BitConverter.Int32BitsToSingle(x > 0 ? b + 1 : b - 1); }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W4A — N4. The invariant InverseAtEUpperBound actually promises is
    //        LowerG(tau, e) >= zeta,  plus  LowerG monotone non-decreasing in t.
    //        Test BOTH separately, so a monotonicity artefact cannot be reported as a bound failure.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void A()
    {
        Console.WriteLine("=== W4A — N4 reconciliation: the bound invariant vs the monotonicity it relies on ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));
        float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };

        long invChecks = 0; int invViol = 0; double worstInv = 0; string atInv = "";
        long monChecks = 0; int monViol = 0; double worstMon = 0; string atMon = "";

        foreach (var te in techs) foreach (var be in bevs) foreach (float am in amounts)
        {
            var op = W3.MakeOp(prog, te, be, 40f, 4, am, 1f, 1f);
            if (!op.present) continue;
            bool lin = te == ShaperExtrusionTechnique.Linear;
            // the e values the march can pin: for Linear the whole [infE, supE] window, else 1
            var es = new List<float>();
            if (lin) { for (int j = 0; j <= 20; j++) es.Add(op.infE + (op.supE - op.infE) * j / 20f); }
            else es.Add(1f);

            foreach (float e in es)
                for (int i = 0; i <= 400; i++)
                {
                    float zeta = i / 400f * op.supG;
                    float tau = ShaperHeight.InverseAtEUpperBound(op, zeta, e);
                    if (ShaperHeight.IsNoCrossSection(tau)) continue;

                    // (1) the invariant the function itself verifies
                    float lg = lin ? e * ShaperHeight.Bevel(op, tau) : ShaperHeight.Composed(op, tau, 0f, 0f);
                    invChecks++;
                    if (lg < zeta) { invViol++; double s = (double)zeta - lg; if (s > worstInv) { worstInv = s; atInv = te + "+" + be + " a=" + am + " e=" + e.ToString("F5") + " zeta=" + zeta.ToString("F7"); } }

                    // (2) monotonicity of LowerG for t >= tau — what the march needs on top of (1)
                    for (int q = 0; q <= 40; q++)
                    {
                        float t = tau + (1f - tau) * q / 40f;
                        if (t > 1f) t = 1f;
                        float g = lin ? e * ShaperHeight.Bevel(op, t) : ShaperHeight.Composed(op, t, 0f, 0f);
                        monChecks++;
                        if (g < zeta) { monViol++; double s = (double)zeta - g; if (s > worstMon) { worstMon = s; atMon = te + "+" + be + " a=" + am + " e=" + e.ToString("F5") + " zeta=" + zeta.ToString("F7") + " tau=" + tau.ToString("F7") + " t=" + t.ToString("F7") + " G=" + g.ToString("F9"); } }
                    }
                }
        }
        Console.WriteLine("  (1) INVARIANT  LowerG(tau, e) >= zeta        : checks=" + invChecks + "   violations=" + invViol + "   worst " + worstInv.ToString("E4"));
        if (atInv != "") Console.WriteLine("      " + atInv);
        Console.WriteLine("  (2) MONOTONE   LowerG(t, e) >= zeta, t>=tau  : checks=" + monChecks + "   violations=" + monViol + "   worst " + worstMon.ToString("E4"));
        if (atMon != "") Console.WriteLine("      " + atMon);
        Console.WriteLine("  1 ulp at 1.0 = " + ((double)NextUp(1f) - 1.0).ToString("E4") + "  — a violation of this size in (2) and none in (1)");
        Console.WriteLine("  means the shortfall is the DOCUMENTED 1-ulp float non-monotonicity of G, not a bound failure.");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W4B — N6/HS-5.2 containment: is InverseLowerBound(zeta) <= inf{ t (a float) : G(t) >= zeta } ?
    //        Dense FLOAT scan, not a lattice, so the n=29/k=15 disagreement is settled either way.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void B()
    {
        Console.WriteLine();
        Console.WriteLine("=== W4B — HS-5.2 containment of the CONTAINING prism, by dense FLOAT scan ===");
        Console.WriteLine("  The march skips air on { d > -tauMin*span }, i.e. it assumes no t < tauMin reaches zeta.");
        Console.WriteLine("  A single float t below tauMin with G(t) >= zeta breaks that. Scanned at 4e-6 t-resolution.");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);

        int probes = 0, over = 0; double worst = 0; string at = "";
        var affected = new SortedSet<int>();
        for (int n = 2; n <= 32; n++)
        {
            var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 40f, n, 0.5f, 1f, 1f);
            for (int k = 0; k <= n - 1; k++)
            {
                float zeta = (n - 1) <= 0 ? 0f : (float)k / (n - 1);
                for (int u = -2; u <= 2; u++)
                {
                    float z = zeta;
                    for (int q = 0; q < Math.Abs(u); q++) z = u > 0 ? NextUp(z) : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(z) - 1);
                    if (z <= 0f || z > op.supG) continue;
                    float tau = ShaperHeight.InverseLowerBound(op, z);
                    if (ShaperHeight.IsNoCrossSection(tau)) continue;
                    probes++;
                    // dense scan strictly BELOW tau
                    float firstReach = -1f;
                    int steps = 250000;
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = (float)((double)tau * i / steps);
                        if (t >= tau) break;
                        if (ShaperHeight.Composed(op, t, 0f, 0f) >= z) { firstReach = t; break; }
                    }
                    if (firstReach >= 0f)
                    {
                        over++; affected.Add(n);
                        double e = (double)tau - firstReach;
                        if (e > worst) { worst = e; at = "n=" + n + " k=" + k + " ulp=" + u + " zeta=" + z.ToString("F7") + "  tauMin=" + tau.ToString("F7") + "  a float that already reaches: " + firstReach.ToString("F7") + "   gap " + e.ToString("E4") + " t-units (1 tread = " + (1f / n).ToString("F7") + ")"; }
                    }
                }
            }
        }
        Console.WriteLine("  probes = " + probes + "   zetas where some t < tauMin ALREADY reaches = " + over);
        Console.WriteLine("  worst over-report = " + worst.ToString("E4") + " t-units");
        if (at != "") Console.WriteLine("    " + at);
        Console.Write("  step counts affected:"); foreach (int a in affected) Console.Write(" " + a); Console.WriteLine();

        // does it lose anything in the actual march? aim rays at exactly those zetas
        Console.WriteLine("  does any of this cost a crossing in the march? (rays aimed at the affected zetas)");
        int rays = 0, lost = 0; double wg = 0;
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];
        foreach (int n in affected)
        {
            var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 290f, n, 0.5f, 1f, 1f);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int k = 0; k <= n - 1; k++)
            {
                float zeta = (float)k / (n - 1);
                foreach (float dzf in new[] { -0.25f, -0.5f })
                {
                    float dz = dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
                    float oz = op.baseZ + op.body * zeta;
                    var rr = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, buf);
                    rays++;
                    W3.Pred p = s => W3.Solid(prog, stack, op, -400f, 0f, oz, dx, 0f, dz, s);
                    var truth = W3.Scan(p, 1400.0, 200000);
                    double gmax = 0;
                    foreach (double tf in truth) { double best = 1e30; for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - tf)); if (best > gmax) gmax = best; }
                    if (gmax > 0.05) { lost++; if (gmax > wg) wg = gmax; }
                }
            }
        }
        Console.WriteLine("    rays = " + rays + "   losing a crossing = " + lost + "   worst gap " + wg.ToString("E4") + " px");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W4C — N5. A LIPSCHITZ test, not a derivative: the bound's only job is
    //        |E(p1) − E(p0)| <= linearEGrad · |p1 − p0|   for the pairs the march forms.
    //        No re-derivation of the fixer's four regimes; pure sampling of the shipped E.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void C()
    {
        Console.WriteLine();
        Console.WriteLine("=== W4C — N5: linearEGrad as a LIPSCHITZ constant for the SHIPPED E (pairs, not derivatives) ===");
        var root = March.SolidPlate(200f, 200f);
        int cfgs = 0, viol = 0; double worstRatio = 0; string at = "";
        float[] angles = { -180f, -135f, -90f, -45f, 0f, 30f, 45f, 90f, 135f, 170f, 180f };
        float[] rots = { 0f, 17f, 41f, 73f, 113f };
        float[] skews = { 0f, 12f, 24f, 40f, 60f };
        float[] scales = { 0.4f, 1f, 2.3f };
        float[] halfHs = { 40f, 120f };

        var rnd = new System.Random(20260831);
        foreach (float ang in angles) foreach (float rot in rots) foreach (float sk in skews) foreach (float sx in scales) foreach (float hh in halfHs)
        {
            var node = March.SolidPlate(120f, hh);
            node.transform.rotation = rot;
            node.transform.skewDegrees = new Vector2(sk, 0f);
            node.transform.scale = new Vector2(sx, 1f);
            var prog = ShaperCompiler.Compile(node, 0f, 0u);
            var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 40f, 4, 0.5f, 1f, 1f, 4, ang);
            if (!op.present) continue;
            cfgs++;
            float g = op.linearEGrad;
            double worstHere = 0; string hereAt = "";
            for (int s = 0; s < 4000; s++)
            {
                float x0 = (float)(rnd.NextDouble() * 800 - 400), y0 = (float)(rnd.NextDouble() * 800 - 400);
                double th = rnd.NextDouble() * Math.PI * 2;
                float h = (float)Math.Pow(10, -1 + rnd.NextDouble() * 2.7);   // 0.1 .. 50 canvas px
                // (a separation below ~0.01 px makes |dE| comparable to E's own float ulp, which
                //  measures quantisation rather than slope; 0.1 px is 1e4 ulps of headroom)
                float x1 = x0 + (float)(Math.Cos(th) * h), y1 = y0 + (float)(Math.Sin(th) * h);
                float n0x, n0y, n1x, n1y;
                ShaperHeight.LocalNormalised(op, x0, y0, out n0x, out n0y);
                ShaperHeight.LocalNormalised(op, x1, y1, out n1x, out n1y);
                float e0 = ShaperHeight.Profile(op, 0f, n0x, n0y);
                float e1 = ShaperHeight.Profile(op, 0f, n1x, n1y);
                double dist = Math.Sqrt((double)(x1 - x0) * (x1 - x0) + (double)(y1 - y0) * (y1 - y0));
                if (dist <= 0) continue;
                double need = Math.Abs((double)e1 - e0) / dist;
                double ratio = g > 0 ? need / g : (need > 0 ? 1e30 : 0);
                if (ratio > worstHere) { worstHere = ratio; hereAt = "h=" + h.ToString("E2") + " |dE|=" + Math.Abs((double)e1 - e0).ToString("E4"); }
            }
            if (worstHere > 1.0 + 1e-4) viol++;
            if (worstHere > worstRatio) { worstRatio = worstHere; at = "angle=" + ang + " rot=" + rot + " skew=" + sk + " sx=" + sx + " halfH=" + hh + " baked=" + g.ToString("E5") + "  " + hereAt; }
        }
        Console.WriteLine("  configurations = " + cfgs + " x 4000 random point PAIRS each");
        Console.WriteLine("  configurations where a pair needs MORE than the baked bound = " + viol);
        Console.WriteLine("  worst needed/baked ratio = " + worstRatio.ToString("F6"));
        Console.WriteLine("    at " + at);
        Console.WriteLine("  NOTE: a Lipschitz test is the right instrument here — E is piecewise linear with");
        Console.WriteLine("  kinks at the clamp edges, so a finite difference that takes its x-partial in one");
        Console.WriteLine("  regime and its y-partial in another synthesises a |grad| no straight segment can");
        Console.WriteLine("  realise. Pairs cannot do that: they measure the actual variation over a segment.");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W4D — FIX-REPORT-2 open question 3: is there any technique whose G gets NEAR zero but not zero?
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void D()
    {
        Console.WriteLine();
        Console.WriteLine("=== W4D — 'G nearly zero' (FIX-REPORT-2 open question 3): smallest POSITIVE G per technique ===");
        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        float[] curves = { 0.2f, 1f, 4f };
        Console.WriteLine(string.Format("  {0,-10} {1,7} {2,16} {3,14} {4,16}", "technique", "curve", "min positive G", "at t", "thickness (px)"));
        foreach (var te in techs) foreach (float c in curves)
        {
            var op = W3.MakeOp(prog, te, ShaperBevelTechnique.None, 300f, 4, 0.5f, c, 1f);
            if (!op.present) continue;
            float smallest = float.MaxValue, atT = 0;
            for (int i = 0; i <= 400000; i++)
            {
                float t = i / 400000f;
                float g = ShaperHeight.Composed(op, t, 0f, 0f);
                if (g > 0f && g < smallest) { smallest = g; atT = t; }
            }
            // and a log-spaced probe right down to denormal t
            for (int i = 0; i <= 400; i++)
            {
                float t = (float)Math.Pow(10, -40 + i * 40.0 / 400);
                float g = ShaperHeight.Composed(op, t, 0f, 0f);
                if (g > 0f && g < smallest) { smallest = g; atT = t; }
            }
            Console.WriteLine(string.Format("  {0,-10} {1,7} {2,16} {3,14} {4,16}", te, c,
                smallest == float.MaxValue ? "none" : smallest.ToString("E4"), atT.ToString("E3"),
                smallest == float.MaxValue ? "-" : (smallest * op.body).ToString("E4")));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W4E — can H6's OWN omission arm see the class W3A found?  H6's exact fixture, two changes:
    //        the ray heights are aimed at a BREAKPOINT, and the step count is swept.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void E()
    {
        Console.WriteLine();
        Console.WriteLine("=== W4E — H6's own omission fixture, re-aimed. Does the audit's arm miss the class by design? ===");
        Console.WriteLine("  H6 uses OpFor(..., depth 90, amount 0.25) with steps = 4 (its default) and ray heights");
        Console.WriteLine("  zf = 0.08 / 0.30 / 0.52 of the extent — fractions, never a tread. Two variants below.");

        var node = March.SolidPlate(120f, 60f);
        var kids = new List<ShaperNode> { ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 120f, rectHalfH = 60f }, "plate", ShaperCombineMode.Add) };
        for (int i = 0; i < 2; i++)
        {
            var slot = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 8f, rectHalfH = 120f };
            var b = ShaperNode.Primitive(slot, "slot" + i, ShaperCombineMode.Subtract);
            b.transform.translate = new Vector2(i == 0 ? -50f : 50f, 0f);
            kids.Add(b);
        }
        var root = ShaperNode.Bag("plate", ShaperCombineMode.Add, kids.ToArray());
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];

        foreach (bool aimed in new[] { false, true })
            foreach (int[] stepSet in new[] { new[] { 4 }, new[] { 4, 8, 12, 13, 14, 15, 18, 20, 22, 23, 27, 29, 31 } })
            {
                int rays = 0, omissions = 0; double worst = 0; string at = "";
                foreach (int n in stepSet)
                {
                    var op = W3.MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 90f, n, 0.25f, 1f, 1f);
                    var scene = new ShaperResolveScene(); scene.Add(prog, op);
                    for (int r = 0; r < 12 * (aimed ? 3 : 1); r++)
                    {
                        float tilt = (r % 4) * 14f + 2f;
                        float rad = tilt * Mathf.Deg2Rad;
                        float ddx = Mathf.Cos(rad), ddz = -Mathf.Sin(rad);
                        float ox = -130f, oy = (r % 3 - 1) * 14f;
                        float zf = aimed
                            ? (float)((r / 4) % Math.Max(1, n)) / Math.Max(1, n - 1)     // a TREAD height
                            : 0.08f + 0.22f * (r / 4);                                    // H6's own fractions
                        float oz = op.baseZ + zf * op.body * op.supG + 70f * Mathf.Tan(rad);
                        var rr = ShaperResolve.Query(scene, ox, oy, oz, ddx, 0f, ddz, buf);
                        rays++;
                        W3.Pred p = s => W3.Solid(prog, stack, op, ox, oy, oz, ddx, 0f, ddz, s);
                        var truth = W3.Scan(p, 400.0, 200000);
                        foreach (double tf in truth)
                        {
                            double best = 1e30;
                            for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - tf));
                            if (best > 0.05) { omissions++; if (best > worst) { worst = best; at = "n=" + n + " tilt=" + tilt + " zf=" + zf.ToString("F4"); } }
                        }
                    }
                }
                Console.WriteLine("  ray heights " + (aimed ? "AIMED AT A TREAD" : "H6's own fractions ") + ", step counts " + (stepSet.Length == 1 ? "{4} (H6's)" : "{4..31}")
                                + " : rays=" + rays + "  omissions=" + omissions + "  worst gap " + worst.ToString("F4") + " px  " + at);
            }
    }

    public static void Run(string sel)
    {
        if (sel == "all" || sel == "A") A();
        if (sel == "all" || sel == "B") B();
        if (sel == "all" || sel == "C") C();
        if (sel == "all" || sel == "D") D();
        if (sel == "all" || sel == "E") E();
    }
}
