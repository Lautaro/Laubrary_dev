using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// V2 — an EXHAUSTIVE sweep of every closed form in ShaperHeight.cs against a cancellation-free
// double reference. The first pass named four; the fixer found three more; this asks whether an
// eighth is left, and checks the fixed six are algebraically identical AND numerically better.
public static class V2Cancel
{
    // ── double references, written from the spec's tables, not from the implementation ─────────
    static double E(ShaperExtrusionTechnique tech, double t, double n, double c, double tau)
    {
        switch (tech)
        {
            case ShaperExtrusionTechnique.Flat: return 1.0;
            case ShaperExtrusionTechnique.Linear: return 1.0;              // position term handled separately
            case ShaperExtrusionTechnique.Stepped: return Math.Min(1.0, Math.Floor(t * n) / (n - 1.0));
            case ShaperExtrusionTechnique.Dome: return Math.Pow(t * (2.0 - t), 1.0 / (2.0 * c));
            case ShaperExtrusionTechnique.Round: return t <= 0 ? 0 : Math.Pow(t, 0.5 / c);
            case ShaperExtrusionTechnique.Taper: return Math.Min(1.0, t / Math.Max(0.05, 0.6 * tau));
            case ShaperExtrusionTechnique.Pyramid: return 1.0 - tau + tau * t;
        }
        return 1.0;
    }

    static double Ep(ShaperExtrusionTechnique tech, double t, double n, double c, double tau)
    {
        switch (tech)
        {
            case ShaperExtrusionTechnique.Dome:
            {
                double m = 1.0 / (2.0 * c), q = t * (2.0 - t);
                if (q <= 0) return m < 1 ? double.PositiveInfinity : (m > 1 ? 0 : 2.0 * (1 - t));
                return 2.0 * m * (1 - t) * Math.Pow(q, m - 1.0);
            }
            case ShaperExtrusionTechnique.Round:
            {
                double p = 0.5 / c;
                if (t <= 0) return p < 1 ? double.PositiveInfinity : (p > 1 ? 0 : 1);
                return p * Math.Pow(t, p - 1.0);
            }
            case ShaperExtrusionTechnique.Taper: { double T = Math.Max(0.05, 0.6 * tau); return t < T ? 1.0 / T : 0.0; }
            case ShaperExtrusionTechnique.Pyramid: return tau;
        }
        return 0.0;
    }

    static double B(ShaperBevelTechnique bev, double u, double m)
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

    static double Bp(ShaperBevelTechnique bev, double u, double m)
    {
        switch (bev)
        {
            case ShaperBevelTechnique.Linear: return 1.0;
            case ShaperBevelTechnique.Rounded: { double q = u * (2 - u); return q <= 0 ? double.PositiveInfinity : (1 - u) / Math.Sqrt(q); }
            case ShaperBevelTechnique.Cove: { double q = 1 - u * u; return q <= 0 ? double.PositiveInfinity : u / Math.Sqrt(q); }
            case ShaperBevelTechnique.Ogee:
                if (u < 0.5) { double q = 1 - 4 * u * u; return q <= 0 ? double.PositiveInfinity : 2 * u / Math.Sqrt(q); }
                else { double w = 1 - u, q = 1 - 4 * w * w; return q <= 0 ? double.PositiveInfinity : 2 * w / Math.Sqrt(q); }
        }
        return 0.0;
    }

    // B^-1(zeta) in u, in double
    static double Binv(ShaperBevelTechnique bev, double z, double m)
    {
        switch (bev)
        {
            case ShaperBevelTechnique.Linear: return Math.Min(1.0, z);
            case ShaperBevelTechnique.Rounded: return z >= 1 ? 1 : 1.0 - Math.Sqrt(1.0 - z * z);
            case ShaperBevelTechnique.Cove: return z >= 1 ? 1 : Math.Sqrt(1.0 - (1.0 - z) * (1.0 - z));
            case ShaperBevelTechnique.Stepped:
            {
                if (z > 1) return 1;
                double k = Math.Ceiling(z * m - 1.0);
                if (k < 0) k = 0; if (k >= m) return 1;
                return k / m;
            }
        }
        return -1;
    }

