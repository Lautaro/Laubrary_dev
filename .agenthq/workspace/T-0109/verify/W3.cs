using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// W3 — THIRD-PASS INDEPENDENT VERIFIER'S OWN PROBES.
// Nothing here calls a probe written by the fixer or by an earlier verifier. Every truth predicate,
// every grid and every bisection is re-implemented from HEIGHT-SPEC.md's clauses.
// ─────────────────────────────────────────────────────────────────────────────────────────────────
public static class W3
{
    // ── my own reading of the AMENDED HS-1.1 ────────────────────────────────────────────────────
    // (x,y,z) is solid  ⟺  d(x,y) ≤ 0  ∧  0 ≤ z−base  ∧  G(t) > 0  ∧  z−base ≤ body·G(t)
    public static bool Solid(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                             float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float z = oz + s * dz;
        float above = z - op.baseZ;
        if (!(above >= 0f)) return false;
        if (above > op.body * op.supG) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float dd = ShaperEvaluator.Distance(field, px, py, stack);
        if (ShaperField.IsEmpty(dd)) return false;
        if (!(dd <= 0f)) return false;
        float t = ShaperHeight.T(op, dd);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        float g = ShaperHeight.Composed(op, t, nx, ny);
        if (!(g > 0f)) return false;
        return above <= op.body * g;
    }

