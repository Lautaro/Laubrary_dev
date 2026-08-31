using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

public static class Tests
{
    // ── S1/S2: the declared slope bounds, measured with MY instrument ─────────────────────────────
    // Max forward difference quotient over t, with the float-rounding floor subtracted, at several h.
    public static void Slopes()
    {
        Console.WriteLine("=== S — HS-4.1's declared slope bounds vs a measured difference quotient ===");
        Console.WriteLine("Measured in DOUBLE from the spec's own formulas AND in float from the shipped code.");
        Console.WriteLine();

        // profiles
        Console.WriteLine("-- extrusion profiles, sup|dE/dt| --");
        var profCases = new List<Tuple<ShaperExtrusionTechnique, float, float, float>>();
        foreach (float c in new float[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.5001f, 0.7f, 1f, 2f, 4f })
        { profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Dome, 4f, c, 1f)); profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Round, 4f, c, 1f)); }
        foreach (float ta in new float[] { 0f, 0.02f, 1f / 12f, 0.1f, 0.5f, 1f })
        { profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Taper, 4f, 1f, ta)); profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Pyramid, 4f, 1f, ta)); }
        profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Flat, 4f, 1f, 1f));
        profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Linear, 4f, 1f, 1f));
        foreach (float st in new float[] { 2f, 4f, 32f }) profCases.Add(Tuple.Create(ShaperExtrusionTechnique.Stepped, st, 1f, 1f));

        foreach (var pc in profCases)
        {
            var op = Fixtures.Make(pc.Item1, ShaperBevelTechnique.None, pc.Item2, pc.Item3, pc.Item4, 0.25f, 3f, 1f, 45f);
            float declared = ShaperHeight.ExtrusionSlopeBound(op);
            double m1 = MaxQuotientE(op, 1e-3), m2 = MaxQuotientE(op, 1e-5), m3 = MaxQuotientE(op, 1e-7);
            string verdict;
            if (float.IsPositiveInfinity(declared))
                verdict = (m3 > m1 * 5.0) ? "INF ok (diverges " + (m3 / Math.Max(1e-12, m1)).ToString("F1") + "x over 1e-3->1e-7)"
                                          : "INF SUSPECT (did not diverge: " + m1.ToString("F3") + " -> " + m3.ToString("F3") + ")";
            else
                verdict = (m3 <= declared * 1.02 + 1e-6) ? "finite ok" : "*** UNDER-DECLARED: measured " + m3.ToString("F4") + " > declared " + declared.ToString("F4");
            Console.WriteLine(string.Format("  {0,-8} steps={1,-4} curve={2,-7} taper={3,-6} declared={4,-12} h1e-3={5,-11:F4} h1e-5={6,-11:F4} h1e-7={7,-11:F4} {8}",
                pc.Item1, pc.Item2, pc.Item3, pc.Item4, Fmt(declared), m1, m2, m3, verdict));
        }

        Console.WriteLine();
        Console.WriteLine("-- bevels, sup|dB/du| --");
        foreach (var bev in Fixtures.Bevels)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 0.5f, 3f, 1f, 45f);
            float declared = ShaperHeight.BevelSlopeBound(op);
            double m1 = MaxQuotientB(op, 1e-3), m3 = MaxQuotientB(op, 1e-7);
            string verdict;
            if (float.IsPositiveInfinity(declared)) verdict = (m3 > m1 * 5.0) ? "INF ok (" + (m3 / Math.Max(1e-12, m1)).ToString("F1") + "x)" : "INF SUSPECT " + m1.ToString("F2") + "->" + m3.ToString("F2");
            else verdict = (m3 <= declared * 1.02 + 1e-6) ? "finite ok" : "*** UNDER-DECLARED " + m3.ToString("F4");
            Console.WriteLine(string.Format("  {0,-9} declared={1,-10} h1e-3={2,-11:F4} h1e-7={3,-11:F4} {4}", bev, Fmt(declared), m1, m3, verdict));
        }

        Console.WriteLine();
        Console.WriteLine("-- composed G, product rule HS-4.2: is the DECLARED composed bound an upper bound? --");
        int under = 0, tot = 0;
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float am in new float[] { 0.05f, 0.25f, 0.5f, 1f })
                    foreach (float cu in new float[] { 0.2f, 0.4f, 1f })
                        foreach (float ta in new float[] { 0.02f, 0.5f, 1f })
                        {
                            var op = Fixtures.Make(tech, bev, 4f, cu, ta, am, 3f, 1f, 45f);
                            float declared = ShaperHeight.ComposedSlopeBound(op);
                            if (float.IsPositiveInfinity(declared)) continue;
                            tot++;
                            double m = MaxQuotientG(op, 1e-6);
                            if (m > declared * 1.02 + 1e-6)
                            {
                                under++;
                                if (under <= 8) Console.WriteLine(string.Format("  *** UNDER-DECLARED {0}+{1} amount={2} curve={3} taper={4}: declared {5:F4}, measured {6:F4}",
                                    tech, bev, am, cu, ta, declared, m));
                            }
                        }
        Console.WriteLine("  finite composed declarations checked: " + tot + ", under-declared: " + under);
    }

    static string Fmt(float f) { return float.IsPositiveInfinity(f) ? "+inf" : f.ToString("F5"); }

    static double MaxQuotientE(in ShaperHeightOp op, double h)
    {
        double best = 0;
        int n = 20000;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            if (t + h > 1.0) break;
            double a = Ref.E(op.technique, t, op.n, op.curve, op.tau, 1.0);
            double b = Ref.E(op.technique, t + h, op.n, op.curve, op.tau, 1.0);
            double q = Math.Abs(b - a) / h;
            if (q > best) best = q;
        }
        // refine near t=0, where every divergence in HS-4.1 lives
        for (int i = 0; i < 4000; i++)
        {
            double t = i * h * 0.5;
            if (t + h > 1.0) break;
            double a = Ref.E(op.technique, t, op.n, op.curve, op.tau, 1.0);
            double b = Ref.E(op.technique, t + h, op.n, op.curve, op.tau, 1.0);
            double q = Math.Abs(b - a) / h;
            if (q > best) best = q;
        }
        return best;
    }

    static double MaxQuotientB(in ShaperHeightOp op, double h)
    {
        double best = 0; int n = 20000;
        for (int i = 0; i <= n; i++)
        {
            double u = i / (double)n; if (u + h > 1.0) break;
            double q = Math.Abs(Ref.B(op.bevel, u + h, op.bevelN) - Ref.B(op.bevel, u, op.bevelN)) / h;
            if (q > best) best = q;
        }
        // refine at 0, 0.5 and 1 — the three named singular loci
        foreach (double c in new double[] { 0.0, 0.5, 1.0 })
            for (int i = -3000; i <= 3000; i++)
            {
                double u = c + i * h * 0.5; if (u < 0 || u + h > 1.0) continue;
                double q = Math.Abs(Ref.B(op.bevel, u + h, op.bevelN) - Ref.B(op.bevel, u, op.bevelN)) / h;
                if (q > best) best = q;
            }
        return best;
    }

    static double MaxQuotientG(in ShaperHeightOp op, double h)
    {
        double best = 0; int n = 60000;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n; if (t + h > 1.0) break;
            double q = Math.Abs(Ref.G(op, t + h, 1.0) - Ref.G(op, t, 1.0)) / h;
            if (q > best) best = q;
        }
        return best;
    }

    // ── D: the ANALYTIC G' vs a central difference where G is differentiable ─────────────────────
    public static void Derivative()
    {
        Console.WriteLine();
        Console.WriteLine("=== D — HS-8.2's analytic G' vs a double central difference ===");
        int bad = 0, checks = 0; double worstRel = 0; string worstAt = "";
        int infWhereFinite = 0; string infAt = "";
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float am in new float[] { 0.25f, 0.6f })
                    foreach (float cu in new float[] { 0.3f, 1f, 3f })
                        foreach (float ta in new float[] { 0.3f, 1f })
                        {
                            var op = Fixtures.Make(tech, bev, 4f, cu, ta, am, 3f, 1f, 45f);
                            for (int i = 1; i < 400; i++)
                            {
                                double t = i / 400.0;
                                // skip points at/near a genuine kink or step
                                if (NearKink(op, t)) continue;
                                double hh = 1e-6;
                                double g1 = Ref.G(op, t + hh, 1.0), g0 = Ref.G(op, t - hh, 1.0);
                                double cd = (g1 - g0) / (2 * hh);
                                float an = ShaperHeight.ComposedDerivative(op, (float)t, 0f, 0f);
                                checks++;
                                if (float.IsPositiveInfinity(an))
                                {
                                    if (Math.Abs(cd) < 1e3) { infWhereFinite++; if (infAt == "") infAt = tech + "+" + bev + " t=" + t.ToString("F4") + " analytic=+inf, central diff=" + cd.ToString("F4") + " (amount=" + am + " curve=" + cu + ")"; }
                                    continue;
                                }
                                double err = Math.Abs(an - cd);
                                double tol = Math.Max(2e-3, 0.05 * Math.Abs(cd));
                                if (err > tol) { bad++; double rel = err / Math.Max(1e-9, Math.Abs(cd)); if (rel > worstRel) { worstRel = rel; worstAt = tech + "+" + bev + " t=" + t.ToString("F4") + " analytic=" + an + " cd=" + cd.ToString("F5") + " amount=" + am + " curve=" + cu + " taper=" + ta; } }
                            }
                        }
        Console.WriteLine("  checks=" + checks + " beyond max(2e-3, 5%)=" + bad + " worstRel=" + worstRel.ToString("F3") + "  " + worstAt);
        Console.WriteLine("  analytic said +inf where the central difference is finite: " + infWhereFinite + "  " + infAt);
    }

    static bool NearKink(in ShaperHeightOp op, double t)
    {
        double eps = 5e-4;
        if (op.bevel != ShaperBevelTechnique.None && op.a > 0)
        {
            if (Math.Abs(t - op.a) < eps) return true;
            double u = t / op.a;
            if (op.bevel == ShaperBevelTechnique.Stepped) { for (int k = 0; k <= op.bevelN; k++) if (Math.Abs(u - k / (double)op.bevelN) < eps / op.a) return true; }
            if (op.bevel == ShaperBevelTechnique.Ogee && Math.Abs(u - 0.5) < 0.02) return true;
            if (op.bevel == ShaperBevelTechnique.Rounded && u < 0.02) return true;
            if (op.bevel == ShaperBevelTechnique.Cove && u > 0.98) return true;
        }
        if (op.technique == ShaperExtrusionTechnique.Stepped) { for (int k = 0; k <= op.n; k++) if (Math.Abs(t - k / (double)op.n) < eps) return true; }
        if (op.technique == ShaperExtrusionTechnique.Taper && Math.Abs(t - op.taperT) < eps) return true;
        if ((op.technique == ShaperExtrusionTechnique.Round || op.technique == ShaperExtrusionTechnique.Dome) && t < 0.02) return true;
        return false;
    }

    // ── MO: monotonicity, targeted at exactly where a sampling argument is weakest ────────────────
    public static void Monotone()
    {
        Console.WriteLine();
        Console.WriteLine("=== MO — HS-5.1 monotonicity, probed AT breakpoints, clamps and t=0/1 exactly ===");
        int viol = 0; long pairs = 0; double worst = 0; string worstAt = "";
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float st in new float[] { 2f, 3f, 4f, 5f, 32f })
                    foreach (float bs in new float[] { 2f, 3f, 16f })
                        foreach (float cu in new float[] { 0f, 0.2f, 0.5f, 1f, 4f, 9f })
                            foreach (float ta in new float[] { 0f, 0.05f, 1f / 12f, 0.5f, 1f })
                                foreach (float am in new float[] { 0f, 1e-6f, 0.05f, 0.25f, 1f })
                                {
                                    var op = Fixtures.Make(tech, bev, st, cu, ta, am, bs, 1f, 45f);
                                    var ts = ProbeTs(op);
                                    float prev = ShaperHeight.Composed(op, ts[0], 0f, 0f);
                                    for (int i = 1; i < ts.Count; i++)
                                    {
                                        float g = ShaperHeight.Composed(op, ts[i], 0f, 0f);
                                        pairs++;
                                        if (g < prev)
                                        {
                                            viol++;
                                            double drop = prev - g;
                                            if (drop > worst) { worst = drop; worstAt = tech + "+" + bev + " t " + ts[i - 1] + "->" + ts[i] + " G " + prev + "->" + g + " (steps=" + st + " bsteps=" + bs + " curve=" + cu + " taper=" + ta + " amount=" + am + ")"; }
                                        }
                                        prev = g;
                                    }
                                }
        Console.WriteLine("  ordered pairs checked=" + pairs + "  violations=" + viol + "  worst drop=" + worst.ToString("E3"));
        if (worstAt != "") Console.WriteLine("  " + worstAt);

        // Linear: E does not depend on t. Check monotone in t for every (nx,ny) and every angle.
        Console.WriteLine("  -- Linear specifically (E constant in t, sup E > 1) --");
        int lv = 0; float maxSup = 0;
        foreach (float ang in new float[] { -180f, -135f, -90f, -45f, 0f, 45f, 90f, 135f, 180f })
            foreach (var bev in Fixtures.Bevels)
                foreach (float am in new float[] { 0.05f, 0.25f, 1f })
                {
                    var op = Fixtures.Make(ShaperExtrusionTechnique.Linear, bev, 4f, 1f, 1f, am, 3f, 1f, ang);
                    if (op.supE > maxSup) maxSup = op.supE;
                    for (int gx = -4; gx <= 4; gx++)
                        for (int gy = -4; gy <= 4; gy++)
                        {
                            float nx = gx / 4f, ny = gy / 4f;
                            var ts = ProbeTs(op);
                            float prev = ShaperHeight.Composed(op, ts[0], nx, ny);
                            for (int i = 1; i < ts.Count; i++)
                            {
                                float g = ShaperHeight.Composed(op, ts[i], nx, ny);
                                if (g < prev) lv++;
                                prev = g;
                            }
                            // sup E claim
                            float e = ShaperHeight.Profile(op, 0.5f, nx, ny);
                            if (e > op.supE + 1e-5f) { Console.WriteLine("  *** Linear E exceeds declared supE: " + e + " > " + op.supE + " at angle " + ang); }
                        }
                }
        Console.WriteLine("  Linear monotonicity violations=" + lv + "   max declared supE=" + maxSup + " (HS-2.3 says 1.84853)");
    }

    static List<float> ProbeTs(in ShaperHeightOp op)
    {
        var l = new List<float> { 0f };
        for (int i = 0; i <= 600; i++) l.Add(i / 600f);
        // every breakpoint of both stepped families, and the band edge, at +-1 ulp
        for (int k = 0; k <= op.n; k++) Around(l, k / (float)op.n);
        for (int k = 0; k <= op.bevelN; k++) Around(l, op.a * k / op.bevelN);
        Around(l, op.a); Around(l, op.taperT); Around(l, 0f); Around(l, 1f); Around(l, 0.5f * op.a);
        l.Sort();
        return l;
    }
    static void Around(List<float> l, float v)
    {
        if (v < 0f || v > 1f) return;
        l.Add(v);
        float dn = NextDown(v), up = NextUp(v);
        if (dn >= 0f) l.Add(dn);
        if (up <= 1f) l.Add(up);
        if (v - 1e-6f >= 0f) l.Add(v - 1e-6f);
        if (v + 1e-6f <= 1f) l.Add(v + 1e-6f);
    }
    static unsafe float NextUp(float f) { if (f == 0f) return float.Epsilon; int b = *(int*)&f; b += f > 0 ? 1 : -1; return *(float*)&b; }
    static unsafe float NextDown(float f) { if (f == 0f) return 0f; int b = *(int*)&f; b -= f > 0 ? 1 : -1; return *(float*)&b; }

    // ── FW: forward float evaluation vs the double reference — the D2 cancellation class ──────────
    public static void Forward()
    {
        Console.WriteLine();
        Console.WriteLine("=== FW — forward float evaluation of E and B vs the double reference ===");
        Console.WriteLine("Hunting the SAME catastrophic-cancellation class the implementer found in Dome's inverse.");
        foreach (var bev in Fixtures.Bevels)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 1f, 3f, 1f, 45f);
            double worstAbs = 0, worstRel = 0; string at = "", atRel = "";
            for (int e = 0; e <= 40; e++)
            {
                double u = Math.Pow(10.0, -e * 0.25);
                if (u > 1) continue;
                float got = ShaperHeight.Bevel(op, (float)u);      // a = 1 so t == u
                double want = Ref.B(bev, u, op.bevelN);
                double abs = Math.Abs(got - want);
                double rel = want > 0 ? abs / want : (abs > 0 ? 1e9 : 0);
                if (abs > worstAbs) { worstAbs = abs; at = "u=" + u.ToString("E2") + " got=" + got.ToString("E4") + " want=" + want.ToString("E4"); }
                if (rel > worstRel) { worstRel = rel; atRel = "u=" + u.ToString("E2") + " got=" + got.ToString("E4") + " want=" + want.ToString("E4"); }
            }
            // and near u=1
            for (int e = 1; e <= 30; e++)
            {
                double u = 1.0 - Math.Pow(10.0, -e * 0.25);
                float got = ShaperHeight.Bevel(op, (float)u);
                double want = Ref.B(bev, u, op.bevelN);
                double abs = Math.Abs(got - want);
                if (abs > worstAbs) { worstAbs = abs; at = "u=1-" + Math.Pow(10.0, -e * 0.25).ToString("E2") + " got=" + got.ToString("E4") + " want=" + want.ToString("E4"); }
            }
            Console.WriteLine(string.Format("  B {0,-9} worstAbs={1:E3} ({2})   worstRel={3:E2} ({4})", bev, worstAbs, at, worstRel, atRel));
        }
    }

    // ── W: HS-6.4's five mechanical wall statements ───────────────────────────────────────────────
    public static void Walls()
    {
        Console.WriteLine();
        Console.WriteLine("=== W — HS-6.4's five mechanical statements about side walls ===");
        foreach (var tech in Fixtures.Techs)
        {
            var op = Fixtures.Make(tech, ShaperBevelTechnique.None, 4f, 1f, 1f, 0.25f, 3f, 10f, 45f);
            Console.WriteLine(string.Format("  {0,-8} default params: WallHeight={1,-10} HasWall={2}", tech, ShaperHeight.WallHeight(op, 0f, 0f), ShaperHeight.HasWall(op, 0f, 0f)));
        }
        Console.WriteLine("  -- 'any bevel other than None and Stepped removes the wall entirely' --");
        foreach (var bev in Fixtures.Bevels)
        {
            bool anyWall = false; float steppedWall = -1;
            foreach (var tech in Fixtures.Techs)
            {
                var op = Fixtures.Make(tech, bev, 4f, 1f, 1f, 0.25f, 3f, 10f, 45f);
                float w = ShaperHeight.WallHeight(op, 0f, 0f);
                if (w > 0f) anyWall = true;
                if (bev == ShaperBevelTechnique.Stepped && tech == ShaperExtrusionTechnique.Flat) steppedWall = w;
            }
            Console.WriteLine(string.Format("  bevel {0,-9} any profile keeps a wall? {1}{2}", bev, anyWall,
                bev == ShaperBevelTechnique.Stepped ? "   Flat wall = " + steppedWall + " (HS-6.4 predicts body*E(0)/m = 10/3 = 3.3333)" : ""));
        }
    }

    // ── N: the normal — unit, finite, never zero, never NaN, never unwritten ──────────────────────
    public static void Normals()
    {
        Console.WriteLine();
        Console.WriteLine("=== N — the Profile normal: unit to 1e-4, finite, non-zero, never NaN ===");
        var root = March.SolidPlate(120f, 90f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int W = 200, H = 160;
        var grid = ShaperSampleGrid.Centred(W, H, 1.5f, 0f);
        var normal = new float[W * H * 3];
        long nan = 0, notUnit = 0, zero = 0, unwritten = 0; double worstUnit = 0; string worstAt = "";
        int cases = 0;
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float cu in new float[] { 0.2f, 1f, 4f })
                {
                    var def = new ShaperHeightDef { technique = tech, bevel = bev, depth = new ZUIValue(30f), curve = new ZUIValue(cu), bevelAmount = new ZUIValue(0.3f) };
                    var hop = ShaperHeightCompiler.Compile(def, prog, 1.5f, 0f, 0f, 0u);
                    var nop = ShaperNormalOp.Default;
                    nop.kind = ShaperNormalKind.Profile;
                    nop.height = hop;
                    cases++;
                    for (int i = 0; i < normal.Length; i++) normal[i] = float.NaN;   // detect "unwritten"
                    ShaperNormals.FillTile(nop, grid, 0, 0, W, H, null, null, normal, 0, W, 0, W, prog, stack);
                    for (int p = 0; p < W * H; p++)
                    {
                        float x = normal[p * 3], y = normal[p * 3 + 1], z = normal[p * 3 + 2];
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) { nan++; unwritten++; continue; }
                        if (float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) { nan++; continue; }
                        double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                        if (len < 1e-9) { zero++; continue; }
                        double err = Math.Abs(len - 1.0);
                        if (err > 1e-4) { notUnit++; if (err > worstUnit) { worstUnit = err; worstAt = tech + "+" + bev + " curve=" + cu + " len=" + len.ToString("R"); } }
                    }
                }
        Console.WriteLine("  configurations=" + cases + "  samples=" + ((long)cases * W * H));
        Console.WriteLine("  NaN/Inf=" + nan + "  unwritten=" + unwritten + "  zero=" + zero + "  |len-1|>1e-4 = " + notUnit + "  worst=" + worstUnit.ToString("E3") + " " + worstAt);
    }

    // ── AL: allocation on the hot path ────────────────────────────────────────────────────────────
    public static void Alloc()
    {
        Console.WriteLine();
        Console.WriteLine("=== AL — allocation on the per-sample hot path ===");
        var root = March.SolidPlate(120f, 90f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int W = 128, H = 128;
        var grid = ShaperSampleGrid.Centred(W, H, 1f, 0f);
        var dist = new float[W * H];
        var hbuf = new float[W * H];
        var nbuf = new float[W * H * 3];
        var cov = new float[W * H];
        ShaperEvaluator.FillTile(prog, grid, 0, 0, W, H, dist, cov, 0, W, stack);
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Dome, bevel = ShaperBevelTechnique.Ogee, depth = new ZUIValue(30f) };
        var hop = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        var nop = ShaperNormalOp.Default; nop.kind = ShaperNormalKind.Profile; nop.height = hop;

        // warm up
        ShaperHeight.FillTile(hop, grid, 0, 0, W, H, dist, hbuf, 0, W, 0, W);
        ShaperNormals.FillTile(nop, grid, 0, 0, W, H, null, null, nbuf, 0, W, 0, W, prog, stack);

        Measure("ShaperHeight.FillTile      ", () => ShaperHeight.FillTile(hop, grid, 0, 0, W, H, dist, hbuf, 0, W, 0, W));
        Measure("ShaperNormals.FillTile(Prof)", () => ShaperNormals.FillTile(nop, grid, 0, 0, W, H, null, null, nbuf, 0, W, 0, W, prog, stack));

        // the resolve hot path
        var scene = new ShaperResolveScene(); scene.Add(prog, hop);
        var buf = new ShaperCrossing[32];
        ShaperResolve.Query(scene, 0f, 0f, 200f, 0f, 0f, -1f, buf);
        ShaperResolve.Query(scene, 0f, 0f, 200f, 0.4f, 0.2f, -1f, buf);
        Measure("ShaperResolve.Query down   ", () => ShaperResolve.Query(scene, 3f, 5f, 200f, 0f, 0f, -1f, buf));
        Measure("ShaperResolve.Query general", () => ShaperResolve.Query(scene, 3f, 5f, 200f, 0.4f, 0.2f, -1f, buf));
    }

    static void Measure(string name, Action a)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        int g0 = GC.CollectionCount(0);
        for (int i = 0; i < 50; i++) a();
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        int g1 = GC.CollectionCount(0);
        Console.WriteLine(string.Format("  {0}  bytes/call = {1}   gen0 collections = {2}", name, (b1 - b0) / 50.0, g1 - g0));
    }

    // ── BP: HS-5.5's slab breakpoints, and whether the "stepped is exact" claim holds ─────────────
    public static void Breakpoints()
    {
        Console.WriteLine();
        Console.WriteLine("=== BP — HS-5.5 breakpoints and the resStep the marcher derives from them ===");
        var dst = new float[ShaperHeight.MaxBreakpoints];
        foreach (var tech in new[] { ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Stepped, ShaperExtrusionTechnique.Dome })
            foreach (var bev in new[] { ShaperBevelTechnique.None, ShaperBevelTechnique.Stepped, ShaperBevelTechnique.Cove })
            {
                var op = Fixtures.Make(tech, bev, 32f, 1f, 1f, 0.25f, 16f, 40f, 45f);
                int n = ShaperHeight.Breakpoints(op, dst);
                Console.WriteLine(string.Format("  {0,-8} + {1,-8} steps=32 bsteps=16 -> {2,2} breakpoints ({3} slabs)", tech, bev, n, n - 1));
            }
        Console.WriteLine("  NOTE: Flat/None gets ONE slab, so its resStep is the ray's WHOLE clipped length / 8.");
    }
}