    static double Einv(ShaperExtrusionTechnique tech, double z, double n, double c, double tau)
    {
        switch (tech)
        {
            case ShaperExtrusionTechnique.Flat: return 0;
            case ShaperExtrusionTechnique.Linear: return 0;
            case ShaperExtrusionTechnique.Stepped: { double k = Math.Ceiling(z * (n - 1)); if (k < 0) k = 0; return k / n; }
            case ShaperExtrusionTechnique.Dome: { double w = Math.Pow(z, 2.0 * c); double r = 1 - w; return r <= 0 ? 1 : 1.0 - Math.Sqrt(r); }
            case ShaperExtrusionTechnique.Round: return Math.Pow(z, 2.0 * c);
            case ShaperExtrusionTechnique.Taper: return z * Math.Max(0.05, 0.6 * tau);
            case ShaperExtrusionTechnique.Pyramid: return tau <= 0 ? 0 : (z - 1 + tau) / tau;
        }
        return 0;
    }

    static double Rel(double got, double want)
    {
        if (double.IsNaN(got) || double.IsNaN(want)) return double.NaN;
        if (double.IsInfinity(want)) return double.IsInfinity(got) ? 0 : 1e30;
        if (double.IsInfinity(got)) return 1e30;
        if (want == 0) return got == 0 ? 0 : 1e30;
        return Math.Abs(got - want) / Math.Abs(want);
    }

    static List<double> Grid()
    {
        var g = new List<double>();
        for (int i = 0; i <= 400; i++) g.Add(i / 400.0);
        for (int e = 1; e <= 44; e++) { g.Add(Math.Pow(10, -e)); g.Add(3.0 * Math.Pow(10, -e)); g.Add(1.0 - Math.Pow(10, -Math.Min(e, 7))); }
        double[] special = { 0.5, 0.25, 0.125, 1.0 / 3, 2.0 / 3, 1.0 / 6, 5.0 / 6, 0.999999, 0.9999999, 1.0 };
        foreach (double s in special)
        {
            g.Add(s);
            for (int k = 1; k <= 4; k++) { g.Add(V2.NextUp((float)s, k)); g.Add(NextDown((float)s, k)); }
        }
        var outl = new List<double>();
        var seen = new HashSet<float>();
        foreach (double d in g) { float f = (float)d; if (f >= 0f && f <= 1f && seen.Add(f)) outl.Add((double)f); }
        outl.Sort();
        return outl;
    }

    static float NextDown(float x, int n)
    {
        for (int i = 0; i < n; i++) { int b = BitConverter.SingleToInt32Bits(x); x = BitConverter.Int32BitsToSingle(x > 0 ? b - 1 : b + 1); }
        return x;
    }

    public static void Run()
    {
        Console.WriteLine("=== V2-CANC - every closed form in ShaperHeight.cs vs a cancellation-free double reference ===");
        Console.WriteLine("Grid: 401 uniform + log-spaced to 1e-44 + every breakpoint +/- 4 ulps. " );
        var grid = Grid();
        Console.WriteLine("grid points = " + grid.Count);
        Console.WriteLine();

        float[] curves = { 0.2f, 0.5f, 1f, 2f, 4f };
        float[] tapers = { 0f, 0.05f, 0.5f, 1f };
        float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };

