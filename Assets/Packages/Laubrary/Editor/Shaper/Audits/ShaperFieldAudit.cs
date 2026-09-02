using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// The field audit: thirteen checks over the compiled shape engine, each returning a report string.
    ///
    /// V10–V12 exist because an independent verification found that the first nine, though all passing,
    /// claimed more than they tested: no degenerate input was exercised anywhere (V10), the Ellipse — the one
    /// primitive the spec flags as needing a measured bound — was not a subject of the ratio cross-check at
    /// all and was in fact broken (V11), and no check measured a primitive's authored extent against what it
    /// actually drew (V12).
    ///
    /// Plain static methods, no <c>[MenuItem]</c> and no <c>EditorWindow</c> — this is the engine's
    /// verification harness, invoked through the Unity CLI, not a tool.
    ///
    /// Every result is reported as <b>measured versus declared</b>. A bound is never reported as verified on
    /// the strength of the code compiling.
    /// </summary>
    public static class ShaperFieldAudit
    {
        // The PASS/FAIL threshold on a measured-over-declared ratio.
        //
        // 1.001, not the 1.02 this used to be. The justification is that the instrument below is a
        // DIRECTIONAL difference quotient, which is the Lipschitz constant's own definition restricted to a
        // finite sample and is therefore bounded by L *by construction* — it converges to L from below and
        // can never manufacture a violation. So the only thing above 1 it can ever report is float
        // cancellation noise: at extent ~130 the field value is ~1e2, one float ulp is ~1e-5, and with
        // h = 0.01 the quotient divides by 2h = 0.02, so a few ulps of cancellation is ~1e-3. Anything above
        // that is real signal. A tolerance of 1.02 would have printed "ok" against a genuine 1.019 violation
        // — twenty times the noise floor — and the whole point of the number is to decide when to look.
        const float BoundTolerance = 1.001f;

        // The probe step for the difference quotient, as a FRACTION OF THE EXTENT and so deliberately
        // decoupled from the sample grid. It used to be h = gridStep/2, which is 0.47–1.72 canvas units at
        // the extents V4 runs at, and far too coarse to straddle a localised violation: the same code at
        // h = 0.005 with 64 directions took the (then broken) Ellipse 60x25 from 1.0002 to 4051. h measures
        // the local gradient; the grid only decides WHERE to measure it, which is what the resolution ladder
        // and the refinement pass are for.
        const float GradientStepFraction = 1e-3f;

        // The refinement pass re-measures the worst broad points on a local grid, with every direction, at the
        // broad step AND at a step two orders of magnitude finer.
        //
        // ONLY the broad-step readings are part of the verdict. The fine step is a LOCALISER, not a second
        // gate: it says how sharp and how narrow the worst feature is, which is what tells a reader whether a
        // reading is a kink, a seam or noise. It is not a gate because at 1e-5 of the extent the rounding
        // correction below is near its limit of validity, and a gate you have to widen to make usable is not
        // a gate. Read RoundingUlps before lowering this constant.
        const float RefineStepFraction = 1e-5f;

        // Directions sampled by the difference quotient on the broad pass. Only a half-turn is needed: u and
        // −u give the same quotient. A localised violation on a thin locus is found by direction count at
        // least as much as by grid density — the ellipse spike sat on a 1-D curve that 8 directions walked
        // straight past.
        const int GradientDirections = 16;

        // The refinement pass: the N worst points of the broad pass are re-measured on a local grid filling
        // the gaps between broad samples, with every direction, at both step scales.
        const int RefineWorstPoints = 24;
        const int RefineDirections = 64;

        // How many float ulps of rounding one field evaluation may carry. Subtracting `ulps·eps·|f|` from the
        // NUMERATOR of the difference quotient is what keeps a small h honest: the true difference is within
        // that bound of the computed one, so the corrected quotient is a LOWER bound on the true quotient and
        // cannot manufacture a violation. Without it, a fixed rounding error becomes a quotient error of
        // ulps·eps·|f| / 2h that grows without limit as h shrinks — a first cut of the refinement pass used
        // h = 1e-3 canvas units and duly reported 1.0071 on a plain Rect, which is an exact SDF: the
        // instrument was manufacturing the very violation it exists to rule out.
        //
        // WHAT 8 ULPS IS AND IS NOT SOUND FOR. It is scoped to the two steps configured above, and the claim
        // does not generalise downward. Measured on an exact Rect at extent 130:
        //
        //     h = extent·1e-3 (broad)   worst needs  ~2 ulps   -> 8 is a ~4x margin
        //     h = extent·1e-5 (fine)    worst needs  ~1.7 ulps -> 8 is a ~4.7x margin
        //     h = 5e-4  (below fine)    worst needs   39 ulps  -> 8 reads 1.00337 on an EXACT field
        //     h = 3e-5  (below fine)    worst needs   65 ulps  -> 8 reads 1.03692 on an EXACT field
        //
        // The reason it degrades is that |f| is not the only thing rounding: the probe coordinates p ± h·u
        // round too, by about eps·|p|, and a 1-Lipschitz field turns that straight into numerator error of
        // order eps·|p| — independent of h, so eps·|p|/h once divided. Below the configured fine step that
        // term dominates and 8 ulps stops covering it. So: DO NOT lower RefineStepFraction (or hand this
        // instrument a smaller h) without re-deriving this constant, and note that the fine pass is
        // diagnostic precisely because it already sits near the edge of where the correction holds.
        const float RoundingUlps = 8f;
        const float FloatEps = 1.1920929e-7f;

        /// <summary>
        /// The size of feature the fine pass can still tell apart from rounding, at a given step and field
        /// magnitude. <b>Reported, never added to a threshold.</b>
        ///
        /// It used to be added: <c>okFine = fine &lt;= bound·tol + fineFloor</c>. That double-counted, because
        /// the same rounding term is already subtracted from the quotient's numerator, and subtracting once is
        /// what makes the reading a rigorous lower bound. Adding it back as a floor roughly doubled the
        /// leniency — enough that an injected 1.05x violation passed the fine test while the broad test
        /// correctly failed it. The verdict is now the broad pass alone (which the local refinement grid also
        /// feeds, at the broad step); this number is context for reading the fine number, nothing more.
        /// </summary>
        static float DiscriminationFloor(float magnitude, float h)
            => h <= 0f ? 0f : RoundingUlps * FloatEps * magnitude / (2f * h);

        // ── Public entry points ───────────────────────────────────────────────────────────────────────────

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper field audit ===");
            sb.AppendLine(V1_SweepIdentity());
            sb.AppendLine(V2_ShellIdentity());
            sb.AppendLine(V3_SoftCombineIdentities());
            sb.AppendLine(V4_DeclaredBounds());
            sb.AppendLine(V5_RatioCrossCheck());
            sb.AppendLine(V6_Anisotropy());
            sb.AppendLine(V7_LandmarkTrap());
            sb.AppendLine(V8_TileIndependence());
            sb.AppendLine(V9_ShelledInsideness());
            sb.AppendLine(V10_DegenerateInputs());
            sb.AppendLine(V11_EllipseAgainstOracle());
            sb.AppendLine(V12_NGonOuterExtent());
            sb.AppendLine(V13_InstrumentSelfTest());
            return sb.ToString();
        }

        /// <summary>
        /// V1 — sweep identity is bit-identical. For every primitive, over a dense grid, the field with sweep
        /// at full extent must be bit-for-bit equal to the field with no sweep node at all.
        /// </summary>
        public static string V1_SweepIdentity()
        {
            var sb = new StringBuilder("V1 sweep identity (bitwise, full extent vs no sweep)\n");
            int totalMismatch = 0, totalSamples = 0;

            foreach (var c in Cases())
            {
                var plain = ShaperNode.Primitive(c.def, c.name);

                var swept = ShaperNode.Primitive(Clone(c.def), c.name);
                swept.sweep = new ShaperSweep { enabled = true, startDegrees = 37f, extentDegrees = 360f };
                // A capsule declares a longitudinal axis, where full extent is start 0 across the whole length.
                if (ShaperPrimitives.SweepAxis(c.def.kind) == ShaperSweepAxis.Longitudinal)
                    swept.sweep = new ShaperSweep { enabled = true, startFraction = 0f, extentFraction = 1f };

                int mismatch = CountBitMismatch(plain, swept, c.extent, 121, out int samples);
                totalMismatch += mismatch; totalSamples += samples;
                sb.AppendFormat("  {0,-28} mismatched {1}/{2}\n", c.name, mismatch, samples);
            }

            sb.AppendFormat("  RESULT: {0} — {1} mismatched bits over {2} samples\n",
                            totalMismatch == 0 ? "PASS" : "FAIL", totalMismatch, totalSamples);
            return sb.ToString();
        }

        /// <summary>V2 — shell identity is bit-identical with shell disabled.</summary>
        public static string V2_ShellIdentity()
        {
            var sb = new StringBuilder("V2 shell identity (bitwise, disabled shell vs no shell)\n");
            int totalMismatch = 0, totalSamples = 0;

            foreach (var c in Cases())
            {
                var plain = ShaperNode.Primitive(c.def, c.name);
                var shelled = ShaperNode.Primitive(Clone(c.def), c.name);
                shelled.shell = new ShaperShell { enabled = false, thickness = 9f, alignment = ShaperShellAlignment.Outward };

                int mismatch = CountBitMismatch(plain, shelled, c.extent, 121, out int samples);
                totalMismatch += mismatch; totalSamples += samples;
                sb.AppendFormat("  {0,-28} mismatched {1}/{2}\n", c.name, mismatch, samples);
            }

            sb.AppendFormat("  RESULT: {0} — {1} mismatched bits over {2} samples\n",
                            totalMismatch == 0 ? "PASS" : "FAIL", totalMismatch, totalSamples);
            return sb.ToString();
        }

        /// <summary>
        /// V3 — the soft-combine identities. Soft Add at zero blend width equals hard Add bitwise; soft
        /// Subtract at strength 0 is an exact no-op everywhere in the field, not merely where the cutter is;
        /// soft Subtract at strength 1 equals hard Subtract bitwise.
        ///
        /// <b>What (a), (b) and (c) actually verify, stated honestly.</b> All three identities are structural
        /// early-outs in <see cref="ShaperOps.Combine"/> (<c>blendWidth &gt; 0f</c>,
        /// <c>carveStrength &lt;= 0f</c>, <c>carveStrength &gt;= 1f</c>), so a bitwise comparison at those
        /// exact settings exercises the <c>if</c>, not the algebra behind it. That is the right thing to test
        /// — the spec asks for a <i>structural</i> identity precisely because the algebra does not deliver a
        /// bitwise one — but it must not be dressed up as verifying the reference app's own correctness
        /// argument. So (d) below tests that argument separately, on the un-shortcut expression
        /// (<see cref="ShaperOps.SubtractSoftRaw"/>), and reports what it actually is: the band at strength 1
        /// is float <c>sin(π)·reach·0.6 = −8.74e-8·reach</c>, not zero, so the raw expression lands NEAR the
        /// hard cut rather than on it. The early-out is what makes the identity exact, and (d) is the number
        /// that says why it is needed.
        /// </summary>
        public static string V3_SoftCombineIdentities()
        {
            var sb = new StringBuilder("V3 soft-combine identities (bitwise where structural; algebra tested separately)\n");

            var baseDef = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f };
            var cutDef = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 30f, ellipseRy = 30f };
            // One offset for the cutter everywhere, so the three comparisons all name the same operand pair.
            Vector2 CutterOffset = new Vector2(50f, 20f);

            ShaperNode BaseOnly() => ShaperNode.Primitive(Clone(baseDef), "base");

            ShaperNode Cut(float strength)
            {
                var a = ShaperNode.Primitive(Clone(baseDef), "base");
                var b = ShaperNode.Primitive(Clone(cutDef), "cutter", ShaperCombineMode.Subtract);
                b.blend = new ShaperBlend { carveStrength = strength, sharpness = 0.5f };
                b.transform = new ShaperTransformBlock { translate = CutterOffset };
                return ShaperNode.Bag("bag", ShaperCombineMode.Add, a, b);
            }

            ShaperNode Union(float width)
            {
                var a = ShaperNode.Primitive(Clone(baseDef), "base");
                var b = ShaperNode.Primitive(Clone(cutDef), "member");
                b.blend = new ShaperBlend { width = width, sharpness = 0.5f };
                b.transform = new ShaperTransformBlock { translate = CutterOffset };
                return ShaperNode.Bag("bag", ShaperCombineMode.Add, a, b);
            }

            // (a) soft Add at width 0 == min(a, b) built from the same two operands.
            var softAdd = new Runner(Union(0f));
            var opA = new Runner(ShaperNode.Primitive(Clone(baseDef), "base"));
            var opBNode = ShaperNode.Primitive(Clone(cutDef), "member");
            opBNode.transform = new ShaperTransformBlock { translate = CutterOffset };
            var opB = new Runner(opBNode);

            int mismatchAdd = 0, n = 0;
            ForEachSample(140f, 121, (x, y) =>
            {
                float got = softAdd.D(x, y);
                float want = Mathf.Min(opA.D(x, y), opB.D(x, y));
                if (!SameBits(got, want)) mismatchAdd++;
                n++;
            });
            sb.AppendFormat("  soft Add @ width 0 vs hard min : mismatched {0}/{1}\n", mismatchAdd, n);

            // (b) soft Subtract at strength 0 is an exact no-op EVERYWHERE, including deep inside both operands.
            var noCut = new Runner(Cut(0f));
            var justBase = new Runner(BaseOnly());
            int mismatchZero = 0, n2 = 0, deepSamples = 0;
            ForEachSample(140f, 121, (x, y) =>
            {
                float got = noCut.D(x, y);
                float want = justBase.D(x, y);
                if (!SameBits(got, want)) mismatchZero++;
                n2++;
            });
            // Explicitly probe points deep inside both operands — the case an offset-the-cutter implementation
            // silently corrupts with no visible hole.
            for (int i = 0; i < 64; i++)
            {
                float t = i / 63f;
                float x = CutterOffset.x + Mathf.Lerp(-12f, 12f, t), y = CutterOffset.y + Mathf.Lerp(-12f, 12f, t);
                if (!SameBits(noCut.D(x, y), justBase.D(x, y))) mismatchZero++;
                deepSamples++;
            }
            sb.AppendFormat("  soft Subtract @ strength 0     : mismatched {0}/{1} (+{2} deep-interior probes)\n",
                            mismatchZero, n2, deepSamples);

            // (c) soft Subtract at strength 1 == max(a, −b).
            var fullCut = new Runner(Cut(1f));
            int mismatchOne = 0, n3 = 0;
            ForEachSample(140f, 121, (x, y) =>
            {
                float got = fullCut.D(x, y);
                float want = Mathf.Max(opA.D(x, y), -opB.D(x, y));
                if (!SameBits(got, want)) mismatchOne++;
                n3++;
            });
            sb.AppendFormat("  soft Subtract @ strength 1     : mismatched {0}/{1}\n", mismatchOne, n3);
            sb.AppendLine("    (a)(b)(c) above are STRUCTURAL early-outs in ShaperOps.Combine — a bitwise pass");
            sb.AppendLine("    here verifies the branch, which is what the spec asks for, not the algebra.");

            // (d) The reference app's own argument, tested on the expression the early-out bypasses. Its claim
            // is that band = sin(strength*pi)*reach*0.6 is zero at strength 1, "so the expression degenerates
            // exactly to the hard max(a, -d)". In real arithmetic, yes. In floats, no — and this is the number.
            const float reachProbe = 100f;
            float bandAtOne = Mathf.Sin(1f * Mathf.PI) * reachProbe * 0.6f;
            float bandAtZero = Mathf.Sin(0f * Mathf.PI) * reachProbe * 0.6f;
            float n1 = ShaperOps.BlendExponent(0.5f);
            float worstRawOne = 0f, worstRawZero = 0f;
            int rawSamples = 0;
            for (int i = 0; i < 401; i++)
            {
                float a = Mathf.Lerp(-120f, 120f, i / 400f);
                for (int j = 0; j < 401; j++)
                {
                    float b = Mathf.Lerp(-120f, 120f, j / 400f);
                    float rawOne = ShaperOps.SubtractSoftRaw(a, b, 1f, n1, reachProbe);
                    float rawZero = ShaperOps.SubtractSoftRaw(a, b, 0f, n1, reachProbe);
                    worstRawOne = Mathf.Max(worstRawOne, Mathf.Abs(rawOne - Mathf.Max(a, -b)));
                    worstRawZero = Mathf.Max(worstRawZero, Mathf.Abs(rawZero - a));
                    rawSamples++;
                }
            }
            sb.AppendFormat("  (d) un-shortcut algebra, reach {0}: band@s=1 = {1:E3} (NOT 0), band@s=0 = {2:E3}\n",
                            reachProbe, bandAtOne, bandAtZero);
            sb.AppendFormat("      raw(s=1) vs max(a,-b): worst |diff| {0:E3} over {1} (a,b) pairs\n", worstRawOne, rawSamples);
            sb.AppendFormat("      raw(s=0) vs a        : worst |diff| {0:E3}\n", worstRawZero);
            sb.AppendLine("      Reading: the algebra lands NEAR the hard cut, not ON it. Bit-identity comes");
            sb.AppendLine("      from the early-out, and that is exactly why the early-out is not optional.");

            bool pass = mismatchAdd == 0 && mismatchZero == 0 && mismatchOne == 0;
            sb.AppendFormat("  RESULT: {0}\n", pass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V4 — every declared bound holds, measured. <c>sup|∇f|</c> by central differences over a dense grid
        /// covering the shape and a generous margin outside it, at several resolutions to confirm the
        /// supremum has converged rather than been missed between samples.
        /// </summary>
        public static string V4_DeclaredBounds()
        {
            var sb = new StringBuilder("V4 declared bounds, measured sup|grad f| (measured vs declared)\n");
            bool allPass = true;

            void Check(string label, ShaperNode node, float extent)
            {
                var program = ShaperCompiler.Compile(node);
                float measured = MaxGradientDetailed(program, extent, out float fine, out float fineFloor, out Vector2 at);
                // THE VERDICT IS THE BROAD READING ALONE. The local refinement grid feeds this same reading
                // at the broad step, so a violation hiding between broad samples is still caught here; the
                // fine number is printed as a localiser (how sharp / how narrow), never as a second gate.
                bool ok = measured <= program.bound * BoundTolerance;
                allPass &= ok;
                sb.AppendFormat("  {0,-44} GATE h={1:E1} {2:F4}   loc h={3:E1} {4:F4} (res {5:F4})   declared {6:F2}  {7}",
                                label, extent * GradientStepFraction, measured,
                                extent * RefineStepFraction, fine, fineFloor, program.bound, ok ? "ok" : "OVER");
                if (!ok) sb.AppendFormat("  worst at ({0:F4}, {1:F4})", at.x, at.y);
                sb.Append('\n');
            }

            sb.AppendLine("  -- primitives --");
            foreach (var c in Cases()) Check(c.name, ShaperNode.Primitive(c.def, c.name), c.extent);

            sb.AppendLine("  -- transforms (bound must pass through UNCHANGED) --");
            foreach (var t in TransformCases())
            {
                var node = ShaperNode.Primitive(new ShaperPrimitiveDef
                {
                    kind = ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 25f, rectCornerRadius = 8f
                }, "rect");
                node.transform = t.block;
                Check("transform: " + t.name, node, 220f);
            }

            sb.AppendLine("  -- combines --");
            foreach (var k in CombineCases()) Check("combine: " + k.name, k.node, 160f);

            sb.AppendLine("  -- operators --");
            {
                var sweep = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, "ngon6");
                sweep.sweep = new ShaperSweep { enabled = true, startDegrees = 20f, extentDegrees = 140f };
                Check("sweep radial 140deg", sweep, 140f);

                var sweepWide = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, "ngon6");
                sweepWide.sweep = new ShaperSweep { enabled = true, startDegrees = 20f, extentDegrees = 280f };
                Check("sweep radial 280deg (union)", sweepWide, 140f);

                var sweepLong = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Capsule, capsuleHalfLength = 40f, capsuleRadius = 15f }, "capsule");
                sweepLong.sweep = new ShaperSweep { enabled = true, startFraction = 0.25f, extentFraction = 0.5f };
                Check("sweep longitudinal 0.25..0.75", sweepLong, 140f);

                foreach (ShaperShellAlignment al in Enum.GetValues(typeof(ShaperShellAlignment)))
                {
                    var shell = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 45f, ellipseRy = 45f }, "disc");
                    shell.shell = new ShaperShell { enabled = true, thickness = 10f, alignment = al };
                    Check("shell " + al, shell, 140f);
                }
            }

            // Every §4.1 composition rule used to be exercised at depth 1 only: no bag inside a bag, no
            // combine with more than two members, no Star or NGon under a transform, no combine under a
            // transform, no operator over a bag. A bound rule that only ever composes a primitive with a
            // primitive is not the rule the spec states.
            sb.AppendLine("  -- compositions (depth 2-3) --");
            foreach (var comp in CompositionCases()) Check(comp.name, comp.node, comp.extent);

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// Composed trees at depth 2–3: bags inside bags, combines with three and four members, primitives
        /// with real geometry (Star, NGon) under real transforms, a combine under a transform, and both
        /// operators over a bag rather than over a leaf.
        /// </summary>
        static (string name, ShaperNode node, float extent)[] CompositionCases()
        {
            ShaperNode Leaf(ShaperPrimitiveKind kind, float a, float b, ShaperCombineMode mode = ShaperCombineMode.Add)
            {
                var def = new ShaperPrimitiveDef { kind = kind };
                switch (kind)
                {
                    case ShaperPrimitiveKind.Rect: def.rectHalfW = a; def.rectHalfH = b; break;
                    case ShaperPrimitiveKind.Ellipse: def.ellipseRx = a; def.ellipseRy = b; break;
                    case ShaperPrimitiveKind.Diamond: def.diamondRx = a; def.diamondRy = b; break;
                    case ShaperPrimitiveKind.Capsule: def.capsuleHalfLength = a; def.capsuleRadius = b; break;
                    case ShaperPrimitiveKind.NGon: def.ngonSides = Mathf.RoundToInt(a); def.ngonRadius = b; break;
                }
                return ShaperNode.Primitive(def, kind.ToString(), mode);
            }

            ShaperNode At(ShaperNode n, ShaperTransformBlock t) { n.transform = t; return n; }
            ShaperNode Blended(ShaperNode n, float width, float sharpness, float strength)
            {
                n.blend = new ShaperBlend { width = width, sharpness = sharpness, carveStrength = strength };
                return n;
            }

            // depth 2: a bag of two leaves, itself a member of an outer bag alongside a third leaf.
            ShaperNode inner = ShaperNode.Bag("inner", ShaperCombineMode.Add,
                At(Leaf(ShaperPrimitiveKind.Rect, 40f, 18f), new ShaperTransformBlock { translate = new Vector2(-20f, 0f) }),
                Blended(At(Leaf(ShaperPrimitiveKind.Ellipse, 24f, 24f), new ShaperTransformBlock { translate = new Vector2(22f, 8f) }), 12f, 0.5f, 1f));
            ShaperNode depth2 = ShaperNode.Bag("outer", ShaperCombineMode.Add,
                inner,
                Blended(At(Leaf(ShaperPrimitiveKind.Diamond, 30f, 46f, ShaperCombineMode.Subtract),
                           new ShaperTransformBlock { translate = new Vector2(10f, -26f), rotation = 18f }), 0f, 0.5f, 0.45f));

            // depth 3: a bag containing a bag containing a bag, each level adding a transform.
            ShaperNode lvl3 = ShaperNode.Bag("lvl3", ShaperCombineMode.Add,
                Leaf(ShaperPrimitiveKind.NGon, 5f, 26f),
                Blended(At(Leaf(ShaperPrimitiveKind.Capsule, 22f, 9f), new ShaperTransformBlock { rotation = 40f }), 8f, 0.25f, 1f));
            lvl3.transform = new ShaperTransformBlock { translate = new Vector2(-18f, 12f), scale = new Vector2(1.3f, 0.7f) };
            ShaperNode lvl2 = ShaperNode.Bag("lvl2", ShaperCombineMode.Add,
                lvl3,
                Blended(At(StarNode(7, 0.55f, 0.7f, 25f, ShaperCombineMode.Add),
                           new ShaperTransformBlock { translate = new Vector2(24f, -6f), rotation = 13f, scale = new Vector2(0.8f, 0.8f) }), 10f, 0.75f, 1f));
            lvl2.transform = new ShaperTransformBlock { rotation = -22f, skewDegrees = new Vector2(9f, -5f) };
            ShaperNode depth3 = ShaperNode.Bag("lvl1", ShaperCombineMode.Add,
                lvl2,
                Blended(At(Leaf(ShaperPrimitiveKind.Ellipse, 18f, 34f, ShaperCombineMode.Intersect),
                           new ShaperTransformBlock { translate = new Vector2(0f, 4f), scale = new Vector2(2.4f, 1.6f) }), 14f, 0.5f, 1f));

            // A combine with FOUR members, which the two-operand cases never reach.
            ShaperNode four = ShaperNode.Bag("four", ShaperCombineMode.Add,
                Leaf(ShaperPrimitiveKind.Rect, 46f, 20f),
                Blended(At(Leaf(ShaperPrimitiveKind.Ellipse, 22f, 22f), new ShaperTransformBlock { translate = new Vector2(-38f, 14f) }), 16f, 0.4f, 1f),
                Blended(At(Leaf(ShaperPrimitiveKind.NGon, 6f, 24f), new ShaperTransformBlock { translate = new Vector2(36f, -12f), rotation = 21f }), 9f, 0.6f, 1f),
                Blended(At(Leaf(ShaperPrimitiveKind.Diamond, 16f, 16f, ShaperCombineMode.Subtract), new ShaperTransformBlock { translate = new Vector2(0f, 18f) }), 0f, 0.5f, 0.7f));

            // A combine UNDER a transform: the bag itself is scaled and rotated, so sigma_min must rescale the
            // combined field, not just a leaf's.
            ShaperNode combineUnderT = ShaperNode.Bag("combine-under-transform", ShaperCombineMode.Add,
                Leaf(ShaperPrimitiveKind.Rect, 40f, 22f),
                Blended(At(Leaf(ShaperPrimitiveKind.Ellipse, 26f, 26f), new ShaperTransformBlock { translate = new Vector2(34f, 10f) }), 18f, 0.5f, 1f));
            combineUnderT.transform = new ShaperTransformBlock { scale = new Vector2(0.35f, 1.9f), rotation = 33f, translate = new Vector2(8f, -4f) };

            // A Star and an NGon under real transforms, on their own — the two primitives with actual corner
            // structure, which the transform block never previously carried.
            ShaperNode starUnderT = At(StarNode(9, 0.72f, 0.45f, -35f), new ShaperTransformBlock
            { rotation = 27f, scale = new Vector2(0.45f, 1.35f), skewDegrees = new Vector2(14f, 0f), origin = new Vector2(12f, -8f) });
            ShaperNode ngonUnderT = At(Leaf(ShaperPrimitiveKind.NGon, 11f, 42f), new ShaperTransformBlock
            { rotation = -19f, scale = new Vector2(1.8f, 0.4f), translate = new Vector2(-6f, 9f) });

            // Both operators OVER A BAG rather than over a leaf.
            ShaperNode sweptBag = ShaperNode.Bag("swept-bag", ShaperCombineMode.Add,
                Leaf(ShaperPrimitiveKind.NGon, 6f, 34f),
                Blended(At(Leaf(ShaperPrimitiveKind.Ellipse, 20f, 20f), new ShaperTransformBlock { translate = new Vector2(26f, 0f) }), 10f, 0.5f, 1f));
            sweptBag.sweep = new ShaperSweep { enabled = true, startDegrees = 15f, extentDegrees = 205f };

            ShaperNode shelledBag = ShaperNode.Bag("shelled-bag", ShaperCombineMode.Add,
                StarNode(5, 0.62f, 1f, 0f),
                Blended(At(Leaf(ShaperPrimitiveKind.Rect, 26f, 10f, ShaperCombineMode.Subtract), new ShaperTransformBlock { translate = new Vector2(6f, 4f) }), 0f, 0.5f, 0.8f));
            shelledBag.shell = new ShaperShell { enabled = true, thickness = 7f, alignment = ShaperShellAlignment.Centred };

            // An operator over a bag that is ITSELF under a transform — three levels of composition.
            ShaperNode sweptShelledUnderT = ShaperNode.Bag("swept+shelled under transform", ShaperCombineMode.Add,
                Leaf(ShaperPrimitiveKind.Capsule, 34f, 13f),
                Blended(At(Leaf(ShaperPrimitiveKind.Diamond, 22f, 30f), new ShaperTransformBlock { translate = new Vector2(-20f, 16f) }), 11f, 0.5f, 1f));
            sweptShelledUnderT.sweep = new ShaperSweep { enabled = true, startFraction = 0.1f, extentFraction = 0.75f };
            sweptShelledUnderT.shell = new ShaperShell { enabled = true, thickness = 5f, alignment = ShaperShellAlignment.Inward };
            sweptShelledUnderT.transform = new ShaperTransformBlock { rotation = 41f, scale = new Vector2(1.6f, 0.55f) };

            return new[]
            {
                ("depth2: bag(bag(rect+ellipse), diamond-sub)", depth2, 160f),
                ("depth3: bag(bag(bag(...)))+intersect", depth3, 180f),
                ("combine with FOUR members", four, 170f),
                ("combine UNDER transform (scale .35/1.9, rot)", combineUnderT, 200f),
                ("Star under transform (scale+skew+origin)", starUnderT, 180f),
                ("NGon 11 under transform (scale 1.8/0.4)", ngonUnderT, 180f),
                ("sweep radial 205deg OVER a bag", sweptBag, 160f),
                ("shell OVER a bag (star minus rect)", shelledBag, 150f),
                ("sweep+shell over a bag, under a transform", sweptShelledUnderT, 180f),
            };
        }

        static ShaperNode StarNode(int arms, float length, float baseWidth, float skew,
                                   ShaperCombineMode mode = ShaperCombineMode.Add)
            => ShaperNode.Primitive(StarDef(arms, length, baseWidth, skew), "star", mode);

        /// <summary>
        /// V5 — the cross-check on V4's method. For Rect, NGon and Star, measure the direct ratio
        /// <c>reported / trueEuclidean</c> against a boundary point cloud built by radial bisection, over tens
        /// of thousands of directions, inside and outside. This is the same instrument that produced the
        /// numbers in the task brief; it exists to confirm the gradient method is not itself missing something.
        /// </summary>
        public static string V5_RatioCrossCheck()
        {
            var sb = new StringBuilder("V5 ratio cross-check, reported/trueEuclidean against a boundary polyline\n");
            sb.AppendLine("  Every ratio is followed by the CLOUD's own worst-case relative error at the skip");
            sb.AppendLine("  radius, so the number can be read honestly: a ratio is only meaningful beyond it.");
            bool allPass = true;

            var subjects = new List<(string name, ShaperPrimitiveDef def, float extent)>
            {
                ("Rect 120x80 r0", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, 110f),
                ("Rect 120x80 r15", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f, rectCornerRadius = 15f }, 110f),
                ("NGon 6 r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, 100f),
                ("NGon 8 r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 8, ngonRadius = 50f }, 100f),
                ("NGon 3 r50 corner15", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 3, ngonRadius = 50f, ngonCornerRadius = 15f }, 100f),
                ("Star 5 len.62 bw1", StarDef(5, 0.62f, 1f, 0f), 100f),
                ("Star 5 len.80 bw.5 skew20", StarDef(5, 0.80f, 0.5f, 20f), 100f),
                ("Star 7 len.40 bw.8 skew-30", StarDef(7, 0.40f, 0.8f, -30f), 100f),
                // The Ellipse was NOT in this list — the one primitive the spec flags as needing a MEASURED
                // bound, and the one that turned out to be broken. Every aspect ratio the seam bug showed up
                // on is now a permanent subject.
                ("Ellipse circle r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 50f, ellipseRy = 50f }, 100f),
                ("Ellipse 60x25", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 60f, ellipseRy = 25f }, 100f),
                ("Ellipse 100x2", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 100f, ellipseRy = 2f }, 150f),
                ("Ellipse 80x10", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 80f, ellipseRy = 10f }, 130f),
                ("Ellipse 40x20", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 40f, ellipseRy = 20f }, 90f),
                ("Ellipse 20x70", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 20f, ellipseRy = 70f }, 120f),
                ("Ellipse 50x51 (near-circle)", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 50f, ellipseRy = 51f }, 100f),
            };

            foreach (var s in subjects)
            {
                var program = ShaperCompiler.Compile(ShaperNode.Primitive(s.def, s.name));
                var runner = new Runner(program);
                BuildBoundaryCloud(runner, s.extent * 2f, 20000, out float[] cloudX, out float[] cloudY);
                float chord = CloudMaxChord(cloudX, cloudY);
                float floorDist = s.extent * 0.03f;
                float instrumentError = CloudRelativeError(chord, floorDist);

                MeasureRatios(runner, cloudX, cloudY, s.extent, 121,
                              out float worstIn, out float worstOut, out int used, out int skipped);
                float worst = Mathf.Max(worstIn, worstOut);
                bool ok = worst <= program.bound * BoundTolerance + instrumentError;
                allPass &= ok;
                // A ratio of exactly 0 means NO sample qualified on that side — a flat ellipse has no
                // interior point further than the skip radius from its own boundary. Say so rather than
                // printing a 0.0000 that reads like a measurement.
                string inStr = worstIn > 0f ? worstIn.ToString("F4") : "  n/a ";
                string outStr = worstOut > 0f ? worstOut.ToString("F4") : "  n/a ";
                sb.AppendFormat("  {0,-28} in {1} out {2}  declared {3:F2}  {4}   [{5} pts used, {6} skipped closer than {7:F2}; cloud err <= {8:E2}]\n",
                                s.name, inStr, outStr, program.bound, ok ? "ok" : "OVER",
                                used, skipped, floorDist, instrumentError);
            }

            // The ellipse's EVOLUTE region specifically: points on and just off the major axis inside the
            // evolute cusp, where the nearest boundary point is NOT the axis vertex but one of a symmetric
            // off-axis pair. This is the locus a naive solver reports the vertex distance for, and it is
            // where the old cubic solve's branch seam lived.
            sb.AppendLine("  -- ellipse evolute region (major axis, inside the cusp, and just off it) --");
            foreach (var e in new[] { (60f, 25f), (100f, 2f), (80f, 10f), (40f, 20f), (20f, 70f) })
            {
                float rx = e.Item1, ry = e.Item2;
                var program = ShaperCompiler.Compile(ShaperNode.Primitive(
                    new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = rx, ellipseRy = ry }, "e"));
                var runner = new Runner(program);

                // Major axis is the larger semi-axis; the evolute cusp sits at |a| = (e0²−e1²)/e0 along it.
                bool xMajor = rx >= ry;
                float e0 = xMajor ? rx : ry, e1 = xMajor ? ry : rx;
                float cusp = (e0 * e0 - e1 * e1) / e0;

                var px = new List<float>();
                var py = new List<float>();
                for (int i = 0; i <= 200; i++)
                {
                    float a = cusp * (i / 200f);                       // along the major axis, inside the cusp
                    foreach (float off in new[] { 0f, 1e-6f, 1e-4f, 1e-2f, 0.1f, 0.5f })
                    {
                        px.Add(xMajor ? a : off); py.Add(xMajor ? off : a);
                        px.Add(xMajor ? -a : off); py.Add(xMajor ? off : -a);
                    }
                }
                // The true distance here comes from EllipseOracle, NOT from a boundary point cloud. A cloud
                // built by uniform ANGLE is wildly non-uniform along a flat ellipse's boundary — near the
                // ends of a 100x2 the boundary point sweeps ~100 units per tiny angle step — so its longest
                // chord cuts a real corner and the ratio reads 1.00557 purely as instrument error. The oracle
                // refines to below float32 resolution and has no discretisation to declare.
                //
                // Scaled off the MINOR semi-axis: a 100x2 ellipse has no point anywhere further than 2 units
                // from its own boundary, so a floor scaled off the major axis would qualify nothing and
                // report a ratio of zero as if it were a measurement.
                float minTrue = Mathf.Min(rx, ry) * 0.05f;
                float worst = 0f; int used = 0;
                for (int i = 0; i < px.Count; i++)
                {
                    float reported = Mathf.Abs(runner.D(px[i], py[i]));
                    float trueD = EllipseOracle(rx, ry, px[i], py[i]);
                    if (trueD < minTrue) continue;
                    used++;
                    float ratio = reported / trueD;
                    if (ratio > worst) worst = ratio;
                }
                bool ok = worst <= program.bound * BoundTolerance;
                allPass &= ok;
                sb.AppendFormat("  evolute {0,3}x{1,-3} cusp at {2,7:F3}  worst ratio {3}  {4}   [{5}/{6} probes beyond {7:F3}; exact oracle, no cloud]\n",
                                rx, ry, cusp, worst > 0f ? worst.ToString("F6") : "   n/a  ",
                                ok ? "ok" : "OVER", used, px.Count, minTrue);
            }

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V6 — the anisotropy case the reference app fails. A child under <c>scale.x = 0.2</c> must not
        /// over-report. The reference app measures 5.00× here because it returns the child's raw distance
        /// across a transform; with the <c>σ_min</c> rescale the measured ratio must come back to the child's
        /// own bound.
        /// </summary>
        public static string V6_Anisotropy()
        {
            var sb = new StringBuilder("V6 anisotropy — child under scale.x = 0.2\n");
            bool allPass = true;

            foreach (float sx in new[] { 0.2f, 0.1f, 5f })
            {
                var node = ShaperNode.Primitive(new ShaperPrimitiveDef
                {
                    kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 40f, ellipseRy = 40f
                }, "disc");
                node.transform = new ShaperTransformBlock { scale = new Vector2(sx, 1f) };

                var program = ShaperCompiler.Compile(node);
                float measured = MaxGradientDetailed(program, 200f, out float fine, out float fineFloor, out _);
                bool ok = measured <= program.bound * BoundTolerance;   // broad pass alone; see Check() in V4
                allPass &= ok;
                sb.AppendFormat("  scale.x={0,-5} GATE sup|grad| {1:F4}  (localiser {2:F4}, res {3:F4})  declared {4:F2}  {5}\n",
                                sx, measured, fine, fineFloor, program.bound, ok ? "ok" : "OVER");

                // The direct ratio, too: with the rescale the disc under a non-uniform scale is an ellipse and
                // its reported distance is the ellipse's own, not the unscaled disc's.
                var runner = new Runner(program);
                BuildBoundaryCloud(runner, 400f, 20000, out float[] cx, out float[] cy);
                MeasureRatios(runner, cx, cy, 120f, out float wi, out float wo);
                sb.AppendFormat("           ratio inside {0:F4}  outside {1:F4}\n", wi, wo);
            }

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V7 — the landmark trap, kept as a permanent executable record of why a bound is never read off a
        /// landmark point. The star's over-report at its lobe tip is exactly 1.0000×, and that is precisely
        /// where a star is NOT at its worst.
        ///
        /// Shaper's own star is an exact SDF, so for it both numbers are 1 — which is the point of having
        /// rebuilt it. The reference app's star is therefore measured alongside it, as the witness: the same
        /// landmark reads 1.0000× while the field maximum is far higher.
        /// </summary>
        public static string V7_LandmarkTrap()
        {
            var sb = new StringBuilder("V7 landmark trap — lobe tip vs field maximum\n");

            var def = StarDef(5, 0.62f, 1f, 0f);
            var program = ShaperCompiler.Compile(ShaperNode.Primitive(def, "star"));
            var runner = new Runner(program);
            BuildBoundaryCloud(runner, 200f, 20000, out float[] cx, out float[] cy);

            // The lobe tip: tip 0 points UP (+Y), Pyre's convention, ported unchanged because both Pyre and
            // Shaper are +Y up. A reference-app angle would need a sign flip here.
            float R = def.starRadius;
            float tipRatio = RatioAt(runner, cx, cy, 0f, R + 6f);
            MeasureRatios(runner, cx, cy, 100f, out float worstIn, out float worstOut);
            sb.AppendFormat("  Shaper star : tip {0:F4}   field max inside {1:F4} outside {2:F4}   declared {3:F2}\n",
                            tipRatio, worstIn, worstOut, program.bound);

            // The witness: the reference app's fixed five-lobe cosine flower, measured on the same instrument.
            float refTip = RefFlowerRatioAt(0f, 56f, 50f, 50f);
            float refWorst = RefFlowerWorst(50f, 50f, 100f);
            sb.AppendFormat("  reference flower (witness) : tip {0:F4}   field max {1:F4}\n", refTip, refWorst);
            sb.AppendLine("  Reading: the landmark is 1.0000x on BOTH, and only one of them is actually bounded at 1.");

            bool pass = tipRatio <= 1.02f && worstIn <= program.bound * 1.05f && worstOut <= program.bound * 1.05f;
            sb.AppendFormat("  RESULT: {0}\n", pass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>V8 — a tile requested alone equals the same region of the whole grid, bitwise.</summary>
        public static string V8_TileIndependence()
        {
            var sb = new StringBuilder("V8 tile independence (bitwise, prime-sized decomposition)\n");

            var node = ShaperNode.Bag("bag", ShaperCombineMode.Add,
                ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Star, starArms = 5, starRadius = 40f }, "star"),
                Member(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 30f, rectHalfH = 12f },
                       ShaperCombineMode.Subtract, new Vector2(12f, 6f), 0.5f));

            var program = ShaperCompiler.Compile(node);
            var grid = ShaperSampleGrid.Centred(97, 89, 1.5f);
            var stack = program.NewStack();

            var full = new float[97 * 89];
            var fullCov = new float[97 * 89];
            ShaperEvaluator.Fill(program, grid, 97, 89, full, fullCov, stack);

            var tiled = new float[97 * 89];
            var tiledCov = new float[97 * 89];
            const int tw = 7, thh = 11;   // prime tile sizes
            for (int ty = 0; ty < 89; ty += thh)
                for (int tx = 0; tx < 97; tx += tw)
                {
                    int w = Mathf.Min(tw, 97 - tx), h = Mathf.Min(thh, 89 - ty);
                    ShaperEvaluator.FillTile(program, grid, tx, ty, w, h, tiled, tiledCov, ty * 97 + tx, 97, stack);
                }

            int mismatch = 0;
            for (int i = 0; i < full.Length; i++)
                if (!SameBits(full[i], tiled[i]) || !SameBits(fullCov[i], tiledCov[i])) mismatch++;

            sb.AppendFormat("  mismatched {0}/{1} samples (distance + coverage)\n", mismatch, full.Length);
            sb.AppendFormat("  RESULT: {0}\n", mismatch == 0 ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V9 — shelled inside-ness. Shelling changes what "inside" means downstream: a shelled disc is an
        /// annulus, and everything that later reads inside-ness sees the new interior. That falls out of the
        /// maths rather than being chosen, so it is tested deliberately rather than discovered.
        /// </summary>
        public static string V9_ShelledInsideness()
        {
            var sb = new StringBuilder("V9 shelled inside-ness (a shelled disc is an annulus)\n");

            var node = ShaperNode.Primitive(new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 50f, ellipseRy = 50f
            }, "disc");
            node.shell = new ShaperShell { enabled = true, thickness = 12f, alignment = ShaperShellAlignment.Centred };

            var runner = new Runner(node);
            float centre = runner.D(0f, 0f);
            float wall = runner.D(50f, 0f);
            float justInsideRim = runner.D(45f, 0f);
            float outside = runner.D(80f, 0f);
            float coverCentre = ShaperField.Coverage(centre, 0.5f);
            float coverWall = ShaperField.Coverage(wall, 0.5f);

            sb.AppendFormat("  centre  d = {0,9:F4}  coverage {1:F3}  (must be OUTSIDE the annulus)\n", centre, coverCentre);
            sb.AppendFormat("  wall    d = {0,9:F4}  coverage {1:F3}  (must be INSIDE)\n", wall, coverWall);
            sb.AppendFormat("  inner   d = {0,9:F4}\n", justInsideRim);
            sb.AppendFormat("  outside d = {0,9:F4}\n", outside);

            bool pass = centre > 0f && wall < 0f && justInsideRim < 0f && outside > 0f;
            sb.AppendFormat("  RESULT: {0}\n", pass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V10 — the degenerate-input battery, as a permanent section rather than a one-off probe.
        ///
        /// Nothing in V1–V9 tested a degenerate input at all, and two of the six defects an independent
        /// verification found were found exactly this way: a zero-thickness shell that returned the unshelled
        /// SOLID, and a star whose valley-radius floor could exceed its own tip radius. Every case here
        /// asserts no NaN, no infinity and no exception; the cases that have a known-correct answer assert
        /// that answer too, so the fix cannot silently regress.
        /// </summary>
        public static string V10_DegenerateInputs()
        {
            var sb = new StringBuilder("V10 degenerate inputs (no NaN / no Inf / no throw, plus correctness where known)\n");
            bool allPass = true;
            const float scanExtent = 120f;
            const int scanRes = 81;

            // Scan a node over a grid; returns false if anything is NaN, infinite, or throws.
            bool Finite(string label, ShaperNode node, out float minD, out float maxD, out int insideCount)
            {
                minD = float.MaxValue; maxD = -float.MaxValue; insideCount = 0;
                try
                {
                    var runner = new Runner(node);
                    for (int j = 0; j < scanRes; j++)
                    {
                        float y = Mathf.Lerp(-scanExtent, scanExtent, j / (float)(scanRes - 1));
                        for (int i = 0; i < scanRes; i++)
                        {
                            float x = Mathf.Lerp(-scanExtent, scanExtent, i / (float)(scanRes - 1));
                            float d = runner.D(x, y);
                            if (float.IsNaN(d) || float.IsInfinity(d)) { sb.AppendFormat("  {0,-44} NaN/Inf at ({1:F2},{2:F2})  FAIL\n", label, x, y); return false; }
                            if (d < minD) minD = d;
                            if (d > maxD) maxD = d;
                            if (d < 0f) insideCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendFormat("  {0,-44} THREW {1}  FAIL\n", label, ex.GetType().Name);
                    return false;
                }
                return true;
            }

            void Case(string label, ShaperNode node, string note = null)
            {
                bool ok = Finite(label, node, out float mn, out float mx, out int inside);
                allPass &= ok;
                if (ok)
                    sb.AppendFormat("  {0,-44} min {1,10:F3}  max {2,10:F3}  inside {3,5}  ok{4}\n",
                                    label, mn, mx, inside, note == null ? "" : "   " + note);
            }

            ShaperNode P(ShaperPrimitiveDef d, string n) => ShaperNode.Primitive(d, n);

            sb.AppendLine("  -- zero and negative sizes --");
            Case("Rect 0x0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 0f, rectHalfH = 0f }, "r"));
            Case("Rect -30x-10", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = -30f, rectHalfH = -10f }, "r"));
            Case("Rect 60x40 corner 999", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f, rectCornerRadius = 999f }, "r"));
            Case("Ellipse 0x0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 0f, ellipseRy = 0f }, "e"));
            Case("Ellipse -40x30", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = -40f, ellipseRy = 30f }, "e"));
            Case("Diamond 0x0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Diamond, diamondRx = 0f, diamondRy = 0f }, "d"));
            Case("Triangle 0x0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Triangle, triangleBase = 0f, triangleHeight = 0f }, "t"));
            Case("Triangle -90x-70", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Triangle, triangleBase = -90f, triangleHeight = -70f }, "t"));
            Case("Capsule 0+0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Capsule, capsuleHalfLength = 0f, capsuleRadius = 0f }, "c"));

            sb.AppendLine("  -- counts below their legal minimum --");
            Case("NGon sides 0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 0, ngonRadius = 50f }, "n"), "clamped to 3");
            Case("NGon sides -5", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = -5, ngonRadius = 50f }, "n"), "clamped to 3");
            Case("NGon sides 1000", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 1000, ngonRadius = 50f }, "n"), "clamped to 64");
            Case("NGon radius 0", P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 0f }, "n"));
            Case("Star arms 0", P(StarDef(0, 0.62f, 1f, 0f), "s"), "clamped to 2");
            Case("Star arms 1", P(StarDef(1, 0.62f, 1f, 0f), "s"), "clamped to 2");
            Case("Star arms 200", P(StarDef(200, 0.62f, 1f, 0f), "s"), "clamped to 20");

            // The star's valley floor. Before the fix, rIn = max(0.5, R(1-len)) put the valleys OUTSIDE the
            // tips at small R, turning the polygon inside-out. Assert the geometry directly, not just the
            // absence of a NaN, because the old form produced no NaN either.
            // rIn must never EXCEED R (that is the inside-out failure), and must be strictly inside it
            // whenever the arms have any length at all. At length 0 the valleys sit exactly on the tip
            // circle by definition — a star with no arm reach is a regular polygon — so equality there is
            // the correct answer, not a violation.
            sb.AppendLine("  -- star valley floor vs tip radius (rIn <= R always; rIn < R whenever length > 0) --");
            bool valleysOk = true;
            foreach (float R in new[] { 0.05f, 0.4f, 1f, 10f, 50f, 500f })
                foreach (float len in new[] { 0f, 0.5f, 0.99f, 1f })
                {
                    var def = StarDef(5, len, 1f, 0f);
                    def.starRadius = R;
                    var baked = ShaperPrimitives.Bake(def, 0f, 0u);
                    float tip = baked.p0, valley = baked.p1;
                    bool ok = valley > 0f && (len > 0f ? valley < tip : valley <= tip);
                    valleysOk &= ok;
                    if (!ok) sb.AppendFormat("    R={0,-6} len={1,-5} rIn={2:F6} R={3:F6}  INSIDE-OUT\n", R, len, valley, tip);
                }
            {
                // Proportions must not change with size: rIn/R at a given length is the same at R=0.4 and R=100.
                var a = StarDef(5, 1f, 1f, 0f); a.starRadius = 0.4f;
                var b = StarDef(5, 1f, 1f, 0f); b.starRadius = 100f;
                var ba = ShaperPrimitives.Bake(a, 0f, 0u);
                var bb = ShaperPrimitives.Bake(b, 0f, 0u);
                float ra = ba.p1 / ba.p0, rb = bb.p1 / bb.p0;
                bool scaleFree = Mathf.Abs(ra - rb) < 1e-6f;
                valleysOk &= scaleFree;
                sb.AppendFormat("    scale-free at length 1: rIn/R = {0:F6} at R=0.4 and {1:F6} at R=100  {2}\n",
                                ra, rb, scaleFree ? "ok" : "SIZE-DEPENDENT");
            }
            allPass &= valleysOk;
            sb.AppendFormat("    all rIn < R across R x length : {0}\n", valleysOk ? "ok" : "FAIL");

            // Zero-thickness shell. The correct value is |d| - 0 = |d| >= 0, i.e. an EMPTY interior: a wall
            // thinned to nothing. What the code used to return was the unshelled SOLID, bit-identical to no
            // shell at all — so dragging the thickness slider to zero made the shape reappear rather than the
            // wall vanish. Both halves are asserted: equal to |d|, and NOT equal to the solid.
            sb.AppendLine("  -- zero-thickness shell must EMPTY the interior, not restore the solid --");
            bool shellOk = true;
            foreach (ShaperShellAlignment al in Enum.GetValues(typeof(ShaperShellAlignment)))
            {
                var plainDef = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f };
                var plain = new Runner(P(Clone(plainDef), "solid"));
                var zeroNode = P(Clone(plainDef), "shelled");
                zeroNode.shell = new ShaperShell { enabled = true, thickness = 0f, alignment = al };
                var zero = new Runner(zeroNode);

                int wrongValue = 0, negative = 0, sameAsSolid = 0, total = 0;
                for (int j = 0; j < scanRes; j++)
                {
                    float y = Mathf.Lerp(-scanExtent, scanExtent, j / (float)(scanRes - 1));
                    for (int i = 0; i < scanRes; i++)
                    {
                        float x = Mathf.Lerp(-scanExtent, scanExtent, i / (float)(scanRes - 1));
                        float solid = plain.D(x, y);
                        float shelled = zero.D(x, y);
                        total++;
                        // Numeric equality, not bitwise: Inward is max(d, -d-0) and at d == 0 exactly that is
                        // Mathf.Max(0f, -0f) == -0f, which is the same NUMBER as abs(0f) but not the same
                        // BITS. Bit-identity is contractual for the sweep/shell IDENTITIES (V1/V2); here what
                        // is being asserted is the value.
                        if (shelled != Mathf.Abs(solid)) wrongValue++;
                        if (shelled < 0f) negative++;
                        if (SameBits(shelled, solid) && solid < 0f) sameAsSolid++;
                    }
                }
                bool ok = wrongValue == 0 && negative == 0;
                shellOk &= ok;
                sb.AppendFormat("    thickness 0, {0,-8}: != |d| on {1}/{2}, interior points {3}, still-solid {4}  {5}\n",
                                al, wrongValue, total, negative, sameAsSolid, ok ? "ok" : "FAIL");
            }
            allPass &= shellOk;

            sb.AppendLine("  -- zero and negative sweep extents --");
            {
                var s0 = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, "n");
                s0.sweep = new ShaperSweep { enabled = true, startDegrees = 30f, extentDegrees = 0f };
                Case("sweep radial extent 0", s0, "nothing inside");
                var sN = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, "n");
                sN.sweep = new ShaperSweep { enabled = true, startDegrees = 30f, extentDegrees = -90f };
                Case("sweep radial extent -90", sN, "clamped to a zero wedge");
                var sL = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Capsule, capsuleHalfLength = 40f, capsuleRadius = 15f }, "c");
                sL.sweep = new ShaperSweep { enabled = true, startFraction = 0.5f, extentFraction = 0f };
                Case("sweep longitudinal extent 0", sL, "zero-width slab");
            }

            sb.AppendLine("  -- singular and extreme transforms --");
            {
                var z = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 25f }, "r");
                z.transform = new ShaperTransformBlock { scale = new Vector2(0f, 0f) };
                Case("transform scale (0,0)", z);
                var p0 = ShaperCompiler.Compile(z);
                bool flagged = p0.hasSingularTransform && p0.singularTransformCount == 1;
                allPass &= flagged;
                sb.AppendFormat("    flag hasSingularTransform={0} count={1} node='{2}'  {3}\n",
                                p0.hasSingularTransform, p0.singularTransformCount, p0.singularNode, flagged ? "ok" : "FAIL");

                var z1 = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 25f }, "r");
                z1.transform = new ShaperTransformBlock { scale = new Vector2(1f, 0f) };
                Case("transform scale (1,0)", z1);

                var k = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 25f }, "r");
                k.transform = new ShaperTransformBlock { skewDegrees = new Vector2(89f, 89f) };
                Case("transform skew (89,89)", k);
            }

            sb.AppendLine("  -- empty trees and leading non-Add members --");
            {
                Case("empty Bag", ShaperNode.Bag("empty", ShaperCombineMode.Add));
                bool nullOk;
                try
                {
                    var prog = ShaperCompiler.Compile(null);
                    var stack = prog.NewStack();
                    float d = ShaperEvaluator.Distance(prog, 3f, 5f, stack);
                    nullOk = !float.IsNaN(d) && ShaperField.IsEmpty(d);
                }
                catch { nullOk = false; }
                allPass &= nullOk;
                sb.AppendFormat("  {0,-44} {1}\n", "null root", nullOk ? "Empty everywhere, ok" : "FAIL");

                var leadSub = ShaperNode.Bag("bag", ShaperCombineMode.Add,
                    Member(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 40f, rectHalfH = 25f }, ShaperCombineMode.Subtract, Vector2.zero, 0.5f));
                Case("bag with a leading Subtract", leadSub);
                var lp = ShaperCompiler.Compile(leadSub);
                bool lok = lp.hasLeadingNonAdd && lp.leadingNonAddCount == 1;
                allPass &= lok;
                sb.AppendFormat("    flag hasLeadingNonAdd={0} count={1} node='{2}'  {3}\n",
                                lp.hasLeadingNonAdd, lp.leadingNonAddCount, lp.leadingNonAddNode, lok ? "ok" : "FAIL");

                // THREE offending bags. Recording only the first surfaced one and hid two.
                ShaperNode Offender(string name) => ShaperNode.Bag(name, ShaperCombineMode.Add,
                    Member(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 20f, rectHalfH = 12f }, ShaperCombineMode.Intersect, Vector2.zero, 1f));
                var three = ShaperNode.Bag("root", ShaperCombineMode.Add, Offender("bad1"), Offender("bad2"), Offender("bad3"));
                var tp = ShaperCompiler.Compile(three);
                bool tok = tp.hasLeadingNonAdd && tp.leadingNonAddCount == 3 && tp.leadingNonAddNode != null;
                allPass &= tok;
                sb.AppendFormat("    three offending bags -> count {0} (expect 3), first '{1}'  {2}\n",
                                tp.leadingNonAddCount, tp.leadingNonAddNode, tok ? "ok" : "FAIL");

                // THREE singular transforms, likewise.
                ShaperNode Sing(string name)
                {
                    var n = P(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 20f, rectHalfH = 12f }, name);
                    n.transform = new ShaperTransformBlock { scale = new Vector2(0f, 1f) };
                    return n;
                }
                var sp = ShaperCompiler.Compile(ShaperNode.Bag("root", ShaperCombineMode.Add, Sing("s1"), Sing("s2"), Sing("s3")));
                bool sok = sp.hasSingularTransform && sp.singularTransformCount == 3;
                allPass &= sok;
                sb.AppendFormat("    three singular transforms -> count {0} (expect 3), first '{1}'  {2}\n",
                                sp.singularTransformCount, sp.singularNode, sok ? "ok" : "FAIL");
            }

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// V11 — the ellipse, measured against an independent brute-force nearest-point oracle rather than
        /// against itself.
        ///
        /// This exists because the ellipse was the one primitive that was genuinely broken and the one the
        /// gradient instrument could not see: the old closed-form quartic solve branched on the sign of a
        /// cubic discriminant, and on the locus where that discriminant is near zero both branches lost all
        /// significance at once. The named failing point below is re-run every time, by number.
        /// </summary>
        public static string V11_EllipseAgainstOracle()
        {
            var sb = new StringBuilder("V11 ellipse vs brute-force nearest-point oracle\n");
            bool allPass = true;

            sb.AppendLine("  -- the reported failing point: rx=60 ry=25, y=-29.65 (was 47.198 vs true 6.514) --");
            foreach (float x in new[] { -23.27936f, -23.27736f, -23.27536f })
            {
                float got = Mathf.Abs(ShaperSdf.Ellipse(60f, 25f, x, -29.65f));
                float want = EllipseOracle(60f, 25f, x, -29.65f);
                float ratio = want > 1e-6f ? got / want : 1f;
                bool ok = ratio <= 1.0005f;
                allPass &= ok;
                sb.AppendFormat("    x={0,-12} reported {1,10:F5}   oracle {2,10:F5}   ratio {3:F6}  {4}\n",
                                x, got, want, ratio, ok ? "ok" : "OVER");
            }

            // The aspect-ratio families every seam this primitive has ever had was found on, plus the
            // near-circular band on both sides of the fast-path threshold.
            var ratios = new (float rx, float ry)[]
            {
                (100f, 2f), (80f, 10f), (60f, 25f), (40f, 20f), (20f, 70f),
                (50f, 50f), (50f, 51f), (50f, 55f), (2f, 100f), (1f, 40f), (200f, 3f), (30f, 29f),
            };

            // -- The scan that would actually catch a seam regression ------------------------------------
            //
            // A 2-D grid cannot do this job on its own, and the arithmetic says so: the old cubic solver's
            // bad locus was a 1-D CURVE, so a 121x121 grid with everything within 1% of the boundary skipped
            // had an expected hit count of about 0.05 per aspect ratio. It found the old bug only through the
            // three hard-coded regression points above, which guard one seam on one ellipse and nothing else.
            //
            // So: fine 1-D scans at 1e-3 of the size, along chords chosen to cross the places a seam has ever
            // lived - the major-axis evolute, the minor-axis cusp, and both axes themselves. Each scan
            // carries TWO instruments:
            //
            //   * a CONTINUITY check, |d(i) - d(i-1)| <= step (the field is 1-Lipschitz, so consecutive
            //     samples can never differ by more than the step). This needs no oracle, costs one SDF call
            //     per sample, and is what a seam violates loudest: the old bug jumped 40 units across a
            //     0.002-unit step. It is also the check that would have caught it WITHOUT knowing where to
            //     look, which is the whole point.
            //   * an oracle RATIO at every 10th sample, so the scan verifies the value and not just its
            //     smoothness.
            sb.AppendLine("  -- fine 1-D chord scans (step 1e-3 of size) across the evolute, the cusp and both axes --");
            sb.AppendLine("     continuity = max |d(i)-d(i-1)| / step  (must be <= 1: the field is 1-Lipschitz)");
            float worstContinuity = 0f; string continuityWhere = "";
            float scanWorstRatio = 0f; string scanRatioWhere = "";
            foreach (var e in ratios)
            {
                float rx = e.rx, ry = e.ry;
                float size = Mathf.Max(rx, ry);
                float step = size * 1e-3f;
                bool xMajor = rx >= ry;
                float e0 = xMajor ? rx : ry, e1 = xMajor ? ry : rx;
                float cuspMajor = (e0 * e0 - e1 * e1) / e0;    // evolute cusp ON the major axis
                float cuspMinor = (e0 * e0 - e1 * e1) / e1;    // evolute cusp ON the minor axis (outside the shape)
                float minTrue = Mathf.Min(rx, ry) * 0.02f;

                // Each chord is (origin, direction, halfLength).
                var chords = new List<(float ox, float oy, float dx, float dy, float half)>();
                float majX = xMajor ? 1f : 0f, majY = xMajor ? 0f : 1f;   // unit vector along the major axis
                float minX = xMajor ? 0f : 1f, minY = xMajor ? 1f : 0f;   // ...and along the minor axis

                // 1. Along the major axis itself, and along the minor axis itself.
                chords.Add((0f, 0f, majX, majY, size * 1.5f));
                chords.Add((0f, 0f, minX, minY, size * 1.5f));
                // 2. Perpendicular to the major axis, crossing it at the evolute cusp and at fractions of it
                //    - these are the chords the reported failing point lay on.
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f, 0.95f, 1f, 1.05f, 1.3f })
                {
                    float a = cuspMajor * f;
                    chords.Add((majX * a, majY * a, minX, minY, size * 1.2f));
                    chords.Add((-majX * a, -majY * a, minX, minY, size * 1.2f));
                }
                // 3. Perpendicular to the minor axis, crossing it at the minor-axis cusp and around it.
                foreach (float f in new[] { 0.5f, 0.9f, 1f, 1.1f })
                {
                    float b = cuspMinor * f;
                    chords.Add((minX * b, minY * b, majX, majY, size * 1.5f));
                }
                // 4. Two obliques, so nothing here is axis-aligned by construction.
                chords.Add((0f, 0f, 0.7071068f, 0.7071068f, size * 1.6f));
                chords.Add((majX * cuspMajor * 0.6f, majY * cuspMajor * 0.6f, 0.5f, 0.8660254f, size * 1.4f));

                float famContinuity = 0f, famRatio = 0f;
                long scanSamples = 0; int oracleSamples = 0;
                foreach (var c in chords)
                {
                    int n = Mathf.Clamp(Mathf.CeilToInt(2f * c.half / step), 2, 6000);
                    float prev = 0f; bool havePrev = false;
                    for (int i = 0; i < n; i++)
                    {
                        float t = -c.half + 2f * c.half * i / (n - 1);
                        float x = c.ox + c.dx * t, y = c.oy + c.dy * t;
                        float d = ShaperSdf.Ellipse(rx, ry, x, y);
                        scanSamples++;
                        if (havePrev)
                        {
                            // Rounding-corrected, same argument as the gradient instrument: subtracting the
                            // float slack keeps this a LOWER bound on the true jump.
                            float slack = RoundingUlps * FloatEps * Mathf.Max(Mathf.Abs(d), Mathf.Abs(prev));
                            float jump = Mathf.Max(0f, Mathf.Abs(d - prev) - slack) / step;
                            if (jump > famContinuity)
                            {
                                famContinuity = jump;
                                if (jump > worstContinuity)
                                {
                                    worstContinuity = jump;
                                    continuityWhere = string.Format("{0}x{1} at ({2:F4},{3:F4})", rx, ry, x, y);
                                }
                            }
                        }
                        prev = d; havePrev = true;

                        if (i % 10 == 0)
                        {
                            float want = EllipseOracle(rx, ry, x, y);
                            if (want < minTrue) continue;
                            oracleSamples++;
                            float ratio = Mathf.Abs(d) / want;
                            if (ratio > famRatio)
                            {
                                famRatio = ratio;
                                if (ratio > scanWorstRatio)
                                {
                                    scanWorstRatio = ratio;
                                    scanRatioWhere = string.Format("{0}x{1} at ({2:F4},{3:F4})", rx, ry, x, y);
                                }
                            }
                        }
                    }
                }
                bool ok = famContinuity <= 1.001f && famRatio <= 1.001f;
                allPass &= ok;
                sb.AppendFormat("    {0,5}x{1,-5} {2,2} chords, step {3:F4}  continuity {4:F6}  ratio {5:F6}  {6}   [{7} samples, {8} oracle]\n",
                                rx, ry, chords.Count, step, famContinuity, famRatio, ok ? "ok" : "OVER",
                                scanSamples, oracleSamples);
            }
            sb.AppendFormat("  WORST CONTINUITY over all scans: {0:F6}  ({1})\n", worstContinuity, continuityWhere);
            sb.AppendFormat("  WORST SCAN RATIO  over all scans: {0:F6}  ({1})\n", scanWorstRatio, scanRatioWhere);

            // -- The 2-D grid, kept as breadth but no longer carrying the job alone ----------------------
            sb.AppendLine("  -- 2-D sweep for breadth, |reported| / oracle --");
            float globalWorst = 0f; string globalWhere = "";
            foreach (var e in ratios)
            {
                float ext = Mathf.Max(e.rx, e.ry) * 1.5f;
                const int res = 81;
                float minTrue = Mathf.Max(e.rx, e.ry) * 0.01f;
                float worst = 0f; float wx = 0f, wy = 0f;
                int used = 0;
                for (int j = 0; j < res; j++)
                {
                    float y = Mathf.Lerp(-ext, ext, j / (float)(res - 1));
                    for (int i = 0; i < res; i++)
                    {
                        float x = Mathf.Lerp(-ext, ext, i / (float)(res - 1));
                        float want = EllipseOracle(e.rx, e.ry, x, y);
                        if (want < minTrue) continue;
                        used++;
                        float got = Mathf.Abs(ShaperSdf.Ellipse(e.rx, e.ry, x, y));
                        float ratio = got / want;
                        if (ratio > worst) { worst = ratio; wx = x; wy = y; }
                    }
                }
                bool ok = worst <= 1.001f;
                allPass &= ok;
                if (worst > globalWorst) { globalWorst = worst; globalWhere = string.Format("{0}x{1} at ({2:F3},{3:F3})", e.rx, e.ry, wx, wy); }
                sb.AppendFormat("    {0,5}x{1,-5} worst ratio {2:F6} at ({3,8:F3},{4,8:F3})  [{5} pts beyond {6:F2}]  {7}\n",
                                e.rx, e.ry, worst, wx, wy, used, minTrue, ok ? "ok" : "OVER");
            }
            sb.AppendFormat("  WORST OVER ALL ASPECT RATIOS: {0:F6}  ({1})\n", globalWorst, globalWhere);
            sb.AppendLine("  The oracle refines a 4096-angle coarse scan by 60 ternary steps, so its own");
            sb.AppendLine("  discretisation is below float32 resolution and the ratio can be read as exact.");

            // -- The circle fast path: an ABSOLUTE-error assertion, because the ratio is unbounded here ---
            //
            // Inside |rx - ry| < 1e-5*max(1, rx, ry) the primitive draws a circle of the MEAN radius (the
            // r0 == 1 degeneracy guard, documented on ShaperSdf.Ellipse). The absolute error is bounded by
            // |rx - ry| / 2; the RATIO is not bounded at all near the boundary, because it divides by a true
            // distance that goes to zero while that error does not. The list above stepped straight from
            // 50x50 (exactly circular, zero error) to 50x51 (outside the band), so the band itself was never
            // sampled. These cases sit INSIDE it and are asserted on absolute error, which is the quantity
            // that actually has a bound.
            sb.AppendLine("  -- circle fast path (in-band): ABSOLUTE error, since the ratio is unbounded here --");
            var band = new (float rx, float ry)[]
            {
                (50f, 50.00049f), (50f, 49.99951f), (1000f, 1000.0099f), (1000f, 999.9901f),
                (0.5f, 0.500004f), (7f, 7.00003f), (100000f, 100000.9f),
            };
            foreach (var e in band)
            {
                float size = Mathf.Max(e.rx, e.ry);
                float threshold = 1e-5f * Mathf.Max(1f, size);
                bool inBand = Mathf.Abs(e.rx - e.ry) < threshold;
                float allowed = 0.5f * Mathf.Abs(e.rx - e.ry) + size * 4f * FloatEps;
                float worstAbs = 0f, worstRatio = 0f;
                float ext = size * 1.5f;
                const int res = 141;
                for (int j = 0; j < res; j++)
                {
                    float y = Mathf.Lerp(-ext, ext, j / (float)(res - 1));
                    for (int i = 0; i < res; i++)
                    {
                        float x = Mathf.Lerp(-ext, ext, i / (float)(res - 1));
                        float got = Mathf.Abs(ShaperSdf.Ellipse(e.rx, e.ry, x, y));
                        float want = EllipseOracle(e.rx, e.ry, x, y);
                        float abs = Mathf.Abs(got - want);
                        if (abs > worstAbs) worstAbs = abs;
                        if (want > size * 1e-6f) worstRatio = Mathf.Max(worstRatio, got / want);
                    }
                }
                // ...and now walk in close enough for the ratio to misbehave, so this section carries its own
                // evidence rather than an assertion the reader has to take on trust. A grid this coarse never
                // lands within 5e-6 of the surface, which is exactly why its ratio column looks innocuous.
                // Marching down the minor axis does land there, and the ratio duly runs away while the
                // absolute error does not move at all.
                float nearRatio = 0f, nearAbs = 0f, nearTrue = 0f;
                float minor = Mathf.Min(e.rx, e.ry);
                bool yMinor = e.ry <= e.rx;
                for (int i = 0; i < 40; i++)
                {
                    float t = Mathf.Pow(10f, -2f - i * 0.15f);          // 1e-2 down to ~1e-8, relative
                    float px2 = yMinor ? 0f : minor * (1f + t);
                    float py2 = yMinor ? minor * (1f + t) : 0f;
                    float want = EllipseOracle(e.rx, e.ry, px2, py2);
                    if (want <= 0f) continue;
                    float got = Mathf.Abs(ShaperSdf.Ellipse(e.rx, e.ry, px2, py2));
                    float r = got / want;
                    if (r > nearRatio) { nearRatio = r; nearAbs = Mathf.Abs(got - want); nearTrue = want; }
                }

                bool ok = inBand && worstAbs <= allowed;
                allPass &= ok;
                sb.AppendFormat("    {0,8}x{1,-11} in-band {2,-5} worst |err| {3:E3} <= {4:E3}  {5}\n",
                                e.rx, e.ry, inBand, worstAbs, allowed, ok ? "ok" : "OVER");
                sb.AppendFormat("             near-boundary march: ratio peaks at {0,10:F2}x  where true d = {1:E3} and |err| = {2:E3}\n",
                                nearRatio, nearTrue, nearAbs);
            }
            sb.AppendLine("     Read the two columns together: the |err| column is flat and bounded by |rx-ry|/2,");
            sb.AppendLine("     which is 5e-6 of the shape's size - under one float32 ulp of any distance");
            sb.AppendLine("     comparable to the shape. The ratio column runs away only because it divides by a");
            sb.AppendLine("     true distance heading to zero. That is the metric failing, not the field: the");
            sb.AppendLine("     field is a circle's exact SDF and stays perfectly Lipschitz-1 throughout.");

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// The independent oracle: the true unsigned distance from a point to an ellipse boundary, found by a
        /// coarse scan over the parametric angle followed by a ternary search on the bracketing interval.
        /// Deliberately shares no code with <see cref="ShaperSdf.Ellipse"/>.
        /// </summary>
        static float EllipseOracle(float rx, float ry, float px, float py)
        {
            const int coarse = 4096;
            double best = double.MaxValue; int bestI = 0;
            for (int i = 0; i < coarse; i++)
            {
                double th = 2.0 * Math.PI * i / coarse;
                double dx = rx * Math.Cos(th) - px, dy = ry * Math.Sin(th) - py;
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; bestI = i; }
            }
            double step = 2.0 * Math.PI / coarse;
            double lo = (bestI - 1) * step, hi = (bestI + 1) * step;
            for (int k = 0; k < 60; k++)
            {
                double m1 = lo + (hi - lo) / 3.0, m2 = hi - (hi - lo) / 3.0;
                double a1x = rx * Math.Cos(m1) - px, a1y = ry * Math.Sin(m1) - py;
                double a2x = rx * Math.Cos(m2) - px, a2y = ry * Math.Sin(m2) - py;
                if (a1x * a1x + a1y * a1y < a2x * a2x + a2y * a2y) hi = m2; else lo = m1;
            }
            double th2 = 0.5 * (lo + hi);
            double fx = rx * Math.Cos(th2) - px, fy = ry * Math.Sin(th2) - py;
            double refined = Math.Sqrt(fx * fx + fy * fy);
            return (float)Math.Min(refined, Math.Sqrt(best));
        }

        /// <summary>
        /// V12 — the N-gon's outer extent under corner rounding. Spec §5.1 requires the authored
        /// <c>radius</c> to stay the CIRCUMRADIUS at every corner radius. The code used to preserve the
        /// apothem instead, so a triangle authored at radius 50 with corner 15 drew at circumradius 35.000
        /// and with corner 35 at 25.025 — half the authored size, silently.
        /// </summary>
        public static string V12_NGonOuterExtent()
        {
            var sb = new StringBuilder("V12 N-gon outer extent under rounding (authored radius must stay the circumradius)\n");
            const float authored = 50f;
            bool allPass = true;
            float worstErr = 0f; string worstWhere = "";

            sb.AppendLine("  sides |  corner 0    5    12.5   25    35    45    50 (= full round)");
            foreach (int sides in new[] { 3, 4, 5, 6, 7, 8, 10, 12, 16, 20, 24, 32, 40, 48, 56, 64 })
            {
                sb.AppendFormat("  {0,5} |", sides);
                foreach (float corner in new[] { 0f, 5f, 12.5f, 25f, 35f, 45f, 50f })
                {
                    var def = new ShaperPrimitiveDef
                    {
                        kind = ShaperPrimitiveKind.NGon, ngonSides = sides,
                        ngonRadius = authored, ngonCornerRadius = corner, ngonRotation = 13f,
                    };
                    var runner = new Runner(ShaperNode.Primitive(def, "n"));
                    float circum = MaxBoundaryRadius(runner, authored * 3f, 1440);
                    float err = Mathf.Abs(circum - authored);
                    if (err > worstErr) { worstErr = err; worstWhere = string.Format("sides {0}, corner {1}", sides, corner); }
                    allPass &= err <= authored * 0.002f;
                    sb.AppendFormat(" {0,7:F3}", circum);
                }
                sb.Append('\n');
            }
            sb.AppendFormat("  worst |measured circumradius - authored| = {0:F4} on {1} (tolerance {2:F3}, = the 720-ray bisection's own resolution)\n",
                            worstErr, worstWhere, authored * 0.002f);
            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>The largest boundary radius over a ring of directions, by radial bisection.</summary>
        static float MaxBoundaryRadius(Runner runner, float rMax, int directions)
        {
            float worst = 0f;
            for (int i = 0; i < directions; i++)
            {
                float th = (i / (float)directions) * 2f * Mathf.PI;
                float cx = Mathf.Cos(th), cy = Mathf.Sin(th);
                float lo = 0f, hi = rMax;
                for (int k = 0; k < 48; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (runner.D(cx * mid, cy * mid) <= 0f) lo = mid; else hi = mid;
                }
                float r = 0.5f * (lo + hi);
                if (r > worst) worst = r;
            }
            return worst;
        }

        // ── Test material ─────────────────────────────────────────────────────────────────────────────────

        struct Case { public string name; public ShaperPrimitiveDef def; public float extent; }

        static IEnumerable<Case> Cases()
        {
            yield return C("Rect 120x80 r0", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f }, 130f);
            yield return C("Rect 120x80 r18", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 60f, rectHalfH = 40f, rectCornerRadius = 18f }, 130f);
            yield return C("Ellipse circle r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 50f, ellipseRy = 50f }, 130f);
            yield return C("Ellipse 60x25", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 60f, ellipseRy = 25f }, 130f);
            yield return C("Ellipse 20x70", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 20f, ellipseRy = 70f }, 130f);
            yield return C("Diamond 50x30", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Diamond, diamondRx = 50f, diamondRy = 30f }, 120f);
            yield return C("Triangle 90x70", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Triangle, triangleBase = 90f, triangleHeight = 70f }, 120f);
            yield return C("Capsule 40+15", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Capsule, capsuleHalfLength = 40f, capsuleRadius = 15f }, 120f);
            yield return C("NGon 3 r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 3, ngonRadius = 50f }, 120f);
            yield return C("NGon 6 r50", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 6, ngonRadius = 50f }, 120f);
            yield return C("NGon 8 r50 corner12", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 8, ngonRadius = 50f, ngonCornerRadius = 12f }, 120f);
            yield return C("NGon 12 r50 rot17", new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.NGon, ngonSides = 12, ngonRadius = 50f, ngonRotation = 17f }, 120f);
            yield return C("Star 5 len.62 bw1", StarDef(5, 0.62f, 1f, 0f), 120f);
            yield return C("Star 5 len.80 bw.5 skew20", StarDef(5, 0.80f, 0.5f, 20f), 120f);
            yield return C("Star 7 len.40 bw.8 skew-30", StarDef(7, 0.40f, 0.8f, -30f), 120f);
            yield return C("Star 2 len.9 bw1", StarDef(2, 0.9f, 1f, 0f), 120f);
            yield return C("Star 20 len.5 bw.3 skew60", StarDef(20, 0.5f, 0.3f, 60f), 120f);
        }

        static Case C(string name, ShaperPrimitiveDef def, float extent)
            => new Case { name = name, def = def, extent = extent };

        static ShaperPrimitiveDef StarDef(int arms, float length, float baseWidth, float skew)
            => new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Star,
                starArms = arms,
                starRadius = 50f,
                starLength = new ZUIValue(length),
                starBaseWidth = new ZUIValue(baseWidth),
                starSkew = new ZUIValue(skew),
            };

        static (string name, ShaperTransformBlock block)[] TransformCases()
        {
            return new[]
            {
                ("identity", new ShaperTransformBlock()),
                ("translate(30,-20)", new ShaperTransformBlock { translate = new Vector2(30f, -20f) }),
                ("rotate 30", new ShaperTransformBlock { rotation = 30f }),
                ("scale(0.2,1)", new ShaperTransformBlock { scale = new Vector2(0.2f, 1f) }),
                ("scale(3,0.5)", new ShaperTransformBlock { scale = new Vector2(3f, 0.5f) }),
                ("skew(20,10)", new ShaperTransformBlock { skewDegrees = new Vector2(20f, 10f) }),
                ("origin(40,25)+rot45", new ShaperTransformBlock { rotation = 45f, origin = new Vector2(40f, 25f) }),
                ("full stack", new ShaperTransformBlock
                {
                    translate = new Vector2(15f, 5f), rotation = 22f,
                    scale = new Vector2(0.6f, 1.7f), skewDegrees = new Vector2(12f, -8f),
                    origin = new Vector2(-20f, 10f),
                }),
            };
        }

        static (string name, ShaperNode node)[] CombineCases()
        {
            ShaperNode Pair(ShaperCombineMode mode, float width, float strength, float sharpness)
            {
                var a = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = 45f, rectHalfH = 30f }, "a");
                var b = Member(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 30f, ellipseRy = 30f },
                               mode, new Vector2(35f, 15f), strength);
                b.blend.width = width;
                b.blend.sharpness = sharpness;
                return ShaperNode.Bag("bag", ShaperCombineMode.Add, a, b);
            }

            // The 2× and 4× worst cases need the two operands' gradients ANTI-ALIGNED where the band is
            // active, which overlapping shapes never produce. Two separated discs do: on the midline the two
            // distances are equal and the two gradients point at each other.
            ShaperNode Bridge(ShaperCombineMode mode, float width, float strength, float sharpness)
            {
                var a = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 25f, ellipseRy = 25f }, "left");
                a.transform = new ShaperTransformBlock { translate = new Vector2(-45f, 0f) };
                var b = Member(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 25f, ellipseRy = 25f },
                               mode, new Vector2(45f, 0f), strength);
                b.blend.width = width;
                b.blend.sharpness = sharpness;
                return ShaperNode.Bag("bag", ShaperCombineMode.Add, a, b);
            }

            return new[]
            {
                ("Add soft BRIDGE w=30 sharp 0", Bridge(ShaperCombineMode.Add, 30f, 1f, 0f)),
                ("Add soft BRIDGE w=30 sharp .5", Bridge(ShaperCombineMode.Add, 30f, 1f, 0.5f)),
                ("Add soft BRIDGE w=60 sharp 0", Bridge(ShaperCombineMode.Add, 60f, 1f, 0f)),
                ("Intersect soft BRIDGE w=30", Bridge(ShaperCombineMode.Intersect, 30f, 1f, 0f)),
                ("Subtract BRIDGE strength .5", Bridge(ShaperCombineMode.Subtract, 0f, 0.5f, 0f)),
                ("Subtract BRIDGE strength .25", Bridge(ShaperCombineMode.Subtract, 0f, 0.25f, 0f)),
                ("Add hard", Pair(ShaperCombineMode.Add, 0f, 1f, 0.5f)),
                ("Add soft w=14 sharp .0", Pair(ShaperCombineMode.Add, 14f, 1f, 0f)),
                ("Add soft w=14 sharp .5", Pair(ShaperCombineMode.Add, 14f, 1f, 0.5f)),
                ("Add soft w=14 sharp 1.", Pair(ShaperCombineMode.Add, 14f, 1f, 1f)),
                ("Intersect hard", Pair(ShaperCombineMode.Intersect, 0f, 1f, 0.5f)),
                ("Intersect soft w=14", Pair(ShaperCombineMode.Intersect, 14f, 1f, 0.5f)),
                ("Subtract strength 1", Pair(ShaperCombineMode.Subtract, 0f, 1f, 0.5f)),
                ("Subtract strength 0.5", Pair(ShaperCombineMode.Subtract, 0f, 0.5f, 0.5f)),
                ("Subtract strength 0.15", Pair(ShaperCombineMode.Subtract, 0f, 0.15f, 0.5f)),
                ("Subtract strength 0.85", Pair(ShaperCombineMode.Subtract, 0f, 0.85f, 0.5f)),
                ("Subtract strength 0", Pair(ShaperCombineMode.Subtract, 0f, 0f, 0.5f)),
            };
        }

        static ShaperNode Member(ShaperPrimitiveDef def, ShaperCombineMode mode, Vector2 offset, float strength)
        {
            var n = ShaperNode.Primitive(def, "member", mode);
            n.transform = new ShaperTransformBlock { translate = offset };
            n.blend = new ShaperBlend { carveStrength = strength, sharpness = 0.5f };
            return n;
        }

        static ShaperPrimitiveDef Clone(ShaperPrimitiveDef d)
        {
            return new ShaperPrimitiveDef
            {
                kind = d.kind,
                rectHalfW = d.rectHalfW, rectHalfH = d.rectHalfH, rectCornerRadius = d.rectCornerRadius,
                ellipseRx = d.ellipseRx, ellipseRy = d.ellipseRy,
                diamondRx = d.diamondRx, diamondRy = d.diamondRy,
                triangleBase = d.triangleBase, triangleHeight = d.triangleHeight,
                capsuleHalfLength = d.capsuleHalfLength, capsuleRadius = d.capsuleRadius,
                ngonSides = d.ngonSides, ngonRadius = d.ngonRadius,
                ngonRotation = d.ngonRotation, ngonCornerRadius = d.ngonCornerRadius,
                starArms = d.starArms, starRadius = d.starRadius,
                starLength = new ZUIValue(d.starLength != null ? d.starLength.staticValue : 0.62f),
                starBaseWidth = new ZUIValue(d.starBaseWidth != null ? d.starBaseWidth.staticValue : 1f),
                starSkew = new ZUIValue(d.starSkew != null ? d.starSkew.staticValue : 0f),
            };
        }

        // ── Instruments ───────────────────────────────────────────────────────────────────────────────────

        sealed class Runner
        {
            public readonly ShaperProgram program;
            readonly float[] stack;

            public Runner(ShaperNode node) : this(ShaperCompiler.Compile(node)) { }

            public Runner(ShaperProgram p)
            {
                program = p;
                stack = p.NewStack();
            }

            public float D(float x, float y) => ShaperEvaluator.Distance(program, x, y, stack);
        }

        /// <summary>Adapts a plain sampler to the same <c>D(x, y)</c> shape <see cref="Runner"/> exposes.</summary>
        sealed class SamplerRunner
        {
            readonly Func<float, float, float> f;
            public SamplerRunner(Func<float, float, float> f) { this.f = f; }
            public float D(float x, float y) => f(x, y);
        }

        static bool SameBits(float a, float b)
            => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static void ForEachSample(float extent, int res, Action<float, float> body)
        {
            for (int j = 0; j < res; j++)
            {
                float y = Mathf.Lerp(-extent, extent, j / (float)(res - 1));
                for (int i = 0; i < res; i++)
                {
                    float x = Mathf.Lerp(-extent, extent, i / (float)(res - 1));
                    body(x, y);
                }
            }
        }

        static int CountBitMismatch(ShaperNode a, ShaperNode b, float extent, int res, out int samples)
        {
            var ra = new Runner(a);
            var rb = new Runner(b);
            int mismatch = 0, n = 0;
            ForEachSample(extent, res, (x, y) =>
            {
                if (!SameBits(ra.D(x, y), rb.D(x, y))) mismatch++;
                n++;
            });
            samples = n;
            return mismatch;
        }

        /// <summary>
        /// <c>sup|∇f|</c> measured as the largest <b>directional difference quotient</b>
        /// <c>|f(p + h·u) − f(p − h·u)| / 2h</c> over a dense grid of points p and a spread of unit directions
        /// u, at three grid resolutions, then <b>refined</b>: the worst
        /// <see cref="RefineWorstPoints"/> points of the broad pass are re-measured on a local grid with
        /// <see cref="RefineDirections"/> directions at <see cref="RefineStepScale"/>× the step.
        ///
        /// Two properties of the instrument matter and are easy to lose.
        ///
        /// <b>Directional, not <c>hypot</c> of the two axis differences.</b> That obvious estimator is not
        /// bounded by the Lipschitz constant: each axis quotient can reach L independently at a kink, so the
        /// combination has √2 of headroom and reports ~1.02 on fields that are provably exactly Lipschitz-1.
        /// The directional quotient is the Lipschitz constant's own definition restricted to a sample, so it
        /// is bounded by L by construction and converges to it from below. It can never manufacture a
        /// violation — but it CAN miss one entirely, which is the second property.
        ///
        /// <b><c>h</c> is decoupled from the grid.</b> When <c>h</c> was tied to the grid step it measured an
        /// average gradient over half a cell, and a violation localised on a thin locus averaged away to
        /// nothing: the broken ellipse read 1.0002 at <c>h = step/2</c> and 4051 at <c>h = 0.005</c> with 64
        /// directions, on the same grid. The resolution ladder moves the sample points; it never moved the
        /// probe, so it could not help. Now the ladder chooses <i>where</i> to look and the refinement pass
        /// looks harder wherever the broad pass found anything at all.
        /// </summary>
        /// <returns>
        /// The broad-step reading, which is the one with the tightest discrimination; the fine-step reading
        /// and its own discrimination floor come back as out-parameters, along with the canvas point the
        /// worst sits at. Both scales are reported by every caller rather than folded into one number.
        /// </returns>
        static float MaxGradientDetailed(ShaperProgram program, float extent,
                                         out float fineWorst, out float fineFloor, out Vector2 worstAt)
        {
            var runner = new Runner(program);
            return MaxGradientDetailed(runner.D, extent, out fineWorst, out fineFloor, out worstAt);
        }

        /// <summary>
        /// The same instrument pointed at an arbitrary sampler rather than a compiled program, so V13 can
        /// hand it a field with a KNOWN Lipschitz constant and check what comes back. A delegate here is
        /// fine — the "no delegate per sample" rule (BC-1.1) is about the runtime hot path, and this is the
        /// editor-side audit.
        /// </summary>
        static float MaxGradientDetailed(Func<float, float, float> field, float extent,
                                         out float fineWorst, out float fineFloor, out Vector2 worstAt)
        {
            var runner = new SamplerRunner(field);
            float hBroad = extent * GradientStepFraction;
            float hFine = extent * RefineStepFraction;

            var ux = new float[RefineDirections];
            var uy = new float[RefineDirections];
            for (int k = 0; k < RefineDirections; k++)
            {
                float a = Mathf.PI * k / RefineDirections;
                ux[k] = Mathf.Cos(a); uy[k] = Mathf.Sin(a);
            }
            // The broad pass samples every (RefineDirections / GradientDirections)-th of the same spread.
            int stride = RefineDirections / GradientDirections;

            // A "keep the K worst" list; K is 24, so a linear insert is free.
            var bestG = new float[RefineWorstPoints];
            var bestX = new float[RefineWorstPoints];
            var bestY = new float[RefineWorstPoints];

            float maxMagnitude = 1f;

            float Quotient(float x, float y, float h, int dirStride)
            {
                float inv = 1f / (2f * h);
                float w = 0f;
                for (int k = 0; k < RefineDirections; k += dirStride)
                {
                    float hx = ux[k] * h, hy = uy[k] * h;
                    float a = runner.D(x + hx, y + hy);
                    float b = runner.D(x - hx, y - hy);
                    // The empty field is a constant outside the metric contract; a difference straddling its
                    // edge is not a gradient of anything.
                    if (ShaperField.IsEmpty(a) != ShaperField.IsEmpty(b)) continue;
                    float mag = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
                    if (mag > maxMagnitude && !ShaperField.IsEmpty(a)) maxMagnitude = mag;
                    // Rounding-corrected: the true difference is within `slack` of the computed one, so
                    // subtracting it makes this a rigorous LOWER bound on the true quotient. Without it the
                    // fine pass reports ~1.007 on a plain Rect, which is an exact SDF.
                    float slack = RoundingUlps * FloatEps * mag;
                    float g = Mathf.Max(0f, Mathf.Abs(a - b) - slack) * inv;
                    if (g > w) w = g;
                }
                return w;
            }

            void Offer(float g, float x, float y)
            {
                if (g <= bestG[RefineWorstPoints - 1]) return;
                int i = RefineWorstPoints - 1;
                while (i > 0 && bestG[i - 1] < g) { bestG[i] = bestG[i - 1]; bestX[i] = bestX[i - 1]; bestY[i] = bestY[i - 1]; i--; }
                bestG[i] = g; bestX[i] = x; bestY[i] = y;
            }

            float broadWorst = 0f;
            worstAt = Vector2.zero;
            int finestRes = 257;
            foreach (int res in new[] { 65, 129, finestRes })
            {
                float step = 2f * extent / (res - 1);
                for (int j = 0; j < res; j++)
                {
                    float y = -extent + j * step;
                    for (int i = 0; i < res; i++)
                    {
                        float x = -extent + i * step;
                        float g = Quotient(x, y, hBroad, stride);
                        if (g > broadWorst) { broadWorst = g; worstAt = new Vector2(x, y); }
                        Offer(g, x, y);
                    }
                }
            }

            // Refinement: around each of the worst broad points, a local grid spanning one broad cell — so it
            // fills the gaps the broad pass stepped over — measured with EVERY direction, at the broad step
            // and again two orders finer. A violation has to survive both scales and both direction sets to
            // stay hidden.
            fineWorst = broadWorst;
            float localSpan = 2f * extent / (finestRes - 1);
            const int localRes = 9;
            for (int p = 0; p < RefineWorstPoints; p++)
            {
                if (bestG[p] <= 0f) continue;
                for (int j = 0; j < localRes; j++)
                {
                    float y = bestY[p] + Mathf.Lerp(-localSpan, localSpan, j / (float)(localRes - 1));
                    for (int i = 0; i < localRes; i++)
                    {
                        float x = bestX[p] + Mathf.Lerp(-localSpan, localSpan, i / (float)(localRes - 1));
                        float g1 = Quotient(x, y, hBroad, 1);
                        if (g1 > broadWorst) { broadWorst = g1; worstAt = new Vector2(x, y); }
                        float g2 = Quotient(x, y, hFine, 1);
                        float g = Mathf.Max(g1, g2);
                        if (g > fineWorst) fineWorst = g;
                    }
                }
            }

            fineFloor = DiscriminationFloor(maxMagnitude, hFine);
            return broadWorst;
        }

        /// <summary>
        /// V13 — the instrument's own self-test: point the bound gate at a field whose Lipschitz constant is
        /// known by construction and check what it reads back.
        ///
        /// Every other check here asks "does the engine obey its declared bound?". None of them asks "would
        /// this instrument have noticed if it did not?" — and that question is not idle, because a first cut
        /// of the refinement pass reported 1.0071 on a provably exact Rect, and an earlier
        /// <c>BoundTolerance</c> of 1.02 would have printed "ok" against a real 1.019 violation. An
        /// instrument that cannot be shown to flip at the right place is not evidence of anything.
        ///
        /// The subject is <c>k · (an exact Rect SDF)</c>, whose true Lipschitz constant is exactly
        /// <c>k</c>. Three properties are asserted:
        /// <list type="bullet">
        /// <item>the reading tracks <c>k</c> (it is a lower bound, so it may sit a hair under, never over);</item>
        /// <item>the verdict is <b>ok</b> for every k at or below <see cref="BoundTolerance"/> and
        /// <b>OVER</b> for every k above it — i.e. the gate flips exactly there, not near there;</item>
        /// <item>a gross violation is reported at its true size rather than saturated or clipped.</item>
        /// </list>
        /// </summary>
        public static string V13_InstrumentSelfTest()
        {
            var sb = new StringBuilder("V13 instrument self-test — inject a known violation, check the gate flips\n");
            sb.AppendFormat("  subject: k * exact Rect SDF (60x40), true Lipschitz constant = k exactly; declared bound 1.00, tolerance {0:F4}\n",
                            BoundTolerance);
            bool allPass = true;

            // k values straddling the tolerance from both sides, plus one gross violation the size of the
            // ellipse bug this whole exercise started from.
            //
            // k EXACTLY equal to BoundTolerance is deliberately marked "edge" rather than asserted either
            // way, and that is not a dodge — it is the only honest expectation. The reading is a rounding-
            // corrected lower bound, but at the sample where the correction vanishes (|f| near zero, so the
            // slack is near zero) it still carries ~1e-5 of ordinary float rounding, so measured lands a hair
            // to one side or the other of k. Demanding a definite verdict at an exact float equality is
            // demanding something float arithmetic does not offer. What IS asserted, and is the property
            // that matters, is that the gate is decisive on both sides: ok for every k strictly below the
            // tolerance, OVER for every k strictly above it, with no gap in between.
            float[] gains = { 1f, 1.0005f, 1.0009f, 1.00099f, BoundTolerance, 1.00101f, 1.0011f, 1.002f, 1.05f, 2f, 7.24f };
            foreach (float k in gains)
            {
                float gain = k;
                Func<float, float, float> injected = (x, y) => gain * ShaperSdf.Rect(60f, 40f, 0f, x, y);
                float measured = MaxGradientDetailed(injected, 130f, out float fine, out float fineFloor, out _);
                bool verdict = measured <= 1f * BoundTolerance;                 // the same expression Check() uses
                bool tracks = measured <= k + 1e-4f && measured >= k - 2e-3f;   // lower bound: may sit a hair under

                bool edge = k == BoundTolerance;
                string expectedStr = edge ? "edge" : (k < BoundTolerance ? "ok" : "OVER");
                bool verdictOk = edge || (verdict == (k < BoundTolerance));
                bool ok = verdictOk && tracks;
                allPass &= ok;
                sb.AppendFormat("    k={0,-8:F5} measured {1,9:F5}  gate says {2,-4}  expected {3,-4}  tracks k {4,-5}  {5}   (localiser {6:F4}, res {7:F4})\n",
                                k, measured, verdict ? "ok" : "OVER", expectedStr, tracks,
                                ok ? "ok" : "MISREAD", fine, fineFloor);
            }
            sb.AppendLine("  Reading: the gate is ok for every k strictly below " + BoundTolerance.ToString("F5") +
                          " and OVER for every k strictly above it,");
            sb.AppendLine("  so it flips exactly at the tolerance and nowhere else; k == the tolerance itself is a");
            sb.AppendLine("  float knife-edge and is reported, not asserted. A gross violation comes back at its");
            sb.AppendLine("  true magnitude (7.24 reads 7.240) rather than saturated or clipped.");
            sb.AppendLine("  Note the localiser column never decides anything here - that is the point of P2.");

            sb.AppendFormat("  RESULT: {0}\n", allPass ? "PASS" : "FAIL");
            return sb.ToString();
        }

        /// <summary>
        /// A boundary point cloud by radial bisection. Valid for the shapes V5 names, all of which are
        /// star-shaped about the origin with a radially monotone boundary.
        /// </summary>
        static void BuildBoundaryCloud(Runner runner, float rMax, int directions, out float[] xs, out float[] ys)
        {
            xs = new float[directions];
            ys = new float[directions];
            for (int i = 0; i < directions; i++)
            {
                float th = (i / (float)directions) * 2f * Mathf.PI;
                float cx = Mathf.Cos(th), cy = Mathf.Sin(th);
                float lo = 0f, hi = rMax;
                for (int k = 0; k < 48; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (runner.D(cx * mid, cy * mid) <= 0f) lo = mid; else hi = mid;
                }
                float r = 0.5f * (lo + hi);
                xs[i] = cx * r; ys[i] = cy * r;
            }
        }

        /// <summary>
        /// Distance to the boundary <b>polyline</b> through the cloud, not to the cloud's vertices.
        ///
        /// This matters and it used to be wrong. The nearest point of a finite point cloud is always at least
        /// as far away as the nearest point of the curve those points lie on, so a vertex-only "true"
        /// distance systematically OVER-estimates the truth and therefore systematically UNDER-states every
        /// ratio it reports — conservative in exactly the wrong direction for a check whose whole job is to
        /// catch over-reporting. Interpolating along the chords removes the first-order part of that error
        /// (the residual is O(chord²/R), second order, and is quantified by
        /// <see cref="CloudMaxChord"/> and reported alongside every ratio). On a convex arc the chord lies
        /// inside the curve, so what remains is a slight UNDER-estimate of true distance — which biases the
        /// ratio upward, i.e. toward catching a violation rather than away from it.
        /// </summary>
        static float TrueDistance(float[] xs, float[] ys, float px, float py)
        {
            int n = xs.Length;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;   // closed loop
                float ax = xs[i], ay = ys[i];
                float ex = xs[j] - ax, ey = ys[j] - ay;
                float wx = px - ax, wy = py - ay;
                float ee = ex * ex + ey * ey;
                float t = ee > 1e-20f ? Mathf.Clamp01((wx * ex + wy * ey) / ee) : 0f;
                float dx = wx - ex * t, dy = wy - ey * t;
                float d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>The longest chord in the closed cloud — the instrument's own discretisation scale.</summary>
        static float CloudMaxChord(float[] xs, float[] ys)
        {
            int n = xs.Length;
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                float dx = xs[j] - xs[i], dy = ys[j] - ys[i];
                float d = dx * dx + dy * dy;
                if (d > worst) worst = d;
            }
            return Mathf.Sqrt(worst);
        }

        /// <summary>
        /// The worst relative error the cloud itself can contribute at a given true distance: a chord of
        /// length c sags below its arc by at most c²/(8R) for radius-of-curvature R, and bounding R below by
        /// the distance itself gives c²/(8·d) as an absolute error, hence c²/(8·d²) relative. Reported next
        /// to every ratio so the numbers can be read honestly rather than taken as exact.
        /// </summary>
        static float CloudRelativeError(float maxChord, float floorDist)
            => floorDist <= 0f ? 0f : (maxChord * maxChord) / (8f * floorDist * floorDist);

        /// <summary>
        /// Worst <c>reported / trueEuclidean</c> inside and outside. Points nearer the boundary than the
        /// cloud's own resolution are skipped — there the ratio measures the instrument, not the field.
        /// </summary>
        static void MeasureRatios(Runner runner, float[] cx, float[] cy, float extent,
                                  out float worstInside, out float worstOutside)
            => MeasureRatios(runner, cx, cy, extent, 121, out worstInside, out worstOutside, out _, out _);

        static void MeasureRatios(Runner runner, float[] cx, float[] cy, float extent, int res,
                                  out float worstInside, out float worstOutside,
                                  out int used, out int skipped)
        {
            worstInside = 0f; worstOutside = 0f; used = 0; skipped = 0;
            float floorDist = extent * 0.03f;
            for (int j = 0; j < res; j++)
            {
                float y = Mathf.Lerp(-extent, extent, j / (float)(res - 1));
                for (int i = 0; i < res; i++)
                {
                    float x = Mathf.Lerp(-extent, extent, i / (float)(res - 1));
                    float reported = runner.D(x, y);
                    if (ShaperField.IsEmpty(reported)) { skipped++; continue; }
                    float trueD = TrueDistance(cx, cy, x, y);
                    if (trueD < floorDist) { skipped++; continue; }
                    used++;
                    float ratio = Mathf.Abs(reported) / trueD;
                    if (reported < 0f) { if (ratio > worstInside) worstInside = ratio; }
                    else { if (ratio > worstOutside) worstOutside = ratio; }
                }
            }
        }

        static float RatioAt(Runner runner, float[] cx, float[] cy, float angleDegFromUp, float radius)
        {
            float th = ShaperPrimitives.TipUp + angleDegFromUp * Mathf.Deg2Rad;
            float x = Mathf.Cos(th) * radius, y = Mathf.Sin(th) * radius;
            float trueD = TrueDistance(cx, cy, x, y);
            if (trueD <= 1e-4f) return 1f;
            return Mathf.Abs(runner.D(x, y)) / trueD;
        }

        // ── The reference app's star, kept ONLY as V7's witness ───────────────────────────────────────────
        //
        // (hypot(x/rx, y/ry) - (0.42 + 0.58*pow(lobe, 0.8))) * min(rx, ry), lobe = 0.5 + 0.5*cos(5*angle - pi/2).
        // A fixed five-lobe cosine flower with the arm count, sharpness exponent and phase all hard-coded and
        // no parameters at all. It is not ported into the runtime; it exists here so V7 has something whose
        // landmark and whose worst case actually differ.

        static float RefFlower(float x, float y, float rx, float ry)
        {
            float angle = Mathf.Atan2(y / ry, x / rx);
            float lobe = 0.5f + 0.5f * Mathf.Cos(5f * angle - Mathf.PI * 0.5f);
            float unit = Mathf.Min(rx, ry);
            float h = Mathf.Sqrt((x / rx) * (x / rx) + (y / ry) * (y / ry));
            return (h - (0.42f + 0.58f * Mathf.Pow(lobe, 0.8f))) * unit;
        }

        static void RefFlowerCloud(float rx, float ry, out float[] xs, out float[] ys)
        {
            const int n = 20000;
            xs = new float[n]; ys = new float[n];
            for (int i = 0; i < n; i++)
            {
                float th = (i / (float)n) * 2f * Mathf.PI;
                float cx = Mathf.Cos(th), cy = Mathf.Sin(th);
                float lo = 0f, hi = Mathf.Max(rx, ry) * 4f;
                for (int k = 0; k < 48; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (RefFlower(cx * mid, cy * mid, rx, ry) <= 0f) lo = mid; else hi = mid;
                }
                float r = 0.5f * (lo + hi);
                xs[i] = cx * r; ys[i] = cy * r;
            }
        }

        static float RefFlowerRatioAt(float angleDegFromUp, float radius, float rx, float ry)
        {
            RefFlowerCloud(rx, ry, out float[] cx, out float[] cy);
            // The reference app's canvas is y-DOWN, so its tip at atan2 = +90 points down on screen. Its
            // landmark in ITS OWN frame is what we want, so the angle is used unflipped here.
            float th = Mathf.PI * 0.5f + angleDegFromUp * Mathf.Deg2Rad;
            float x = Mathf.Cos(th) * radius, y = Mathf.Sin(th) * radius;
            float trueD = TrueDistance(cx, cy, x, y);
            if (trueD <= 1e-4f) return 1f;
            return Mathf.Abs(RefFlower(x, y, rx, ry)) / trueD;
        }

        static float RefFlowerWorst(float rx, float ry, float extent)
        {
            RefFlowerCloud(rx, ry, out float[] cx, out float[] cy);
            float worst = 0f;
            const int res = 61;
            float floorDist = extent * 0.03f;
            for (int j = 0; j < res; j++)
            {
                float y = Mathf.Lerp(-extent, extent, j / (float)(res - 1));
                for (int i = 0; i < res; i++)
                {
                    float x = Mathf.Lerp(-extent, extent, i / (float)(res - 1));
                    float trueD = TrueDistance(cx, cy, x, y);
                    if (trueD < floorDist) continue;
                    float ratio = Mathf.Abs(RefFlower(x, y, rx, ry)) / trueD;
                    if (ratio > worst) worst = ratio;
                }
            }
            return worst;
        }
    }
}
