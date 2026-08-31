using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// ============================================================================================
// INDEPENDENT VERIFIER instrument for T-0109. Runs the REAL Runtime/Shaper code (compiled from
// D:\UNITY\Laubrary Dev - Shaper) against a double-precision reference of the SPEC's formulas
// (HEIGHT-SPEC.md HS-2.2 / HS-3.2), derived here from the spec text, not from the implementation.
// ============================================================================================
public static class Ref
{
    // Reference E(t) in DOUBLE, straight from HS-2.2's table.
    public static double E(ShaperExtrusionTechnique tech, double t, double n, double c, double tau, double linConst)
    {
        switch (tech)
        {
            case ShaperExtrusionTechnique.Flat: return 1.0;
            case ShaperExtrusionTechnique.Linear: return linConst;
            case ShaperExtrusionTechnique.Stepped: return Math.Min(1.0, Math.Floor(t * n) / (n - 1.0));
            case ShaperExtrusionTechnique.Dome: return Math.Pow(t * (2.0 - t), 1.0 / (2.0 * c));
            case ShaperExtrusionTechnique.Round: return t <= 0 ? 0 : Math.Pow(t, 0.5 / c);
            case ShaperExtrusionTechnique.Taper: return Math.Min(1.0, t / Math.Max(0.05, 0.6 * tau));
            case ShaperExtrusionTechnique.Pyramid: return 1.0 - tau + tau * t;
        }
        return 1.0;
    }

    // Reference B(u) in DOUBLE, straight from HS-3.2's table.
    public static double B(ShaperBevelTechnique bev, double u, double m)
    {
        switch (bev)
        {
            case ShaperBevelTechnique.None: return 1.0;
            case ShaperBevelTechnique.Linear: return u;
            case ShaperBevelTechnique.Rounded: return Math.Sqrt(u * (2.0 - u));
            case ShaperBevelTechnique.Cove: return 1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - u * u));
            case ShaperBevelTechnique.Ogee:
                if (u < 0.5) return 0.5 * (1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - 4.0 * u * u)));
                else { double w = 1.0 - u; return 0.5 + 0.5 * Math.Sqrt(Math.Max(0.0, 1.0 - 4.0 * w * w)); }
            case ShaperBevelTechnique.Stepped: return Math.Min(1.0, (Math.Floor(u * m) + 1.0) / m);
        }
        return 1.0;
    }

    public static double G(in ShaperHeightOp op, double t, double linConst)
    {
        double e = E(op.technique, t, op.n, op.curve, op.tau, linConst);
        double b = 1.0;
        if (op.bevel != ShaperBevelTechnique.None && op.a > 0.0 && t < op.a)
        {
            double u = t / op.a; if (u < 0) u = 0; if (u > 1) u = 1;
            b = B(op.bevel, u, op.bevelN);
        }
        return e * b;
    }

    // The EXACT infimum { t in [0,1] : G(t) >= zeta }, by 200-iteration bisection in double on the
    // monotone reference G. Returns +inf when no t reaches zeta.
    public static double Inf(in ShaperHeightOp op, double zeta, double linConst)
    {
        if (G(op, 0.0, linConst) >= zeta) return 0.0;
        if (G(op, 1.0, linConst) < zeta) return double.PositiveInfinity;
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (mid <= lo || mid >= hi) break;
            if (G(op, mid, linConst) >= zeta) hi = mid; else lo = mid;
        }
        return hi;
    }
}

public static class Fixtures
{
    public static ShaperHeightOp Make(ShaperExtrusionTechnique tech, ShaperBevelTechnique bev,
                                      float steps, float curve, float taper, float amount, float bevelSteps,
                                      float depth, float angle)
    {
        var def = new ShaperHeightDef();
        def.technique = tech; def.bevel = bev;
        def.steps = new ZUIValue(steps); def.curve = new ZUIValue(curve);
        def.taper = new ZUIValue(taper); def.bevelAmount = new ZUIValue(amount);
        def.bevelSteps = new ZUIValue(bevelSteps); def.depth = new ZUIValue(depth);
        def.angle = new ZUIValue(angle);
        return ShaperHeightCompiler.Compile(def, null, 1f, 0f, 0f, 0u);
    }

    public static ShaperExtrusionTechnique[] Techs = {
        ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Linear, ShaperExtrusionTechnique.Stepped,
        ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round, ShaperExtrusionTechnique.Taper,
        ShaperExtrusionTechnique.Pyramid };
    public static ShaperBevelTechnique[] Bevels = {
        ShaperBevelTechnique.None, ShaperBevelTechnique.Linear, ShaperBevelTechnique.Rounded,
        ShaperBevelTechnique.Cove, ShaperBevelTechnique.Ogee, ShaperBevelTechnique.Stepped };
}