        // ── forward Bevel B(u) ────────────────────────────────────────────────────────────────
        Console.WriteLine("-- forward Bevel B(u), a = 1 so t == u --");
        Console.WriteLine(string.Format("{0,-9} {1,14} {2,14} {3}", "bevel", "worst rel err", "worst abs err", "at"));
        foreach (var bev in Fixtures.Bevels)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 1f, 3f, 1f, 45f);
            double wr = 0, wa = 0; string at = "";
            foreach (double u in grid)
            {
                float g = ShaperHeight.Bevel(op, (float)u);
                double w = B(bev, u, op.bevelN);
                double r = Rel(g, w);
                if (r > wr) { wr = r; at = "u=" + u.ToString("E3") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
                if (Math.Abs(g - w) > wa) wa = Math.Abs(g - w);
            }
            Console.WriteLine(string.Format("{0,-9} {1,14:E3} {2,14:E3} {3}", bev, wr, wa, at));
        }
        Console.WriteLine();

        // ── forward Profile E(t) ──────────────────────────────────────────────────────────────
        Console.WriteLine("-- forward Profile E(t) --");
        Console.WriteLine(string.Format("{0,-9} {1,7} {2,14} {3}", "tech", "param", "worst rel err", "at"));
        foreach (var tech in Fixtures.Techs)
        {
            if (tech == ShaperExtrusionTechnique.Linear) continue;
            foreach (float c in curves) foreach (float ta in tapers) foreach (float n in new float[] { 2f, 4f, 32f })
            {
                if (tech != ShaperExtrusionTechnique.Dome && tech != ShaperExtrusionTechnique.Round && c != 1f) continue;
                if (tech != ShaperExtrusionTechnique.Taper && tech != ShaperExtrusionTechnique.Pyramid && ta != 1f) continue;
                if (tech != ShaperExtrusionTechnique.Stepped && n != 4f) continue;
                var op = Fixtures.Make(tech, ShaperBevelTechnique.None, n, c, ta, 0.25f, 3f, 1f, 45f);
                double wr = 0; string at = "";
                foreach (double t in grid)
                {
                    float g = ShaperHeight.Profile(op, (float)t, 0f, 0f);
                    double w = E(tech, t, op.n, op.curve, op.tau);
                    double r = Rel(g, w);
                    if (r > wr && r < 1e29) { wr = r; at = "t=" + t.ToString("E3") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
                }
                if (wr > 1e-5)
                    Console.WriteLine(string.Format("{0,-9} c={1} ta={2} n={3} {4,14:E3} {5}", tech, c, ta, n, wr, at));
            }
        }
        Console.WriteLine("  (only rows with rel err > 1e-5 printed)");
        Console.WriteLine();

        // ── derivatives ───────────────────────────────────────────────────────────────────────
        Console.WriteLine("-- BevelDerivative B'(u), a = 1 --");
        foreach (var bev in Fixtures.Bevels)
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 1f, 3f, 1f, 45f);
            double wr = 0; string at = "";
            foreach (double u in grid)
            {
                if (u >= 1) continue;
                float g = ShaperHeight.BevelDerivative(op, (float)u);
                double w = Bp(bev, u, op.bevelN);
                double r = Rel(g, w);
                if (r > wr) { wr = r; at = "u=" + u.ToString("E4") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
            }
            Console.WriteLine(string.Format("{0,-9} {1,14:E3}  {2}", bev, wr, at));
        }
        Console.WriteLine();

        Console.WriteLine("-- ProfileDerivative E'(t) --");
        foreach (var tech in Fixtures.Techs)
        {
            foreach (float c in new float[] { 0.2f, 0.5f, 1f, 4f })
            {
                if (tech != ShaperExtrusionTechnique.Dome && tech != ShaperExtrusionTechnique.Round && c != 1f) continue;
                var op = Fixtures.Make(tech, ShaperBevelTechnique.None, 4f, c, 1f, 0.25f, 3f, 1f, 45f);
                double wr = 0; string at = "";
                foreach (double t in grid)
                {
                    float g = ShaperHeight.ProfileDerivative(op, (float)t);
                    double w = Ep(tech, t, op.n, op.curve, op.tau);
                    double r = Rel(g, w);
                    if (r > wr && r < 1e29) { wr = r; at = "t=" + t.ToString("E4") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
                }
                if (wr > 1e-5) Console.WriteLine(string.Format("{0,-9} c={1,-5} {2,14:E3}  {3}", tech, c, wr, at));
            }
        }
        Console.WriteLine("  (only rows with rel err > 1e-5 printed)");
        Console.WriteLine();

        // ── inverses ──────────────────────────────────────────────────────────────────────────
        Console.WriteLine("-- BevelInverseU B^-1(zeta) --");
        foreach (var bev in Fixtures.Bevels)
        {
            if (bev == ShaperBevelTechnique.Ogee || bev == ShaperBevelTechnique.None) continue;
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 1f, 3f, 1f, 45f);
            double wr = 0; string at = "";
            foreach (double z in grid)
            {
                if (z <= 0) continue;
                float g = ShaperHeight.BevelInverseU(op, (float)z);
                double w = Binv(bev, z, op.bevelN);
                double r = Rel(g, w);
                if (r > wr) { wr = r; at = "zeta=" + z.ToString("E4") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
            }
            Console.WriteLine(string.Format("{0,-9} {1,14:E3}  {2}", bev, wr, at));
        }
        Console.WriteLine();

        Console.WriteLine("-- ProfileInverse E^-1(zeta) --");
        foreach (var tech in Fixtures.Techs)
        {
            foreach (float c in new float[] { 0.2f, 0.5f, 1f, 4f })
                foreach (float ta in new float[] { 0.05f, 0.5f, 1f })
                    foreach (float n in new float[] { 2f, 4f, 32f })
                    {
                        if (tech != ShaperExtrusionTechnique.Dome && tech != ShaperExtrusionTechnique.Round && c != 1f) continue;
                        if (tech != ShaperExtrusionTechnique.Taper && tech != ShaperExtrusionTechnique.Pyramid && ta != 1f) continue;
                        if (tech != ShaperExtrusionTechnique.Stepped && n != 4f) continue;
                        var op = Fixtures.Make(tech, ShaperBevelTechnique.None, n, c, ta, 0.25f, 3f, 1f, 45f);
                        double wr = 0; string at = "";
                        foreach (double z in grid)
                        {
                            if (z <= 0) continue;
                            float g = ShaperHeight.ProfileInverse(op, (float)z, 0f, 0f);
                            if (ShaperHeight.IsNoCrossSection(g)) continue;
                            double w = Einv(tech, z, op.n, op.curve, op.tau);
                            if (w < 0) w = 0; if (w > 1) w = 1;
                            double r = Rel(g, w);
                            if (r > wr && r < 1e29) { wr = r; at = "zeta=" + z.ToString("E4") + " got=" + g.ToString("E5") + " want=" + w.ToString("E5"); }
                        }
                        if (wr > 1e-5) Console.WriteLine(string.Format("{0,-9} c={1,-5} ta={2,-5} n={3,-4} {4,14:E3}  {5}", tech, c, ta, n, wr, at));
                    }
        }
        Console.WriteLine("  (only rows with rel err > 1e-5 printed)");
        Console.WriteLine();

        // ── ROUND TRIP: G(Ginv(zeta)) >= zeta, all 42, log grid to 1e-30 ─────────────────────
        Console.WriteLine("-- round trip G(Ginv(zeta)) >= zeta, all 42 combinations, log grid to 1e-30 --");
        double worstShort = 0; string wsAt = ""; long rt = 0;
        foreach (var tech in Fixtures.Techs)
            foreach (var bev in Fixtures.Bevels)
                foreach (float am in amounts)
                {
                    var op = Fixtures.Make(tech, bev, 4f, 1f, 1f, am, 3f, 1f, 45f);
                    foreach (double z in grid)
                    {
                        if (z <= 0 || z > op.supG) continue;
                        float g = ShaperHeight.Inverse(op, (float)z, 0f, 0f);
                        if (ShaperHeight.IsNoCrossSection(g)) continue;
                        rt++;
                        double reach = ShaperHeight.Composed(op, g, 0f, 0f);
                        double s = z - reach;
                        if (s > worstShort) { worstShort = s; wsAt = tech + "+" + bev + " a=" + am + " zeta=" + z.ToString("E4") + " Ginv=" + g.ToString("E5") + " reach=" + reach.ToString("E5"); }
                    }
                }
        Console.WriteLine("  round trips = " + rt + "   worst reach shortfall = " + worstShort.ToString("E4"));
        Console.WriteLine("  " + wsAt);
        Console.WriteLine();

        // ── DENORMAL sweep on every fixed form ────────────────────────────────────────────────
        Console.WriteLine("-- denormal sweep: every fixed form from 1e0 down to 1e-45 --");
        int nonFinite = 0, negative = 0;
        for (int e = 0; e <= 45; e++)
        {
            float x = (float)Math.Pow(10, -e);
            foreach (var bev in Fixtures.Bevels)
            {
                var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, 1f, 3f, 1f, 45f);
                float[] vals = { ShaperHeight.Bevel(op, x), ShaperHeight.BevelInverseU(op, x), ShaperHeight.BevelDerivative(op, x) };
                foreach (float v in vals) { if (float.IsNaN(v)) nonFinite++; if (v < 0 && !float.IsNaN(v) && v != -1f) negative++; }
            }
            foreach (var tech in Fixtures.Techs)
            {
                var op = Fixtures.Make(tech, ShaperBevelTechnique.None, 4f, 1f, 1f, 0.25f, 3f, 1f, 45f);
                float[] vals = { ShaperHeight.Profile(op, x, 0f, 0f), ShaperHeight.ProfileInverse(op, x, 0f, 0f) };
                foreach (float v in vals) { if (float.IsNaN(v)) nonFinite++; if (v < 0) negative++; }
            }
        }
        Console.WriteLine("  non-finite (excluding declared +inf derivatives): " + nonFinite + "   negative results: " + negative);
    }
}
