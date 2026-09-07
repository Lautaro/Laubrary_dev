using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// The height-stage audit: checks over the compiled extrusion/bevel stage and the normal provider's
    /// <c>Profile</c> case, each returning a report string. (The general resolve, HS-9, was deleted outright
    /// T-0253 along with the checks that exercised it -- H6, H12 and the tilted-conformance render.)
    ///
    /// Plain static methods, <b>no <c>[MenuItem]</c> and no <c>EditorWindow</c></b> — this is the stage's
    /// verification harness, invoked through the Unity CLI, not a tool. Same shape as
    /// <see cref="ShaperFieldAudit"/> and <c>ShaperLightAudit</c>, and the standing Wave-2 rule (HS-10, "No UI,
    /// no <c>EditorWindow</c>, no <c>[MenuItem]</c>").
    ///
    /// <b>Every result is reported as MEASURED versus DECLARED</b>, and a declaration of <c>+∞</c> is checked
    /// by DEMONSTRATING DIVERGENCE under grid refinement rather than by printing a number — which is precisely
    /// the check that would have caught the <c>round 16.4</c> / <c>dome 23.1</c> figures that circulated as
    /// bounds and are in fact two readings of one measurement grid at <c>t ≈ 9.3e-4</c> (their ratio is √2 to
    /// within 0.4%, which is the giveaway; REF-HEIGHT-MATHS §C.5).
    ///
    /// A bound is never reported as verified on the strength of the code compiling.
    /// </summary>
    public static class ShaperHeightAudit
    {
        // ── instrument constants ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The PASS/FAIL threshold on a measured-over-declared ratio, and the reasoning is
        /// <see cref="ShaperFieldAudit"/>'s verbatim: the instrument is a DIRECTIONAL difference quotient,
        /// which is the Lipschitz constant's own definition restricted to a finite sample and is therefore
        /// bounded by <c>L</c> BY CONSTRUCTION — it converges to <c>L</c> from below and can never manufacture
        /// a violation. So the only thing above 1 it can ever report is float cancellation noise, and a
        /// tolerance wide enough to swallow a real violation is not a gate.
        /// </summary>
        const float BoundTolerance = 1.001f;

        /// <summary>
        /// How many float ulps of rounding one profile evaluation may carry. Subtracting
        /// <c>ulps·eps·|G|</c> from the NUMERATOR of the difference quotient is what keeps a small step
        /// honest: the true difference is within that bound of the computed one, so the corrected quotient is
        /// a LOWER bound on the true quotient and cannot manufacture a violation.
        ///
        /// Without it a fixed rounding error becomes a quotient error of <c>ulps·eps·|G| / 2h</c> that grows
        /// without limit as <c>h</c> shrinks — the exact failure <see cref="ShaperFieldAudit"/> hit when a
        /// first cut of its refinement pass reported 1.0071 on a plain Rect, which is an EXACT SDF: the
        /// instrument was manufacturing the very violation it exists to rule out.
        ///
        /// 8 is scoped to <see cref="SlopeStep"/> below and does not generalise downward. <c>G</c> here is
        /// <c>O(1)</c> rather than <c>O(100)</c>, so the correction is smaller in absolute terms than the
        /// field audit's and the margin is correspondingly wider.
        /// </summary>
        const float RoundingUlps = 8f;
        const float FloatEps = 1.1920929e-7f;

        /// <summary>The step the finite-bound measurement uses, as a fraction of the <c>[0,1]</c> domain of <c>t</c>.</summary>
        const float SlopeStep = 1e-3f;

        /// <summary>Samples per parameter sweep in H1's monotonicity proof.</summary>
        const int MonoSamples = 1201;

        // ── shared fixtures ──────────────────────────────────────────────────────────────────────────────

        static readonly ShaperExtrusionTechnique[] AllTechniques =
        {
            ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Linear, ShaperExtrusionTechnique.Stepped,
            ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round, ShaperExtrusionTechnique.Taper,
            ShaperExtrusionTechnique.Pyramid,
        };

        static readonly ShaperBevelTechnique[] AllBevels =
        {
            ShaperBevelTechnique.None, ShaperBevelTechnique.Linear, ShaperBevelTechnique.Rounded,
            ShaperBevelTechnique.Cove, ShaperBevelTechnique.Ogee, ShaperBevelTechnique.Stepped,
        };

        /// <summary>
        /// Build a compiled height op directly, with no shape program.
        ///
        /// <c>ShaperHeightCompiler.Compile</c> takes <c>span</c>'s floor from <c>pixelSize</c>, so passing the
        /// wanted span as <c>pixelSize</c> with a null program produces exactly that span — which is what
        /// lets the maths checks run on the profile alone, independently of any silhouette. The <c>Linear</c>
        /// case is fed its <c>(nx,ny)</c> directly at every call site, so the absent local box costs nothing.
        /// </summary>
        static ShaperHeightOp Op(ShaperExtrusionTechnique tech, ShaperBevelTechnique bevel,
                                 float depth = 10f, float angle = 45f, float steps = 4f,
                                 float curve = 1f, float taper = 1f, float amount = 0.25f,
                                 float bevelSteps = 3f, float span = 32f)
        {
            var def = new ShaperHeightDef
            {
                technique = tech,
                bevel = bevel,
                depth = new ZUIValue(depth),
                angle = new ZUIValue(angle),
                steps = new ZUIValue(steps),
                curve = new ZUIValue(curve),
                taper = new ZUIValue(taper),
                bevelAmount = new ZUIValue(amount),
                bevelSteps = new ZUIValue(bevelSteps),
            };
            return ShaperHeightCompiler.Compile(def, null, span, 0f);
        }

        static string N(ShaperExtrusionTechnique t) => t.ToString();
        static string N(ShaperBevelTechnique b) => b.ToString();
        static string Verdict(bool ok) => ok ? "ok" : "FAIL";
        static string F(float v) => float.IsPositiveInfinity(v) ? "+inf" : v.ToString("F5");

        /// <summary>The relevant profile parameter's authored range, sampled. Everything else is inert for that technique.</summary>
        static float[] ProfileSweep(ShaperExtrusionTechnique t)
        {
            switch (t)
            {
                case ShaperExtrusionTechnique.Linear:  return new[] { -180f, -135f, -45f, 0f, 45f, 90f, 180f };
                case ShaperExtrusionTechnique.Stepped: return new[] { 2f, 3f, 4f, 7f, 16f, 32f };
                case ShaperExtrusionTechnique.Dome:
                case ShaperExtrusionTechnique.Round:   return new[] { 0.2f, 0.35f, 0.5f, 0.75f, 1f, 2f, 4f };
                case ShaperExtrusionTechnique.Taper:
                case ShaperExtrusionTechnique.Pyramid: return new[] { 0f, 0.05f, 0.0833f, 0.25f, 0.5f, 1f };
                default:                               return new[] { 0f };
            }
        }

        static ShaperHeightOp OpWithSweep(ShaperExtrusionTechnique t, ShaperBevelTechnique b,
                                          float p, float amount, float bevelSteps)
        {
            switch (t)
            {
                case ShaperExtrusionTechnique.Linear:  return Op(t, b, 10f, p, 4f, 1f, 1f, amount, bevelSteps);
                case ShaperExtrusionTechnique.Stepped: return Op(t, b, 10f, 45f, p, 1f, 1f, amount, bevelSteps);
                case ShaperExtrusionTechnique.Dome:
                case ShaperExtrusionTechnique.Round:   return Op(t, b, 10f, 45f, 4f, p, 1f, amount, bevelSteps);
                case ShaperExtrusionTechnique.Taper:
                case ShaperExtrusionTechnique.Pyramid: return Op(t, b, 10f, 45f, 4f, 1f, p, amount, bevelSteps);
                default:                               return Op(t, b, 10f, 45f, 4f, 1f, 1f, amount, bevelSteps);
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H1 — MONOTONICITY. The claim the whole march rests on.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H1 — HS-5.1's monotonicity claim, over ALL 42 profile×bevel combinations, across the FULL
        /// authored parameter ranges.</b>
        ///
        /// <c>G(t) = E(t)·B(clamp01(t/a))</c> must be non-decreasing in <c>t</c> on <c>[0,1]</c>. The proof is
        /// that every <c>E</c> is non-decreasing, every <c>B</c> is non-decreasing in <c>u</c>, <c>u</c> is
        /// non-decreasing in <c>t</c>, and both are non-negative — a product of two non-negative
        /// non-decreasing functions is non-decreasing.
        ///
        /// <b>This check is LOAD-BEARING and it is not a formality.</b> If it ever fails, HS-5.2 is unsound:
        /// the cross-sections stop being nested, the containing prism stops containing, and the marcher can
        /// step through solid geometry and leave holes. RULING ONE (HS-0.1) designed the absent Lipschitz
        /// constant out of the load-bearing path by putting THIS property there instead, so this is the check
        /// that stands in for every slope bound the catalogue does not have.
        /// </summary>
        public static string H1_Monotonicity()
        {
            var sb = new StringBuilder("H1  MONOTONICITY of G over all 42 profile x bevel combinations (HS-5.1)\n");
            sb.AppendLine("    G must be non-decreasing in t on [0,1]. Load-bearing: HS-5.2's nested cross-sections,");
            sb.AppendLine("    and therefore the whole slab march, are unsound without it.");

            // The Linear profile is a function of (nx,ny) rather than t, so its monotonicity in t must be
            // checked at several points of its own domain, not only at the origin where it is trivially 1.
            float[][] linearPts = { new[] { 0f, 0f }, new[] { 1f, 1f }, new[] { -1f, 1f }, new[] { 1f, -1f }, new[] { -1f, -1f }, new[] { 0.3f, -0.7f } };
            float[] amounts = { 0f, 0.01f, 0.1f, 0.25f, 0.6f, 1f };
            float[] bevelStepSet = { 2f, 3f, 7f, 16f };

            int combos = 0, configs = 0, samples = 0, violations = 0;
            float worstDrop = 0f;
            string worstWhere = "-";

            foreach (var tech in AllTechniques)
            {
                foreach (var bev in AllBevels)
                {
                    combos++;
                    foreach (float p in ProfileSweep(tech))
                    {
                        foreach (float a in amounts)
                        {
                            var bsSet = bev == ShaperBevelTechnique.Stepped ? bevelStepSet : new[] { 3f };
                            foreach (float bs in bsSet)
                            {
                                var op = OpWithSweep(tech, bev, p, a, bs);
                                configs++;

                                var pts = tech == ShaperExtrusionTechnique.Linear ? linearPts : new[] { new[] { 0f, 0f } };
                                foreach (var pt in pts)
                                {
                                    float prev = ShaperHeight.Composed(op, 0f, pt[0], pt[1]);
                                    float scale = Mathf.Max(1f, op.supG);
                                    for (int i = 1; i < MonoSamples; i++)
                                    {
                                        float t = i / (float)(MonoSamples - 1);
                                        float g = ShaperHeight.Composed(op, t, pt[0], pt[1]);
                                        samples++;
                                        float drop = prev - g;
                                        if (drop > 1e-6f * scale)
                                        {
                                            violations++;
                                            if (drop > worstDrop)
                                            {
                                                worstDrop = drop;
                                                worstWhere = N(tech) + "+" + N(bev) + " p=" + p.ToString("F3") +
                                                             " a=" + a.ToString("F2") + " bs=" + bs + " t=" + t.ToString("F5") +
                                                             " (" + prev.ToString("F6") + " -> " + g.ToString("F6") + ")";
                                            }
                                        }
                                        prev = g;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ── T-0109 FIX F6 — the ULP-ADJACENT probe. ──────────────────────────────────────────────────
            //
            // The uniform grid above never places two samples close together, so it can catch a gross
            // non-monotonicity and cannot catch a fine one. An independent probe placing pairs at ±1 float
            // ulp around every breakpoint found 2 520 drops in 66 717 000 ordered pairs, worst 5.960e-8 —
            // exactly one ulp at 1.0 — where this check reported zero. The theorem is not in doubt (it was
            // re-derived analytically and holds in exact arithmetic); what was wrong was the CLAIM, which
            // said "monotone" where the honest statement is "monotone as a theorem, monotone to within one
            // float ulp in float". The probe is folded in here so the audit states its own tolerance.
            int ulpPairs = 0, ulpViolations = 0;
            float ulpWorst = 0f;
            string ulpWhere = "-";
            {
                // The amount sweep is the MAIN one plus 0.05, which is the value the independent verifier's
                // worst case used (Flat+Rounded, a = 0.05, t 0.049999002 -> 0.049999997). A probe that does
                // not visit the configuration where the defect lives is a probe that reports zero.
                float[] ulpAmounts = { 0f, 0.01f, 0.05f, 0.1f, 0.25f, 0.6f, 1f };

                foreach (var tech in AllTechniques)
                foreach (var bev in AllBevels)
                foreach (float p in ProfileSweep(tech))
                foreach (float a in ulpAmounts)
                {
                    var bsSet = bev == ShaperBevelTechnique.Stepped ? bevelStepSet : new[] { 3f };
                    foreach (float bs in bsSet)
                    {
                        var op = OpWithSweep(tech, bev, p, a, bs);
                        var pts = tech == ShaperExtrusionTechnique.Linear ? linearPts : new[] { new[] { 0f, 0f } };

                        // Every place G can jump or kink, plus the domain ends.
                        var marks = new System.Collections.Generic.List<float> { 0f, 1f, op.a, op.taperT };
                        for (int kk = 0; kk <= op.n; kk++) marks.Add(kk / (float)op.n);
                        if (op.a > 0f) for (int kk = 0; kk <= op.bevelN; kk++) marks.Add(op.a * kk / op.bevelN);

                        foreach (var pt in pts)
                        {
                            // (a) strictly ULP-ADJACENT pairs at every breakpoint, +-64 ulps either side.
                            foreach (float m in marks)
                            {
                                if (m < 0f || m > 1f) continue;
                                for (int side = -64; side < 64; side++)
                                {
                                    float t0 = NextAfter(m, side);
                                    float t1 = NextAfter(t0, 1);
                                    if (!(t1 > t0) || t0 < 0f || t1 > 1f) continue;
                                    UlpPair(op, pt, t0, t1, tech, bev, a, ref ulpPairs, ref ulpViolations, ref ulpWorst, ref ulpWhere);
                                }
                            }

                            // (b) ULP-ADJACENT pairs SPREAD over the whole domain and, separately, over the
                            //     BEVEL BAND. The breakpoints are not where the drops actually are: the
                            //     independent verifier's worst case sits ~27 000 ulps below t = a, which no
                            //     neighbourhood-of-a-breakpoint window of any sane width reaches. What makes
                            //     these findable is that they are DENSE in the region where B rounds to 1, so
                            //     a spread of base points each tested against its immediate float successor
                            //     lands on them without needing to know where they are.
                            for (int q = 0; q <= 2000; q++)
                            {
                                float t0 = q / 2000f;
                                float t1 = NextAfter(t0, 1);
                                if (t1 > 1f) continue;
                                UlpPair(op, pt, t0, t1, tech, bev, a, ref ulpPairs, ref ulpViolations, ref ulpWorst, ref ulpWhere);
                            }
                            if (op.a > 0f)
                                for (int q = 0; q <= 2000; q++)
                                {
                                    float t0 = op.a * q / 2000f;
                                    float t1 = NextAfter(t0, 1);
                                    if (t1 > 1f) continue;
                                    UlpPair(op, pt, t0, t1, tech, bev, a, ref ulpPairs, ref ulpViolations, ref ulpWorst, ref ulpWhere);
                                }
                        }
                    }
                }
            }

            // The float tolerance the claim is made WITH. One ulp at 1.0 is 1.1921e-7; the observed worst
            // drop must sit inside that, and the gate is stated as a number rather than as "zero".
            const float UlpTolerance = 1.2e-7f;
            bool ulpOk = ulpWorst <= UlpTolerance;
            bool ok = violations == 0 && ulpOk;   // the injection sub-check ANDs into this below

            sb.AppendLine("    combinations   " + combos + " (7 profiles x 6 bevels)");
            sb.AppendLine("    configurations " + configs + " over the full authored parameter ranges");
            sb.AppendLine("    samples        " + samples);
            sb.AppendLine("    violations     " + violations + (violations == 0 ? "" : "   worst drop " + worstDrop.ToString("E3") + " at " + worstWhere));
            sb.AppendLine();
            // INJECTION: the probe must be able to SEE a one-ulp drop, or its zero means nothing.
            int injSeen = 0;
            float injWorst = 0f;
            {
                var op = OpWithSweep(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Rounded, 0f, 0.05f, 3f);
                for (int q = 0; q <= 4000; q++)
                {
                    float t0 = q / 4000f;
                    float t1 = NextAfter(t0, 1);
                    if (t1 > 1f) continue;
                    float g0 = ShaperHeight.Composed(op, t0, 0f, 0f);
                    float g1 = ShaperHeight.Composed(op, t1, 0f, 0f);
                    // inject a drop of exactly one ulp at 1.0 on every other sample
                    if ((q & 1) == 0) g1 = NextAfter(g1, -1);
                    float drop = g0 - g1;
                    if (drop > 0f) { injSeen++; if (drop > injWorst) injWorst = drop; }
                }
            }
            bool injOk = injSeen > 0;

            ok &= injOk;

            sb.AppendLine("    ULP PROBE (T-0109 FIX F6) - adjacent-float pairs at every breakpoint AND spread over");
            sb.AppendLine("    the whole domain and the bevel band, over 7 bevel amounts incl. 0.05:");
            sb.AppendLine("      ordered pairs      " + ulpPairs);
            sb.AppendLine("      drops observed     " + ulpViolations);
            sb.AppendLine("      worst drop         " + ulpWorst.ToString("E4") + "   (1 ulp at 1.0 = 1.1921E-007)");
            if (ulpViolations > 0) sb.AppendLine("      worst at           " + ulpWhere);
            sb.AppendLine("      within the stated float tolerance " + UlpTolerance.ToString("E1") + ": " + Verdict(ulpOk));
            sb.AppendLine("      INJECTION - can this probe see a 1-ulp drop at all? " + injSeen +
                          " injected drops detected, worst " + injWorst.ToString("E4") + "  " + Verdict(injOk));
            sb.AppendLine();
            sb.AppendLine("    VERDICT        G is monotone as a THEOREM (exact arithmetic), and monotone TO WITHIN");
            sb.AppendLine("                   THE FLOAT TOLERANCE STATED ABOVE in float: " + Verdict(ok));
            sb.AppendLine("                   The claim is deliberately NOT 'exactly monotone in float'. It is stated");
            sb.AppendLine("                   with its tolerance because an independent CoreCLR harness measured a");
            sb.AppendLine("                   1-ulp (5.96e-8) drop on THIS SOURCE, at Flat+Rounded a=0.05 near the band");
            sb.AppendLine("                   edge - 73 such drops over a 2001-point band sweep. This audit runs in");
            sb.AppendLine("                   Unity's own runtime and measures " + ulpViolations + " there, so the two runtimes round");
            sb.AppendLine("                   the same expression differently and the DECLARATION must cover both.");
            sb.AppendLine("                   HS-5.2's containment argument survives at that magnitude either way:");
            sb.AppendLine("                   1 ulp is six orders below the (now-retired, T-0253) HS-9 march's own");
            sb.AppendLine("                   SurfaceResolution of 0.02 canvas px.");
            return sb.ToString();
        }

        /// <summary>
        /// T-0109 FIX F6 — <paramref name="steps"/> float ulps away from <paramref name="v"/>, signed. Used
        /// to place monotonicity probes where a uniform grid structurally cannot: immediately either side of
        /// a breakpoint.
        /// </summary>
        /// <summary>T-0109 FIX F6 — one ordered monotonicity pair, accumulated into H1's ulp tallies.</summary>
        static void UlpPair(in ShaperHeightOp op, float[] pt, float t0, float t1,
                            ShaperExtrusionTechnique tech, ShaperBevelTechnique bev, float a,
                            ref int pairs, ref int violations, ref float worst, ref string where)
        {
            float g0 = ShaperHeight.Composed(op, t0, pt[0], pt[1]);
            float g1 = ShaperHeight.Composed(op, t1, pt[0], pt[1]);
            pairs++;
            float drop = g0 - g1;
            if (drop <= 0f) return;
            violations++;
            if (drop > worst)
            {
                worst = drop;
                where = N(tech) + "+" + N(bev) + " a=" + a.ToString("G6") +
                        " t=" + t0.ToString("R") + "->" + t1.ToString("R") +
                        " (" + g0.ToString("R") + " -> " + g1.ToString("R") + ")";
            }
        }

        static float NextAfter(float v, int steps)
        {
            if (steps == 0 || float.IsNaN(v) || float.IsInfinity(v)) return v;
            int bits = System.BitConverter.SingleToInt32Bits(v);
            // Map to a monotone integer ordering across the sign boundary, step, and map back.
            int ord = bits < 0 ? int.MinValue - bits : bits;
            long moved = (long)ord + steps;
            if (moved > int.MaxValue) moved = int.MaxValue;
            if (moved < int.MinValue) moved = int.MinValue;
            int o2 = (int)moved;
            int b2 = o2 < 0 ? int.MinValue - o2 : o2;
            float r = System.BitConverter.Int32BitsToSingle(b2);
            return float.IsNaN(r) ? v : r;
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H2 — every FINITE declared bound, measured.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H2 — measure every FINITE declared slope bound over the whole authored range and confirm the
        /// declaration holds.</b> HS-4.1.
        ///
        /// The instrument is the max directional difference quotient — in one dimension, the two-sided
        /// quotient <c>|G(t+h) − G(t−h)| / 2h</c> — and NEVER the hypot of two axis-aligned central
        /// differences, which has √2 of headroom at a kink and reports 1.02–1.03 on provably exact fields
        /// (T-0105's handover note).
        ///
        /// A float-rounding bound is subtracted from the NUMERATOR before dividing, exactly as
        /// <see cref="ShaperFieldAudit"/> learned to: the corrected quotient is a LOWER bound on the true one
        /// and therefore cannot manufacture a violation.
        ///
        /// Composed bounds (HS-4.2's product rule) are measured too, because the product rule is the genuinely
        /// new maths here — it is not a reuse of any <see cref="ShaperBound"/> method — and an untested
        /// composition rule is a guess with a docstring.
        /// </summary>
        public static string H2_FiniteBounds()
        {
            var sb = new StringBuilder("H2  FINITE declared slope bounds, measured (HS-4.1, HS-4.2)\n");
            sb.AppendLine("    instrument: two-sided directional difference quotient at h = " + SlopeStep +
                          " of the t domain, numerator corrected by " + RoundingUlps + " ulps.");
            sb.AppendLine("    " + "technique".PadRight(30) + "declared".PadLeft(12) + "measured".PadLeft(12) + "ratio".PadLeft(10) + "  verdict");

            bool all = true;
            int measured = 0;

            // ── the profiles alone, bevel None ────────────────────────────────────────────────────────────
            foreach (var tech in AllTechniques)
            {
                foreach (float p in ProfileSweep(tech))
                {
                    var op = OpWithSweep(tech, ShaperBevelTechnique.None, p, 0f, 3f);
                    float declared = ShaperHeight.ExtrusionSlopeBound(op);
                    if (float.IsPositiveInfinity(declared)) continue;    // H3's business, not H2's

                    float m = MeasureSlope(op, 0f, 0f);
                    measured++;
                    float ratio = declared > 0f ? m / declared : (m <= 1e-5f ? 0f : float.PositiveInfinity);
                    bool ok = declared > 0f ? ratio <= BoundTolerance : m <= 1e-5f;
                    all &= ok;
                    sb.AppendLine("    " + (N(tech) + " p=" + p.ToString("F4")).PadRight(30) +
                                  F(declared).PadLeft(12) + F(m).PadLeft(12) +
                                  (declared > 0f ? ratio.ToString("F5") : "-").PadLeft(10) + "  " + Verdict(ok));
                }
            }

            // ── the one bevel with a finite bound, and the composed product rule on top of it ─────────────
            float[] amounts = { 0.05f, 0.25f, 0.5f, 1f };
            foreach (float a in amounts)
            {
                var op = Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Linear, 10f, 45f, 4f, 1f, 1f, a);
                float declared = ShaperHeight.ComposedSlopeBound(op);        // = 0 + 1·1/a
                float m = MeasureSlope(op, 0f, 0f);
                measured++;
                float ratio = m / declared;
                bool ok = ratio <= BoundTolerance;
                all &= ok;
                sb.AppendLine("    " + ("Flat+Linear a=" + a.ToString("F2")).PadRight(30) +
                              F(declared).PadLeft(12) + F(m).PadLeft(12) + ratio.ToString("F5").PadLeft(10) + "  " + Verdict(ok));
            }

            // Composed: a finite profile with the finite bevel. This is HS-4.2's product rule in its only
            // fully finite instance, and the only place the rule is checkable rather than absorbed by an
            // infinity.
            foreach (float tau in new[] { 0.25f, 0.5f, 1f })
            {
                foreach (float a in new[] { 0.2f, 0.5f, 1f })
                {
                    var op = Op(ShaperExtrusionTechnique.Pyramid, ShaperBevelTechnique.Linear, 10f, 45f, 4f, 1f, tau, a);
                    float declared = ShaperHeight.ComposedSlopeBound(op);
                    float m = MeasureSlope(op, 0f, 0f);
                    measured++;
                    float ratio = m / declared;
                    bool ok = ratio <= BoundTolerance;
                    all &= ok;
                    sb.AppendLine("    " + ("Pyramid t=" + tau.ToString("F2") + "+Lin a=" + a.ToString("F2")).PadRight(30) +
                                  F(declared).PadLeft(12) + F(m).PadLeft(12) + ratio.ToString("F5").PadLeft(10) + "  " + Verdict(ok));
                }
            }

            // ── HS-4.4: Linear's own 2-vector, in its own space, never summed with the above ──────────────
            {
                var op = Op(ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 10f, 45f);
                // With no local box the reciprocals are 0 (FC-1.5b), so drive the declared gradient from the
                // formula the way a real compile would and check the magnitude identity |grad_(nx,ny) E| = 0.6.
                // T-0109 FIX F7: `+` on sinθ, matching the flipped +Y-up forward formula. The magnitude
                // identity |grad| = 0.6 is invariant to the flip, so this line is bookkeeping, not the test.
                float gx = 0.6f * op.cosAngle, gy = 0.6f * op.sinAngle;
                float mag = Mathf.Sqrt(gx * gx + gy * gy);
                bool ok = Mathf.Abs(mag - 0.6f) < 1e-5f;
                all &= ok;
                sb.AppendLine("    " + "Linear |grad_(nx,ny)E|".PadRight(30) + "0.60000".PadLeft(12) +
                              mag.ToString("F5").PadLeft(12) + (mag / 0.6f).ToString("F5").PadLeft(10) + "  " + Verdict(ok));
                sb.AppendLine("      (constant 0.6 for EVERY angle, and it is declared as a SEPARATE 2-vector:");
                sb.AppendLine("       HS-4.4 forbids summing it with the t-space figure - different variables.)");
            }

            // ── supE > 1 on Linear: body is NOT a height budget (HS-2.3) ──────────────────────────────────
            {
                var op = Op(ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 10f, 45f);
                float expect = 1f + 0.6f * Mathf.Sqrt(2f);
                bool ok = Mathf.Abs(op.supE - expect) < 1e-5f && op.supE > 1f;
                all &= ok;
                sb.AppendLine("    " + "Linear supE at 45 deg".PadRight(30) + expect.ToString("F5").PadLeft(12) +
                              op.supE.ToString("F5").PadLeft(12) + "".PadLeft(10) + "  " + Verdict(ok));
                sb.AppendLine("      supE = 1.84853 > 1, so `body` is NOT a height budget on this ONE technique.");
            }

            sb.AppendLine("    " + measured + " finite declarations measured.  VERDICT " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// The measuring instrument: the largest rounding-corrected two-sided difference quotient of <c>G</c>
        /// over <c>t ∈ (0,1)</c>, at <see cref="SlopeStep"/>.
        /// </summary>
        static float MeasureSlope(in ShaperHeightOp op, float nx, float ny, float h = SlopeStep, int samples = 20000)
        {
            float worst = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = h + (1f - 2f * h) * (i / (float)samples);
                float a = ShaperHeight.Composed(op, t - h, nx, ny);
                float b = ShaperHeight.Composed(op, t + h, nx, ny);
                float num = Mathf.Abs(b - a) - RoundingUlps * FloatEps * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
                if (num <= 0f) continue;
                float q = num / (2f * h);
                if (q > worst) worst = q;
            }
            return worst;
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H3 — every INFINITE declaration, demonstrated by DIVERGENCE.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H3 — for every declaration of <c>+∞</c>, demonstrate DIVERGENCE under grid refinement rather
        /// than report a number.</b> HS-4.1.
        ///
        /// <b>This is the check that would have caught the task's own quoted figures.</b> <c>round 16.4</c>
        /// and <c>dome 23.1</c> are not maxima: they are the analytic derivatives of the two profiles at ONE
        /// AND THE SAME sample point <c>t ≈ 9.3e-4</c>, which is why their ratio is √2 to within 0.4% — the
        /// small-<c>t</c> ratio of the two derivatives — rather than the ratio of two independent maxima. A
        /// measurement that only reports "the biggest number I saw" cannot tell a maximum from a sample of a
        /// divergence; a measurement that REFINES can. Each row below shrinks the step by an order of
        /// magnitude and the measured value must rise without bound.
        /// </summary>
        public static string H3_Divergence()
        {
            var sb = new StringBuilder("H3  INFINITE declarations, demonstrated by DIVERGENCE under refinement (HS-4.1)\n");
            sb.AppendLine("    A declaration of +inf is verified by REFINEMENT, not by a number. Each row halves");
            sb.AppendLine("    the grid by an order of magnitude; the measured quotient must rise without bound.");
            sb.AppendLine("    This is exactly what the circulated figures round=16.4 / dome=23.1 fail: both are");
            sb.AppendLine("    one grid's reading at t ~ 9.3e-4 (their ratio is sqrt(2) to 0.4%), not maxima.");

            float[] steps = { 1e-2f, 1e-3f, 1e-4f, 1e-5f, 1e-6f };
            bool all = true;

            // The instrument is TARGETED, and it has to be. A uniform sweep of `t` cannot demonstrate the
            // divergence of a DISCONTINUOUS profile: at h = 1e-6 a 4000-point grid has to land within 1e-6 of
            // a riser to straddle it, which it never does, so the measured value collapses to 0 and reads as
            // "converged" when it is the opposite. Each declaration names its own singular locus — Round and
            // Dome at t → 0⁺, Rounded at u → 0⁺, Cove at u → 1⁻, Ogee at u → ½, both Stepped families at
            // every riser — and the quotient is centred there. That is not stacking the deck: the claim being
            // tested is "the supremum is unbounded", and a supremum is demonstrated AT its argument.
            Action<string, ShaperHeightOp> row = (label, op) =>
            {
                var line = new StringBuilder("    " + label.PadRight(30));
                float first = 0f, last = 0f;
                bool rising = true;
                for (int i = 0; i < steps.Length; i++)
                {
                    float m = MeasureAtSingularities(op, steps[i]);
                    if (i == 0) first = m;
                    if (i > 0 && m < last * 1.2f) rising = false;
                    last = m;
                    line.Append(m.ToString("G6").PadLeft(14));
                }
                // The ratio gate is 20x over four decades of refinement, not 100x. A 1/sqrt(h) divergence
                // -- which is what Rounded, Cove and Ogee are -- rises by exactly sqrt(10^4) = 100x over
                // this ladder, so a 100x gate sits ON the true value and fails on the last float ulp: Cove
                // measured 992.941 against a 994.983 threshold and was reported as "did not diverge" while
                // its readings had risen by a clean factor of sqrt(10) per decade. 20x still separates a
                // divergence (>= 100x here) from a convergence (~1x) by a wide margin.
                bool diverges = rising && last > first * 20f;
                all &= diverges;
                line.Append("   " + (diverges ? "DIVERGES ok" : "DID NOT DIVERGE - FAIL"));
                sb.AppendLine(line.ToString());
            };

            sb.Append("    " + "declaration".PadRight(30));
            for (int i = 0; i < steps.Length; i++) sb.Append(("h=" + steps[i].ToString("G2")).PadLeft(14));
            sb.AppendLine("");

            row("Round curve=1 (DEFAULT)", Op(ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None, 10f, 45f, 4f, 1f));
            row("Round curve=4", Op(ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None, 10f, 45f, 4f, 4f));
            row("Dome  curve=1 (DEFAULT)", Op(ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 10f, 45f, 4f, 1f));
            row("Dome  curve=4", Op(ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 10f, 45f, 4f, 4f));
            row("Stepped n=4 (DEFAULT)", Op(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 10f, 45f, 4f));
            row("Stepped n=32", Op(ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 10f, 45f, 32f));
            row("bevel Rounded a=1", Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Rounded, 10f, 45f, 4f, 1f, 1f, 1f));
            row("bevel Cove a=1", Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Cove, 10f, 45f, 4f, 1f, 1f, 1f));
            row("bevel Ogee a=1", Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Ogee, 10f, 45f, 4f, 1f, 1f, 1f));
            row("bevel Stepped m=3", Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Stepped, 10f, 45f, 4f, 1f, 1f, 1f, 3f));

            // ── T-0109 FIX F6 — THE DECLARED BLIND BAND, measured rather than left for someone to trip on ──
            //
            // "Verified by demonstrating divergence under grid refinement" is a method with a region where it
            // cannot work, and the audit did not say so. `Dome`/`Round` declare +inf for every curve > 0.5,
            // and the declaration is mathematically right — the exponent is 0.5/c − 1, which is negative for
            // any c > 0.5 — but at c = 0.5 + eps the exponent is −2e-4 and t would have to reach ~1e-15000
            // for the quotient to reach 10. So the divergence is TRUE and UNDEMONSTRABLE by refinement there.
            //
            // The band is measured and printed as a fact, and it is deliberately NOT gated: failing the run
            // because a correct declaration cannot be demonstrated would be the wrong answer, and quietly
            // omitting it is how a future refactor ends up "fixing" the declaration to a finite number on the
            // strength of a flat measurement. The row that matters is the last column of c = 0.5001.
            sb.AppendLine();
            sb.AppendLine("    DECLARED BLIND BAND (T-0109 FIX F6) - refinement cannot demonstrate divergence for");
            sb.AppendLine("    curve just above 0.5, though the declaration there is CORRECT. Not gated, reported.");
            sb.Append("    " + "curve".PadRight(30));
            for (int i = 0; i < steps.Length; i++) sb.Append(("h=" + steps[i].ToString("G2")).PadLeft(14));
            sb.AppendLine("   declared");
            foreach (float c in new[] { 0.5f, 0.5001f, 0.55f, 0.7f, 1f })
            {
                foreach (var tk in new[] { ShaperExtrusionTechnique.Dome, ShaperExtrusionTechnique.Round })
                {
                    var opc = Op(tk, ShaperBevelTechnique.None, 10f, 45f, 4f, c);
                    var line = new StringBuilder("    " + (N(tk) + " curve=" + c.ToString("G6")).PadRight(30));
                    float f0 = 0f, l0 = 0f;
                    for (int i = 0; i < steps.Length; i++)
                    {
                        float m = MeasureAtSingularities(opc, steps[i]);
                        if (i == 0) f0 = m;
                        l0 = m;
                        line.Append(m.ToString("G6").PadLeft(14));
                    }
                    float decl = ShaperHeight.ExtrusionSlopeBound(opc);
                    bool demonstrable = l0 > f0 * 10f;
                    line.Append("   " + (float.IsPositiveInfinity(decl) ? "+inf" : decl.ToString("G6")) +
                                (float.IsPositiveInfinity(decl) ? (demonstrable ? "  (demonstrable)" : "  BLIND - true but not demonstrable") : ""));
                    sb.AppendLine(line.ToString());
                }
            }
            sb.AppendLine("    The blind band is curve in (0.5, ~0.6]: the exponent 0.5/c - 1 is so close to 0 that");
            sb.AppendLine("    t must reach ~1e-15000 for the quotient to reach 10. DO NOT 'fix' the declaration to a");
            sb.AppendLine("    finite number on the strength of a flat measurement here - the measurement is the limit,");
            sb.AppendLine("    not the maths. Below curve = 0.5 the declaration is FINITE and H2 measures it instead.");
            sb.AppendLine();

            // The counterpart assertion HS-4.1 makes explicitly: both DEFAULTS land in the infinite branch.
            var d1 = Op(ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None);
            var r1 = Op(ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None);
            bool defaultsInfinite = float.IsPositiveInfinity(ShaperHeight.ExtrusionSlopeBound(d1)) &&
                                    float.IsPositiveInfinity(ShaperHeight.ExtrusionSlopeBound(r1));
            all &= defaultsInfinite;
            sb.AppendLine("    Dome and Round at their OWN DEFAULT curve=1 declare +inf: " + Verdict(defaultsInfinite));

            // IsLipschitz must refuse exactly the seven, and accept exactly the five.
            int finite = 0, infinite = 0;
            var names = new StringBuilder();
            foreach (var tech in AllTechniques)
            {
                var op = Op(tech, ShaperBevelTechnique.None);
                bool lip = ShaperHeight.IsLipschitz(op);
                if (lip) finite++; else { infinite++; names.Append(N(tech) + " "); }
            }
            foreach (var bev in AllBevels)
            {
                if (bev == ShaperBevelTechnique.None) continue;
                var op = Op(ShaperExtrusionTechnique.Flat, bev, 10f, 45f, 4f, 1f, 1f, 0.25f);
                bool lip = ShaperHeight.IsLipschitz(op);
                if (lip) finite++; else { infinite++; names.Append("bevel " + N(bev) + " "); }
            }
            bool split = finite == 5 && infinite == 7;
            all &= split;
            sb.AppendLine("    IsLipschitz over the twelve techniques at their defaults: " + finite + " finite, " +
                          infinite + " infinite (expected 5 / 7): " + Verdict(split));
            sb.AppendLine("      refused by name: " + names.ToString().Trim());
            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// The largest rounding-corrected quotient measured AT the declaration's own named singular loci, at
        /// step <paramref name="h"/>. Used only by H3, where the quantity being demonstrated is a supremum
        /// that does not exist.
        /// </summary>
        static float MeasureAtSingularities(ShaperHeightOp op, float h)
        {
            float worst = 0f;
            var local = op;   // an `in` parameter cannot be captured by a lambda (CS1628).
            Action<float> at = t0 =>
            {
                float lo = Mathf.Clamp(t0 - h, 0f, 1f);
                float hi = Mathf.Clamp(t0 + h, 0f, 1f);
                if (hi <= lo) return;
                float a = ShaperHeight.Composed(local, lo, 0f, 0f);
                float b = ShaperHeight.Composed(local, hi, 0f, 0f);
                float num = Mathf.Abs(b - a) - RoundingUlps * FloatEps * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
                if (num <= 0f) return;
                float q = num / (hi - lo);
                if (q > worst) worst = q;
            };

            // t → 0⁺ : Round, Dome, and the Rounded bevel's silhouette end.
            at(h);
            // the stepped extrusion's risers
            if (op.technique == ShaperExtrusionTechnique.Stepped)
                for (int k = 1; k < op.n; k++) at(k / (float)op.n);
            if (op.a > 0f && op.bevel != ShaperBevelTechnique.None)
            {
                // the band, in u: the Rounded end, the Cove end, the Ogee inflection, the Stepped risers.
                at(h);
                at(op.a - h);
                at(0.5f * op.a);
                if (op.bevel == ShaperBevelTechnique.Stepped)
                    for (int k = 1; k < op.bevelN; k++) at(op.a * k / op.bevelN);
            }
            return worst;
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H4 — every closed-form inverse round-tripped against the bisection.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H4 — round-trip every closed-form <c>Ginv</c> against the bisection fallback.</b> HS-5.7.
        ///
        /// This is what catches an algebra slip in HS-5.7's list, and it is cheap. Two properties are checked
        /// at every sampled <c>ζ</c>: the closed form agrees with a pure bisection on <c>G</c> to the
        /// bisection's own resolution, and the round-trip <c>G(Ginv(ζ)) ≥ ζ</c> holds (the inverse must reach
        /// the height it was asked for, or the containing prism of HS-5.3 stops containing).
        ///
        /// <b>T-0109 FIX F3 — this check was HOLLOW for the exact defect class it exists to catch, and both
        /// reasons are now closed.</b>
        /// <list type="number">
        /// <item>Its <c>ζ</c> grid was <c>(i/200)·supG</c>, so the smallest non-zero <c>ζ</c> it ever tested
        /// was 5e-3 — and every catastrophic-cancellation defect in this family lives below that. Measured on
        /// IDENTICAL code, the old grid reported a worst reach shortfall of 8.866e-7 on <c>Flat+Rounded</c>
        /// where a log-spaced grid reported <b>1.259e-4 at ζ = 1.26e-4, 142× larger</b>. D2 was caught at all
        /// only because it happened to surface at ζ = 0.005, the old grid's FIRST point. The grid is now
        /// log-spaced down to 1e-9 as well as linear, and includes the denormal tail.</item>
        /// <item>It compared the closed form against <see cref="ShaperHeight.Bisect"/>, which evaluates the
        /// SAME <c>float</c> <c>Composed</c> as the subject — so a defect shared by forward and inverse was
        /// invisible by construction. The reach half never had that problem and is now the load-bearing
        /// half; the <c>|Δt|</c> half is kept and labelled for what it is.</item>
        /// </list>
        /// The INJECTION sub-check below is the answer to "would this fail if the thing it tests were
        /// broken?": it perturbs <c>Ginv</c> by a known amount and confirms the check reads the perturbation
        /// back at the right size. A check that cannot fail is not a check.
        /// </summary>
        public static string H4_InverseRoundTrip()
        {
            var sb = new StringBuilder("H4  closed-form Ginv round-tripped against the bisection (HS-5.7)\n");
            sb.AppendLine("    Two properties per sampled zeta: |closed - bisected| within the bisection's own");
            sb.AppendLine("    resolution, and G(Ginv(zeta)) >= zeta (the prism of HS-5.3 must actually contain).");
            sb.AppendLine("    T-0109 FIX F3: the zeta grid is now LOG-spaced to 1e-9 as well as linear - the old");
            sb.AppendLine("    (i/200)*supG grid floored at 5e-3 and every cancellation defect lives below that.");
            sb.AppendLine("    " + "combination".PadRight(34) + "worst |dt|".PadLeft(12) + "worst reach".PadLeft(14) + "  verdict");

            float[] amounts = { 0f, 0.25f, 0.6f, 1f };
            bool all = true;
            int checks = 0;

            foreach (var tech in AllTechniques)
            {
                foreach (var bev in AllBevels)
                {
                    float worstDt = 0f, worstReach = 0f;
                    foreach (float p in ProfileSweep(tech))
                    {
                        foreach (float a in amounts)
                        {
                            var op = OpWithSweep(tech, bev, p, a, 3f);
                            float top = ShaperHeight.Composed(op, 1f, 0f, 0f);
                            for (int i = 0; i <= 340; i++)
                            {
                                // 0..200 keep the original LINEAR grid, so the figures stay comparable with
                                // the pre-fix run; 201..340 add a LOG grid from supG down to 1e-9, which is
                                // where the cancellation family actually lives.
                                float zeta = i <= 200
                                           ? (i / 200f) * top
                                           : top * Mathf.Pow(10f, -9f * (i - 200) / 140f);
                                float closed = ShaperHeight.Inverse(op, zeta, 0f, 0f);
                                if (ShaperHeight.IsNoCrossSection(closed)) continue;
                                float bis = ShaperHeight.Bisect(op, zeta, 0f, 0f, 0f, 1f);
                                // Both find the SMALLEST t reaching zeta. On a step function the pure
                                // bisection converges to the jump from above, so the two agree to the
                                // bisection's own resolution and no better.
                                float dt = Mathf.Abs(closed - bis);
                                if (dt > worstDt) worstDt = dt;

                                float reached = ShaperHeight.Composed(op, closed, 0f, 0f);
                                float shortfall = zeta - reached;
                                if (shortfall > worstReach) worstReach = shortfall;
                                checks++;
                            }
                        }
                    }
                    bool ok = worstDt < 2e-3f && worstReach < 1e-5f;
                    all &= ok;
                    sb.AppendLine("    " + (N(tech) + " + " + N(bev)).PadRight(34) +
                                  worstDt.ToString("E3").PadLeft(12) + worstReach.ToString("E3").PadLeft(14) + "  " + Verdict(ok));
                }
            }

            // ── T-0109 FIX F3, the INJECTION sub-check: can this check see a perturbation at all? ─────────
            //
            // Ginv is perturbed by a known multiplicative factor and the reach shortfall is re-measured. A
            // check that reads a 1.05x perturbation back as a proportionate shortfall genuinely detects the
            // defect class; one that reports the same figure either way is measuring its own grid.
            sb.AppendLine();
            sb.AppendLine("    INJECTION (T-0109 FIX F3): perturb Ginv by a factor and re-measure the reach shortfall.");
            sb.AppendLine("    " + "factor".PadRight(12) + "worst reach shortfall".PadLeft(24) + "   reads the perturbation back?");
            {
                var op = OpWithSweep(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Rounded, 1f, 0.25f, 3f);
                float top = ShaperHeight.Composed(op, 1f, 0f, 0f);
                float baseline = 0f;
                foreach (float factor in new[] { 1f, 0.95f, 0.5f, 0f })
                {
                    float worst = 0f;
                    for (int i = 0; i <= 340; i++)
                    {
                        float zeta = i <= 200 ? (i / 200f) * top : top * Mathf.Pow(10f, -9f * (i - 200) / 140f);
                        if (zeta <= 0f) continue;
                        float t = ShaperHeight.Inverse(op, zeta, 0f, 0f);
                        if (ShaperHeight.IsNoCrossSection(t)) continue;
                        t *= factor;                                  // the injected defect
                        float shortfall = zeta - ShaperHeight.Composed(op, t, 0f, 0f);
                        if (shortfall > worst) worst = shortfall;
                    }
                    if (factor == 1f) baseline = worst;
                    bool reads = factor == 1f || worst > baseline * 20f + 1e-4f;
                    sb.AppendLine("    " + factor.ToString("F2").PadRight(12) + worst.ToString("E4").PadLeft(24) +
                                  "   " + (factor == 1f ? "(baseline)" : Verdict(reads)));
                    if (factor != 1f) all &= reads;
                }
            }
            sb.AppendLine();

            // NoCrossSection above sup G, and 0 at or below G(0) — the two named boundary answers of HS-5.7.
            var f = Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None);
            bool sentinel = ShaperHeight.IsNoCrossSection(ShaperHeight.Inverse(f, 1.5f, 0f, 0f));
            bool zeroAtBase = ShaperHeight.Inverse(f, 0.5f, 0f, 0f) == 0f;
            all &= sentinel && zeroAtBase;
            sb.AppendLine("    zeta > sup G returns the NoCrossSection sentinel: " + Verdict(sentinel));
            sb.AppendLine("    zeta <= G(0) returns exactly 0: " + Verdict(zeroAtBase));
            sb.AppendLine("    " + checks + " round-trips.  VERDICT " + Verdict(all));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H5 — identity early-outs are BIT-IDENTICAL.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H5 — the identity early-outs are BIT-IDENTICAL, not merely numerically equal.</b>
        ///
        /// The same discipline <c>ShaperEvaluator</c>'s Sweep early-out already uses
        /// (<c>ShaperEvaluator.cs:88-91</c>): "relying on 'the wedge test happens to pass' would leave a max
        /// against a computed value free to perturb the last bit". Three claims:
        /// <list type="number">
        /// <item><c>Flat</c> + <c>None</c> equals a plain prism EXACTLY — every sample is bitwise <c>body</c>.</item>
        /// <item><c>bevelAmount = 0</c> is bitwise identical to <c>None</c>, for every profile. This one is
        /// structural rather than lucky: <c>index.html:1161</c>'s <c>amount &lt;= 0</c> is a HARD identity
        /// taken before any arithmetic, and <see cref="ShaperHeight.Bevel"/> keeps it that way.</item>
        /// <item><c>depth = 0</c> publishes height 0 and no wall (HS-2.2's kept <c>body ≤ 0</c> early-out).</item>
        /// </list>
        /// </summary>
        public static string H5_BitIdentity()
        {
            var sb = new StringBuilder("H5  identity early-outs are BIT-IDENTICAL (HS-2.2, HS-3.2)\n");
            bool all = true;

            // (1) Flat + None is a plain prism, bitwise.
            {
                var op = Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 13.75f);
                int mismatch = 0;
                int bodyBits = BitConverter.ToInt32(BitConverter.GetBytes(op.body), 0);
                for (int i = 0; i <= 100000; i++)
                {
                    float t = i / 100000f;
                    float h = ShaperHeight.Height(op, t, 0f, 0f);
                    if (BitConverter.ToInt32(BitConverter.GetBytes(h), 0) != bodyBits) mismatch++;
                }
                bool ok = mismatch == 0;
                all &= ok;
                sb.AppendLine("    Flat + None == a plain prism: " + mismatch + " mismatched bit patterns over 100001 samples  " + Verdict(ok));
            }

            // (2) bevelAmount = 0 is bitwise None, for every profile and every bevel technique.
            {
                int mismatch = 0, cases = 0;
                foreach (var tech in AllTechniques)
                {
                    var none = Op(tech, ShaperBevelTechnique.None);
                    foreach (var bev in AllBevels)
                    {
                        if (bev == ShaperBevelTechnique.None) continue;
                        var zero = Op(tech, bev, 10f, 45f, 4f, 1f, 1f, 0f);
                        cases++;
                        for (int i = 0; i <= 20000; i++)
                        {
                            float t = i / 20000f;
                            float a = ShaperHeight.Height(none, t, 0.3f, -0.4f);
                            float b = ShaperHeight.Height(zero, t, 0.3f, -0.4f);
                            if (BitConverter.ToInt32(BitConverter.GetBytes(a), 0) !=
                                BitConverter.ToInt32(BitConverter.GetBytes(b), 0)) mismatch++;
                        }
                    }
                }
                bool ok = mismatch == 0;
                all &= ok;
                sb.AppendLine("    bevelAmount = 0 == None: " + mismatch + " mismatched bit patterns over " +
                              cases + " x 20001 samples  " + Verdict(ok));
            }

            // (3) depth = 0 publishes height 0 and no wall, through the block entry as well as the scalar.
            {
                var op = Op(ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 0f);
                const int W = 32;
                var dist = new float[W * W];
                var hgt = new float[W * W];
                for (int i = 0; i < dist.Length; i++) { dist[i] = -5f; hgt[i] = 12345f; }
                var grid = ShaperSampleGrid.Centred(W, W, 1f);
                ShaperHeight.FillTile(op, grid, 0, 0, W, W, dist, hgt, 0, W, 0, W);
                int nonZero = 0;
                for (int i = 0; i < hgt.Length; i++) if (hgt[i] != 0f) nonZero++;
                bool ok = nonZero == 0 && ShaperHeight.WallHeight(op, 0f, 0f) == 0f && !ShaperHeight.HasWall(op, 0f, 0f);
                all &= ok;
                sb.AppendLine("    depth = 0: " + nonZero + " non-zero height samples of " + hgt.Length +
                              ", wall height " + ShaperHeight.WallHeight(op, 0f, 0f) + "  " + Verdict(ok));
            }

            // (4) HS-1.4: the published set follows presence, and nothing else.
            {
                var absent = ShaperHeightCompiler.Compile(null, null, 1f, 0f);
                var present = Op(ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.Rounded, 10f);
                bool ok = ShaperHeight.Publishes(absent) == ShaperQuantitySet.ShippedShapeEngine &&
                          ShaperHeight.Publishes(present) == ShaperQuantitySet.ShapeEngineWithHeight &&
                          !ShaperQuantities.Contains(ShaperHeight.Publishes(absent), ShaperQuantity.Height) &&
                          ShaperQuantities.Contains(ShaperHeight.Publishes(present), ShaperQuantity.Height) &&
                          !ShaperQuantities.Contains(ShaperHeight.Publishes(present), ShaperQuantity.Depth);
                all &= ok;
                sb.AppendLine("    HS-1.4 published set: absent -> " + ShaperHeight.Publishes(absent) +
                              ", present -> " + ShaperHeight.Publishes(present) + "  " + Verdict(ok));
            }

            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H7 — HS-6.4's five mechanical statements.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H7 — HS-6.4's five mechanical statements about which profiles and bevels leave a side wall.</b>
        ///
        /// They are asserted rather than described because together they are why HS-0.2's ruling can be made
        /// now and its rendering deferred: for most authored content there is nothing to render, and that is a
        /// measurable claim rather than a hope.
        /// </summary>
        public static string H7_WallStatements()
        {
            var sb = new StringBuilder("H7  HS-6.4's five mechanical statements about the side wall\n");
            bool all = true;

            // 1 — at their DEFAULT parameters, five of the seven profiles have E(0) = 0 and so no wall.
            {
                var noWall = new List<string>();
                var wall = new List<string>();
                foreach (var tech in AllTechniques)
                {
                    var op = Op(tech, ShaperBevelTechnique.None, 10f);
                    float w = ShaperHeight.WallHeight(op, 0f, 0f);
                    if (w <= 0f) noWall.Add(N(tech)); else wall.Add(N(tech) + "=" + w.ToString("F3"));
                }
                bool ok = noWall.Count == 5 && wall.Count == 2 &&
                          wall.Exists(s => s.StartsWith("Flat")) && wall.Exists(s => s.StartsWith("Linear"));
                all &= ok;
                sb.AppendLine("    1  at DEFAULT params, no wall on: " + string.Join(", ", noWall.ToArray()) + "  (expected 5)  " + Verdict(ok));

                // ── T-0109 FIX F6 — statement 2 was measured at ONE unrepresentative point. ───────────────
                //
                // WallHeight(op, nx, ny) was called with nx = ny = 0, which is exactly where `Linear`'s tilt
                // term 0.6(cos θ·nx + sin θ·ny) VANISHES. It reported "Linear = 10.000" for body 10 — the
                // one number that makes `Linear` look like a height-budgeted profile, which HS-2.3 says in
                // as many words that it is not. Over the real support box `Linear`'s wall reaches
                // supG·body = 1.8485281·body. The statement is now measured over the box, both figures are
                // printed, and the ASSERTED number is the supG one. It also prints an `ok` token, which it
                // did not before while its four siblings did.
                var lin = Op(ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 10f, 45f);
                float wallAtOrigin = ShaperHeight.WallHeight(lin, 0f, 0f);
                float wallMax = 0f;
                for (int iy = -12; iy <= 12; iy++)
                    for (int ix = -12; ix <= 12; ix++)
                    {
                        float w = ShaperHeight.WallHeight(lin, ix / 12f, iy / 12f);
                        if (w > wallMax) wallMax = w;
                    }
                float wallExpect = lin.supG * lin.body;
                bool ok2 = ok && Mathf.Abs(wallMax - wallExpect) < 1e-3f && wallMax > wallAtOrigin * 1.5f;
                all &= ok2;
                sb.AppendLine("    2  full-height wall ONLY on: " + string.Join(", ", wall.ToArray()) + "  (expected Flat, Linear)  " + Verdict(ok2));
                sb.AppendLine("       Linear wall, body=" + lin.body.ToString("F3") + ": at (nx,ny)=(0,0) it is " +
                              wallAtOrigin.ToString("F4") + " - the ONE point where its tilt vanishes - but over");
                sb.AppendLine("       the support box it reaches " + wallMax.ToString("F4") + " = supG*body = " +
                              wallExpect.ToString("F4") + ". HS-2.3: body is NOT a height budget on Linear.");
            }

            // 3 — any bevel other than None and Stepped forces B(0) = 0, removing the wall from EVERY profile.
            {
                int removed = 0, cases = 0;
                foreach (var bev in new[] { ShaperBevelTechnique.Linear, ShaperBevelTechnique.Rounded,
                                            ShaperBevelTechnique.Cove, ShaperBevelTechnique.Ogee })
                    foreach (var tech in AllTechniques)
                    {
                        var op = Op(tech, bev, 10f, 45f, 4f, 1f, 1f, 0.25f);
                        cases++;
                        if (ShaperHeight.WallHeight(op, 0.5f, 0.5f) <= 1e-7f) removed++;
                    }
                bool ok = removed == cases;
                all &= ok;
                sb.AppendLine("    3  bevel Linear/Rounded/Cove/Ogee removes the wall from every profile: " +
                              removed + "/" + cases + "  " + Verdict(ok));
            }

            // 4 — Stepped bevel leaves a wall of exactly body·E(0)/m.
            {
                bool ok = true;
                var detail = new StringBuilder();
                foreach (int m in new[] { 2, 3, 5, 16 })
                    foreach (var tech in new[] { ShaperExtrusionTechnique.Flat, ShaperExtrusionTechnique.Pyramid })
                    {
                        float tau = tech == ShaperExtrusionTechnique.Pyramid ? 0.4f : 1f;
                        var op = Op(tech, ShaperBevelTechnique.Stepped, 10f, 45f, 4f, 1f, tau, 0.3f, m);
                        float e0 = ShaperHeight.Profile(op, 0f, 0f, 0f);
                        float expect = op.body * e0 / m;
                        float got = ShaperHeight.WallHeight(op, 0f, 0f);
                        if (Mathf.Abs(got - expect) > 1e-4f) { ok = false; detail.Append(N(tech) + " m=" + m + " "); }
                    }
                all &= ok;
                sb.AppendLine("    4  Stepped bevel leaves a wall of exactly body*E(0)/m: " + Verdict(ok) +
                              (ok ? "" : "  offenders: " + detail));
            }

            // 5 — a wall exists exactly where G(0) > 0, and nowhere else.
            {
                int agree = 0, cases = 0;
                foreach (var tech in AllTechniques)
                    foreach (var bev in AllBevels)
                        foreach (float a in new[] { 0f, 0.25f, 1f })
                        {
                            var op = Op(tech, bev, 10f, 45f, 4f, 1f, 1f, a);
                            cases++;
                            bool g0 = ShaperHeight.Composed(op, 0f, 0.5f, 0.5f) > 0f;
                            if (g0 == ShaperHeight.HasWall(op, 0.5f, 0.5f)) agree++;
                        }
                bool ok = agree == cases;
                all &= ok;
                sb.AppendLine("    5  a wall exists exactly where G(0) > 0: " + agree + "/" + cases + "  " + Verdict(ok));
            }

            // HS-6.1's ruling ("every Wall crossing publishes edgeDistance EXACTLY 0") was checked here against
            // the general resolve's own crossing list; that resolve (HS-9) was deleted outright (T-0253) along
            // with this sub-check, since it was the only caller of ShaperResolve.Query in this method.

            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H8 — allocation.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H8 — the hot path allocates 0 bytes over ≥ 500 000 samples, and causes 0 gen-0 collections.</b>
        ///
        /// <c>GC.GetAllocatedBytesForCurrentThread()</c> is reported for completeness and is KNOWN INERT on
        /// this Mono runtime — <c>ShaperLightAudit</c>'s own calibration drove it with allocations from 1 KB to
        /// 64 MB and it returned a constant zero at every size. The instrument that actually works here is the
        /// managed heap delta plus the gen-0 collection count, and the detection floor is reported as a number
        /// rather than asserted.
        /// </summary>
        public static string H8_Allocation()
        {
            var sb = new StringBuilder("H8  hot-path allocation over >= 500,000 samples\n");

            Func<long> threadProbe;
            {
                var mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",
                                              BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                threadProbe = mi != null ? (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi) : (() => 0L);
            }

            const int W = 256, H = 256, Reps = 8;
            var node = Rect(90f, 70f, 18f);
            var prog = ShaperCompiler.Compile(node);
            var stack = prog.NewStack();
            var hop = OpFor(prog, ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.Ogee, 20f, 0.3f, 0f);
            var grid = ShaperSampleGrid.Centred(W, H, 1f);

            var dist = new float[W * H];
            var cov = new float[W * H];
            var hgt = new float[W * H];
            var nrm = new float[W * H * 3];
            var bp = new float[ShaperHeight.MaxBreakpoints];

            ShaperEvaluator.FillTile(prog, grid, 0, 0, W, H, dist, cov, 0, W, stack);

            var nop = ShaperNormalOp.Default;
            nop.kind = ShaperNormalKind.Profile;
            nop.height = hop;
            nop.reflection = 0.4f;

            // Warm every path once, so JIT and first-touch costs are outside the measurement.
            ShaperHeight.FillTile(hop, grid, 0, 0, W, H, dist, hgt, 0, W, 0, W);
            ShaperNormals.FillTile(nop, grid, 0, 0, 8, 8, dist, null, nrm, 0, W, 0, W, prog, stack);
            ShaperHeight.Breakpoints(hop, bp);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long heap0 = GC.GetTotalMemory(false);
            long thread0 = threadProbe();
            int gen0 = GC.CollectionCount(0);

            int samples = 0;
            for (int r = 0; r < Reps; r++)
            {
                ShaperHeight.FillTile(hop, grid, 0, 0, W, H, dist, hgt, 0, W, 0, W);
                samples += W * H;
                ShaperHeight.Breakpoints(hop, bp);
            }
            // The scalar entry points, the inverse (including its bisection) and the analytic derivative.
            for (int i = 0; i < 200000; i++)
            {
                float t = (i % 1000) / 999f;
                ShaperHeight.Composed(hop, t, 0.2f, -0.3f);
                ShaperHeight.ComposedDerivative(hop, t, 0.2f, -0.3f);
                ShaperHeight.Inverse(hop, t, 0f, 0f);
                samples += 3;
            }
            // The normal provider's Profile case over a real tile.
            ShaperNormals.FillTile(nop, grid, 0, 0, W, H, dist, null, nrm, 0, W, 0, W, prog, stack);
            samples += W * H;

            long heap1 = GC.GetTotalMemory(false);
            long thread1 = threadProbe();
            int gen1 = GC.CollectionCount(0);

            long heapDelta = heap1 - heap0;
            bool ok = heapDelta <= 0 && gen1 == gen0;

            sb.AppendLine("    samples exercised   " + samples + " (target >= 500,000)");
            sb.AppendLine("    managed heap delta  " + heapDelta + " bytes");
            sb.AppendLine("    gen-0 collections   " + (gen1 - gen0));
            sb.AppendLine("    thread probe        " + (thread1 - thread0) +
                          " bytes - INERT on this Mono runtime, reported for completeness only");
            sb.AppendLine("    paths covered: ShaperHeight.FillTile, Composed, ComposedDerivative, Inverse,");
            sb.AppendLine("                   Breakpoints, ShaperNormals.FillTile (Profile case).");
            sb.AppendLine("    VERDICT " + Verdict(ok) + (samples >= 500000 ? "" : "   (SAMPLE TARGET MISSED)"));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H9 — analytic G' against a fine central difference.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H9 — the analytic <c>G′</c> agrees with a fine central difference of <c>G</c> wherever <c>G</c>
        /// is differentiable, and is correctly refused where it is not.</b> HS-8.2.
        ///
        /// The refusal half is as much of the check as the agreement half: at a <c>Stepped</c> riser, at
        /// <c>t = a</c>, at <c>t = T</c> on a taper and at the two ends, <c>G</c> genuinely has no derivative,
        /// and a difference quotient there measures the discontinuity rather than a slope. Those points are
        /// EXCLUDED BY NAME and counted, so the exclusion is a stated set rather than a silent filter that
        /// could grow to hide a real disagreement.
        /// </summary>
        public static string H9_AnalyticDerivative()
        {
            var sb = new StringBuilder("H9  analytic G' vs a fine central difference of G (HS-8.2)\n");
            sb.AppendLine("    Points where G is genuinely non-differentiable are EXCLUDED BY NAME and counted:");
            sb.AppendLine("    stepped risers (t = k/n, u = k/m), the band edge t = a, taper's knee t = T, and t in {0,1}.");
            sb.AppendLine("    The gate is PER POINT: err <= max(2e-3 absolute, 5% relative). Both halves are");
            sb.AppendLine("    needed and neither alone works. Relative alone fails on Dome, where G' -> 0 near");
            sb.AppendLine("    t = 1 so a 7e-4 absolute error is a 10% relative one and means nothing. Absolute");
            sb.AppendLine("    alone fails next to a Rounded or Cove bevel's singular end, where G' ~ 12 and the");
            sb.AppendLine("    CENTRAL DIFFERENCE is the inaccurate half - its truncation error is O(h^2 |G'''|)");
            sb.AppendLine("    and |G'''| is enormous there, so an absolute gate would be testing the instrument.");
            sb.AppendLine("    " + "combination".PadRight(34) + "tested".PadLeft(9) + "excluded".PadLeft(10) +
                          "worst abs".PadLeft(13) + "worst rel".PadLeft(12) + "  bad" + "  verdict");

            const float h = 1e-4f;
            bool all = true;

            foreach (var tech in AllTechniques)
            {
                foreach (var bev in AllBevels)
                {
                    var op = OpWithSweep(tech, bev, tech == ShaperExtrusionTechnique.Stepped ? 5f :
                                                     tech == ShaperExtrusionTechnique.Linear ? 30f :
                                                     tech == ShaperExtrusionTechnique.Dome ||
                                                     tech == ShaperExtrusionTechnique.Round ? 0.35f : 0.5f,
                                         0.3f, 4f);
                    int tested = 0, excluded = 0, bad = 0;
                    float worst = 0f, worstRel = 0f;
                    for (int i = 1; i < 4000; i++)
                    {
                        float t = i / 4000f;
                        if (NearBreak(op, t, 4f * h)) { excluded++; continue; }
                        float g = ShaperHeight.ComposedDerivative(op, t, 0.2f, -0.3f);
                        if (float.IsInfinity(g) || float.IsNaN(g)) { excluded++; continue; }
                        float fd = (ShaperHeight.Composed(op, t + h, 0.2f, -0.3f) -
                                    ShaperHeight.Composed(op, t - h, 0.2f, -0.3f)) / (2f * h);
                        tested++;
                        float err = Mathf.Abs(g - fd);
                        if (err > worst) worst = err;
                        float mag = Mathf.Max(Mathf.Abs(g), Mathf.Abs(fd));
                        float rel = err / Mathf.Max(1e-6f, mag);
                        if (rel > worstRel) worstRel = rel;
                        if (err > Mathf.Max(2e-3f, 0.05f * mag)) bad++;
                    }
                    bool ok = tested > 0 && bad == 0;
                    all &= ok;
                    sb.AppendLine("    " + (N(tech) + " + " + N(bev)).PadRight(34) + tested.ToString().PadLeft(9) +
                                  excluded.ToString().PadLeft(10) + worst.ToString("E3").PadLeft(13) +
                                  worstRel.ToString("E3").PadLeft(12) + bad.ToString().PadLeft(5) + "  " + Verdict(ok));
                }
            }
            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>The named non-differentiable set of HS-8.2, as a predicate rather than a filtered-out surprise.</summary>
        static bool NearBreak(in ShaperHeightOp op, float t, float w)
        {
            if (t < w || t > 1f - w) return true;
            if (op.a > 0f && op.bevel != ShaperBevelTechnique.None && Mathf.Abs(t - op.a) < w) return true;
            if (op.technique == ShaperExtrusionTechnique.Stepped)
                for (int k = 0; k <= op.n; k++) if (Mathf.Abs(t - k / (float)op.n) < w) return true;
            if (op.technique == ShaperExtrusionTechnique.Taper && Mathf.Abs(t - op.taperT) < w) return true;
            if (op.bevel == ShaperBevelTechnique.Stepped && op.a > 0f)
                for (int k = 0; k <= op.bevelN; k++) if (Mathf.Abs(t - op.a * k / op.bevelN) < w) return true;
            if (op.bevel == ShaperBevelTechnique.Ogee && op.a > 0f && Mathf.Abs(t - 0.5f * op.a) < 20f * w) return true;
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H10 — reflectionFlatten is genuinely read.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H10 — <c>reflectionFlatten</c> is GENUINELY READ, measured behaviourally.</b>
        ///
        /// BC-3.6 required the reference's three hand-tuned constants to become "named, defaulted dials with
        /// their provenance in a comment. None of them is a magic number in the new code" — and a named dial
        /// that nothing reads is the same defect wearing a better hat. The check perturbs the dial and
        /// requires the output to move; a read whose value is discarded would move nothing, which is exactly
        /// how an undeclared read stayed invisible to FC's FT-12 until it was measured this way.
        ///
        /// All three dials are checked, not only the third, because the same argument applies to each.
        /// </summary>
        public static string H10_DialsAreRead()
        {
            var sb = new StringBuilder("H10 the three normal dials are GENUINELY READ, measured behaviourally\n");

            const int W = 48;
            var node = Rect(18f, 14f, 4f);
            var prog = ShaperCompiler.Compile(node);
            var stack = prog.NewStack();
            var hop = OpFor(prog, ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 12f, 0f, 0f);
            var grid = ShaperSampleGrid.Centred(W, W, 1f);
            var dist = new float[W * W];
            var cov = new float[W * W];
            ShaperEvaluator.FillTile(prog, grid, 0, 0, W, W, dist, cov, 0, W, stack);

            Func<float, float, float, float[]> run = (gain, zbase, flat) =>
            {
                var nop = ShaperNormalOp.Default;
                nop.kind = ShaperNormalKind.Profile;
                nop.height = hop;
                nop.slopeGain = gain;
                nop.normalZBase = zbase;
                nop.reflectionFlatten = flat;
                nop.reflection = 0.8f;          // non-zero, or reflectionFlatten is multiplied by nothing
                var n = new float[W * W * 3];
                ShaperNormals.FillTile(nop, grid, 0, 0, W, W, dist, null, n, 0, W, 0, W, prog, stack);
                return n;
            };

            var baseline = run(0.65f, 1.4f, 1.5f);
            var noFlatten = run(0.65f, 1.4f, 0f);
            var otherGain = run(0.20f, 1.4f, 1.5f);
            var otherZ = run(0.65f, 3.0f, 1.5f);

            Func<float[], float[], int> diff = (a, b) =>
            {
                int n = 0;
                for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-6f) n++;
                return n;
            };

            int dFlat = diff(baseline, noFlatten);
            int dGain = diff(baseline, otherGain);
            int dZ = diff(baseline, otherZ);

            bool okFlat = dFlat > 0, okGain = dGain > 0, okZ = dZ > 0;
            bool all = okFlat && okGain && okZ;

            sb.AppendLine("    reflectionFlatten 1.5 -> 0 changed " + dFlat + " of " + baseline.Length +
                          " normal components  " + Verdict(okFlat));
            sb.AppendLine("    slopeGain 0.65 -> 0.20 changed    " + dGain + "  " + Verdict(okGain));
            sb.AppendLine("    normalZBase 1.4 -> 3.0 changed    " + dZ + "  " + Verdict(okZ));

            // LR-3.5's three absolutes on the provider's output, checked on the same sheet.
            int bad = 0, notUnit = 0;
            for (int i = 0; i < baseline.Length; i += 3)
            {
                float x = baseline[i], y = baseline[i + 1], z = baseline[i + 2];
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) bad++;
                else if (x == 0f && y == 0f && z == 0f) bad++;
                else if (Mathf.Abs(Mathf.Sqrt(x * x + y * y + z * z) - 1f) > 1e-4f) notUnit++;
            }
            bool lr35 = bad == 0 && notUnit == 0;
            all &= lr35;
            sb.AppendLine("    LR-3.5: " + bad + " NaN-or-zero, " + notUnit + " not unit to 1e-4  " + Verdict(lr35));
            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // H11 — the stage is WIRED: it has a caller in the shipping path, and the fallbacks are loud.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <b>H11 — the height stage is reachable from an AUTHORED document through the real paint pass.</b>
        /// T-0109 FIX F4; added because nothing in H1–H10 could tell the difference between a correct stage
        /// and a correct stage nobody calls, and that is exactly what shipped.
        ///
        /// Four assertions, each the negation of a measured defect:
        /// <list type="number">
        /// <item><c>ShaperHeight.FillTile</c> has a caller outside this audit — <c>buf.ownHeight</c> comes
        /// back non-zero from <c>ShaperFillResolver.PaintTile</c>.</item>
        /// <item>FC-2.5's <c>height_final = height_shape + heightDelta·coverageEff</c> is a REAL SUM:
        /// <c>buf.height</c> carries <c>height_shape</c>, where before it was <c>0 + heightDelta·ce</c>.
        /// Stated in TWO halves after T-0109 FIX N7 — 2a on a document with no fill (which proves the SEED
        /// only, because <c>heightDelta ≡ 0</c> there makes the identity hold for a reason unrelated to the
        /// sum), and 2b on a document authoring <c>heightDelta = 3</c>, where both terms are non-zero on the
        /// same samples and the maximum height must rise by exactly the authored delta.</item>
        /// <item><c>ShaperLightScene.pointZ</c> is <c>base + height</c> and not <c>Array.Clear</c>ed, so a
        /// point lamp no longer shades an extruded layer as a flat sheet on the base plane.</item>
        /// <item><c>ShaperNormalKind.Profile</c> actually runs when authored — and when its declared inputs
        /// ARE absent the fallback is COUNTED on <c>scene.normalDegenerate</c> rather than written silently.
        /// Both directions are exercised, because a diagnostic that never fires is not a diagnostic.</item>
        /// </list>
        /// </summary>
        public static string H11_StageIsWired()
        {
            var sb = new StringBuilder("H11 the height stage is WIRED to the shipping paint pass (T-0109 FIX F4)\n");
            sb.AppendLine("    Before this fix ShaperHeight.FillTile had NO caller outside this audit, buf.height was");
            sb.AppendLine("    0 + heightDelta*ce, pointZ was Array.Clear'ed, and an authored Profile normal provider");
            sb.AppendLine("    silently degraded to (0,0,1). Each line below is the negation of one of those.");
            bool all = true;

            const int W = 48, H = 40;
            const float Px = 1f;
            var grid = ShaperSampleGrid.Centred(W, H, Px);
            int n = W * H;

            var doc = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
            var layer = new ShaperLayer
            {
                name = "extruded",
                root = Rect(15f, 11f, 3f),
                height = new ShaperHeightDef
                {
                    technique = ShaperExtrusionTechnique.Dome,
                    bevel = ShaperBevelTechnique.Rounded,
                    depth = new ZUIValue(24f),
                    bevelAmount = new ZUIValue(0.3f)
                },
                zOffset = new ZUIValue(5f)
            };
            layer.response.normalKind = ShaperNormalKind.Profile;
            doc.layers.Add(layer);

            var lightProg = ShaperLightCompiler.CompileDocument(doc);
            var fdoc = ShaperFillResolver.Resolve(layer.root, doc.phase01, doc.seed,
                                                  0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
            int k = Mathf.Max(1, fdoc.owners.Count);
            var buf = new ShaperFillBuffers(n, k);
            ShaperProgram layerProg = fdoc.owners.Count > 0 ? fdoc.owners[0].shape : null;

            var scene = ShaperLightCompiler.BindLayer(doc, 0, lightProg, buf.sampleCapacity, buf.ownerCapacity,
                                                      layerProg, Px);
            ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, W, H, buf,
                                         new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                         scene);

            float expectedBase = 0f * doc.layerSpacing + 5f;
            var hop = scene.heightOp[0];

            // (1) the sheet is written at all.
            int nonZeroOwn = 0; float maxOwn = 0f;
            for (int i = 0; i < n; i++) { float v = buf.ownHeight[i]; if (v != 0f) nonZeroOwn++; if (v > maxOwn) maxOwn = v; }
            bool wired = nonZeroOwn > 0 && maxOwn > 1f;
            all &= wired;
            sb.AppendLine("    1  ShaperHeight.FillTile called from PaintTile: " + nonZeroOwn + "/" + n +
                          " samples carry a height, max " + maxOwn.ToString("F3") + " canvas px  " + Verdict(wired));

            // (2) FC-2.5 is a real sum: buf.height carries height_shape.
            int seeded = 0; float worstSeed = 0f;
            for (int i = 0; i < n; i++)
            {
                float diff = Mathf.Abs(buf.height[i] - buf.ownHeight[i]);
                if (diff <= 1e-4f) seeded++; else worstSeed = Mathf.Max(worstSeed, diff);
            }
            bool sumOk = seeded == n;
            all &= sumOk;
            sb.AppendLine("    2a FC-2.5 height_final carries height_shape (delta 0 document): " + seeded + "/" + n +
                          " samples" + (worstSeed > 0f ? " (worst mismatch " + worstSeed.ToString("E3") + ")" : "") + "  " + Verdict(sumOk));

            // T-0109 FIX N7 — 2a alone PROVES THE SEED, NOT THE SUM.
            //
            // The document above authors no fill, so heightDelta ≡ 0 and `height == ownHeight` is true for a
            // reason that has nothing to do with FC-2.5 being a sum: it would hold identically if the `+=`
            // had been a plain assignment, or if the delta term had been deleted. A statement whose fixture
            // makes one of its two terms identically zero cannot distinguish `a + b` from `a`.
            //
            // So the same document is re-run with a Solid fill carrying a NON-ZERO heightDelta, and three
            // things are required at once: both terms are non-zero on a real population of samples, the
            // residual `height − ownHeight` is a legal `heightDelta·coverageEff` (i.e. ce ∈ [0,1] against the
            // authored delta), and the MAXIMUM height rises by exactly the authored delta. The last is the
            // one a broken sum cannot fake.
            {
                const float Delta = 3f;
                var doc2 = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
                var layer2 = new ShaperLayer
                {
                    name = "extruded+delta",
                    root = Rect(15f, 11f, 3f),
                    height = new ShaperHeightDef
                    {
                        technique = ShaperExtrusionTechnique.Dome,
                        bevel = ShaperBevelTechnique.Rounded,
                        depth = new ZUIValue(24f),
                        bevelAmount = new ZUIValue(0.3f)
                    },
                    zOffset = new ZUIValue(5f)
                };
                layer2.root.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.Solid,
                    solidColor = Color.white,
                    veil = new ZUIValue(1f),
                    heightDelta = new ZUIValue(Delta),
                    composite = ShaperFillComposite.Over,
                };
                layer2.response.normalKind = ShaperNormalKind.Profile;
                doc2.layers.Add(layer2);

                var lp2 = ShaperLightCompiler.CompileDocument(doc2);
                var fdoc2 = ShaperFillResolver.Resolve(layer2.root, doc2.phase01, doc2.seed,
                                                       0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
                int k2 = Mathf.Max(1, fdoc2.owners.Count);
                var buf2 = new ShaperFillBuffers(n, k2);
                ShaperProgram lprog2 = fdoc2.owners.Count > 0 ? fdoc2.owners[0].shape : null;
                var scene2 = ShaperLightCompiler.BindLayer(doc2, 0, lp2, buf2.sampleCapacity, buf2.ownerCapacity,
                                                           lprog2, Px);
                ShaperFillResolver.PaintTile(fdoc2, grid, 0, 0, W, H, buf2,
                                             new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                             scene2);

                int shapeNZ = 0, deltaNZ = 0, bothNZ = 0, legalCe = 0;
                float maxWith = 0f, maxWithout = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (buf2.ownHeight[i] != 0f) shapeNZ++;
                    float resid = buf2.height[i] - buf2.ownHeight[i];
                    if (resid != 0f) deltaNZ++;
                    if (buf2.ownHeight[i] != 0f && resid != 0f) bothNZ++;
                    float ce = resid / Delta;
                    if (ce >= -1e-4f && ce <= 1f + 1e-4f) legalCe++;
                    if (buf2.height[i] > maxWith) maxWith = buf2.height[i];
                    if (buf.height[i] > maxWithout) maxWithout = buf.height[i];
                }
                float rise = maxWith - maxWithout;
                bool realSum = bothNZ > 0 && legalCe == n && Mathf.Abs(rise - Delta) <= 1e-3f;
                all &= realSum;
                sb.AppendLine("    2b FC-2.5 is a real SUM, not a seed (T-0109 FIX N7 - 2a's document has");
                sb.AppendLine("       heightDelta = 0 identically, so it cannot tell `a+b` from `a`):");
                sb.AppendLine("         samples with height_shape != 0                 " + shapeNZ + "/" + n);
                sb.AppendLine("         samples with heightDelta   != 0                " + deltaNZ + "/" + n);
                sb.AppendLine("         samples with BOTH terms non-zero               " + bothNZ + "/" + n +
                              "   <- the sum is genuinely a sum");
                sb.AppendLine("         (height - height_shape)/delta a legal coverageEff in [0,1]: " + legalCe + "/" + n);
                sb.AppendLine("         max height_final with delta " + Delta.ToString("F1") + ": " + maxWith.ToString("F4") +
                              "   with delta 0: " + maxWithout.ToString("F4") +
                              "   rise " + rise.ToString("F4") + " (expected " + Delta.ToString("F4") + ")");
                sb.AppendLine("       " + Verdict(realSum));
            }

            // (3) pointZ is base + height, not 0.
            int zOk = 0, zZero = 0;
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(scene.pointZ[i] - (hop.baseZ + buf.ownHeight[i])) <= 1e-3f) zOk++;
                if (scene.pointZ[i] == 0f) zZero++;
            }
            bool pzOk = zOk == n && zZero < n && Mathf.Abs(hop.baseZ - expectedBase) < 1e-4f;
            all &= pzOk;
            sb.AppendLine("    3  LR-1.5 pointZ = base + height (base " + hop.baseZ.ToString("F3") +
                          " from HS-7.2, expected " + expectedBase.ToString("F3") + "): " + zOk + "/" + n +
                          " match, " + zZero + " still exactly 0  " + Verdict(pzOk));

            // (4a) Profile runs: the normals are NOT all (0,0,1), and the degenerate counter is 0.
            int flat = 0;
            for (int i = 0; i < n; i++)
                if (scene.normal[i * 3 + 0] == 0f && scene.normal[i * 3 + 1] == 0f && scene.normal[i * 3 + 2] == 1f) flat++;
            bool profileRan = scene.normalDegenerate[0] == 0 && flat < n;
            all &= profileRan;
            sb.AppendLine("    4a Profile normal provider RUNS when authored: degenerate count " +
                          scene.normalDegenerate[0] + ", " + flat + "/" + n + " samples still exactly (0,0,1)  " +
                          Verdict(profileRan));

            // (4b) INJECTION: take the height stage away and the SAME authored Profile must now report the
            //      fallback loudly instead of writing (0,0,1) in silence.
            {
                var doc2 = new ShaperDocument { canvasWidth = W, canvasHeight = H, pixelSize = Px };
                var layer2 = new ShaperLayer { name = "flat", root = Rect(15f, 11f, 3f), height = null };
                layer2.response.normalKind = ShaperNormalKind.Profile;
                doc2.layers.Add(layer2);
                var lp2 = ShaperLightCompiler.CompileDocument(doc2);
                var fd2 = ShaperFillResolver.Resolve(layer2.root, 0f, 0u, 0.5f * (W - 1) * Px, 0.5f * (H - 1) * Px);
                var buf2 = new ShaperFillBuffers(n, Mathf.Max(1, fd2.owners.Count));
                var sc2 = ShaperLightCompiler.BindLayer(doc2, 0, lp2, buf2.sampleCapacity, buf2.ownerCapacity,
                                                        fd2.owners.Count > 0 ? fd2.owners[0].shape : null, Px);
                ShaperFillResolver.PaintTile(fd2, grid, 0, 0, W, H, buf2,
                                             new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine }, sc2);
                bool loud = sc2.normalDegenerate[0] == n;
                all &= loud;
                sb.AppendLine("    4b INJECTION - same authored Profile with NO height stage: degenerate count " +
                              sc2.normalDegenerate[0] + "/" + n + " (the fallback is LOUD, not silent)  " + Verdict(loud));
            }

            sb.AppendLine("    VERDICT " + Verdict(all));
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // Shared scene helpers, used by the checks above and by both renders below.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        static ShaperNode Rect(float hw, float hh, float r)
            => ShaperNode.Primitive(new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh, rectCornerRadius = r
            });

        /// <summary>T-0109 FIX F5 — a thin vertical slot, subtracted, centred at <paramref name="cx"/>.</summary>
        static ShaperNode SlotAt(float cx, float halfW, float halfH)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Rect, rectHalfW = halfW, rectHalfH = halfH
            }, "slot", ShaperCombineMode.Subtract);
            n.transform.translate = new Vector2(cx, 0f);
            return n;
        }

        /// <summary>
        /// <b>T-0109 FIX F5 — HS-1.1's membership predicate, written HERE.</b>
        ///
        /// It is deliberately a SECOND implementation and not a call into the general resolve (the HS-9 march,
        /// since deleted, T-0253): the whole point of the omission check is to compare the march against
        /// something that does not share the march's own machinery. This reads the set definition
        /// literally — inside the silhouette, and below <c>base + body·G(t)</c> — and nothing else.
        /// </summary>
        static bool TruthInside(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                float ox, float oy, float oz, float dx, float dy, float dz, float s)
        {
            float z = oz + s * dz;
            float above = z - op.baseZ;
            if (above < 0f || above > op.body * op.supG) return false;
            float px = ox + s * dx, py = oy + s * dy;
            float d = ShaperEvaluator.Distance(field, px, py, stack);
            if (d > 0f || ShaperField.IsEmpty(d)) return false;
            float t = ShaperHeight.T(op, d);
            float nx = 0f, ny = 0f;
            if (op.technique == ShaperExtrusionTechnique.Linear)
                ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);

            // T-0109 FIX N3, the reference half. `G(t) = 0` means there is NO SOLID here, not a membrane of
            // zero thickness — and because `G` is zero over a BAND for a Stepped profile or bevel (the whole
            // ring t < 1/n around the silhouette), the closed reading gave the layer a two-dimensional
            // zero-measure skirt on the base plane. This second implementation carries the amended HS-1.1,
            // not the old one; keeping the old reading here is what let this check certify the very pairs it
            // was built to catch.
            float g = ShaperHeight.Composed(op, t, nx, ny);
            if (!(g > 0f)) return false;
            return above <= op.body * g;
        }

        /// <summary>
        /// T-0109 FIX F5 — the ground-truth crossing list: a dense uniform scan of
        /// <see cref="TruthInside"/> with each flip bisected. Slow and stupid on purpose; that is what makes
        /// it an independent oracle rather than a second copy of the thing being tested.
        /// </summary>
        static List<float> TruthCrossings(ShaperProgram field, float[] stack, in ShaperHeightOp op,
                                          float ox, float oy, float oz, float dx, float dy, float dz,
                                          float sMax, int samples)
        {
            var res = new List<float>();
            bool prev = TruthInside(field, stack, op, ox, oy, oz, dx, dy, dz, 0f);
            if (prev) res.Add(0f);
            for (int i = 1; i <= samples; i++)
            {
                float s = sMax * i / samples;
                bool m = TruthInside(field, stack, op, ox, oy, oz, dx, dy, dz, s);
                if (m != prev)
                {
                    float lo = sMax * (i - 1) / samples, hi = s;
                    for (int k = 0; k < 40; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (TruthInside(field, stack, op, ox, oy, oz, dx, dy, dz, mid) == prev) lo = mid; else hi = mid;
                    }
                    res.Add(hi);
                    prev = m;
                }
            }
            return res;
        }

        static ShaperNode Star(float radius, int arms)
            => ShaperNode.Primitive(new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Star, starArms = arms, starRadius = radius, starLength = new ZUIValue(0.6f)
            });

        /// <summary>Compile a height stage against a real program, which is where <c>span</c> and the local frame come from.</summary>
        static ShaperHeightOp OpFor(ShaperProgram prog, ShaperExtrusionTechnique tech, ShaperBevelTechnique bevel,
                                    float depth, float amount, float baseZ,
                                    float angle = 45f, float steps = 4f, float curve = 1f,
                                    float taper = 1f, float bevelSteps = 3f)
        {
            var def = new ShaperHeightDef
            {
                technique = tech,
                bevel = bevel,
                depth = new ZUIValue(depth),
                angle = new ZUIValue(angle),
                steps = new ZUIValue(steps),
                curve = new ZUIValue(curve),
                taper = new ZUIValue(taper),
                bevelAmount = new ZUIValue(amount),
                bevelSteps = new ZUIValue(bevelSteps),
            };
            return ShaperHeightCompiler.Compile(def, prog, 1f, baseZ);
        }

        /// <summary>
        /// The rig both renders are lit by: one directional key plus one CLOSE POINT LAMP.
        ///
        /// <b>The point lamp is not decoration and the first cut of the contact sheet proved it.</b> A
        /// directional light reads only the NORMAL, and the analytic normal of a Stepped profile is exactly
        /// flat on every tread with a riser of zero screen width (HS-8.4) - so under a directional key alone a
        /// terraced solid renders as a featureless plateau, and three of the sheet's cells showed nothing at
        /// all. That is CORRECT shading of a genuinely flat surface, not a bug, but it makes the sheet useless
        /// for the one profile family whose whole point is its height. A point lamp close to the surface reads
        /// the surface POSITION as well as its direction (<c>ShaperLightLaw.Shade</c> takes <c>pz</c>), so the
        /// treads separate by distance falloff and the profile becomes visible without anything being faked.
        /// </summary>
        static ShaperLightRig Rig()
        {
            var rig = new ShaperLightRig { ambientColour = Color.white, ambientIntensity = new ZUIValue(0.14f) };
            rig.lights.Add(new ShaperLight
            {
                name = "key",
                kind = ShaperLightKind.Directional,
                colour = new Color(1f, 0.95f, 0.88f),
                intensity = new ZUIValue(0.95f),
                yaw = new ZUIValue(-55f),
                pitch = new ZUIValue(34f),
                specular = new ZUIValue(0.9f),
            });
            rig.lights.Add(new ShaperLight
            {
                name = "height lamp",
                kind = ShaperLightKind.Point,
                colour = new Color(0.72f, 0.86f, 1f),
                intensity = new ZUIValue(1.5f),
                posX = new ZUIValue(26f),
                posY = new ZUIValue(-20f),
                posZ = new ZUIValue(30f),
                range = new ZUIValue(70f),
                specular = new ZUIValue(0.5f),
            });
            return rig;
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════
        // The two renders.
        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The height contact sheet: every extrusion profile against a representative bevel, rendered and lit
        /// through the REAL pipeline — <see cref="ShaperEvaluator"/> for the field,
        /// <see cref="ShaperHeight.FillTile"/> for the height sheet, <see cref="ShaperNormals"/>'s
        /// <c>Profile</c> case for the normal, and <see cref="ShaperLightLaw.Shade"/> for the light — so a
        /// human can SEE the profiles rather than read a passing table about them.
        ///
        /// T-0105's precedent, and FT-20's reason: "A passing table is not a picture… kept and LOOKED AT BY A
        /// HUMAN." Cells are numbered on the image and the numbers map to names in the audit's text output,
        /// which is the same convention T-0105's own contact sheet used.
        /// </summary>
        public static string HeightContactSheet(string path)
        {
            const int Cell = 104, Cols = 6, Pad = 5;

            var cells = new List<KeyValuePair<string, ShaperHeightOp>>();
            var shapes = new List<ShaperNode>();

            Action<string, ShaperNode, ShaperExtrusionTechnique, ShaperBevelTechnique, float, float, float, float, float, float> C =
                (nm, shape, tech, bev, depth, amount, angle, steps, curve, taper) =>
                {
                    var prog = ShaperCompiler.Compile(shape);
                    cells.Add(new KeyValuePair<string, ShaperHeightOp>(nm,
                        OpFor(prog, tech, bev, depth, amount, 0f, angle, steps, curve, taper, 3f)));
                    shapes.Add(shape);
                };

            Func<ShaperNode> rr = () => Rect(34f, 26f, 8f);
            Func<ShaperNode> st = () => Star(34f, 6);

            // 01-07 — every profile, NO bevel. The profiles alone, comparable on one silhouette.
            C("01 Flat", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("02 Linear 45", rr(), ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("03 Stepped n=4", rr(), ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("04 Dome c=1", rr(), ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("05 Round c=1", rr(), ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("06 Taper t=1", rr(), ShaperExtrusionTechnique.Taper, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("07 Pyramid t=1", rr(), ShaperExtrusionTechnique.Pyramid, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);

            // 08-14 — the SAME seven under a representative bevel (Rounded, a = 0.35), which is the pairing
            // HS-3.3's product rule is about: a bevel MULTIPLIES the profile, so a domed rim and a filleted
            // rim compound into a much sharper edge than either alone.
            C("08 Flat+Rounded", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("09 Linear+Rounded", rr(), ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("10 Stepped+Rounded", rr(), ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("11 Dome+Rounded", rr(), ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("12 Round+Rounded", rr(), ShaperExtrusionTechnique.Round, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("13 Taper+Rounded", rr(), ShaperExtrusionTechnique.Taper, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);
            C("14 Pyramid+Rounded", rr(), ShaperExtrusionTechnique.Pyramid, ShaperBevelTechnique.Rounded, 16f, 0.35f, 45f, 4f, 1f, 1f);

            // 15-20 — ALL SIX bevels on one Flat plateau, so the bevel family is visible on its own.
            C("15 bevel None", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.None, 16f, 0.4f, 45f, 4f, 1f, 1f);
            C("16 bevel Linear", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Linear, 16f, 0.4f, 45f, 4f, 1f, 1f);
            C("17 bevel Rounded", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Rounded, 16f, 0.4f, 45f, 4f, 1f, 1f);
            C("18 bevel Cove", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Cove, 16f, 0.4f, 45f, 4f, 1f, 1f);
            C("19 bevel Ogee", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Ogee, 16f, 0.4f, 45f, 4f, 1f, 1f);
            C("20 bevel Stepped", rr(), ShaperExtrusionTechnique.Flat, ShaperBevelTechnique.Stepped, 16f, 0.4f, 45f, 4f, 1f, 1f);

            // 21-24 — the parameter extremes that change the READ of a profile, not just its numbers.
            C("21 Stepped n=12", rr(), ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.None, 16f, 0f, 45f, 12f, 1f, 1f);
            C("22 Dome c=0.25", rr(), ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 0.25f, 1f);
            C("23 Round c=4", rr(), ShaperExtrusionTechnique.Round, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 4f, 1f);
            C("24 Taper t=0.3", rr(), ShaperExtrusionTechnique.Taper, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 0.3f);

            // 25-26 — a non-convex silhouette, where the bevel band and the profile ramp both bend.
            C("25 Star Dome", st(), ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 16f, 0f, 45f, 4f, 1f, 1f);
            C("26 Star Stepped+Step", st(), ShaperExtrusionTechnique.Stepped, ShaperBevelTechnique.Stepped, 16f, 0.4f, 45f, 6f, 1f, 1f);

            // 27-28 — the two identity cells, present so "nothing to see" is visible rather than absent.
            C("27 depth 0 (nothing)", rr(), ShaperExtrusionTechnique.Dome, ShaperBevelTechnique.None, 0f, 0f, 45f, 4f, 1f, 1f);
            C("28 Linear 135", rr(), ShaperExtrusionTechnique.Linear, ShaperBevelTechnique.None, 16f, 0f, 135f, 4f, 1f, 1f);

            int rows = (cells.Count + Cols - 1) / Cols;
            int texW = Cols * Cell + (Cols + 1) * Pad;
            int texH = rows * Cell + (rows + 1) * Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(20, 21, 25, 255);
            sheet.SetPixels32(bg);

            var lightProg = ShaperLightCompiler.Compile(Rig(), 0f, 0u);
            var resp = ShaperLightCompiler.CompileResponse(new ShaperLightResponse(), "sheet", 0f, 0u);
            var px = new Color32[Cell * Cell];

            for (int c = 0; c < cells.Count; c++)
            {
                RenderCell(shapes[c], cells[c].Value, lightProg, resp, Cell, px);
                DrawNumber(px, Cell, c + 1);
                int col = c % Cols, row = rows - 1 - (c / Cols);
                sheet.SetPixels32(Pad + col * (Cell + Pad), Pad + row * (Cell + Pad), Cell, Cell, px);
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);

            var sb = new StringBuilder("HEIGHT CONTACT SHEET\n");
            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", " + texW + "x" + texH + " px -> " + path);
            sb.AppendLine("  Every cell is the real pipeline: ShaperEvaluator -> ShaperHeight.FillTile ->");
            sb.AppendLine("  ShaperNormals(Profile) -> ShaperLightLaw.Shade, one directional key + ambient.");
            for (int c = 0; c < cells.Count; c++)
                sb.AppendLine("    row " + (c / Cols) + " col " + (c % Cols) + "  " + cells[c].Key);
            sb.AppendLine("  Cells whose CORRECT reading is 'nothing to see', stated so they are not read as bugs:");
            sb.AppendLine("    27 depth 0 - a zero-thickness sheet publishes height 0 and no wall (HS-2.2), so it");
            sb.AppendLine("       shades perfectly flat. That is the identity, not a failure to render.");
            sb.AppendLine("    01/02/15 - Flat and Linear are the only profiles with a full-height side wall, and");
            sb.AppendLine("       a wall is invisible straight down (HS-6.5). See tilted-conformance.png for one.");
            return sb.ToString();
        }

        /// <summary>Render one contact-sheet cell through the whole real pipeline.</summary>
        static void RenderCell(ShaperNode shape, in ShaperHeightOp hop, ShaperLightProgram lightProg,
                               in ShaperResponseCompiled resp, int cell, Color32[] outPx)
        {
            var prog = ShaperCompiler.Compile(shape);
            var stack = prog.NewStack();
            float pixelSize = 92f / cell;
            var grid = ShaperSampleGrid.Centred(cell, cell, pixelSize);

            int n = cell * cell;
            var dist = new float[n];
            var cov = new float[n];
            var hgt = new float[n];
            var nrm = new float[n * 3];

            ShaperEvaluator.FillTile(prog, grid, 0, 0, cell, cell, dist, cov, 0, cell, stack);
            ShaperHeight.FillTile(hop, grid, 0, 0, cell, cell, dist, hgt, 0, cell, 0, cell);

            var nop = ShaperNormalOp.Default;
            nop.kind = ShaperNormalKind.Profile;
            nop.height = hop;
            ShaperNormals.FillTile(nop, grid, 0, 0, cell, cell, dist, hgt, nrm, 0, cell, 0, cell, prog, stack);

            var albedo = new Color(0.82f, 0.55f, 0.32f);
            for (int j = 0; j < cell; j++)
                for (int i = 0; i < cell; i++)
                {
                    int k = j * cell + i;
                    float a = Mathf.Clamp01(cov[k]);
                    float r = 0.08f, g = 0.085f, b = 0.10f;
                    if (a > 0.001f)
                    {
                        ShaperLightLaw.Shade(lightProg.rig, resp,
                                             grid.X(i), grid.Y(j), hop.baseZ + hgt[k],
                                             nrm[k * 3], nrm[k * 3 + 1], nrm[k * 3 + 2],
                                             0f, 0f, 1f,
                                             out float lr, out float lg, out float lb,
                                             out float sr, out float sg, out float sb2);
                        float cr = albedo.r * lr + sr, cg = albedo.g * lg + sg, cb = albedo.b * lb + sb2;
                        r = Mathf.Lerp(r, cr, a); g = Mathf.Lerp(g, cg, a); b = Mathf.Lerp(b, cb, a);
                    }
                    outPx[k] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(r)) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(g)) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(b)) * 255f), 0, 255), 255);
                }
        }

        // A compact 3x5 digit font, so each contact-sheet cell carries its own index and the numbers map to
        // names in the text output. T-0105's contact sheet logged its names the same way.
        // Three column masks per glyph; bit r is row r counted from the BOTTOM, which is the direction
        // DrawNumber walks and the direction Texture2D.SetPixels32 addresses. The first cut of this table had
        // several glyphs' columns in the opposite order, which is invisible on a symmetric digit and turns a
        // 2 into a 5 - so the sheet was labelled with plausible-looking WRONG numbers, which is worse than no
        // labels at all. Verified by eye against the rendered sheet, not by re-reading the table.
        static readonly byte[] Digits =
        {
            0x1F, 0x11, 0x1F, // 0
            0x00, 0x1F, 0x00, // 1
            0x17, 0x15, 0x1D, // 2
            0x11, 0x15, 0x1F, // 3
            0x1C, 0x04, 0x1F, // 4
            0x1D, 0x15, 0x17, // 5
            0x1F, 0x15, 0x17, // 6
            0x10, 0x10, 0x1F, // 7
            0x1F, 0x15, 0x1F, // 8
            0x1D, 0x15, 0x1F, // 9
        };

        static void DrawNumber(Color32[] px, int cell, int value)
        {
            string s = value.ToString("00");
            int x0 = 3, y0 = cell - 9;
            for (int c = 0; c < s.Length; c++)
            {
                int d = s[c] - '0';
                for (int col = 0; col < 3; col++)
                {
                    byte bits = Digits[d * 3 + col];
                    for (int row = 0; row < 5; row++)
                    {
                        int x = x0 + c * 4 + col, y = y0 + row;
                        if (x < 0 || x >= cell || y < 0 || y >= cell) continue;
                        px[y * cell + x] = ((bits >> row) & 1) != 0
                            ? new Color32(250, 250, 250, 255)
                            : new Color32(0, 0, 0, 255);
                    }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Run every check and both renders, and return the whole report.</summary>
        // H6 (branch agreement), H12 (V1/V2/V3 regression traps) and TiltedConformance all exercised the
        // general resolve (HS-9); that resolve was deleted outright (T-0253) and so were they.
        public static string RunAll(string contactSheetPath = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0109 HEIGHT AUDIT - extrusion, bevel and Z offset");
            sb.AppendLine("Measured versus declared. A bound is never reported as verified on the strength of");
            sb.AppendLine("the code compiling, and a declaration of +inf is verified by DIVERGENCE, not a number.");
            sb.AppendLine("run at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   dataPath " + Application.dataPath);
            sb.AppendLine(new string('=', 100));
            sb.AppendLine();
            sb.AppendLine(H1_Monotonicity());
            sb.AppendLine(H2_FiniteBounds());
            sb.AppendLine(H3_Divergence());
            sb.AppendLine(H4_InverseRoundTrip());
            sb.AppendLine(H5_BitIdentity());
            sb.AppendLine(H7_WallStatements());
            sb.AppendLine(H8_Allocation());
            sb.AppendLine(H9_AnalyticDerivative());
            sb.AppendLine(H10_DialsAreRead());
            sb.AppendLine(H11_StageIsWired());
            if (!string.IsNullOrEmpty(contactSheetPath)) sb.AppendLine(HeightContactSheet(contactSheetPath));
            return sb.ToString();
        }
    }
}