class Main1
{
    public static void Main(string[] args)
    {
        string which = args.Length > 0 ? args[0] : "all";
        if (which == "all" || which == "A") TestA_InverseConservative();
        if (which == "all" || which == "A2") TestA2_ReachRoundTrip();
        if (which == "all" || which == "M") March.Run();
        if (which == "all" || which == "M2") March2.Run();
        if (which == "all" || which == "S") Tests.Slopes();
        if (which == "all" || which == "D") Tests.Derivative();
        if (which == "all" || which == "MO") Tests.Monotone();
        if (which == "all" || which == "FW") Tests.Forward();
        if (which == "all" || which == "W") Tests.Walls();
        if (which == "all" || which == "N") Tests.Normals();
        if (which == "all" || which == "AL") Tests.Alloc();
        if (which == "all" || which == "BP") Tests.Breakpoints();
        if (which == "all" || which == "DP") Tests2.DepthCorruption();
        if (which == "all" || which == "TR") Tests2.Truncation();
        if (which == "all" || which == "Z") Tests2.ZOffset();
        if (which == "all" || which == "INJ") { Tests2.InjectH9(); Tests2.InjectH4(); }
        if (which == "all" || which == "ML") Tests2.MultiLayer();
        if (which == "all" || which == "WK") W2.Run();
        if (which == "all" || which == "ILB") ILB.Run();
        if (which == "all" || which == "H6") March.RunAgreement();
        if (which == "F2") F2.Run();
        if (which == "F1") F1.Run();
        if (which == "F3") F3.Run();
        if (which == "IUB") IUB.Run();
    }

    // ── TEST A: the ONE unsafe direction. Ginv must NEVER over-estimate the true infimum. ──────────
    static void TestA_InverseConservative()
    {
        Console.WriteLine("=== TEST A — Ginv over-estimate hunt (the UNSAFE direction) ===");
        Console.WriteLine("An over-large Ginv shrinks the containing prism below the true cross-section.");
        Console.WriteLine("Reported as t-units (fraction of span). span=1px here, so t-units == canvas px.");
        float[] curves = { 0.2f, 0.2001f, 0.5f, 0.5001f, 0.7f, 1f, 2f, 3.9999f, 4f, 0f, -1f, 8f };
        float[] tapers = { 0f, 0.0001f, 0.05f, 1f / 12f, 0.0833334f, 0.1f, 0.5f, 0.9999f, 1f };
        float[] amounts = { 0.0001f, 0.05f, 0.25f, 0.5f, 0.75f, 0.9999f, 1f };
        float[] stepsA = { 2f, 3f, 4f, 7f, 16f, 32f };
        float[] bstepsA = { 2f, 3f, 5f, 16f };
        float[] zetas = BuildZetas();

        int cases = 0; long checks = 0;
        var worst = new SortedDictionary<string, double>();
        var worstAt = new Dictionary<string, string>();

        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
            {
                foreach (float st in stepsA)
                    foreach (float cu in curves)
                        foreach (float ta in tapers)
                            foreach (float am in amounts)
                                foreach (float bs in bstepsA)
                                {
                                    // prune irrelevant parameter axes to keep the sweep honest but finite
                                    if (tech != ShaperExtrusionTechnique.Stepped && st != 4f) continue;
                                    if (tech != ShaperExtrusionTechnique.Dome && tech != ShaperExtrusionTechnique.Round && cu != 1f) continue;
                                    if (tech != ShaperExtrusionTechnique.Taper && tech != ShaperExtrusionTechnique.Pyramid && ta != 1f) continue;
                                    if (bev == ShaperBevelTechnique.None && (am != 0.25f || bs != 3f)) continue;
                                    if (bev != ShaperBevelTechnique.Stepped && bs != 3f) continue;

                                    var op = Fixtures.Make(tech, bev, st, cu, ta, am, bs, 10f, 45f);
                                    cases++;
                                    double linConst = tech == ShaperExtrusionTechnique.Linear
                                        ? ShaperHeight.Profile(op, 0f, 0f, 0f) : 1.0;

                                    foreach (float z in zetas)
                                    {
                                        if (z <= 0f) continue;
                                        checks++;
                                        float got = ShaperHeight.Inverse(op, z, 0f, 0f);
                                        double truth = Ref.Inf(op, z, linConst);
                                        if (float.IsPositiveInfinity(got))
                                        {
                                            if (!double.IsPositiveInfinity(truth))
                                            {
                                                // claimed "no cross-section" where one exists: the marcher
                                                // SKIPS the slab entirely -> a whole missing solid.
                                                Record(worst, worstAt, tech + "+" + bev + " FALSE-NOCROSS", 1e9,
                                                       string.Format("zeta={0:R} truthInf={1:R} params st={2} cu={3} ta={4} am={5} bs={6}", z, truth, st, cu, ta, am, bs));
                                            }
                                            continue;
                                        }
                                        if (double.IsPositiveInfinity(truth)) continue;   // impl found one, ref didn't: safe
                                        double over = got - truth;
                                        if (over > 0)
                                            Record(worst, worstAt, tech + "+" + bev, over,
                                                   string.Format("zeta={0:R} got={1:R} trueInf={2:R} over={3:E3} params st={4} cu={5} ta={6} am={7} bs={8}", z, got, truth, over, st, cu, ta, am, bs));
                                    }
                                }
            }

        Console.WriteLine("cases=" + cases + " checks=" + checks);
        Console.WriteLine("-- combinations where Ginv OVER-reported (unsafe direction), worst first --");
        var list = new List<KeyValuePair<string, double>>(worst);
        list.Sort((p, q) => q.Value.CompareTo(p.Value));
        int shown = 0;
        foreach (var kv in list)
        {
            if (kv.Value <= 1e-7) continue;
            Console.WriteLine(string.Format("  {0,-24} maxOver={1:E4}   {2}", kv.Key, kv.Value, worstAt[kv.Key]));
            if (++shown > 25) { Console.WriteLine("  ..."); break; }
        }
        if (shown == 0) Console.WriteLine("  (none above 1e-7)");
        Console.WriteLine("-- all combinations, max over-report (incl. tiny) --");
        foreach (var kv in list) Console.WriteLine(string.Format("  {0,-24} {1:E3}", kv.Key, kv.Value));
    }

