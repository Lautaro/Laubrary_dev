using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Which surface of the solid a crossing is on. HS-6.
    ///
    /// The boundary of HS-1.1's solid has exactly three parts and no others: the base plane, the top surface
    /// <c>z = base + body·G</c>, and the vertical silhouette side. The first two are horizontal faces and are
    /// both <see cref="Cap"/>; the third is the <see cref="Wall"/>.
    /// </summary>
    public enum ShaperSurfaceKind
    {
        /// <summary>A horizontal face — the top surface, or the base plane underneath it.</summary>
        Cap = 0,
        /// <summary>The vertical silhouette side. HS-6.1: <c>edgeDistance ≡ 0</c> and <c>t = 0</c> on it.</summary>
        Wall = 1,
    }

    /// <summary>
    /// Which of the two implementations behind <see cref="ShaperResolve.Query"/> actually ran. HS-9.2.
    ///
    /// It is reported because the conformance check of HS-9.5 must ASSERT that the general branch was taken
    /// rather than assume it — a tilted render that silently fell back to the closed form would prove nothing
    /// and would look identical.
    ///
    /// It is NOT a selector: BC-2.3 binds the SHAPE of the call, and "a caller MUST NOT be able to tell that
    /// v1 only handles one direction by the shape of what it calls". The branch is chosen by the RAY, inside,
    /// and reported afterwards as a fact.
    /// </summary>
    public enum ShaperResolveBranch
    {
        /// <summary>The <c>−Z</c> closed form: one point sample, no march, today's cost. HS-9.2.</summary>
        StraightDown = 0,
        /// <summary>The slab march of HS-5.3.</summary>
        General = 1,
    }

    /// <summary>
    /// One surface the ray crossed. HS-9.3 — everything a downstream consumer needs so it never has to
    /// re-evaluate the field to find out what it just hit.
    /// </summary>
    public struct ShaperCrossing
    {
        /// <summary>Index into <see cref="ShaperResolveScene.layers"/>.</summary>
        public int layer;

        /// <summary>
        /// The ray parameter at the crossing, in CANVAS PIXELS from the ray origin — the direction is
        /// normalised on entry to <see cref="ShaperResolve.Query"/>, so this is a real length and a span
        /// between two crossings is a real thickness.
        /// </summary>
        public float rayT;

        /// <summary>True when the ray is entering solid here, false when leaving it.</summary>
        public bool entering;

        /// <summary>Cap or Wall. HS-6.</summary>
        public ShaperSurfaceKind kind;

        /// <summary>
        /// The crossing point's coordinates in the LAYER's own space.
        ///
        /// In Wave 2 a <see cref="ShaperLayer"/> carries no transform of its own — it is a name, an enable, a
        /// shape root, a response block and a Z offset — so a layer's local frame IS the canvas frame in XY
        /// and these are the crossing point's canvas X and Y. When a per-layer transform arrives, this is the
        /// one place its inverse is applied, and no consumer's arithmetic changes.
        /// </summary>
        public float localX, localY;

        /// <summary>
        /// The shape's signed distance at the crossing, canvas pixels, negative inside
        /// (<c>ShaperField.cs:9-10</c>).
        ///
        /// <b>Exactly 0 on a <see cref="ShaperSurfaceKind.Wall"/>, by HS-6.1's ruling</b> — a wall inherits
        /// the rim value it descends from, and every wall point descends vertically from a rim point whose
        /// inside-distance is exactly zero. The value is not invented; it is read from the point it descends
        /// from. This is also the only one of BC Part 5's three candidate rules under which
        /// <c>edgeDistance</c> is CONTINUOUS across the silhouette edge (the top surface's <c>t → 0</c> as the
        /// rim is approached, and the wall's is identically 0), which matters because a discontinuity there
        /// would show as a hard seam in every <c>ByEdgeDistance</c> gradient at the most visible line in the
        /// picture (HS-6.3).
        /// </summary>
        public float edgeDistance;

        /// <summary>
        /// The crossing's height above the layer's base plane, canvas pixels (BC-3.3 #2, "0 = base plane").
        ///
        /// On a wall this is the vertical position ON the wall — which is exactly why HS-6.3 rejects the
        /// competing rule "the wall carries its own top-to-bottom parameterisation": that rule wants a number
        /// this field already publishes, exactly and in the right units, and would have made
        /// <c>edgeDistance</c> mean two different things on two surfaces of one solid to get it.
        /// </summary>
        public float height;
    }

    /// <summary>One layer of a resolvable scene: a compiled shape field, its stack, and its height stage.</summary>
    public sealed class ShaperResolveLayer
    {
        /// <summary>The compiled shape tree. Its declared <see cref="ShaperProgram.bound"/> is the ONLY bound the march uses.</summary>
        public ShaperProgram field;

        /// <summary>A value stack for <see cref="field"/>. Owned here so a query allocates nothing.</summary>
        public float[] stack;

        /// <summary>The compiled height stage, including its <see cref="ShaperHeightOp.baseZ"/> (HS-7.2).</summary>
        public ShaperHeightOp height;

        /// <summary>Mirrors <see cref="ShaperLayer.enabled"/>. A disabled layer is skipped entirely.</summary>
        public bool enabled = true;
    }

    /// <summary>
    /// A scene the resolve can be run against: an ordered layer list plus the scratch a query needs, so that
    /// <see cref="ShaperResolve.Query"/> allocates NOTHING beyond the caller's crossing buffer.
    /// </summary>
    public sealed class ShaperResolveScene
    {
        /// <summary>Ordered bottom-most first, mirroring <see cref="ShaperDocument.layers"/>. Never reordered.</summary>
        public readonly List<ShaperResolveLayer> layers = new List<ShaperResolveLayer>();

        /// <summary>
        /// HS-5.5's slab boundaries, rebuilt per layer per query into this one array. Sized at
        /// <see cref="ShaperHeight.MaxBreakpoints"/> for the "both stepped" case, which is the one to size for.
        /// </summary>
        public readonly float[] breakpoints = new float[ShaperHeight.MaxBreakpoints];

        /// <summary>Add a layer and return it.</summary>
        public ShaperResolveLayer Add(ShaperProgram field, in ShaperHeightOp height)
        {
            var l = new ShaperResolveLayer { field = field, stack = field != null ? field.NewStack() : new float[1], height = height };
            layers.Add(l);
            return l;
        }
    }

    /// <summary>The outcome of one <see cref="ShaperResolve.Query"/>.</summary>
    public struct ShaperResolveResult
    {
        /// <summary>How many entries of the caller's buffer were written, ascending in <see cref="ShaperCrossing.rayT"/>.</summary>
        public int count;

        /// <summary>Which implementation ran. HS-9.2, and the thing HS-9.5's conformance check asserts.</summary>
        public ShaperResolveBranch branch;

        /// <summary>True when the caller's buffer filled and further crossings were dropped.</summary>
        public bool truncated;

        /// <summary>
        /// <b>T-0109 FIX F1 — a DIAGNOSTIC, not a failure.</b> How many times the adaptive bracket ran out of
        /// subdivision depth (<see cref="ShaperResolve.MaxBracketDepth"/>) without proving the point either
        /// empty or solid, and fell back to sampling at <see cref="ShaperResolve.SurfaceResolution"/>.
        ///
        /// Non-zero means the accuracy guarantee for those steps is the resolution one ("no feature of
        /// ray-extent ≥ SurfaceResolution is missed") rather than the exact one. It is reported rather than
        /// swallowed because the pre-fix code did the opposite: it fell back on EVERY step, silently, and
        /// lost whole features.
        ///
        /// <b>T-0109 FIX V4 — zero does NOT mean every step was a proof, and this comment used to say it did.</b>
        /// The bracket loop leaves by <c>if (sigma &lt;= SurfaceResolution) break;</c> as well as by exhausting
        /// <see cref="MaxBracketDepth"/>, and only the second is counted here. A step that narrowed to
        /// <c>SurfaceResolution</c> without ever closing the ambiguous shell is a SAMPLED step, not a proved
        /// one, and it increments nothing. Measured live: a ray at Stepped n = 27 reports
        /// <c>bracketCapped = 0</c> while stepping straight over a genuine 0.0154 px air sliver between two
        /// treads — correct under the declared guarantee, but not a proof. If a caller ever needs "was this
        /// answer exact?", a separate resolution-floored counter is what it would have to read.
        /// </summary>
        public int bracketCapped;

        /// <summary>
        /// T-0109 FIX F1 — true when a slab's inner loop hit <see cref="ShaperResolve.MaxStepsPerSlab"/> and
        /// stopped before reaching the slab's far end. Crossings beyond that point were not looked for. A
        /// grazing ray on a pathological field is the case that can do it.
        /// </summary>
        public bool stepsExhausted;
    }

    /// <summary>
    /// BC-2.2's general resolve query. HS-9.
    ///
    /// <b>The API is the general one from day one.</b> Given a ray it reports EVERY layer the ray passes
    /// through and, for each surface it crosses, that surface's depth and the local coordinates of the
    /// crossing point on that layer. Every layer, not the front-most — which is what makes an occluded-outline
    /// pass fall out for free later instead of needing a second sweep (BC-2.2).
    ///
    /// <b>Two implementations behind that one API, chosen by the RAY and never by the caller</b> (HS-9.2,
    /// BC-2.3). When the direction is the canvas <c>−Z</c> axis to within tolerance the closed form runs:
    /// <c>t</c> is fixed for the sample, <c>G(t)</c> is evaluated once, the crossing is
    /// <c>base + body·G(t)</c> directly — algebraically the same point sample the tool performs today, at the
    /// same cost, no march. Otherwise the slab march of HS-5.3 runs. The production path in Wave 2 only ever
    /// takes the first; the second exists, is exercised, and is what HS-9.5's tilted frame proves.
    ///
    /// <b>The march reads no profile slope anywhere</b> — RULING ONE (HS-0.1). Because <c>G</c> is monotone
    /// (HS-5.1) every horizontal cross-section of the solid is a LEVEL SET of the shape's own field offset by
    /// a constant, and offsetting a signed distance field by a constant preserves its declared bound exactly
    /// (the same result <see cref="ShaperBound.Shell"/> and <c>BORDER-CONTRACT.md:108</c> already rest on). So
    /// the marcher steps on <see cref="ShaperProgram.bound"/> — finite, already proven by T-0105's measurement
    /// — and the constant that does not exist for seven of the twelve techniques is never needed.
    ///
    /// <b>Deferred, explicitly</b> (HS-9.4): no acceleration structure, no camera object, no perspective, no
    /// shadow rays, and no exposure of tilt in any UI. The march exists to be correct, not to be fast.
    ///
    /// Allocates nothing per query beyond the caller-supplied crossing buffer.
    /// </summary>
    public static class ShaperResolve
    {
        /// <summary>
        /// <b>DEPRECATED by T-0109 FIX V3 and no longer read by the branch test, which now requires EXACT
        /// axis alignment.</b> It was how far off the <c>−Z</c> axis a normalised direction could be and
        /// still take the closed form, on the reading that the tolerance was "a float-noise allowance and
        /// nothing more". That reading was wrong, and the reason it was wrong is the whole point of HS-0.1.
        ///
        /// The closed form is not an approximation of the march; it is a different formula, exact only ON the
        /// axis. Off the axis, the z error it induces is the profile's height change over the lateral drift —
        /// and seven of the twelve techniques have an UNBOUNDED <c>dG/dd</c>, so there is no lateral
        /// tolerance, however small, for which that error is bounded. Measured over 1008 branch-paired rays
        /// the general branch was wrong 0 times and this branch 101 times, worst <b>4.7018 px</b>
        /// (Flat + Ogee at x = −190: 150.0000 against a true 154.7018). The verifier's own sweep shows the
        /// deficit shrinking as roughly the CUBE ROOT of the tolerance — 1e-1 → 149.5 px, 1e-2 → 83.0,
        /// 1e-3 → 31.2, 1e-4 → 10.4, 2e-5 → 4.7 — which is the signature of an unbounded slope, not of
        /// convergence. A smaller constant would have bought three more decimal places and kept the defect.
        ///
        /// This costs the production path nothing: the straight-down renderer builds its direction as exactly
        /// <c>(0, 0, −1)</c>, and normalising it leaves <c>dx</c> and <c>dy</c> at exactly zero.
        /// </summary>
        public const float StraightDownTolerance = 1e-5f;

        /// <summary>
        /// <b>DEPRECATED by T-0109 FIX F1 and no longer read by the march.</b> It was the number of samples
        /// the march took across a slab once it was inside the containing prism, applied as a step FLOOR —
        /// <c>if (safe > step) step = safe</c> — so the provably-safe skip could only ever make a step BIGGER
        /// and never smaller. Because <c>a1 − a0</c> is the ray's parameter length through the slab and not
        /// the slab's Z extent, that floor was 50 canvas pixels on a 400 px plate, and 16 of 32 measured
        /// configurations lost a whole crossing pair — the largest feature lost being 46.2 px against a
        /// 57.7 px step, which then made <see cref="Depth"/> report 400.000 where the true solid was 380.000,
        /// exactly the envelope answer HS-6.6 forbids.
        ///
        /// The constant is kept only because the audit prints it; <see cref="SurfaceResolution"/> is what
        /// governs accuracy now, and it is an ABSOLUTE tolerance rather than a fraction of the ray's length.
        /// </summary>
        public const int SlabSamples = 8;

        /// <summary>
        /// <b>T-0109 FIX F1 — the march's one remaining resolution parameter, and the whole of what its
        /// accuracy now depends on.</b> Canvas pixels.
        ///
        /// Every step the march takes is either PROVABLY safe (the point is outside the containing prism, or
        /// inside the contained one, for the whole of the step) or is at most this long. So the guarantee is
        /// mechanical and statable: <b>no feature whose extent ALONG THE RAY is at least
        /// <c>SurfaceResolution</c> can be missed</b>, because any interval not proven empty-or-solid is
        /// sampled at no coarser than this spacing. It does not depend on the slab count, on
        /// <see cref="ShaperHeight.SlabQuality"/>, on the ray's clipped length, or on which technique is
        /// authored.
        /// </summary>
        public const float SurfaceResolution = 0.02f;

        /// <summary>
        /// How many times the march may halve its candidate step trying to close the ambiguous shell between
        /// the containing and contained prisms, before it accepts <see cref="SurfaceResolution"/> and samples.
        /// Halving 12 times from a full slab takes a 400 px slab to 0.1 px, so the cap is generous; when it
        /// IS hit the result reports it on <see cref="ShaperResolveResult.bracketCapped"/> rather than
        /// silently truncating anything.
        /// </summary>
        public const int MaxBracketDepth = 24;

        /// <summary>Halvings used to locate a surface once a step has straddled it. HS-5.4.</summary>
        public const int SurfaceIterations = 34;

        /// <summary>
        /// A hard stop on the inner loop, so a pathological field can waste time but never hang. Raised from
        /// 4096 by T-0109 FIX F1: removing the step floor means a ray that grazes a surface for its whole
        /// length now takes steps of <see cref="SurfaceResolution"/> rather than of <c>length/8</c>. Hitting
        /// it is reported on <see cref="ShaperResolveResult.stepsExhausted"/>.
        /// </summary>
        public const int MaxStepsPerSlab = 1 << 17;

        /// <summary>
        /// HS-9.1 — the general query.
        ///
        /// <paramref name="crossings"/> is caller-owned and caller-sized; nothing else is allocated. The
        /// direction is normalised internally, so <see cref="ShaperCrossing.rayT"/> is in canvas pixels
        /// whatever length the caller passed.
        ///
        /// Crossings are returned ascending in <see cref="ShaperCrossing.rayT"/> across ALL layers, so a
        /// consumer walking the list walks the ray. A ray whose ORIGIN is already inside a layer's solid gets
        /// an entering crossing at <c>rayT = 0</c>, so entries and exits always pair up.
        /// </summary>
        public static ShaperResolveResult Query(ShaperResolveScene scene,
                                                float ox, float oy, float oz,
                                                float dx, float dy, float dz,
                                                ShaperCrossing[] crossings)
        {
            var result = new ShaperResolveResult { count = 0, branch = ShaperResolveBranch.General, truncated = false,
                                                   bracketCapped = 0, stepsExhausted = false };
            if (scene == null || crossings == null || crossings.Length == 0) return result;

            float len = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(len > 1e-12f) || float.IsNaN(len)) return result;
            float inv = 1f / len;
            dx *= inv; dy *= inv; dz *= inv;

            // THE BRANCH IS CHOSEN BY THE RAY. BC-2.3: a caller must not be able to tell from the shape of
            // what it calls that only one direction is fast.
            //
            // T-0109 FIX V3: EXACTLY axis-aligned, not nearly. See StraightDownTolerance's own summary for
            // why no non-zero tolerance can be made safe — the induced z error is governed by dG/dd, which is
            // unbounded for seven of the twelve techniques, so the error does not go to zero with the
            // tolerance in any useful way. Exact zeros are what the straight-down renderer actually supplies.
            bool straightDown = dz < 0f && dx == 0f && dy == 0f;
            result.branch = straightDown ? ShaperResolveBranch.StraightDown : ShaperResolveBranch.General;

            int count = 0;
            bool truncated = false;
            int bracketCapped = 0;
            bool stepsExhausted = false;

            for (int li = 0; li < scene.layers.Count; li++)
            {
                ShaperResolveLayer L = scene.layers[li];
                if (L == null || !L.enabled || L.field == null || L.stack == null) continue;

                ShaperHeightOp op = L.height;

                // HS-2.2 / HS-9: a zero-thickness layer publishes height 0 and no wall, and the resolve skips
                // it entirely. It is not a degenerate case to be handled — there is no solid to cross.
                if (!op.present || op.body <= 0f) continue;

                if (straightDown)
                    QueryStraightDown(L, li, ox, oy, oz, crossings, ref count, ref truncated);
                else
                    QueryGeneral(scene, L, li, ox, oy, oz, dx, dy, dz, crossings, ref count, ref truncated,
                                 ref bracketCapped, ref stepsExhausted);
            }

            SortByRayT(crossings, count);
            result.count = count;
            result.truncated = truncated;
            result.bracketCapped = bracketCapped;
            result.stepsExhausted = stepsExhausted;
            return result;
        }

        // ── HS-9.2, branch one: the closed form ──────────────────────────────────────────────────────────

        /// <summary>
        /// The <c>−Z</c> closed form. <c>t</c> is fixed for the sample, <c>G(t)</c> is evaluated ONCE, and the
        /// crossings are <c>base + body·G(t)</c> and <c>base</c> directly. No march, no bisection, no
        /// breakpoints — algebraically the same point sample the tool already performs and at the same cost.
        ///
        /// Both crossings are <see cref="ShaperSurfaceKind.Cap"/>, always: a wall is exactly parallel to this
        /// ray, so it has zero screen area and cannot be hit. That is HS-6.5's reason the wall RULE can be
        /// decided now and its rendering deferred — and the reason HS-9.5's tilted frame is the only thing in
        /// Wave 2 that exercises the ruling at all.
        /// </summary>
        static void QueryStraightDown(ShaperResolveLayer L, int li,
                                      float ox, float oy, float oz,
                                      ShaperCrossing[] crossings, ref int count, ref bool truncated)
        {
            ShaperHeightOp op = L.height;

            float d = ShaperEvaluator.Distance(L.field, ox, oy, L.stack);
            if (d > 0f || ShaperField.IsEmpty(d)) return;

            float t = ShaperHeight.T(op, d);
            float nx = 0f, ny = 0f;
            if (op.technique == ShaperExtrusionTechnique.Linear)
                ShaperHeight.LocalNormalised(op, ox, oy, out nx, out ny);

            float h = op.body * ShaperHeight.Composed(op, t, nx, ny);

            // T-0109 FIX N3, this branch's half. G(t) = 0 means no solid here — see Member. Without this the
            // closed form emits sTop == sBottom, a crossing pair of width exactly zero, wherever a Stepped
            // profile's or bevel's dead band reaches the sample; the two branches would then disagree about
            // the same solid, which is exactly what BC-2.3 forbids.
            if (!(h > 0f)) return;

            // z(s) = oz − s, so s = oz − z.
            float sTop = oz - (op.baseZ + h);
            float sBottom = oz - op.baseZ;

            if (sBottom <= 0f) return;                       // the whole layer is behind the ray origin
            if (sTop < 0f) sTop = 0f;                        // the origin is inside: enter at rayT = 0

            Emit(crossings, ref count, ref truncated, li, sTop, true, ShaperSurfaceKind.Cap, ox, oy, d, h);
            Emit(crossings, ref count, ref truncated, li, sBottom, false, ShaperSurfaceKind.Cap, ox, oy, d, 0f);
        }

        // ── HS-9.2, branch two: the slab march of HS-5.3 ─────────────────────────────────────────────────

        /// <summary>
        /// The general branch. Partitions the layer's Z extent by <see cref="ShaperHeight.Breakpoints"/>,
        /// steps conservatively through each slab using the SHAPE'S OWN declared bound and the fixed
        /// containing prism <c>d ≤ −τ_min·span</c>, and locates every surface by BISECTION on the EXACT
        /// membership predicate (HS-5.4).
        ///
        /// <b>Why a conservative <c>τ_min</c> is safe.</b> Because <c>G</c> is monotone the cross-sections are
        /// nested and shrink upward, so the whole solid within a slab is contained in the single prism taken
        /// at the slab's LOWEST <c>ζ</c> — a fixed set for the whole slab, independent of how <c>z</c> varies
        /// inside it. An over-large prism can only make the marcher stop EARLY, never late, and the bisection
        /// then finds the true surface.
        ///
        /// <b>T-0109 FIX F1 — the step is a TWO-SIDED BRACKET with ADAPTIVE SUBDIVISION, and there is no
        /// resolution floor.</b> What shipped before applied <c>resStep = (a1−a0)/SlabSamples</c> as a step
        /// FLOOR: <c>if (safe > step) step = safe</c>, so the provably-safe skip could only ever make a step
        /// LARGER. Near a surface, where <c>safe → 0</c>, the march took <c>resStep</c> regardless — 50 canvas
        /// pixels on a 400 px plate, because <c>a1 − a0</c> is the ray's PARAMETER LENGTH through the slab and
        /// not the slab's Z extent — and anything thinner than that along the ray was jumped clean over with
        /// no flip seen and no bisection triggered. Measured: 16 of 32 configurations lost a crossing pair,
        /// the largest lost feature 46.2 px against a 57.7 px step, and <see cref="Depth"/> consequently
        /// reported 400.000 where the true solid was 380.000 — the outer envelope, which HS-6.6 rejects by
        /// name.
        ///
        /// The hole the floor was papering over is real and is in HS-5.3: its step expression has every term
        /// negative once the point is inside the containing prism AND inside the slab, so it does not define
        /// a step there. It is closed here, not floored over:
        /// <list type="number">
        /// <item>For a candidate step <c>σ</c>, form the <c>ζ</c> window the ray can reach within <c>σ</c>,
        /// and (for <c>Linear</c> only) the <c>E</c> window it can reach, from
        /// <see cref="ShaperHeightOp.linearEGrad"/>.</item>
        /// <item>Take BOTH prisms over that window: <c>τ_min = Ginv(ζ_lo)</c> at the most permissive <c>E</c>,
        /// which CONTAINS every solid point of the window, and <c>τ_max = Ginv(ζ_hi)</c> at the least
        /// permissive <c>E</c>, which is CONTAINED in the solid throughout it. Monotonicity (HS-5.1) is what
        /// makes both true.</item>
        /// <item>Outside prism(<c>τ_min</c>): provably air for the whole of <c>σ</c> — step by the
        /// conservative distance, computed from <see cref="ShaperProgram.bound"/> and never floored. Inside
        /// prism(<c>τ_max</c>): provably solid — step by the mirrored distance.</item>
        /// <item>Between them is the ambiguous shell, of width exactly <c>(τ_max − τ_min)·span</c> in
        /// <c>d</c>. HALVE <c>σ</c> and re-bracket: a smaller <c>σ</c> gives a narrower <c>ζ</c> window and
        /// therefore a narrower shell. Stop at <see cref="SurfaceResolution"/>, where the residual ambiguity
        /// is below the tolerance and the sample-plus-bisection resolves it; if the depth cap is reached,
        /// report it on <see cref="ShaperResolveResult.bracketCapped"/> — never truncate silently.</item>
        /// </list>
        /// For <c>Flat</c>/<c>None</c> the two prisms COINCIDE (<c>τ_min = τ_max = 0</c>), the shell is empty,
        /// and the march degenerates to the shape's own sphere-trace — exact, which is why the slotted-plate
        /// reproduction that lost 2 of 4 crossings now returns all four and <see cref="Depth"/> returns
        /// 380.000.
        ///
        /// <b>What is now true of HS-5.4 and HS-5.5, stated honestly.</b> Accuracy no longer depends on the
        /// slab count OR on the ray's clipped length; it depends on <see cref="SurfaceResolution"/>, an
        /// absolute canvas-pixel tolerance. "Correctness holds for any slab count ≥ 1" is true in that
        /// qualified sense and is EXACT (not merely tolerance-bounded) whenever the shell is empty, which is
        /// every <c>Flat</c>/<c>Linear</c>-without-a-smooth-bevel case, every horizontal ray, and every
        /// stepped tread. It is not an unconditional claim and must not be written as one.
        ///
        /// <b>No profile slope appears anywhere in this method.</b> The only bound divided by is
        /// <see cref="ShaperProgram.bound"/>.
        /// </summary>
        static void QueryGeneral(ShaperResolveScene scene, ShaperResolveLayer L, int li,
                                 float ox, float oy, float oz, float dx, float dy, float dz,
                                 ShaperCrossing[] crossings, ref int count, ref bool truncated,
                                 ref int bracketCapped, ref bool stepsExhausted)
        {
            ShaperHeightOp op = L.height;
            ShaperProgram field = L.field;

            float zLo = op.baseZ;
            float zHi = op.baseZ + op.body * op.supG;

            // 1 — clip the ray to the layer's Z extent, exactly (a ray-plane intersection, bound 1).
            float s0 = 0f, s1 = float.MaxValue;
            if (Mathf.Abs(dz) > 1e-9f)
            {
                float sa = (zLo - oz) / dz, sb = (zHi - oz) / dz;
                if (sa > sb) { float tmp = sa; sa = sb; sb = tmp; }
                if (sa > s0) s0 = sa;
                if (sb < s1) s1 = sb;
            }
            else if (oz < zLo || oz > zHi) return;

            // 2 — clip to the layer's XY support box (R7). The containing prism {d ≤ −τ·span} is a subset of
            //     {d ≤ 0}, which is a subset of the support box, so no growth is needed and the clip is
            //     conservative by construction. This is also what makes a horizontal ray's parameter range
            //     finite.
            if (!ClipSlab(ox, dx, field.supportCx - field.supportHalfW, field.supportCx + field.supportHalfW, ref s0, ref s1)) return;
            if (!ClipSlab(oy, dy, field.supportCy - field.supportHalfH, field.supportCy + field.supportHalfH, ref s0, ref s1)) return;
            if (s1 <= s0) return;
            if (s1 > 1e8f) s1 = 1e8f;

            int layerStart = count;

            // 3 — is the ray ALREADY inside at its origin-side end of the clipped range? Everything after
            //     this is a list of FLIPS, and the pairing below reads the parity off this one value.
            bool insideAtStart = Member(L, op, ox, oy, oz, dx, dy, dz, s0);
            if (insideAtStart)
                EmitAt(L, op, li, ox, oy, oz, dx, dy, dz, s0, true, crossings, ref count, ref truncated);

            // 4 — the slabs. HS-5.5.
            int bp = ShaperHeight.Breakpoints(op, scene.breakpoints);
            float dxy = Mathf.Sqrt(dx * dx + dy * dy);
            float bound = field.bound > 0f ? field.bound : 1f;

            float invBody = op.body > 0f ? 1f / op.body : 0f;
            float absDz = Mathf.Abs(dz);
            bool linear = op.technique == ShaperExtrusionTechnique.Linear;
            float eGrad = linear ? op.linearEGrad : 0f;

            // T-0109 FIX N1 — the LAST non-degenerate slab, so its top can stay CLOSED while every interior
            // boundary is half-open. Without this the half-open rule would drop a Z-parallel ray sitting
            // exactly on the layer's top plane (z = base + body·supG) out of every slab, which trades a
            // double-march for a lost march. See ClipSlab's `hiInclusive`.
            int lastSlab = -1;
            for (int k = 0; k + 1 < bp; k++)
                if (scene.breakpoints[k + 1] > scene.breakpoints[k]) lastSlab = k;

            // T-0109 FIX N2 — walk the slabs in RAY order, not in ζ order.
            //
            // The slabs partition Z, so for a DESCENDING ray (dz < 0) the lowest-ζ slab is met LAST, at the
            // largest ray parameter. Iterating in ζ order therefore emitted crossings in roughly DECREASING
            // rayT, and since Emit drops on buffer overflow, the retained list was a SUFFIX of the ray while
            // step 5 stamped parity onto it as though it were a prefix — measured 144 mis-stamped cases and
            // `Depth` over-reporting solid by up to 83.138 px, which is exactly what HS-6.6 forbids.
            //
            // Reversing the traversal makes the whole layer emit monotonically increasing in rayT (step 3 at
            // s0 first, each slab's march from a0 upward, step 4b at s1 last), so a truncated list is a
            // genuine PREFIX in ray order and the parity stamped forward from `insideAtStart` is correct for
            // every element it keeps. BC-2.2 says "report every layer the ray passes through" — passes
            // through, i.e. in the order it passes.
            bool descending = dz < 0f;
            for (int kk = 0; kk + 1 < bp; kk++)
            {
                int k = descending ? (bp - 2 - kk) : kk;

                float zetaLo = scene.breakpoints[k];
                float zetaHi = scene.breakpoints[k + 1];
                if (!(zetaHi > zetaLo)) continue;

                float za = op.baseZ + op.body * zetaLo;
                float zb = op.baseZ + op.body * zetaHi;

                float a0 = s0, a1 = s1;
                if (!ClipSlab(oz, dz, za, zb, ref a0, ref a1, k == lastSlab)) continue;
                if (a1 <= a0) continue;

                // The slab-wide bracket: the containing prism at the slab's lowest ζ and the most permissive
                // E, and the contained prism at its highest ζ and the least permissive E. Both are valid for
                // the WHOLE slab, so they cost one pair of inverses per slab rather than per step, and they
                // resolve every point that is comfortably outside or comfortably inside. The per-step
                // adaptive bracket below runs only for points in the slab-wide shell — which is where the
                // pre-fix code fell through to its resolution floor and lost features.
                float tauMinSlab = ShaperHeight.InverseLowerBound(op, zetaLo);
                if (ShaperHeight.IsNoCrossSection(tauMinSlab)) continue;
                float tauMaxSlab = ShaperHeight.InverseUpperBound(op, zetaHi);
                bool hasInnerSlab = !ShaperHeight.IsNoCrossSection(tauMaxSlab);

                float s = a0;
                bool mPrev = Member(L, op, ox, oy, oz, dx, dy, dz, s);
                int guard = 0;
                float prevStep = a1 - a0;

                while (s < a1)
                {
                    if (guard++ >= MaxStepsPerSlab) { stepsExhausted = true; break; }

                    float px = ox + s * dx, py = oy + s * dy;
                    float d = ShaperEvaluator.Distance(field, px, py, L.stack);
                    float zeta = (oz + s * dz - op.baseZ) * invBody;
                    if (zeta < zetaLo) zeta = zetaLo; else if (zeta > zetaHi) zeta = zetaHi;

                    float eHere = 1f;
                    if (linear)
                    {
                        float lnx, lny;
                        ShaperHeight.LocalNormalised(op, px, py, out lnx, out lny);
                        eHere = ShaperHeight.Profile(op, 0f, lnx, lny);
                    }

                    float remain = a1 - s;
                    float step = -1f;
                    int depth = 0;

                    // ── the cheap slab-wide bracket first, then the adaptive one only if it is ambiguous ──
                    float invBD = dxy > 1e-9f ? 1f / (bound * dxy) : float.MaxValue;
                    {
                        float gOut = d + tauMinSlab * op.span;
                        if (gOut > 0f)
                        {
                            float safe = dxy > 1e-9f ? gOut * invBD : float.MaxValue;
                            step = safe < remain ? safe : remain;
                        }
                        // T-0109 FIX V1: the SOLID skip is taken only while we already know we are inside.
                        // See the per-step mirror below for the full argument; the two skips share one
                        // defect and one gate.
                        else if (hasInnerSlab && mPrev)
                        {
                            float gIn = -d - tauMaxSlab * op.span;
                            if (gIn > 0f)
                            {
                                float safe = dxy > 1e-9f ? gIn * invBD : float.MaxValue;
                                step = safe < remain ? safe : remain;
                            }
                        }
                    }

                    // ── the two-sided bracket, subdivided until it proves something or hits the tolerance ──
                    //
                    // σ starts at twice the LAST accepted step rather than at the whole remaining slab, which
                    // is purely a cost matter and never a correctness one: a smaller σ gives a narrower
                    // window, a tighter bracket and a shorter — therefore still provable — step. Far from any
                    // surface σ doubles back up to the slab in a few steps; hugging a surface it stays small,
                    // so the subdivision loop below almost always exits at depth 0 or 1 instead of walking
                    // all the way down from the slab length on every single step.
                    float sigma = prevStep * 2f;
                    if (sigma > remain) sigma = remain;
                    if (sigma < SurfaceResolution) sigma = SurfaceResolution;
                    for (; step < 0f && depth < MaxBracketDepth; depth++)
                    {
                        // The ζ window the ray can reach within σ, clipped to the slab (which it cannot
                        // leave, because σ ≤ remain and the slab clip already bounds a1).
                        float dzeta = absDz * sigma * invBody;
                        float wLo = zeta - dzeta, wHi = zeta + dzeta;
                        if (wLo < zetaLo) wLo = zetaLo;
                        if (wHi > zetaHi) wHi = zetaHi;

                        // The E window Linear can reach within σ. Zero-width for every other technique,
                        // whose E is a function of t and not of position.
                        float de = eGrad * sigma * dxy;
                        float eLo = eHere - de, eHi = eHere + de;

                        float tauMin = ShaperHeight.InverseAtE(op, wLo, eHi);
                        if (ShaperHeight.IsNoCrossSection(tauMin)) { step = sigma; break; }

                        // OUTSIDE the containing prism: the exact, provably safe empty-space skip. The prism
                        // is a vertical extrusion of {d ≤ −τ_min·span}, so the 2D distance to that set is a
                        // lower bound on the 3D distance to the solid, and dividing by the shape's declared
                        // bound turns the reported distance into a true one (BC-4.2). NEVER floored.
                        float gapOut = d + tauMin * op.span;
                        if (gapOut > 0f)
                        {
                            float safe = dxy > 1e-9f ? gapOut / (bound * dxy) : float.MaxValue;
                            step = safe < sigma ? safe : sigma;
                            break;
                        }

                        // INSIDE the contained prism: the same argument mirrored, skipping SOLID. It needs
                        // the opposite bound — τ_max must be an UPPER bound on Ginv, or on Linear it can call
                        // air solid (T-0109 FIX F1; the pre-fix code used the lower bound for both).
                        //
                        // T-0109 FIX N4: through the VERIFIED-AND-NUDGED form, not raw InverseAtE. F1
                        // hardened the slab-wide τ_max above (`tauMaxSlab`) and left this one — the per-step
                        // bracket, the machinery F1 was written for — calling the closed form directly, which
                        // is short by a few float roundings and so is not an upper bound at all: 2 526
                        // violations in 2 974 632 checks, worst 3.3379e-6.
                        //
                        // T-0109 FIX V1 — and this gate, not the bound, is what the third pass caught. The
                        // prism's CLAIM is correct: every point it calls solid reads solid on a walk of the
                        // interval, and G(τ_max) ≥ ζ_hi holds for every slab. But the prism proves solid with
                        // a NON-STRICT G ≥ ζ while Member applies HS-1.1's STRICT reading, so at a slab
                        // boundary the prism can say SOLID at the very point Member calls AIR. Taking the
                        // skip there jumps from an air sample to another air sample across a whole solid
                        // span: mPrev never flips, no crossing is emitted, and the span vanishes. Measured:
                        // 26 of 7440 rays lost a true crossing, worst gap 26.207 px, worst solid-length
                        // under-report 24.175 px — a 400x400 plate, Stepped n=8, losing [552.382, 576.557]
                        // entirely.
                        //
                        // The skip is entitled to jump; it is not entitled to jump while believing it is
                        // outside. When the prism and Member disagree, that disagreement is itself the signal
                        // that a boundary is AT the current point, so the right move is to decline the skip
                        // and fall through to the bounded step — the next sample then lands inside and the
                        // entry is emitted by the ordinary flip path. Declining costs only speed, and only
                        // on the one step where a boundary is already known to be present.
                        float tauMax = mPrev ? ShaperHeight.InverseAtEUpperBound(op, wHi, eLo)
                                             : ShaperHeight.NoCrossSection;
                        if (!ShaperHeight.IsNoCrossSection(tauMax))
                        {
                            float gapIn = -d - tauMax * op.span;
                            if (gapIn > 0f)
                            {
                                float safe = dxy > 1e-9f ? gapIn / (bound * dxy) : float.MaxValue;
                                step = safe < sigma ? safe : sigma;
                                break;
                            }
                        }

                        // The ambiguous shell, of width (τ_max − τ_min)·span in d. Halving σ narrows the ζ
                        // and E windows and therefore narrows the shell.
                        if (sigma <= SurfaceResolution) break;
                        sigma *= 0.5f;
                        if (sigma < SurfaceResolution) sigma = SurfaceResolution;
                    }

                    if (depth >= MaxBracketDepth) bracketCapped++;

                    // A step is either a proof or is at most SurfaceResolution long. That single sentence is
                    // the accuracy guarantee, and it is what the pre-fix floor destroyed.
                    if (!(step > SurfaceResolution)) step = SurfaceResolution;
                    prevStep = step;

                    float sNext = s + step;
                    if (sNext > a1) sNext = a1;

                    bool m = Member(L, op, ox, oy, oz, dx, dy, dz, sNext);
                    if (m != mPrev)
                    {
                        float hit = BisectSurface(L, op, ox, oy, oz, dx, dy, dz, s, mPrev, sNext);
                        EmitAt(L, op, li, ox, oy, oz, dx, dy, dz, hit, m, crossings, ref count, ref truncated);
                        mPrev = m;
                    }

                    if (sNext <= s) break;
                    s = sNext;
                }
            }

            // 4b — the mirror of step 3, and its absence was a real defect the audit caught (H6).
            //
            // The march only ever reports FLIPS strictly inside the clipped range, so a ray still inside the
            // solid at s1 had its exit silently dropped — and s1 is the BASE PLANE for any downward ray,
            // which is where every ordinary crossing pair ends. The straight-down branch emits that exit
            // explicitly; this branch must too, or the two disagree by exactly one crossing on every sample
            // and entries stop pairing with exits.
            if (Member(L, op, ox, oy, oz, dx, dy, dz, s1))
                EmitAt(L, op, li, ox, oy, oz, dx, dy, dz, s1, false, crossings, ref count, ref truncated);

            // 5 — order this layer's flips and stamp the entering/exiting parity onto them. Membership at s0
            //     is known, and every flip toggles it, so the parity is not a guess.
            SortByRayT(crossings, count, layerStart);
            bool inside = insideAtStart;
            for (int i = layerStart; i < count; i++)
            {
                if (i == layerStart && insideAtStart) { inside = true; continue; }   // the origin-inside entry
                inside = !inside;
                var c = crossings[i];
                c.entering = inside;
                crossings[i] = c;
            }
        }

        // ── the exact membership predicate, HS-5.4 ───────────────────────────────────────────────────────

        /// <summary>
        /// HS-5.4 — membership is EXACT and O(1): evaluate <c>d</c>, form <c>t</c>, evaluate <c>G(t)</c>,
        /// compare with <c>ζ</c>. It is HS-1.1's set definition read literally and nothing else — including,
        /// since T-0109 FIX N3, HS-1.1's <c>G &gt; 0</c> conjunct.
        ///
        /// This is what makes the whole scheme robust to a conservative prism: the march's only job is to skip
        /// empty space without overshooting, and the moment a step lands on the other side of this predicate
        /// the bisection takes over.
        /// </summary>
        static bool Member(ShaperResolveLayer L, in ShaperHeightOp op,
                           float ox, float oy, float oz, float dx, float dy, float dz, float s)
        {
            float z = oz + s * dz;
            float above = z - op.baseZ;
            if (above < 0f) return false;
            if (above > op.body * op.supG) return false;

            float px = ox + s * dx, py = oy + s * dy;
            float d = ShaperEvaluator.Distance(L.field, px, py, L.stack);
            if (d > 0f || ShaperField.IsEmpty(d)) return false;

            float t = ShaperHeight.T(op, d);
            float nx = 0f, ny = 0f;
            if (op.technique == ShaperExtrusionTechnique.Linear)
                ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);

            // T-0109 FIX N3 — where the surface height is ZERO there is NO SOLID, not a zero-thickness skirt.
            //
            // HS-1.1's set read literally is `0 ≤ above ≤ body·G(t)`, and at G(t) = 0 that degenerates to the
            // single plane `z = base`. For a point that would be harmless. But `G` is zero over a BAND, not a
            // point: a Stepped extrusion has E(t) = 0 for the whole band t < 1/n, i.e. a ring of width span/n
            // around the entire silhouette, and a Stepped bevel does the same at the rim. So the closed set
            // gave the layer a zero-thickness membrane over a two-dimensional region, and every tilted ray
            // whose base-plane exit landed on it emitted an entering flip from the march and an exiting flip
            // from step 4b at the SAME rayT — a phantom crossing pair of width exactly 0.000, on all six
            // Stepped combinations (42 of 42 extras in H6's own fixture).
            //
            // The rule is `G(t) > 0`, and it is the same rule Query already applies to `op.body <= 0` one
            // level up (HS-2.2: "a zero-thickness layer publishes height 0 and no wall … there is no solid to
            // cross"). This is that sentence applied POINTWISE, which is where it was always true.
            //
            // It changes no answer anywhere G > 0, i.e. nowhere any surface is rendered; it only stops the
            // resolve manufacturing zero-measure spans. QueryStraightDown carries the mirror of this guard so
            // BC-2.3's two branches keep agreeing.
            float g = ShaperHeight.Composed(op, t, nx, ny);
            if (!(g > 0f)) return false;

            return above <= op.body * g;
        }

        /// <summary>Bisection on the exact predicate between a known-<paramref name="mLo"/> point and its opposite.</summary>
        static float BisectSurface(ShaperResolveLayer L, in ShaperHeightOp op,
                                   float ox, float oy, float oz, float dx, float dy, float dz,
                                   float lo, bool mLo, float hi)
        {
            for (int i = 0; i < SurfaceIterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Member(L, op, ox, oy, oz, dx, dy, dz, mid) == mLo) lo = mid; else hi = mid;
            }
            return hi;
        }

        // ── emitting ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build and append the crossing at ray parameter <paramref name="s"/>, classifying the surface.
        ///
        /// <b>The classification is mechanical, not heuristic.</b> The solid's boundary has exactly three
        /// parts (HS-1.1): the base plane <c>z = base</c>, the top surface <c>z = base + body·G</c>, and the
        /// vertical silhouette side. A crossing on either horizontal face is a <see cref="ShaperSurfaceKind.Cap"/>;
        /// anything left is the <see cref="ShaperSurfaceKind.Wall"/>, and HS-6.1 then applies:
        /// <c>edgeDistance ≡ 0</c> and <c>t = 0</c>, read from the rim point it descends from rather than
        /// invented.
        ///
        /// HS-6.2's wall NORMAL — <c>normalize((∂d/∂x, ∂d/∂y, 0))</c>, the silhouette gradient with a zero Z
        /// component — is not carried on a crossing because nothing in Wave 2 shades a crossing; the ruling is
        /// recorded on the type and in <see cref="ShaperSurfaceKind.Wall"/>, and the two consequences LR-9.3
        /// predicts (rim saturates on a wall, and the diffuse term differs in sign from the top face for a
        /// light above the plane) are correct behaviour rather than defects: a vertical wall genuinely IS lit
        /// differently from a horizontal cap, and the alternative would be to lie about the geometry.
        /// </summary>
        static void EmitAt(ShaperResolveLayer L, in ShaperHeightOp op, int li,
                           float ox, float oy, float oz, float dx, float dy, float dz,
                           float s, bool entering,
                           ShaperCrossing[] crossings, ref int count, ref bool truncated)
        {
            float px = ox + s * dx, py = oy + s * dy, pz = oz + s * dz;
            float d = ShaperEvaluator.Distance(L.field, px, py, L.stack);
            float t = ShaperHeight.T(op, d);
            float nx = 0f, ny = 0f;
            if (op.technique == ShaperExtrusionTechnique.Linear)
                ShaperHeight.LocalNormalised(op, px, py, out nx, out ny);

            float surface = op.body * ShaperHeight.Composed(op, t, nx, ny);
            float above = pz - op.baseZ;

            float zTol = Mathf.Max(1e-3f, 1e-3f * op.body);
            ShaperSurfaceKind kind;
            if (Mathf.Abs(above) <= zTol) kind = ShaperSurfaceKind.Cap;             // the base plane
            else if (Mathf.Abs(above - surface) <= zTol) kind = ShaperSurfaceKind.Cap;  // the top surface
            else kind = ShaperSurfaceKind.Wall;                                     // nothing else is left

            Emit(crossings, ref count, ref truncated, li, s, entering, kind, px, py,
                 kind == ShaperSurfaceKind.Wall ? 0f : d, above);
        }

        static void Emit(ShaperCrossing[] crossings, ref int count, ref bool truncated,
                         int layer, float rayT, bool entering, ShaperSurfaceKind kind,
                         float lx, float ly, float edgeDistance, float height)
        {
            if (count >= crossings.Length) { truncated = true; return; }
            crossings[count++] = new ShaperCrossing
            {
                layer = layer,
                rayT = rayT,
                entering = entering,
                kind = kind,
                localX = lx,
                localY = ly,
                edgeDistance = edgeDistance,
                height = height,
            };
        }

        // ── HS-6.6, the sibling question BC-3.5 bundles with the wall ────────────────────────────────────

        /// <summary>
        /// HS-6.6 — a layer's <c>depth</c> is the <b>SUM of its entry-to-exit spans</b>, not the outer
        /// envelope and not the first span.
        ///
        /// A ray crossing several disjoint spans of one layer is ordinary the moment a subtractive combine or
        /// a hollow-out is authored. <c>depth</c> is defined as "the thickness of solid the sampling ray
        /// traverses" (BC-3.5), and a ray through a hollow shell traverses two walls' worth of solid and no
        /// air — so reporting the envelope would report solid where there is none.
        ///
        /// The resolve reports every span separately, so a consumer wanting "first span" or
        /// "first-entry-to-last-exit" computes either from the crossing list without a second query. This
        /// helper is the sum because the sum is the DEFINED one.
        /// </summary>
        public static float Depth(ShaperCrossing[] crossings, int count, int layer)
        {
            if (crossings == null) return 0f;
            float total = 0f, open = 0f;
            bool inside = false;
            for (int i = 0; i < count; i++)
            {
                if (crossings[i].layer != layer) continue;
                if (crossings[i].entering)
                {
                    if (!inside) { open = crossings[i].rayT; inside = true; }
                }
                else if (inside)
                {
                    total += crossings[i].rayT - open;
                    inside = false;
                }
            }
            return total;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ray-vs-slab clip on one axis, exact. Returns false when the ray misses the slab entirely.
        ///
        /// <paramref name="hiInclusive"/> governs the PARALLEL branch only, and exists for T-0109 FIX N1.
        /// Adjacent Z slabs share their boundary exactly — slab <c>k</c>'s <c>zb</c> and slab <c>k+1</c>'s
        /// <c>za</c> are the same expression — so a ray with <c>|dz| &lt; 1e-9</c> sitting exactly on an
        /// interior breakpoint was accepted by BOTH, both marched the whole clipped range, and both emitted
        /// the SAME flips. The parity stamp then toggled on every duplicate and <c>Depth</c> summed two
        /// zero-length spans: measured <c>Depth 0.000</c> against a true 399.609 canvas px, on 530 of 552
        /// breakpoint rays across 40 of 42 profile × bevel combinations.
        ///
        /// Passing <c>false</c> makes the interval half-open <c>[lo, hi)</c> so a parallel ray belongs to
        /// EXACTLY ONE slab. The caller passes <c>true</c> for the last slab (and for the XY support-box
        /// clips, which are outer extents with no neighbour to share with), which is what keeps the outer
        /// ends of the Z extent closed rather than trading a doubled march for a lost one.
        /// </summary>
        static bool ClipSlab(float o, float d, float lo, float hi, ref float s0, ref float s1, bool hiInclusive = true)
        {
            if (Mathf.Abs(d) < 1e-9f) return o >= lo && (hiInclusive ? o <= hi : o < hi);
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { float t = a; a = b; b = t; }
            if (a > s0) s0 = a;
            if (b < s1) s1 = b;
            return s1 > s0;
        }

        /// <summary>Insertion sort by ray parameter over <c>[from, count)</c>. No allocation; the lists are short.</summary>
        static void SortByRayT(ShaperCrossing[] c, int count, int from = 0)
        {
            for (int i = from + 1; i < count; i++)
            {
                ShaperCrossing key = c[i];
                int j = i - 1;
                while (j >= from && c[j].rayT > key.rayT) { c[j + 1] = c[j]; j--; }
                c[j + 1] = key;
            }
        }
    }
}
