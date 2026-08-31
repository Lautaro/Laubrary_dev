using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// ============================================================================================
// SECOND-PASS INDEPENDENT VERIFIER, T-0109. Attacks the FIXES, not the original defects.
// ============================================================================================
public static class V2
{
    // ─────────────────────────────────────────────────────────────────────────────────────────
    // V2A — the per-step contained prism uses InverseAtE RAW, with no verify-and-nudge.
    //       InverseUpperBound got the hardening; ShaperResolve's per-step tauMax did not.
    //       Mirror of IUB, but calling exactly what the march calls.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    public static void A_InverseAtEUpperBound()
    {
        Console.WriteLine("=== V2A - InverseAtE used AS AN UPPER BOUND (what ShaperResolve's PER-STEP contained prism calls) ===");
        Console.WriteLine("The march does: tauMax = InverseAtE(op, wHi, eLo); then declares every point with");
        Console.WriteLine("  d <= -tauMax*span to be SOLID. Soundness needs: for all t >= tauMax, LowerG(t,eLo) >= wHi.");
        Console.WriteLine("InverseUpperBound VERIFIES this and bisects up if it fails. InverseAtE does NOT.");
        Console.WriteLine();

        for (int pass = 0; pass < 2; pass++)
        {
            bool hardened = pass == 1;
            long checks = 0, viol = 0; double worst = 0; string at = "";
            double worstTau = 0; string atTau = "";
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
                                float tau = hardened ? ShaperHeight.InverseUpperBound(op, zeta)
                                                     : ShaperHeight.InverseAtE(op, zeta, e);
                                if (ShaperHeight.IsNoCrossSection(tau)) continue;
                                // the exact soundness test the march relies on
                                for (int k = 0; k <= 40; k++)
                                {
                                    float t = tau + (1f - tau) * (k / 40f);
                                    if (t < tau) continue;
                                    float g = LowerG(op, t, e);
                                    checks++;
                                    if (g < zeta)
                                    {
                                        viol++;
                                        double m = zeta - g;
                                        if (m > worst) { worst = m; at = tech + "+" + bev + " ang=" + ang + " a=" + am + " zeta=" + zeta + " tau=" + tau + " t=" + t + " LowerG=" + g; }
                                        // how far up would tau have to move to be sound?
                                        double dtau = TrueUpper(op, zeta, e) - tau;
                                        if (dtau > worstTau) { worstTau = dtau; atTau = tech + "+" + bev + " ang=" + ang + " a=" + am + " zeta=" + zeta; }
                                    }
                                }
                            }
                        }
            Console.WriteLine((hardened ? "  InverseUpperBound (slab-wide, HARDENED):  " : "  InverseAtE        (per-step, RAW):        ")
                + "checks=" + checks + "  violations=" + viol + "  worst shortfall in zeta = " + worst.ToString("E4"));
            if (at != "") Console.WriteLine("      worst: " + at);
            if (worstTau > 0) Console.WriteLine("      worst tau under-report (t-units) = " + worstTau.ToString("E4") + "   at " + atTau);
        }
    }

    static float LowerG(in ShaperHeightOp op, float t, float e)
        => op.technique == ShaperExtrusionTechnique.Linear ? e * ShaperHeight.Bevel(op, t)
                                                           : ShaperHeight.Composed(op, t, 0f, 0f);

    // the smallest t this float arithmetic can PROVE reaches zeta, by bisection on LowerG
    static float TrueUpper(in ShaperHeightOp op, float zeta, float e)
    {
        if (LowerG(op, 1f, e) < zeta) return 1f;
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 60; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (mid <= lo || mid >= hi) break;
            if (LowerG(op, mid, e) >= zeta) hi = mid; else lo = mid;
        }
        return hi;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // V2B — a HORIZONTAL ray sitting exactly on an interior slab breakpoint is inside TWO slabs,
    //       because ClipSlab's parallel case is inclusive at BOTH ends. Both slabs march the whole
    //       range and emit the same flips, so the crossing list is doubled and the parity stamp
    //       in step 5 (which toggles on every entry) comes out wrong.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    public static void B_BreakpointDuplicate()
    {
        Console.WriteLine();
        Console.WriteLine("=== V2B - a horizontal ray exactly ON an interior slab breakpoint ===");
        Console.WriteLine("ClipSlab's parallel branch is `o >= lo && o <= hi` - INCLUSIVE at both ends - so a ray at");
        Console.WriteLine("z == a breakpoint is accepted by the slab below it AND the slab above it. Both march the");
        Console.WriteLine("whole clipped range and emit the SAME flips.");
        Console.WriteLine();

        var root = March.SolidPlate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        // Flat + Stepped bevel, bevelN = 4, body = 40  =>  G takes values 1/4, 2/4, 3/4, 1
        // so interior breakpoints in z sit at 10, 20, 30.
        var def = new ShaperHeightDef
        {
            technique = ShaperExtrusionTechnique.Flat,
            bevel = ShaperBevelTechnique.Stepped,
            bevelAmount = new ZUIValue(0.5f),
            bevelSteps = new ZUIValue(4f),
            depth = new ZUIValue(40f)
        };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        var bps = new float[64];
        int nb = ShaperHeight.Breakpoints(op, bps);
        Console.Write("  breakpoints (zeta): ");
        for (int i = 0; i < nb; i++) Console.Write(bps[i].ToString("F4") + " ");
        Console.WriteLine();
        Console.Write("  breakpoints (z)   : ");
        for (int i = 0; i < nb; i++) Console.Write((op.baseZ + op.body * bps[i]).ToString("F4") + " ");
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine(string.Format("{0,12} {1,7} {2,7} {3,10} {4,10} {5}", "ray z", "true", "got", "Depth", "trueDepth", "parity(entering flags)"));
        var zs = new List<float>();
        for (int i = 0; i < nb; i++) zs.Add(op.baseZ + op.body * bps[i]);
        zs.Add(op.baseZ + op.body * 0.5f + 0.37f);   // a control: NOT on a breakpoint
        int bad = 0;
        foreach (float z in zs)
        {
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[64];
            var r = ShaperResolve.Query(scene, -400f, 0f, z, 1f, 0f, 0f, buf);
            var truth = March.TrueCrossings(prog, stack, op, -400f, 0f, z, 1f, 0f, 0f, 1200f, 400000);
            float depth = ShaperResolve.Depth(buf, r.count, 0);
            double trueDepth = 0;
            for (int i = 0; i + 1 < truth.Count; i += 2) trueDepth += truth[i + 1] - truth[i];
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.count; i++) sb.Append(buf[i].entering ? "I" : "O");
            bool ok = r.count == truth.Count && Math.Abs(depth - trueDepth) < 0.05;
            if (!ok) bad++;
            Console.WriteLine(string.Format("{0,12:F4} {1,7} {2,7} {3,10:F3} {4,10:F3} {5}  {6}",
                z, truth.Count, r.count, depth, trueDepth, sb.ToString(), ok ? "ok" : "*** WRONG ***"));
        }
        Console.WriteLine("  rows wrong: " + bad);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // V2C — TRUNCATION PARITY. Slabs are iterated in zeta order, not ray order. For a DOWNWARD
    //       ray the lowest-zeta slab is at the LARGEST ray parameter, so crossings are emitted
    //       in DECREASING rayT. Truncation keeps the FIRST emitted, i.e. the LAST in ray order -
    //       a SUFFIX - and step 5 then stamps parity onto it starting from insideAtStart.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    public static void C_TruncationParity()
    {
        Console.WriteLine();
        Console.WriteLine("=== V2C - truncation parity on an out-of-order crossing slice (the item BOTH passes left untested) ===");
        Console.WriteLine("Slabs are iterated by zeta; a DOWNWARD ray meets them in DECREASING rayT, so emission order is");
        Console.WriteLine("REVERSED. A truncated list therefore retains a SUFFIX of the ray while step 5 stamps parity as if");
        Console.WriteLine("it were a PREFIX starting from insideAtStart.");
        Console.WriteLine();

        var root = MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var def = new ShaperHeightDef
        {
            technique = ShaperExtrusionTechnique.Stepped,
            steps = new ZUIValue(8f),
            bevel = ShaperBevelTechnique.None,
            depth = new ZUIValue(300f)
        };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);

        // Search for the ray with the MOST crossings, and one that STARTS INSIDE the solid.
        float bx = 0, bz = 0, bdx = 1, bdz = 0; int best = 0;
        foreach (float tilt in new float[] { 2f, 5f, 8f, 11f, 15f, 20f, 25f, 30f, 40f, 50f, 60f, 70f, 80f })
            foreach (float z0 in new float[] { 20f, 40f, 60f, 80f, 120f, 160f, 200f, 240f, 280f, 299f })
            {
                double rad = tilt * Math.PI / 180.0;
                float dx = (float)Math.Cos(rad), dz = (float)(-Math.Sin(rad));
                float ox = -196f;
                var sc = new ShaperResolveScene(); sc.Add(prog, op);
                var b2 = new ShaperCrossing[256];
                var rr = ShaperResolve.Query(sc, ox, 0f, z0, dx, 0f, dz, b2);
                var bpsX = new float[64]; int nbX = ShaperHeight.Breakpoints(op, bpsX);
                int score = rr.count * 100 + SlabSpread(op, b2, rr.count, bpsX, nbX) * 1000;
                if (rr.count >= 4 && score > best) { best = score; bx = ox; bz = z0; bdx = dx; bdz = dz; }
            }
        Console.WriteLine("  chosen ray: origin=(" + bx + ",0," + bz + ") dir=(" + bdx.ToString("F4") + ",0," + bdz.ToString("F4") + ")  score=" + best);

        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var full = new ShaperCrossing[256];
        var rFull = ShaperResolve.Query(scene, bx, 0f, bz, bdx, 0f, bdz, full);
        Console.WriteLine("  untruncated: count=" + rFull.count + " truncated=" + rFull.truncated + " branch=" + rFull.branch
                          + "  Depth=" + ShaperResolve.Depth(full, rFull.count, 0).ToString("F3"));
        var truth = March.TrueCrossings(prog, stack, op, bx, 0f, bz, bdx, 0f, bdz, 900f, 900000);
        double trueDepth = 0;
        for (int i = 0; i + 1 < truth.Count; i += 2) trueDepth += truth[i + 1] - truth[i];
        Console.WriteLine("  ground truth: crossings=" + truth.Count + "  solid length=" + trueDepth.ToString("F3"));
        Console.Write("  untruncated rayT: ");
        for (int i = 0; i < rFull.count; i++) Console.Write(full[i].rayT.ToString("F2") + (full[i].entering ? "I " : "O "));
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine(string.Format("{0,6} {1,7} {2,10} {3,12} {4,12} {5}", "cap", "count", "truncated", "Depth", "err", "parity"));
        int wrongParity = 0, wrongDepth = 0;
        for (int cap = rFull.count; cap >= 1; cap--)
        {
            var buf = new ShaperCrossing[cap];
            var r = ShaperResolve.Query(scene, bx, 0f, bz, bdx, 0f, bdz, buf);
            float depth = ShaperResolve.Depth(buf, r.count, 0);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.count; i++) sb.Append(buf[i].entering ? "I" : "O");
            bool alt = true;
            for (int i = 0; i < r.count; i++) if (buf[i].entering != (i % 2 == 0)) { alt = false; break; }
            bool over = depth > trueDepth + 0.05;
            if (!alt) wrongParity++;
            if (over) wrongDepth++;
            Console.WriteLine(string.Format("{0,6} {1,7} {2,10} {3,12:F3} {4,12:F3} {5}  {6}{7}",
                cap, r.count, r.truncated, depth, depth - trueDepth, sb.ToString(),
                alt ? "" : "PARITY-BROKEN ", over ? "DEPTH-OVER-REPORTS-SOLID" : ""));
        }
        Console.WriteLine("  caps whose parity is not a legal alternating sequence: " + wrongParity);
        Console.WriteLine("  caps whose Depth OVER-reports the true solid length:   " + wrongDepth);
    }

    public static ShaperNode MultiSlotPlate(float halfW, float halfH)
    {
        // A WIDE, SHALLOW plate: span = min(halfW, halfH) = halfH, so a point halfH in from the
        // long edge already has t = 1 and the terrace reaches full body height between the slots.
        var outer = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH };
        var a = ShaperNode.Primitive(outer, "plate", ShaperCombineMode.Add);
        var kids = new List<ShaperNode> { a };
        float[] cx = { -100f, 100f };
        for (int i = 0; i < cx.Length; i++)
        {
            var slot = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 6f, rectHalfH = halfH * 2f };
            var b = ShaperNode.Primitive(slot, "slot" + i, ShaperCombineMode.Subtract);
            b.transform.translate = new Vector2(cx[i], 0f);
            kids.Add(b);
        }
        return ShaperNode.Bag("plate", ShaperCombineMode.Add, kids.ToArray());
    }

    /// <summary>How many DISTINCT zeta slabs do the reported crossings fall into?</summary>
    public static int SlabSpread(in ShaperHeightOp op, ShaperCrossing[] c, int n, float[] bps, int nb)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            float zeta = (c[i].height) / op.body;
            int k = 0;
            for (int j = 0; j + 1 < nb; j++) if (zeta >= bps[j]) k = j;
            seen.Add(k);
        }
        return seen.Count;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // V2D — ABSOLUTE epsilons in scale-free quantities. The fixer fixed ONE symptom of the
    //       -1e-6 in ProfileInverse(Stepped) (the k<1 case) but left the epsilon itself, and
    //       the same epsilon is in BevelInverseU(Stepped). Probe at zeta one ulp above a tread.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    public static void D_AbsoluteEpsilons()
    {
        Console.WriteLine();
        Console.WriteLine("=== V2D - the -1e-6 ABSOLUTE epsilons, probed where they bite (zeta just above a tread) ===");
        Console.WriteLine("H4 measures |Ginv - Bisect| on a linear + log grid. Neither lands one ulp above a tread,");
        Console.WriteLine("which is exactly where an ABSOLUTE epsilon on a scale-free quantity mis-rounds.");
        Console.WriteLine();

        Console.WriteLine("  (a) ProfileInverse(Stepped):  k = ceil(zeta*(n-1) - 1e-6)");
        Console.WriteLine(string.Format("{0,5} {1,16} {2,14} {3,14} {4,12} {5,12}", "n", "zeta", "Ginv", "Bisect", "|dt|", "reach short"));
        double worstDt = 0, worstReach = 0; string atDt = "";
        foreach (int n in new int[] { 2, 3, 4, 5, 8, 16, 32 })
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int k = 1; k < n; k++)
            {
                float tread = k / (float)(n - 1);
                if (tread > 1f) continue;
                for (int u = 1; u <= 3; u++)
                {
                    float zeta = NextUp(tread, u);
                    if (zeta > op.supG) continue;
                    float ginv = ShaperHeight.ProfileInverse(op, zeta, 0f, 0f);
                    float bis = ShaperHeight.Bisect(op, zeta, 0f, 0f, 0f, 1f);
                    float g = ShaperHeight.Composed(op, ginv, 0f, 0f);
                    double dt = Math.Abs(ginv - bis);
                    double reach = Math.Max(0, zeta - g);
                    if (dt > worstDt) { worstDt = dt; atDt = "n=" + n + " k=" + k + " zeta=" + zeta.ToString("R"); }
                    if (reach > worstReach) worstReach = reach;
                    if (dt > 1e-6 && u == 1)
                        Console.WriteLine(string.Format("{0,5} {1,16} {2,14:F6} {3,14:F6} {4,12:E3} {5,12:E3}",
                            n, zeta.ToString("R"), ginv, bis, dt, reach));
                }
            }
        }
        Console.WriteLine("  worst |dt| = " + worstDt.ToString("E3") + " at " + atDt + "   worst reach shortfall = " + worstReach.ToString("E3"));
        Console.WriteLine();

        Console.WriteLine("  (b) BevelInverseU(Stepped):  k = ceil(zeta*m - 1 - 1e-6)");
        double worstDt2 = 0, worstReach2 = 0; string atDt2 = "";
        foreach (int m in new int[] { 2, 3, 4, 5, 8, 16 })
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Stepped, 4f, 1f, 1f, 1f, m, 1f, 45f);
            for (int k = 1; k <= m; k++)
            {
                float lvl = k / (float)m;
                for (int u = 1; u <= 3; u++)
                {
                    float zeta = NextUp(lvl, u);
                    if (zeta > 1f) continue;
                    float uu = ShaperHeight.BevelInverseU(op, zeta);
                    float bis = ShaperHeight.Bisect(op, zeta, 0f, 0f, 0f, 1f);
                    float g = ShaperHeight.Composed(op, uu * op.a, 0f, 0f);
                    double dt = Math.Abs(uu * op.a - bis);
                    double reach = Math.Max(0, zeta - g);
                    if (dt > worstDt2) { worstDt2 = dt; atDt2 = "m=" + m + " k=" + k + " zeta=" + zeta.ToString("R"); }
                    if (reach > worstReach2) worstReach2 = reach;
                }
            }
        }
        Console.WriteLine("  worst |dt| = " + worstDt2.ToString("E3") + " at " + atDt2 + "   worst reach shortfall = " + worstReach2.ToString("E3"));
        Console.WriteLine();

        Console.WriteLine("  (c) does the SHIPPED march's containing prism stay sound at those zetas?");
        long v = 0; long c = 0; double w = 0;
        foreach (int n in new int[] { 2, 3, 4, 5, 8, 16, 32 })
        {
            var op = Fixtures.Make(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, n, 1f, 1f, 0.25f, 3f, 1f, 45f);
            for (int k = 1; k < n; k++)
            {
                float tread = k / (float)(n - 1);
                for (int uu = 0; uu <= 3; uu++)
                {
                    float zeta = uu == 0 ? tread : NextUp(tread, uu);
                    if (zeta > op.supG || zeta <= 0) continue;
                    float lo = ShaperHeight.InverseLowerBound(op, zeta);
                    if (ShaperHeight.IsNoCrossSection(lo)) continue;
                    // containment: every t BELOW tauMin must have G < zeta
                    for (int j = 0; j <= 200; j++)
                    {
                        float t = lo * (j / 200f);
                        if (t >= lo) continue;
                        c++;
                        float g = ShaperHeight.Composed(op, t, 0f, 0f);
                        if (g >= zeta) { v++; if (g - zeta > w) w = g - zeta; }
                    }
                }
            }
        }
        Console.WriteLine("  InverseLowerBound containment at tread-adjacent zetas: checks=" + c + " violations=" + v);
    }

    public static float NextUp(float x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            int b = BitConverter.SingleToInt32Bits(x);
            x = BitConverter.Int32BitsToSingle(x >= 0 ? b + 1 : b - 1);
        }
        return x;
    }
}