    // ── TEST A2: the reach half. G(Ginv(z)) >= z. A shortfall is D2's failure mode. ────────────────
    static void TestA2_ReachRoundTrip()
    {
        Console.WriteLine();
        Console.WriteLine("=== TEST A2 — reach: does G(Ginv(zeta)) actually reach zeta? ===");
        float[] zetas = BuildZetas();
        float[] curves = { 0.2f, 0.5f, 1f, 2f, 4f };
        float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };
        var worst = new SortedDictionary<string, double>();
        var worstAt = new Dictionary<string, string>();
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float cu in curves)
                    foreach (float am in amounts)
                    {
                        if (tech != ShaperExtrusionTechnique.Dome && tech != ShaperExtrusionTechnique.Round && cu != 1f) continue;
                        if (bev == ShaperBevelTechnique.None && am != 0.25f) continue;
                        var op = Fixtures.Make(tech, bev, 4f, cu, 1f, am, 3f, 10f, 45f);
                        foreach (float z in zetas)
                        {
                            if (z <= 0f) continue;
                            float got = ShaperHeight.Inverse(op, z, 0f, 0f);
                            if (float.IsPositiveInfinity(got)) continue;
                            float back = ShaperHeight.Composed(op, got, 0f, 0f);
                            double shortfall = (double)z - back;
                            if (shortfall > 0)
                                Record(worst, worstAt, tech + "+" + bev, shortfall,
                                       string.Format("zeta={0:R} Ginv={1:R} G(Ginv)={2:R} short={3:E3} curve={4} amount={5}", z, got, back, shortfall, cu, am));
                        }
                    }
        var list = new List<KeyValuePair<string, double>>(worst);
        list.Sort((p, q) => q.Value.CompareTo(p.Value));
        int shown = 0;
        foreach (var kv in list)
        {
            Console.WriteLine(string.Format("  {0,-24} maxShortfall={1:E4}   {2}", kv.Key, kv.Value, worstAt[kv.Key]));
            if (++shown > 20) break;
        }
        if (shown == 0) Console.WriteLine("  (no shortfall anywhere)");
    }

    static float[] BuildZetas()
    {
        var l = new List<float>();
        for (int i = 1; i <= 200; i++) l.Add(i / 200f);
        double[] tiny = { 1e-1, 5e-2, 1e-2, 5e-3, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9, 1e-12, 1e-20, 1e-30 };
        foreach (var t in tiny) { l.Add((float)t); l.Add((float)(1.0 - t)); }
        // exact breakpoints of both stepped families and the ogee inflection
        for (int m = 2; m <= 16; m++) for (int k = 0; k <= m; k++) { l.Add(k / (float)m); l.Add(k / (float)m - 1e-6f); l.Add(k / (float)m + 1e-6f); }
        for (int n = 2; n <= 32; n++) for (int k = 0; k <= n - 1; k++) { l.Add(k / (float)(n - 1)); l.Add(k / (float)(n - 1) - 1e-6f); l.Add(k / (float)(n - 1) + 1e-6f); }
        l.Add(0.5f); l.Add(0.5f - 1e-6f); l.Add(0.5f + 1e-6f);
        l.Add(1f); l.Add(1f - 1e-7f);
        var arr = l.ToArray();
        return arr;
    }

    static void Record(SortedDictionary<string, double> worst, Dictionary<string, string> at, string key, double v, string desc)
    {
        double cur;
        if (!worst.TryGetValue(key, out cur) || v > cur) { worst[key] = v; at[key] = desc; }
    }
}
