using System;
using UnityEngine;
using Laubrary.Shaper;

// T-0109 FIXER — F2 before/after. The "before" column re-implements the EXACT expression that was in
// ShaperHeight.cs prior to the fix, in float, so the comparison is like-for-like and does not need a second
// checkout. The "after" column calls the shipped code. The reference is double, and where the DOUBLE
// reference itself cancels, a cancellation-free double form is used instead (marked).
public static class F2
{
    static float OldBevelCove(float u)      { float q = 1f - u * u; return 1f - (q > 0f ? Mathf.Sqrt(q) : 0f); }
    static float OldBevelOgee(float u)
    {
        if (u < 0.5f) { float q = 1f - 4f * u * u; return 0.5f * (1f - (q > 0f ? Mathf.Sqrt(q) : 0f)); }
        float w = 1f - u; float r = 1f - 4f * w * w; return 0.5f + 0.5f * (r > 0f ? Mathf.Sqrt(r) : 0f);
    }
    static float OldInvRounded(float z)     { if (z >= 1f) return 1f; return 1f - Mathf.Sqrt(1f - z * z); }
    static float OldInvCove(float z)        { if (z >= 1f) return 1f; float w = 1f - z; return Mathf.Sqrt(1f - w * w); }

    // Cancellation-free DOUBLE references.
    static double RefCove(double u)   { return u * u / (1.0 + Math.Sqrt((1.0 - u) * (1.0 + u))); }
    static double RefOgee(double u)
    {
        if (u < 0.5) return 2.0 * u * u / (1.0 + Math.Sqrt((1.0 - 2.0 * u) * (1.0 + 2.0 * u)));
        double w = 1.0 - u; return 1.0 - 2.0 * w * w / (1.0 + Math.Sqrt((1.0 - 2.0 * w) * (1.0 + 2.0 * w)));
    }
    static double RefInvRounded(double z) { return z * z / (1.0 + Math.Sqrt((1.0 - z) * (1.0 + z))); }
    static double RefInvCove(double z)    { return Math.Sqrt(z * (2.0 - z)); }

