using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// <see cref="ShaperHeightDef"/> → <see cref="ShaperHeightOp"/>: the one place a height dial is read.
    ///
    /// Every scalar goes through <see cref="ShaperValue.Sample"/> ONCE per compile (LR-1.8 / BC-1.2), with a
    /// distinct seed offset per dial so a <c>MinMax</c> dial on <c>curve</c> and one on <c>taper</c> get
    /// uncorrelated draws — the same discipline <c>ShaperLightCompiler</c> uses
    /// (<c>ShaperLightCompiler.cs:244-327</c>). Nothing here is ever called from a per-sample loop.
    ///
    /// Everything the per-sample path would otherwise recompute is precomputed: the trig, the reciprocals,
    /// the exponents, <c>span</c>, <c>sup E</c>, <c>sup G</c> and all three declared slope bounds. The
    /// per-sample path is then a switch over a struct with no divides.
    ///
    /// <b>The reference's clamps are applied VERBATIM, at their own lines</b> — see
    /// <see cref="ShaperHeight.StepCount"/>, <see cref="ShaperHeight.Curve"/>, <see cref="ShaperHeight.Taper"/>
    /// and <see cref="ShaperHeight.Amount"/>, each of which carries the <c>index.html</c> line it came from.
    ///
    /// <b>One reference behaviour deliberately NOT reproduced, and it is a JS falsiness artefact rather than a
    /// design decision.</b> <c>index.html:1150-1151</c> reads <c>Math.max(0.2, Number(settings.curve)||1)</c>,
    /// so an authored <c>curve</c> of EXACTLY 0 becomes 1, not 0.2 — and <c>Math.round(settings.steps)||4</c>
    /// turns a <c>steps</c> of 0 into 4 (REF-HEIGHT-MATHS §H6). Shaper's dials are floats with real defaults
    /// and no <c>undefined</c>, so <c>0</c> is a value an author typed and it is clamped to the range's floor
    /// like any other out-of-range value. Reproducing the falsiness would mean a <c>curve</c> slider that
    /// jumps from 0.2 to 1 as it passes zero.
    /// </summary>
    public static class ShaperHeightCompiler
    {
        // Seed offsets, one per dial. Distinct so two MinMax dials never draw the same number.
        const uint SeedDepth = 1u, SeedAngle = 33u, SeedSteps = 65u, SeedCurve = 97u,
                   SeedTaper = 129u, SeedAmount = 161u, SeedBevelSteps = 193u, SeedZOffset = 225u;

        /// <summary>
        /// Compile one layer's height stage against the shape program it will be evaluated over.
        ///
        /// <paramref name="program"/> supplies <c>span</c> (HS-1.2) and the node-local frame
        /// <see cref="ShaperExtrusionTechnique.Linear"/> reads (HS-2.3); <paramref name="pixelSize"/> is the
        /// grid's sample spacing and is <c>span</c>'s floor; <paramref name="baseZ"/> is HS-7.2's
        /// <c>i × layerSpacing + zOffset(i)</c>, computed once per layer per compile by
        /// <see cref="LayerBase"/>.
        ///
        /// A null <paramref name="def"/> returns a stage with <see cref="ShaperHeightOp.present"/> false,
        /// which is HS-1.4's "and NOT when it is absent" — not a zero-depth stage, an ABSENT one.
        /// </summary>
        public static ShaperHeightOp Compile(ShaperHeightDef def, ShaperProgram program,
                                             float pixelSize, float baseZ,
                                             float phase01 = 0f, uint seed = 0u)
        {
            var op = new ShaperHeightOp
            {
                present = def != null,
                baseZ = baseZ,
                // Identity local frame, degenerate reciprocals. FC-1.5b's shape: a degenerate box gives (0,0)
                // WITHOUT dividing, so `Linear` on a singular or empty node reads nx = ny = 0 and becomes a
                // plain constant-thickness slab rather than a NaN.
                m00 = 1f, m01 = 0f, m02 = 0f,
                m10 = 0f, m11 = 1f, m12 = 0f,
                invLocalHalfW = 0f, invLocalHalfH = 0f,
                supE = 1f, supG = 1f, infE = 1f, linearEGrad = 0f,
                n = 2, invN1 = 1f,
                bevelN = 2, invBevelN = 0.5f,
                curve = 1f, domeExp = 0.5f, roundExp = 0.5f,
                tau = 1f, taperT = 0.6f, invTaperT = 1f / 0.6f,
                span = Mathf.Max(pixelSize, 1e-4f),
            };
            op.invSpan = 1f / op.span;

            if (def == null) return op;

            op.technique = def.technique;
            op.bevel = def.bevel;

            // ── body: THICKNESS, never negative (HS-1.3, index.html:1362's Math.max(0, …)) ───────────────
            op.body = Mathf.Max(0f, ShaperValue.Sample(def.depth, phase01, seed + SeedDepth, 0f));

            // ── span (HS-1.2) ─────────────────────────────────────────────────────────────────────────────
            //
            // Three deliberate departures from index.html:1339 (`span = max(1, min(halfW, halfH))`), each for
            // a stated reason:
            //   1. The floor is `pixelSize`, not `1`, because our unit is the canvas pixel (LR-1.5) and
            //      pixelSize IS the sample spacing — so "never smaller than one sample" means the same thing
            //      in both, in each one's own units.
            //   2. The extents are the ROOT-LOCAL support box scaled by `rootSigmaMin`, not the canvas
            //      bounding box. Same choice and same reason as FC-1.5a: the canvas box grows and shrinks as
            //      the node rotates (ShaperCompiler.cs:178-179 carries local half-extents through the FORWARD
            //      matrix with an absolute-value corner sum), so a `span` taken from it would make a dome
            //      breathe once per quarter turn with nothing authored changing.
            //   3. It is a COMPILE-TIME scalar, read once, never recomputed per sample.
            //
            // `rootSigmaMin` converts a canvas distance into the root node's own local units, which is the
            // frame `localSupportHalfW` is expressed in — so dividing the local half-extent BY it would be the
            // wrong direction; multiplying takes the local extent back to canvas pixels, which is where `d`
            // lives.
            float pxFloor = Mathf.Max(pixelSize, 1e-4f);
            if (program != null)
            {
                if (program.hasLocalSupport)
                {
                    float sMin = program.rootSigmaMin > 0f ? program.rootSigmaMin : 1f;
                    float shorter = Mathf.Min(program.localSupportHalfW, program.localSupportHalfH);
                    op.span = Mathf.Max(pxFloor, sMin * shorter);
                }
                else
                {
                    op.span = Mathf.Max(pxFloor, Mathf.Min(program.supportHalfW, program.supportHalfH));
                }
                if (float.IsNaN(op.span) || float.IsInfinity(op.span)) op.span = pxFloor;
            }
            op.invSpan = 1f / op.span;

            // ── the node-local frame Linear reads (HS-2.3) ────────────────────────────────────────────────
            if (program != null)
            {
                if (program.rootInvertible)
                {
                    op.m00 = program.rootInverse.m00; op.m01 = program.rootInverse.m01; op.m02 = program.rootInverse.m02;
                    op.m10 = program.rootInverse.m10; op.m11 = program.rootInverse.m11; op.m12 = program.rootInverse.m12;
                }
                if (program.hasLocalSupport)
                {
                    op.localCx = program.localSupportCx;
                    op.localCy = program.localSupportCy;
                    // FC-1.5b again: store 0 in the reciprocal rather than branching in the loop.
                    op.invLocalHalfW = program.localSupportHalfW > 1e-5f ? 1f / program.localSupportHalfW : 0f;
                    op.invLocalHalfH = program.localSupportHalfH > 1e-5f ? 1f / program.localSupportHalfH : 0f;
                }
            }

            // ── Linear: the angle (index.html:1148) ───────────────────────────────────────────────────────
            float angleDeg = ShaperValue.Sample(def.angle, phase01, seed + SeedAngle, 45f);
            angleDeg = Mathf.Clamp(angleDeg, -180f, 180f);                 // project_document.py:251
            float theta = angleDeg * Mathf.Deg2Rad;
            op.cosAngle = Mathf.Cos(theta);
            op.sinAngle = Mathf.Sin(theta);

            // ── Stepped extrusion: n = max(2, round(steps)) (index.html:1149) ─────────────────────────────
            float stepsRaw = ShaperValue.Sample(def.steps, phase01, seed + SeedSteps, 4f);
            op.n = Mathf.Clamp(ShaperHeight.StepCount(stepsRaw), 2, 32);   // project_document.py:252
            op.invN1 = 1f / (op.n - 1);

            // ── Dome / Round: c = max(0.2, curve) (index.html:1150, :1151) ────────────────────────────────
            float curveRaw = ShaperValue.Sample(def.curve, phase01, seed + SeedCurve, 1f);
            op.curve = Mathf.Min(4f, ShaperHeight.Curve(curveRaw));        // project_document.py:253
            op.domeExp = 1f / (2f * op.curve);                             // HS-2.2's collapsed exponent
            op.roundExp = 0.5f / op.curve;

            // ── Taper / Pyramid: τ = clamp01(taper), T = max(0.05, 0.6τ) (index.html:1152, :1153) ─────────
            float taperRaw = ShaperValue.Sample(def.taper, phase01, seed + SeedTaper, 1f);
            op.tau = ShaperHeight.Taper(taperRaw);                         // project_document.py:254
            op.taperT = Mathf.Max(0.05f, op.tau * 0.6f);                   // the clamp that caps L at 20, REF §C.2
            op.invTaperT = 1f / op.taperT;

            // ── Bevel: a = clamp01(amount), m = max(2, round(steps)) (index.html:1160, :1167) ─────────────
            op.a = ShaperHeight.Amount(ShaperValue.Sample(def.bevelAmount, phase01, seed + SeedAmount, 0.25f));
            op.invA = op.a > 0f ? 1f / op.a : 0f;

            float bevelStepsRaw = ShaperValue.Sample(def.bevelSteps, phase01, seed + SeedBevelSteps, 3f);
            op.bevelN = Mathf.Clamp(ShaperHeight.BevelStepCount(bevelStepsRaw), 2, 16);   // project_document.py:267
            op.invBevelN = 1f / op.bevelN;

            // ── sup E and sup G (HS-2.3) ──────────────────────────────────────────────────────────────────
            //
            // 1 for every profile EXCEPT Linear, where 1 + 0.6(|cosθ| + |sinθ|) reaches 1 + 0.6√2 = 1.84853 at
            // θ = ±45° — which is the DEFAULT angle. So on exactly this one technique `body` is NOT a height
            // budget and any code asserting height ≤ body is wrong. sup G = sup E, because B ≡ 1 off the band
            // and every t-profile reaches E(1) = 1 there.
            op.supE = op.technique == ShaperExtrusionTechnique.Linear
                ? 1f + 0.6f * (Mathf.Abs(op.cosAngle) + Mathf.Abs(op.sinAngle))
                : 1f;
            op.supG = op.supE;

            // ── inf E and |∇E|, T-0109 FIX F1 ─────────────────────────────────────────────────────────────
            //
            // inf E is sup E's mirror and exists for the same reason: the march's SOLID-space skip needs an
            // UPPER bound on Ginv where the empty-space skip needs a lower one, and on Linear those are two
            // different numbers because E varies across the canvas. It is never negative in practice
            // (1 − 0.6√2 = 0.15147) but is floored at 0 so nothing downstream ever divides by a negative.
            //
            // linearEGrad bounds how much E can change over one canvas pixel of travel, which is what lets
            // the march bracket Linear LOCALLY (a bracket that shrinks with the step) instead of using the
            // canvas-wide [inf E, sup E].
            //
            // T-0109 FIX N5 — <b>the CLAMP does not only reduce the variation, and the old comment saying it
            // did was false.</b> E = 1 + 0.6(cosθ·nx + sinθ·ny) with nx and ny clamped to [−1,1]
            // INDEPENDENTLY, so ∇E has four regimes, not one: both axes live (the exact Jacobian of the
            // unclamped map), nx pinned (only the sinθ·∇ny term survives), ny pinned (only the cosθ·∇nx term
            // survives), and both pinned (zero). Where the two terms partially CANCEL, a surviving single
            // term is LARGER than their sum — so the exact unclamped Jacobian, which is what was baked, is
            // not an upper bound on |∇E| anywhere a support-box edge is crossed. Measured over 1650
            // configurations (11 angles × 5 rotations × 5 skews × 3 scales × 2 aspects): the true sup
            // exceeded the baked value in 385 of them, worst ratio 3.6235× (angle −135°, rotation 17°, skew
            // 60°, scale 2.3 — unclamped 3.48485e-3, ny-clamped 1.26274e-2).
            //
            // The consequence is that the per-step E window `de = eGrad·σ·dxy` was too narrow near the local
            // support-box boundary, so neither the containing nor the contained prism was guaranteed to
            // bracket the true E over the step and both "proofs" lost their proof status there. No march
            // failure was ever produced from it (5 184 targeted Linear rays, 0 omissions), but a bound that
            // is not a bound is precisely what T-0105 was built to eliminate.
            //
            // The fix is the MAX over the regimes, which is the true supremum of |∇E| over the whole plane
            // and is still one compile-time constant. It costs a wider window on the configurations where
            // the terms cancel and NOTHING on the ones where they do not (the max is the old value whenever
            // the sum dominates); a wider window only ever makes the march take shorter, still-provable
            // steps, so this is a speed cost and never a correctness one.
            if (op.technique == ShaperExtrusionTechnique.Linear)
            {
                op.infE = Mathf.Max(0f, 1f - 0.6f * (Mathf.Abs(op.cosAngle) + Mathf.Abs(op.sinAngle)));

                // `+` on sinθ: the T-0109 FIX F7 frame flip. Must match ShaperHeight.Profile's Linear case.
                // (ax, ay) is ∇(0.6·cosθ·nx); (bx, by) is ∇(0.6·sinθ·ny). Each vanishes where its own axis
                // is clamped, which is what makes the three non-zero regimes below.
                float ax = 0.6f * op.cosAngle * op.m00 * op.invLocalHalfW;
                float ay = 0.6f * op.cosAngle * op.m01 * op.invLocalHalfW;
                float bx = 0.6f * op.sinAngle * op.m10 * op.invLocalHalfH;
                float by = 0.6f * op.sinAngle * op.m11 * op.invLocalHalfH;

                float sx = ax + bx, sy = ay + by;
                float gBoth = Mathf.Sqrt(sx * sx + sy * sy);   // neither axis clamped — the old baked value
                float gNyOnly = Mathf.Sqrt(bx * bx + by * by); // nx clamped: only the sinθ term survives
                float gNxOnly = Mathf.Sqrt(ax * ax + ay * ay); // ny clamped: only the cosθ term survives
                                                               // both clamped: ∇E = 0, never the max

                float g = gBoth;
                if (gNyOnly > g) g = gNyOnly;
                if (gNxOnly > g) g = gNxOnly;

                op.linearEGrad = g;
                if (float.IsNaN(op.linearEGrad) || float.IsInfinity(op.linearEGrad)) op.linearEGrad = 0f;
            }
            else
            {
                op.infE = 1f;
                op.linearEGrad = 0f;
            }

            // ── the declared bounds (HS-4), baked so a consumer reads a float rather than calling a switch ─
            op.extrusionSlope = ShaperHeight.ExtrusionSlopeBound(op);
            op.bevelSlope = ShaperHeight.BevelSlopeBound(op);
            op.composedSlope = ShaperHeight.ComposedSlopeBound(op);

            return op;
        }

        /// <summary>
        /// HS-7.2 — <b><c>base(i) = i × layerSpacing + zOffset(i)</c></b>, computed ONCE per layer per compile.
        /// <c>i</c> is the layer's index in <see cref="ShaperDocument.layers"/>, bottom-most = 0, and no stage
        /// may reorder that list (<c>ShaperLightRig.cs:349</c>).
        ///
        /// This is the layer's <c>base</c> in HS-1.1 and it is the ONLY Z contributor — exactly the property
        /// that makes the change small. <c>zOffset</c> is sampled on the DOCUMENT's phase, like every other
        /// layer-level dial and like the rig itself (LR-1.8).
        ///
        /// <b>HS-7.3 — two traps from the reference deliberately NOT inherited.</b>
        /// <list type="number">
        /// <item>The reference's composite height buffer uses <c>−9999</c> as its empty-cell sentinel, tested
        /// against <c>−900</c> (<c>index.html:1391</c> vs <c>:1438</c>). With <c>base = order·0.75 ≥ 0</c> that
        /// threshold was unreachable — but a SIGNED <c>zOffset</c> reaches it, and a layer pushed far enough
        /// back would VANISH rather than go behind. Shaper carries occupancy in <c>coverage</c>, which already
        /// exists, so no sentinel is introduced here and <b>no range restriction on <c>zOffset</c> is
        /// needed</b>.</item>
        /// <item>The reference fuses layers within <c>FUSION_CELLS = 1.7</c> cells of each other
        /// (<c>index.html:982</c>, used at <c>:1441</c> and <c>:1444</c>), smooth-maxing them into one welded
        /// blob instead of z-testing. Since its own <c>base</c> steps by only 0.75 per layer, ADJACENT LAYERS
        /// ALREADY FUSE BY DEFAULT there, and any offset under ~1.7 cells changes the fused shape rather than
        /// the stacking order. Shaper has no fusion band, so a small <c>zOffset</c> does exactly what it
        /// says.</item>
        /// </list>
        /// Both are recorded because a build agent testing a new Z dial against the reference's behaviour would
        /// otherwise conclude the dial "doesn't do anything".
        /// </summary>
        public static float LayerBase(ShaperDocument doc, int layerIndex, float phase01, uint seed)
        {
            if (doc == null || layerIndex < 0 || layerIndex >= doc.layers.Count) return 0f;
            ShaperLayer layer = doc.layers[layerIndex];
            float z = ShaperValue.Sample(layer != null ? layer.zOffset : null,
                                         phase01, seed + SeedZOffset + (uint)layerIndex, 0f);
            return layerIndex * doc.layerSpacing + z;
        }
    }
}