    // the OLD closed reading (no G>0 conjunct), used only to show nothing was lost
    public static bool SolidClosed(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                   float ox, float oy, float oz, float dx, float dy, float dz, float s)
    {
        float z = oz + s * dz;
        float above = z - op.baseZ;
        if (!(above >= 0f)) return false;
        if (above > op.body * op.supG) return false;
        float px = ox + s * dx, py = oy + s * dy;
        float dd = ShaperEvaluator.Distance(field, px, py, stack);
        if (ShaperField.IsEmpty(dd)) return false;
        if (!(dd <= 0f)) return false;
        float t = ShaperHeight.T(op, dd);
        float nx = 0f, ny = 0f;
        if (op.technique == ShaperExtrusionTechnique.Linear)
            ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);
        float g = ShaperHeight.Composed(op, t, nx, ny);
        return above <= op.body * g;
    }

    public delegate bool Pred(float s);

    /// <summary>Dense scan + bisection for every sign change of <paramref name="p"/> on [0, sMax].</summary>
    public static List<double> Scan(Pred p, double sMax, int samples)
    {
        var outl = new List<double>();
        bool prev = p(0f);
        for (int i = 1; i <= samples; i++)
        {
            float s = (float)(sMax * i / samples);
            bool m = p(s);
            if (m != prev)
            {
                double lo = sMax * (i - 1) / samples, hi = sMax * i / samples;
                for (int k = 0; k < 44; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (p((float)mid) == prev) lo = mid; else hi = mid;
                }
                outl.Add(hi);
                prev = m;
            }
        }
        return outl;
    }

    /// <summary>Total solid length along the ray by direct integration of the predicate (no crossings).</summary>
    public static double SolidLength(List<double> flips, bool insideAtZero, double sMax)
    {
        double total = 0; bool inside = insideAtZero; double last = 0;
        foreach (double f in flips) { if (inside) total += f - last; inside = !inside; last = f; }
        if (inside) total += sMax - last;
        return total;
    }

    public static ShaperNode Plate(float halfW, float halfH) { return March.SolidPlate(halfW, halfH); }

    public static ShaperHeightOp MakeOp(ShaperProgram prog, ShaperExtrusionTechnique tech, ShaperBevelTechnique bev,
                                        float depth, int steps, float amount, float curve, float taper,
                                        int bevSteps = 4, float angle = 45f, float zOffset = 0f)
    {
        var def = new ShaperHeightDef
        {
            technique = tech, bevel = bev,
            depth = new ZUIValue(depth),
            steps = new ZUIValue(steps),
            bevelAmount = new ZUIValue(amount),
            curve = new ZUIValue(curve),
            taper = new ZUIValue(taper),
            bevelSteps = new ZUIValue(bevSteps),
            angle = new ZUIValue(angle)
        };
        return ShaperHeightCompiler.Compile(def, prog, 1f, zOffset, 0f, 0u);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3A — does the general march lose a genuine crossing?  My own sweep, my own truth.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void A_LostCrossings()
    {
        Console.WriteLine("=== W3A — general march: does it lose a genuine crossing? (my truth, my grid) ===");
        Console.WriteLine("Truth = a 1.2e6-sample scan of MY OWN amended-HS-1.1 predicate + 44-step bisection.");
        Console.WriteLine("A ray FAILS if a true flip has no emitted crossing within 0.05 px, or Depth differs > 0.05 px.");

        var root = Plate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];

        int rays = 0, lost = 0, spurious = 0;
        double worstGap = 0, worstDepthLoss = 0;
        string worstDesc = "", worstDepthDesc = "";
        var affected = new SortedSet<int>();

        float[] dzs = { -0.3f, -0.6f, -0.8f };
        float[] offs = { -3e-4f, 0f, 3e-4f };

        for (int n = 2; n <= 32; n++)
        {
            var op = MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 290f, n, 0.5f, 1f, 1f);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int k = 0; k < n; k++)
            {
                float zeta = (n - 1) <= 0 ? 0f : (float)k / (n - 1);
                foreach (float dzf in dzs)
                    foreach (float off in offs)
                    {
                        float z = op.baseZ + op.body * zeta + off;
                        float dz = dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
                        float ox = -400f, oy = 0f, oz = z;
                        var rr = ShaperResolve.Query(scene, ox, oy, oz, dx, 0f, dz, buf);
                        rays++;

                        Pred p = s => Solid(prog, stack, op, ox, oy, oz, dx, 0f, dz, s);
                        double sMax = 1400.0;
                        var truth = Scan(p, sMax, 200000);

                        // match every true flip to an emitted one
                        double gapMax = 0;
                        foreach (double tf in truth)
                        {
                            double best = 1e30;
                            for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - tf));
                            if (best > gapMax) gapMax = best;
                        }
                        // and every emitted one to a true flip
                        double spurMax = 0;
                        for (int i = 0; i < rr.count; i++)
                        {
                            double best = 1e30;
                            foreach (double tf in truth) best = Math.Min(best, Math.Abs(buf[i].rayT - tf));
                            if (best > spurMax) spurMax = best;
                        }

                        double trueLen = SolidLength(truth, p(0f), sMax);
                        double gotLen = 0; bool ins = false; double last = 0;
                        for (int i = 0; i < rr.count; i++)
                        {
                            if (buf[i].entering) { ins = true; last = buf[i].rayT; }
                            else if (ins) { gotLen += buf[i].rayT - last; ins = false; }
                        }
                        double dLoss = trueLen - gotLen;

                        if (gapMax > 0.05) { lost++; affected.Add(n); if (gapMax > worstGap) { worstGap = gapMax; worstDesc = "n=" + n + " k=" + k + " dz=" + dzf + " off=" + off + " true=" + truth.Count + " got=" + rr.count; } }
                        if (spurMax > 0.05 && rr.count > 0) spurious++;
                        if (dLoss > worstDepthLoss) { worstDepthLoss = dLoss; worstDepthDesc = "n=" + n + " k=" + k + " dz=" + dzf + " off=" + off + "  true " + trueLen.ToString("F4") + " got " + gotLen.ToString("F4"); }
                    }
            }
        }
        Console.WriteLine("  rays = " + rays);
        Console.WriteLine("  rays LOSING a true crossing (gap > 0.05 px) = " + lost + "   worst gap = " + worstGap.ToString("E4") + " px");
        Console.WriteLine("    worst: " + worstDesc);
        Console.WriteLine("  rays with a SPURIOUS emitted crossing        = " + spurious);
        Console.WriteLine("  worst SOLID LENGTH under-report              = " + worstDepthLoss.ToString("F4") + " px");
        Console.WriteLine("    at: " + worstDepthDesc);
        Console.Write("  step counts affected:");
        foreach (int a in affected) Console.Write(" " + a);
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3B — the minimal repro, and the contradiction that causes it.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void B_Mechanism()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3B — minimal repro + the contained-prism / membership contradiction ===");
        var root = Plate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        int n = 8;
        var op = MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 290f, n, 0.5f, 1f, 1f);
        var scene = new ShaperResolveScene(); scene.Add(prog, op);
        var buf = new ShaperCrossing[512];

        float zeta0 = 6f / (n - 1);
        float oz = op.baseZ + op.body * zeta0 + 3e-4f;
        float dzf = 0.3f, dz = -dzf, dx = Mathf.Sqrt(1f - dzf * dzf);
        float ox = -400f, oy = 0f;

        var rr = ShaperResolve.Query(scene, ox, oy, oz, dx, 0f, dz, buf);
        Console.WriteLine("  fixture: 400x400 solid plate, Stepped n=8 bevel None, depth 290 (body=" + op.body + ", span=" + op.span + ")");
        Console.WriteLine("  ray: origin (" + ox + "," + oy + "," + oz.ToString("F6") + ") dir (" + dx.ToString("F5") + ",0," + dz + ")");
        Console.Write("  EMITTED : "); for (int i = 0; i < rr.count; i++) Console.Write(buf[i].rayT.ToString("F4") + (buf[i].entering ? "I " : "O ")); Console.WriteLine("   (branch=" + rr.branch + " capped=" + rr.bracketCapped + " exhausted=" + rr.stepsExhausted + " truncated=" + rr.truncated + ")");

        Pred p = s => Solid(prog, stack, op, ox, oy, oz, dx, 0f, dz, s);
        var truth = Scan(p, 1400.0, 1400000);
        Console.Write("  TRUTH   : "); foreach (double t in truth) Console.Write(t.ToString("F4") + " "); Console.WriteLine();
        Console.WriteLine("  true solid length " + SolidLength(truth, p(0f), 1400.0).ToString("F4") + " px");

        // the missed span
        for (int j = 0; j + 1 < truth.Count; j += 2)
        {
            double lo = truth[j], hi = truth[j + 1];
            double best = 1e30;
            for (int i = 0; i < rr.count; i++) best = Math.Min(best, Math.Abs(buf[i].rayT - lo));
            if (best <= 0.05) continue;
            Console.WriteLine("  MISSED SOLID SPAN [" + lo.ToString("F4") + ", " + hi.ToString("F4") + "]  width " + (hi - lo).ToString("F4") + " px");

            // where is the slab boundary?
            var bps = new float[512];
            int nb = ShaperHeight.Breakpoints(op, bps);
            Console.WriteLine("  breakpoints = " + nb);
            Console.WriteLine("  slab boundaries in ray parameter, and the predicate either side:");
            Console.WriteLine(string.Format("  {0,3} {1,10} {2,12} {3,14} {4,8} {5,8} {6,8}", "k", "zeta", "z", "s", "P(s-e)", "P(s)", "P(s+e)"));
            for (int k = 0; k < nb; k++)
            {
                float zk = op.baseZ + op.body * bps[k];
                float s = (zk - oz) / dz;
                if (s < 0 || s > 1400) continue;
                Console.WriteLine(string.Format("  {0,3} {1,10:F6} {2,12:F5} {3,14:F5} {4,8} {5,8} {6,8}",
                    k, bps[k], zk, s, p(s - 1e-3f), p(s), p(s + 1e-3f)));
            }

            // the contradiction: at the slab entry, is the contained prism claiming SOLID while the
            // exact predicate says AIR?
            Console.WriteLine();
            Console.WriteLine("  THE CONTRADICTION — at the entry of the slab that contains the missed span:");
            for (int k = 0; k + 1 < nb; k++)
            {
                float zetaLo = bps[k], zetaHi = bps[k + 1];
                if (!(zetaHi > zetaLo)) continue;
                float za = op.baseZ + op.body * zetaLo, zb = op.baseZ + op.body * zetaHi;
                // descending ray: enters this slab at z = zb
                float s = (zb - oz) / dz;
                if (!(s > lo - 5 && s < lo + 5)) continue;
                float px = ox + s * dx;
                float d = ShaperEvaluator.Distance(prog, px, 0f, stack);
                float tauMax = ShaperHeight.InverseUpperBound(op, zetaHi);
                float gapIn = -d - tauMax * op.span;
                float bound = prog.bound > 0f ? prog.bound : 1f;
                float safe = gapIn / (bound * dx);
                Console.WriteLine("    slab k=" + k + "  zetaLo=" + zetaLo.ToString("F6") + " zetaHi=" + zetaHi.ToString("F6"));
                Console.WriteLine("      slab entry s = " + s.ToString("F5") + "   exact predicate says solid = " + p(s));
                Console.WriteLine("      d = " + d.ToString("F5") + "  tauMaxSlab = " + tauMax.ToString("F6") + "  gapIn = -d - tauMax*span = " + gapIn.ToString("F5"));
                Console.WriteLine("      gapIn > 0  =>  the CONTAINED prism proves SOLID here and skips " + safe.ToString("F4") + " px forward, to s = " + (s + safe).ToString("F4"));
                Console.WriteLine("      predicate at the landing point = " + p(s + safe));
                Console.WriteLine("      *** prism says SOLID, exact predicate says " + (p(s) ? "SOLID" : "AIR") + " — at the SAME point. One of them is wrong. ***");
                Console.WriteLine("      the true span end is " + hi.ToString("F4") + ", so the skip lands past it and mPrev never flips.");
            }
            break;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3C — N1 attacks: degenerate breakpoint runs, the OUTER boundaries, lastSlab.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void C_N1()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3C — N1: half-open slabs under degenerate breakpoint sets and at the OUTER planes ===");
        var root = Plate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];
        var bps = new float[512];

        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));

        // (a) how many combinations even HAVE a degenerate (duplicated) breakpoint run?
        int degCombos = 0, maxRun = 0; string degWhere = "";
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = MakeOp(prog, te, be, 40f, 4, 0.5f, 1f, 1f);
            int nb = ShaperHeight.Breakpoints(op, bps);
            int run = 1, best = 1; int bestAt = 0;
            for (int i = 1; i < nb; i++) { if (bps[i] == bps[i - 1]) { run++; if (run > best) { best = run; bestAt = i; } } else run = 1; }
            if (best > 1) { degCombos++; if (best > maxRun) { maxRun = best; degWhere = te + "+" + be + " at index " + bestAt; } }
        }
        Console.WriteLine("  (a) combinations with a DUPLICATED breakpoint in the emitted list: " + degCombos + " / " + (techs.Length * bevs.Length)
                        + "   longest equal run = " + maxRun + (degWhere == "" ? "" : "  (" + degWhere + ")"));
        Console.WriteLine("      -> Insert()'s 1e-7 dedup means the shipped list is strictly increasing; the trailing-degenerate");
        Console.WriteLine("         case `lastSlab` guards against is therefore NOT REACHABLE from any authored dial today.");

        // (b) a Z-PARALLEL ray at every plane of interest, on all 42 combinations.
        int rays = 0, wrongOuterTop = 0, wrongOuterBot = 0, dupInterior = 0;
        double worstErr = 0; string worstAt = "";
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = MakeOp(prog, te, be, 40f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            int nb = ShaperHeight.Breakpoints(op, bps);

            // planes: the two OUTER ones + every interior breakpoint
            var planes = new List<KeyValuePair<string, float>>();
            planes.Add(new KeyValuePair<string, float>("OUTER-bottom", op.baseZ));
            planes.Add(new KeyValuePair<string, float>("OUTER-top", op.baseZ + op.body * op.supG));
            for (int k = 1; k + 1 < nb; k++) planes.Add(new KeyValuePair<string, float>("interior[" + k + "]", op.baseZ + op.body * bps[k]));

            foreach (var pl in planes)
            {
                float oz = pl.Value;
                float ox = -400f, oy = 0f, dx = 1f, dz = 0f;
                var rr = ShaperResolve.Query(scene, ox, oy, oz, dx, 0f, dz, buf);
                rays++;
                Pred p = s => Solid(prog, stack, op, ox, oy, oz, dx, 0f, dz, s);
                var truth = Scan(p, 900.0, 900000);
                double trueLen = SolidLength(truth, p(0f), 900.0);
                double gotLen = 0; bool ins = false; double last = 0;
                for (int i = 0; i < rr.count; i++)
                {
                    if (buf[i].entering) { ins = true; last = buf[i].rayT; }
                    else if (ins) { gotLen += buf[i].rayT - last; ins = false; }
                }
                // duplicate rayT is the N1 signature
                int dups = 0;
                for (int i = 1; i < rr.count; i++) if (buf[i].rayT == buf[i - 1].rayT) dups++;
                double err = Math.Abs(trueLen - gotLen);
                if (err > worstErr) { worstErr = err; worstAt = te + "+" + be + " " + pl.Key + " true " + trueLen.ToString("F3") + " got " + gotLen.ToString("F3"); }
                if (err > 0.05)
                {
                    if (pl.Key == "OUTER-top") wrongOuterTop++;
                    else if (pl.Key == "OUTER-bottom") wrongOuterBot++;
                }
                if (dups > 0 && pl.Key.StartsWith("interior")) dupInterior++;
            }
        }
        Console.WriteLine("  (b) Z-PARALLEL rays fired = " + rays + " over 42 combinations (2 outer planes + every interior breakpoint)");
        Console.WriteLine("      rays wrong at the OUTER TOP plane    = " + wrongOuterTop + "   (the plane `lastSlab` exists to keep closed)");
        Console.WriteLine("      rays wrong at the OUTER BOTTOM plane = " + wrongOuterBot);
        Console.WriteLine("      interior rays still emitting a DUPLICATED rayT (the N1 signature) = " + dupInterior);
        Console.WriteLine("      worst |solid-length error| = " + worstErr.ToString("F4") + " px   at " + worstAt);

        // (c) hiInclusive propagation to the XY support-box clips
        Console.WriteLine("  (c) the two XY support-box clips pass hiInclusive = default true (outer extents).");
        Console.WriteLine("      probe: a ray running exactly ALONG the support box's +X face, and along its top edge.");
        {
            var op = MakeOp(prog, ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 40f, 4, 0.5f, 1f, 1f);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            float faceY = prog.supportCy + prog.supportHalfH;
            float oz = op.baseZ + 0.5f * op.body * op.supG;
            var rr = ShaperResolve.Query(scene, -400f, faceY, oz, 1f, 0f, 0f, buf);
            Pred p = s => Solid(prog, stack, op, -400f, faceY, oz, 1f, 0f, 0f, s);
            var truth = Scan(p, 900.0, 900000);
            Console.WriteLine("      ray along y = supportCy + supportHalfH = " + faceY.ToString("F4") + " : emitted " + rr.count + ", true flips " + truth.Count);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3D — N2: dz == 0 and dz == -0f, and monotonicity of emission in rayT.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void D_N2()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3D — N2: `descending = dz < 0f` at dz == 0f and dz == -0f, + emission monotonicity ===");
        Console.WriteLine("  -0f < 0f is FALSE in IEEE, so a negative-zero dz takes the ASCENDING branch. Does that matter?");
        Console.WriteLine("  It cannot: |dz| < 1e-9 means the ray never leaves one z, so every slab it is accepted by");
        Console.WriteLine("  spans the whole [s0,s1] and slab ORDER cannot change the ray parameters. Measured below.");

        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));

        int cases = 0, differ = 0, nonMono = 0;
        double worst = 0;
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = MakeOp(prog, te, be, 40f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var bps = new float[512];
            int nb = ShaperHeight.Breakpoints(op, bps);
            for (int k = 0; k < nb; k++)
            {
                float oz = op.baseZ + op.body * bps[k];
                var rp = ShaperResolve.Query(scene, -400f, 0f, oz, 1f, 0f, 0f, buf);
                var pos = new List<float>(); for (int i = 0; i < rp.count; i++) pos.Add(buf[i].rayT);
                var rn = ShaperResolve.Query(scene, -400f, 0f, oz, 1f, 0f, -0f, buf);
                var neg = new List<float>(); for (int i = 0; i < rn.count; i++) neg.Add(buf[i].rayT);
                cases++;
                if (pos.Count != neg.Count) { differ++; continue; }
                for (int i = 0; i < pos.Count; i++) { double e = Math.Abs(pos[i] - neg[i]); if (e > worst) worst = e; if (e > 1e-4) { differ++; break; } }
            }
        }
        Console.WriteLine("  dz=+0f vs dz=-0f over 42 combinations x every breakpoint plane: cases = " + cases
                        + "   results DIFFERING = " + differ + "   worst |rayT diff| = " + worst.ToString("E4") + " px");

        // emission monotonicity, per layer, for both ray-direction signs, WITHOUT truncation
        Console.WriteLine("  emission monotone in rayT per layer (probed by re-running with a buffer large enough that");
        Console.WriteLine("  nothing is dropped, then checking the array BEFORE the final global sort could matter):");
        int mrays = 0; nonMono = 0;
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = MakeOp(prog, te, be, 40f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int a = 0; a < 8; a++)
            {
                float ang = 20f + a * 8f;
                float rad = ang * Mathf.Deg2Rad;
                foreach (int sgn in new[] { -1, 1 })
                {
                    float dz = sgn * Mathf.Sin(rad), dx = Mathf.Cos(rad);
                    float oz = sgn < 0 ? op.baseZ + op.body * op.supG + 60f : op.baseZ - 60f;
                    var rr = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, buf);
                    mrays++;
                    for (int i = 1; i < rr.count; i++) if (buf[i].rayT < buf[i - 1].rayT) { nonMono++; break; }
                }
            }
        }
        Console.WriteLine("    rays = " + mrays + "   non-monotone after Query = " + nonMono + " (Query's global sort makes this a tautology; the");
        Console.WriteLine("    real N2 property is the TRUNCATION prefix, measured in W3E)");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3E — N2 truncation: a hostile cap sweep, and truncation ACROSS layers.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void E_Truncation()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3E — truncation: Depth must never OVER-report (HS-6.6), single AND multi layer ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));

        // --- single layer ---
        int cases = 0, over = 0, nonPrefix = 0, misStamp = 0;
        double worstOver = 0;
        var full = new ShaperCrossing[512];
        foreach (var te in techs)
        {
            var op = MakeOp(prog, te, ShaperBevelTechnique.Stepped, 300f, 4, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int a = 0; a < 7; a++)
            {
                float ang = 20f + a * 10f, rad = ang * Mathf.Deg2Rad;
                float dz = -Mathf.Sin(rad), dx = Mathf.Cos(rad);
                float oz = op.baseZ + op.body * op.supG + 40f;
                var rf = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, full);
                if (rf.count < 3) continue;
                double trueLen = 0; { bool ins = false; double last = 0; for (int i = 0; i < rf.count; i++) { if (full[i].entering) { ins = true; last = full[i].rayT; } else if (ins) { trueLen += full[i].rayT - last; ins = false; } } }
                var refT = new List<float>(); for (int i = 0; i < rf.count; i++) refT.Add(full[i].rayT);

                for (int cap = 1; cap < rf.count; cap++)
                {
                    var small = new ShaperCrossing[cap];
                    var rs = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, small);
                    if (!rs.truncated) continue;
                    cases++;
                    // prefix?
                    bool pref = true;
                    for (int i = 0; i < rs.count; i++) if (i >= refT.Count || Math.Abs(small[i].rayT - refT[i]) > 1e-3) { pref = false; break; }
                    if (!pref) nonPrefix++;
                    // parity
                    bool bad = false;
                    for (int i = 0; i < rs.count; i++)
                    {
                        int idx = -1; double best = 1e30;
                        for (int j = 0; j < rf.count; j++) { double e = Math.Abs(full[j].rayT - small[i].rayT); if (e < best) { best = e; idx = j; } }
                        if (idx >= 0 && best < 1e-3 && full[idx].entering != small[i].entering) { bad = true; break; }
                    }
                    if (bad) misStamp++;
                    double gotLen = 0; { bool ins = false; double last = 0; for (int i = 0; i < rs.count; i++) { if (small[i].entering) { ins = true; last = small[i].rayT; } else if (ins) { gotLen += small[i].rayT - last; ins = false; } } }
                    if (gotLen - trueLen > 0.05) { over++; if (gotLen - trueLen > worstOver) worstOver = gotLen - trueLen; }
                }
            }
        }
        Console.WriteLine("  SINGLE LAYER: truncated cases = " + cases + "   non-prefix = " + nonPrefix
                        + "   mis-stamped parity = " + misStamp + "   Depth OVER-reporting = " + over + "   worst over-report = " + worstOver.ToString("F3") + " px");

        // --- MULTI LAYER: the fix report's own open question #2 ---
        Console.WriteLine("  MULTI LAYER (FIX-REPORT-2's open question 2 — layer 0 starving layer 1):");
        {
            var opA = MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.Stepped, 60f, 8, 0.5f, 1f, 1f, 8, 45f, 0f);
            var opB = MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.Stepped, 60f, 8, 0.5f, 1f, 1f, 8, 45f, 120f);
            var scene = new ShaperResolveScene(); scene.Add(prog, opA); scene.Add(prog, opB);
            int mcases = 0, mover = 0, mmis = 0, mnonpref = 0; double mworst = 0; string mworstAt = "";
            for (int a = 0; a < 9; a++)
            {
                float ang = 15f + a * 8f, rad = ang * Mathf.Deg2Rad;
                float dz = -Mathf.Sin(rad), dx = Mathf.Cos(rad);
                float oz = 260f;
                var rf = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, full);
                if (rf.count < 4) continue;
                double trueLen = 0; { bool ins = false; double last = 0; for (int i = 0; i < rf.count; i++) { if (full[i].entering) { ins = true; last = full[i].rayT; } else if (ins) { trueLen += full[i].rayT - last; ins = false; } } }
                var refT = new List<float>(); for (int i = 0; i < rf.count; i++) refT.Add(full[i].rayT);
                for (int cap = 1; cap < rf.count; cap++)
                {
                    var small = new ShaperCrossing[cap];
                    var rs = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, small);
                    if (!rs.truncated) continue;
                    mcases++;
                    bool pref = true;
                    for (int i = 0; i < rs.count; i++) if (i >= refT.Count || Math.Abs(small[i].rayT - refT[i]) > 1e-3) { pref = false; break; }
                    if (!pref) mnonpref++;
                    bool bad = false;
                    for (int i = 0; i < rs.count; i++)
                    {
                        int idx = -1; double best = 1e30;
                        for (int j = 0; j < rf.count; j++) { double e = Math.Abs(full[j].rayT - small[i].rayT); if (e < best) { best = e; idx = j; } }
                        if (idx >= 0 && best < 1e-3 && full[idx].entering != small[i].entering) { bad = true; break; }
                    }
                    if (bad) mmis++;
                    double gotLen = 0; { bool ins = false; double last = 0; for (int i = 0; i < rs.count; i++) { if (small[i].entering) { ins = true; last = small[i].rayT; } else if (ins) { gotLen += small[i].rayT - last; ins = false; } } }
                    if (gotLen - trueLen > 0.05) { mover++; if (gotLen - trueLen > mworst) { mworst = gotLen - trueLen; mworstAt = "ang=" + ang + " cap=" + cap + " full=" + rf.count + " got " + gotLen.ToString("F3") + " true " + trueLen.ToString("F3"); } }
                }
            }
            Console.WriteLine("    two layers (base 0 and base 120), truncated cases = " + mcases
                            + "   non-prefix = " + mnonpref + "   mis-stamped = " + mmis + "   Depth OVER-reporting = " + mover);
            Console.WriteLine("    worst over-report = " + mworst.ToString("F3") + " px" + (mworstAt == "" ? "" : "   at " + mworstAt));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3F — N3: does `G > 0` create a NEW discontinuity, and what about G *nearly* zero?
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void F_N3()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3F — N3: no zero-width pairs; nothing lost vs the OLD closed reading; G nearly-zero ===");
        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();
        var buf = new ShaperCrossing[512];
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));

        int rays = 0, zeroWidth = 0, lostVsClosed = 0;
        double worstLoss = 0; string worstAt = "";
        foreach (var te in techs) foreach (var be in bevs)
        {
            var op = MakeOp(prog, te, be, 300f, 5, 0.5f, 1f, 1f);
            if (!op.present || op.body <= 0f) continue;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            for (int a = 0; a < 6; a++)
            {
                float ang = 8f + a * 11f, rad = ang * Mathf.Deg2Rad;
                float dz = -Mathf.Sin(rad), dx = Mathf.Cos(rad);
                float oz = op.baseZ + op.body * op.supG + 30f;
                var rr = ShaperResolve.Query(scene, -400f, 0f, oz, dx, 0f, dz, buf);
                rays++;
                for (int i = 1; i < rr.count; i++)
                    if (Math.Abs(buf[i].rayT - buf[i - 1].rayT) <= 1e-5f && buf[i - 1].entering && !buf[i].entering) zeroWidth++;

                Pred pc = s => SolidClosed(prog, stack, op, -400f, 0f, oz, dx, 0f, dz, s);
                var truthClosed = Scan(pc, 1400.0, 700000);
                double closedLen = SolidLength(truthClosed, pc(0f), 1400.0);
                double gotLen = 0; { bool ins = false; double last = 0; for (int i = 0; i < rr.count; i++) { if (buf[i].entering) { ins = true; last = buf[i].rayT; } else if (ins) { gotLen += buf[i].rayT - last; ins = false; } } }
                double loss = closedLen - gotLen;
                if (loss > 0.05) { lostVsClosed++; if (loss > worstLoss) { worstLoss = loss; worstAt = te + "+" + be + " ang=" + ang; } }
            }
        }
        Console.WriteLine("  rays = " + rays);
        Console.WriteLine("  emitted pairs of width <= 1e-5 px (the N3 signature) = " + zeroWidth);
        Console.WriteLine("  rays where Depth LOST solid vs the OLD CLOSED reading = " + lostVsClosed + "   worst " + worstLoss.ToString("F4") + " px " + worstAt);

        // G nearly zero — the fix report's open question 3
        Console.WriteLine("  G *nearly* zero (FIX-REPORT-2 open question 3): Round curve=4, smallest positive G on a fine t grid:");
        {
            var op = MakeOp(prog, ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None, 300f, 4, 0.5f, 4f, 1f);
            float smallest = float.MaxValue; float atT = 0;
            for (int i = 1; i <= 2000000; i++)
            {
                float t = i / 2000000f;
                float g = ShaperHeight.Composed(op, t, 0f, 0f);
                if (g > 0f && g < smallest) { smallest = g; atT = t; }
            }
            Console.WriteLine("    smallest positive G = " + smallest.ToString("E4") + " at t = " + atT.ToString("E4")
                            + "  -> solid thickness there = " + (smallest * op.body).ToString("E4") + " canvas px");
            Console.WriteLine("    (a span this thin is far below SurfaceResolution 0.02 px; the march resolves 0.019 px)");
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            int nz = 0;
            for (int a = 0; a < 12; a++)
            {
                float ang = 4f + a * 7f, rad = ang * Mathf.Deg2Rad;
                float dz = -Mathf.Sin(rad), dx = Mathf.Cos(rad);
                var rr = ShaperResolve.Query(scene, -400f, 0f, op.baseZ + op.body * op.supG + 20f, dx, 0f, dz, buf);
                for (int i = 1; i < rr.count; i++)
                    if (Math.Abs(buf[i].rayT - buf[i - 1].rayT) <= 1e-4f && buf[i - 1].entering && !buf[i].entering) nz++;
            }
            Console.WriteLine("    Round curve=4, 12 tilted rays: emitted pairs narrower than 1e-4 px = " + nz);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3G — N4: is InverseAtEUpperBound an upper bound?  My own grid, DOUBLE reference.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void G_N4()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3G — N4: InverseAtEUpperBound as an UPPER bound. My grid; the test is the SHIPPED one ===");
        Console.WriteLine("  The claim under test (ShaperResolve.cs, per-step bracket): tau = InverseAtEUpperBound(op, wHi, eLo)");
        Console.WriteLine("  is such that t >= tau  =>  G(t) >= wHi.  I check exactly that, at t = tau itself, in float,");
        Console.WriteLine("  using the SHIPPED Composed() — so no reference of mine can be blamed for a violation.");
        var root = Plate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var techs = (ShaperExtrusionTechnique[])Enum.GetValues(typeof(ShaperExtrusionTechnique));
        var bevs = (ShaperBevelTechnique[])Enum.GetValues(typeof(ShaperBevelTechnique));
        float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };

        long checksU = 0, checksR = 0; int violU = 0, violR = 0;
        double worstU = 0, worstR = 0; string atU = "", atR = "";
        foreach (var te in techs) foreach (var be in bevs) foreach (float am in amounts)
        {
            var op = MakeOp(prog, te, be, 40f, 4, am, 1f, 1f);
            if (!op.present) continue;
            for (int i = 0; i <= 800; i++)
            {
                float zeta = i / 800f * op.supG;
                float tauU = ShaperHeight.InverseAtEUpperBound(op, zeta, 1f);
                if (!ShaperHeight.IsNoCrossSection(tauU))
                {
                    checksU++;
                    float g = ShaperHeight.Composed(op, tauU, 0f, 0f);
                    if (g < zeta) { violU++; double sh = (double)zeta - g; if (sh > worstU) { worstU = sh; atU = te + "+" + be + " a=" + am + " zeta=" + zeta.ToString("F7") + " tau=" + tauU.ToString("F9") + " G(tau)=" + g.ToString("F9"); } }
                }
                float tauR = ShaperHeight.InverseAtE(op, zeta, 1f);
                if (!ShaperHeight.IsNoCrossSection(tauR))
                {
                    checksR++;
                    float g = ShaperHeight.Composed(op, tauR, 0f, 0f);
                    if (g < zeta) { violR++; double sh = (double)zeta - g; if (sh > worstR) { worstR = sh; atR = te + "+" + be + " a=" + am + " zeta=" + zeta.ToString("F7"); } }
                }
            }
        }
        Console.WriteLine("  InverseAtE           (pre-N4): checks=" + checksR + "  G(tau) < zeta in " + violR + "   worst shortfall " + worstR.ToString("E4"));
        Console.WriteLine("      worst: " + atR);
        Console.WriteLine("  InverseAtEUpperBound (shipped): checks=" + checksU + "  G(tau) < zeta in " + violU + "   worst shortfall " + worstU.ToString("E4"));
        Console.WriteLine("      worst: " + atU);
        Console.WriteLine("  FIX-REPORT-2's N4 row claims 0 violations for the shipped function.");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // W3H — N6: the Stepped inverses, and whether InverseLowerBound still CONTAINS.
    // ═════════════════════════════════════════════════════════════════════════════════════════════
    public static void H_N6()
    {
        Console.WriteLine();
        Console.WriteLine("=== W3H — N6: Stepped inverse round-trip, and the CONTAINING prism (HS-5.2) ===");
        var root = Plate(200f, 200f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);

        // (a) round-trip: G(Ginv(zeta)) >= zeta at every tread, +/- 4 ulps
        int probes = 0, shortfalls = 0; double worstShort = 0; string atShort = "";
        // (b) containment: is Ginv(zeta) <= the TRUE infimum { t : G(t) >= zeta } ?
        int cprobes = 0, over = 0; double worstOver = 0; string atOver = "";
        for (int n = 2; n <= 32; n++)
        {
            var op = MakeOp(prog, ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 40f, n, 0.5f, 1f, 1f);
            for (int k = 0; k <= n - 1; k++)
            {
                float zeta = (n - 1) <= 0 ? 0f : (float)k / (n - 1);
                for (int u = -4; u <= 4; u++)
                {
                    float z = zeta;
                    for (int q = 0; q < Math.Abs(u); q++) z = u > 0 ? NextUp(z) : NextDown(z);
                    if (z < 0f || z > op.supG) continue;
                    float tau = ShaperHeight.InverseLowerBound(op, z);
                    if (ShaperHeight.IsNoCrossSection(tau)) continue;
                    probes++;
                    float g = ShaperHeight.Composed(op, tau, 0f, 0f);
                    if (g < z) { shortfalls++; double sh = (double)z - g; if (sh > worstShort) { worstShort = sh; atShort = "n=" + n + " k=" + k + " ulp=" + u; } }

                    // TRUE infimum by scan over the exact tread lattice
                    float trueInf = float.MaxValue;
                    for (int j = 0; j <= n; j++)
                    {
                        float t = j / (float)n;
                        if (t > 1f) t = 1f;
                        if (ShaperHeight.Composed(op, t, 0f, 0f) >= z) { trueInf = t; break; }
                    }
                    if (trueInf == float.MaxValue) continue;
                    cprobes++;
                    if (tau > trueInf + 1e-7f)
                    {
                        over++;
                        double e = (double)tau - trueInf;
                        if (e > worstOver) { worstOver = e; atOver = "n=" + n + " k=" + k + " ulp=" + u + " zeta=" + z.ToString("F7") + "  Ginv=" + tau.ToString("F7") + "  trueInf=" + trueInf.ToString("F7") + "  (1 tread = " + (1f / n).ToString("F7") + ")"; }
                    }
                }
            }
        }
        Console.WriteLine("  (a) reach: probes=" + probes + "  G(Ginv(zeta)) < zeta in " + shortfalls + "   worst " + worstShort.ToString("E4") + " " + atShort);
        Console.WriteLine("  (b) CONTAINMENT (HS-5.2, the unsound direction): probes=" + cprobes
                        + "   Ginv OVER-reports the true infimum in " + over);
        Console.WriteLine("      worst over-report = " + worstOver.ToString("E4") + " t-units");
        if (atOver != "") Console.WriteLine("      " + atOver);
    }

    static float NextUp(float x) { if (x == 0f) return float.Epsilon; int b = BitConverter.SingleToInt32Bits(x); return BitConverter.Int32BitsToSingle(x > 0 ? b + 1 : b - 1); }
    static float NextDown(float x) { if (x == 0f) return -float.Epsilon; int b = BitConverter.SingleToInt32Bits(x); return BitConverter.Int32BitsToSingle(x > 0 ? b - 1 : b + 1); }

    public static void Run(string sel)
    {
        if (sel == "all" || sel == "A") A_LostCrossings();
        if (sel == "all" || sel == "B") B_Mechanism();
        if (sel == "all" || sel == "C") C_N1();
        if (sel == "all" || sel == "D") D_N2();
        if (sel == "all" || sel == "E") E_Truncation();
        if (sel == "all" || sel == "F") F_N3();
        if (sel == "all" || sel == "G") G_N4();
        if (sel == "all" || sel == "H") H_N6();
    }
}