    static ShaperHeightOp Op(ShaperBevelTechnique bev, float amount, int bevelSteps)
    {
        var root = March.SolidPlate(150f, 100f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var def = new ShaperHeightDef
        {
            technique = ShaperExtrusionTechnique.Flat, bevel = bev,
            depth = new ZUIValue(40f), bevelAmount = new ZUIValue(amount),
            bevelSteps = new ZUIValue((float)bevelSteps)
        };
        return ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
    }

    static string Rel(double got, double want)
    {
        if (want == 0.0) return got == 0.0 ? "0" : "inf";
        return (Math.Abs(got - want) / Math.Abs(want)).ToString("0.000E+000");
    }

    public static void Run()
    {
        Console.WriteLine("=== F2 - catastrophic cancellation, BEFORE (old expression) vs AFTER (shipped) ===");
        Console.WriteLine("a = 1 so u == t. Reference is a cancellation-free DOUBLE form.");
        Console.WriteLine();

        var opCove = Op(ShaperBevelTechnique.Cove, 1f, 3);
        var opOgee = Op(ShaperBevelTechnique.Ogee, 1f, 3);

        Console.WriteLine("-- forward Bevel(Cove), the verifier's cited point u = 1.78e-4 and a sweep --");
        Console.WriteLine(string.Format("{0,12} {1,16} {2,16} {3,16} {4,12} {5,12}", "u", "old", "new", "ref(double)", "relOld", "relNew"));
        foreach (float u in new float[] { 1.78e-4f, 1e-4f, 1e-5f, 1e-6f, 1e-7f, 1e-10f, 1e-20f, 1e-30f, 0.25f, 0.9f })
        {
            float o = OldBevelCove(u), n = ShaperHeight.Bevel(opCove, u); double r = RefCove(u);
            Console.WriteLine(string.Format("{0,12:E3} {1,16:E5} {2,16:E5} {3,16:E5} {4,12} {5,12}", u, o, n, r, Rel(o, r), Rel(n, r)));
        }
        Console.WriteLine();

        Console.WriteLine("-- forward Bevel(Ogee), cited point u = 1.00e-4 --");
        Console.WriteLine(string.Format("{0,12} {1,16} {2,16} {3,16} {4,12} {5,12}", "u", "old", "new", "ref(double)", "relOld", "relNew"));
        foreach (float u in new float[] { 1e-4f, 1e-5f, 1e-6f, 1e-10f, 1e-20f, 0.4999f, 0.5001f, 0.9999f })
        {
            float o = OldBevelOgee(u), n = ShaperHeight.Bevel(opOgee, u); double r = RefOgee(u);
            Console.WriteLine(string.Format("{0,12:E3} {1,16:E5} {2,16:E5} {3,16:E5} {4,12} {5,12}", u, o, n, r, Rel(o, r), Rel(n, r)));
        }
        Console.WriteLine();

        var opR = Op(ShaperBevelTechnique.Rounded, 0.05f, 3);
        Console.WriteLine("-- BevelInverseU(Rounded): OLD returns exactly 0 below zeta ~ 2.4e-4 --");
        Console.WriteLine(string.Format("{0,12} {1,16} {2,16} {3,16} {4,12} {5,12}", "zeta", "old", "new", "ref(double)", "relOld", "relNew"));
        foreach (float z in new float[] { 2.4e-4f, 1e-4f, 1e-5f, 1e-7f, 1e-10f, 1e-20f, 1e-38f, 0.5f, 0.99f, 0.999999f })
        {
            float o = OldInvRounded(z), n = ShaperHeight.BevelInverseU(opR, z); double r = RefInvRounded(z);
            Console.WriteLine(string.Format("{0,12:E3} {1,16:E5} {2,16:E5} {3,16:E5} {4,12} {5,12}", z, o, n, r, Rel(o, r), Rel(n, r)));
        }
        Console.WriteLine();

        var opC = Op(ShaperBevelTechnique.Cove, 1f, 3);
        Console.WriteLine("-- BevelInverseU(Cove): OLD OVER-reports (the unsafe direction) --");
        Console.WriteLine(string.Format("{0,12} {1,16} {2,16} {3,16} {4,12} {5,12}", "zeta", "old", "new", "ref(double)", "relOld", "relNew"));
        foreach (float z in new float[] { 1e-7f, 1e-6f, 1e-5f, 1e-4f, 1e-10f, 1e-20f, 1e-30f, 0.5f, 0.999f, 0.999999f })
        {
            float o = OldInvCove(z), n = ShaperHeight.BevelInverseU(opC, z); double r = RefInvCove(z);
            Console.WriteLine(string.Format("{0,12:E3} {1,16:E5} {2,16:E5} {3,16:E5} {4,12} {5,12}", z, o, n, r, Rel(o, r), Rel(n, r)));
        }
        Console.WriteLine();

        // Round-trip reach: does G(Ginv(zeta)) >= zeta?  This is D2's own failure mode.
        Console.WriteLine("-- reach round trip, Flat + <bevel>, amount 0.05, log-spaced zeta: shortfall = zeta - G(Ginv(zeta)) --");
        foreach (ShaperBevelTechnique bev in new[]{ ShaperBevelTechnique.Rounded, ShaperBevelTechnique.Cove,
                                                    ShaperBevelTechnique.Ogee, ShaperBevelTechnique.Linear,
                                                    ShaperBevelTechnique.Stepped })
        {
            var op = Op(bev, 0.05f, 3);
            double worst = 0; double at = 0;
            for (int i = 0; i <= 900; i++)
            {
                double z = Math.Pow(10.0, -9.0 + i * 9.0 / 900.0);
                float t = ShaperHeight.Inverse(op, (float)z, 0f, 0f);
                if (ShaperHeight.IsNoCrossSection(t)) continue;
                double g = ShaperHeight.Composed(op, t, 0f, 0f);
                double s = z - g;
                if (s > worst) { worst = s; at = z; }
            }
            Console.WriteLine(string.Format("   Flat+{0,-8} worst reach shortfall = {1:E4} at zeta = {2:E4}", bev, worst, at));
        }
        Console.WriteLine();

        // Denormal / extreme inputs must not produce NaN or Inf.
        Console.WriteLine("-- denormal sweep: NaN/Inf hunt over both fixed forwards and both fixed inverses --");
        int bad = 0;
        for (int e = 0; e <= 45; e++)
        {
            float v = (float)Math.Pow(10.0, -e);
            float[] vals = { ShaperHeight.Bevel(opCove, v), ShaperHeight.Bevel(opOgee, v),
                             ShaperHeight.BevelInverseU(opR, v), ShaperHeight.BevelInverseU(opC, v) };
            foreach (float f in vals) if (float.IsNaN(f) || float.IsInfinity(f) || f < 0f) { bad++; Console.WriteLine("   BAD at 1e-" + e + ": " + f); }
        }
        Console.WriteLine("   non-finite or negative results: " + bad);
    }
}
