using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// One resolved border: the dials of a <see cref="ShaperBorderDef"/> after
    /// <see cref="ShaperValue.Sample"/> has been applied once, at compile time.
    ///
    /// It exists so that the width is sampled EXACTLY ONCE per compile and then carried, rather than sampled
    /// again by every stage that needs it. Two reads of one <c>ZUIValue</c> is not merely wasteful: a
    /// <c>MinMax</c> dial draws from a hash and an <c>Oscillation</c> dial from a phase, so a second read at a
    /// different site is a second, differently-seeded number, and the strip would then be a different width from
    /// the dilation that publishes it.
    /// </summary>
    public struct ShaperResolvedBorder
    {
        /// <summary>False when there is no border at all: null, disabled, or width ≤ 0 (BD-1.5).</summary>
        public bool live;
        /// <summary>The resolved TOTAL thickness in canvas pixels (BD-1.4). Always &gt; 0 when <see cref="live"/>.</summary>
        public float width;
        /// <summary>BD-2.1 outward reach: <c>w</c> Outward, <c>w/2</c> Straddling, <c>0</c> Inward.</summary>
        public float reach;
        public ShaperShellAlignment alignment;
        /// <summary>BD-2.3. The dial as authored; the JOIN only actually happens when <see cref="reach"/> &gt; 0 too.</summary>
        public bool joinsCoverage;

        /// <summary>True when this border actually dilates its node's published field (BD-2.2).</summary>
        public bool Joins => live && joinsCoverage && reach > 0f;

        public static readonly ShaperResolvedBorder Nothing = default;
    }

    /// <summary>
    /// The border stage's geometry: how a strip is derived from a node's field, and what joining does to what
    /// the rest of the tree sees. BORDER-CONTRACT Parts B1 and B2.
    ///
    /// <b>A border is derived from the node's signed DISTANCE, not from its coverage (BD-1.2).</b> Both B3 and
    /// C5 of the design say a border "traces that node's coverage"; in effect that is what happens, in mechanism
    /// it is not, and the difference is the whole reason this stage is better than the one it replaces. Three
    /// reasons, and the first is decisive on its own:
    ///
    /// <list type="number">
    /// <item><b>Outward is impossible from coverage.</b> Coverage is <c>1 − smoothstep(−h, +h, d)</c>
    /// (<see cref="ShaperField.Coverage"/>) and is IDENTICALLY ZERO more than <c>h</c> outside the boundary.
    /// Every point of an outward strip beyond half a pixel is in that dead zone. An implementation reading
    /// coverage must therefore reconstruct an exterior distance, and the only way to reconstruct one is a
    /// distance transform over the whole canvas — which is exactly what Pyre does
    /// (<c>PyreRenderer.cs:958-985</c>) and exactly why Pyre's border cannot go outward
    /// (<c>PyreRenderer.cs:1021</c>, <c>if (fa == 0) continue;</c>).</item>
    /// <item><b>Coverage measures from the wrong place.</b> Pyre seeds its transform at pixels whose alpha is
    /// EXACTLY zero. On any shape with a soft edge every pixel of the fade has alpha above zero, so the distance
    /// origin is the outer end of the fade rather than the visual boundary, and the whole band lands inside the
    /// fade where the rim is then multiplied by a small coverage and washes out. That defect is not fixable
    /// inside a coverage-reading design; it disappears entirely in a distance-reading one.</item>
    /// <item><b>Distance is already there.</b> <see cref="ShaperEvaluator.FillTile"/> writes the signed distance
    /// and the coverage into two arrays in the same pass, and the resolver already keeps a per-owner
    /// <c>ownDistance</c> sheet because a <c>ByEdgeDistance</c> gradient needs it. Nothing new is computed.</item>
    /// </list>
    ///
    /// <b>The strip field is the EXISTING Shell operator, exactly (BD-1.3).</b> Given the node's distance
    /// <c>d</c>:
    /// <list type="table">
    /// <item><term>Inward</term><description><c>max(d, −d − w)</c>, the band <c>d ∈ [−w, 0]</c></description></item>
    /// <item><term>Outward</term><description><c>max(−d, d − w)</c>, the band <c>d ∈ [0, +w]</c></description></item>
    /// <item><term>Straddling</term><description><c>abs(d) − w/2</c>, the band <c>d ∈ [−w/2, +w/2]</c></description></item>
    /// </list>
    /// Those three expressions are <see cref="ShaperOps.Shell"/> verbatim, with
    /// <see cref="ShaperShellAlignment.Centred"/> reading as Straddling. <b>The border stage adds no new
    /// geometry operator</b> — it reuses Shell, reuses <see cref="ShaperBound.Shell"/>, and inherits both the
    /// Lipschitz argument and the audit coverage that already exist for them. BT-2 checks the reuse bit for bit
    /// precisely so that a second copy of those three expressions cannot creep in later.
    ///
    /// The reuse is safe for a reason worth stating: Shell as a shape-node MODIFIER replaces the node's field
    /// with the band. The border stage evaluates the same function but <b>keeps both</b> — the node keeps
    /// publishing <c>d</c>, and the strip is a second field derived alongside it. That is the entire structural
    /// difference between a shell and a border, and it is one buffer, not one operator.
    ///
    /// <b>Antialiasing (BD-1.6).</b> Nothing here computes coverage; the strip is a program like any other and
    /// its coverage comes from <see cref="ShaperField.Coverage"/> with the SAME <c>halfBand</c> the node itself
    /// uses. Two consequences are exact rather than approximate, and both are measured by the audit:
    /// near the boundary an INWARD strip's field is <c>max(d, −d − w) = d</c> for all <c>d &gt; −w/2</c>, so the
    /// two fields are literally the same field there and the rim can never be brighter or dimmer than the
    /// silhouette it traces (BT-4); near the boundary an OUTWARD strip's field is <c>−d</c>, and
    /// <c>smoothstep</c> is odd about its midpoint, so <c>Coverage(−d, h) = 1 − Coverage(d, h)</c> in real
    /// arithmetic and the pair sums to 1 to within ONE ULP in float (BT-5). BD-1.6 says "exactly 1 at every
    /// sample" and that is an over-claim: <c>fl(0.5 − d)</c> is not <c>1 − fl(d + 0.5)</c>, and the cubic is then
    /// evaluated at two slightly different points. Swept over 400,001 values of <c>d</c> across the band the
    /// float sum differs from 1 at 10,018 of them, always by exactly one ULP; on a TRIANGLE every one of the
    /// 136 ring samples differs, while on a DISC none of the 208 do - which is why the first version of BT-5,
    /// measured on a disc alone, reported the over-claim as verified. Pyre's hard-coded one-pixel linear feather
    /// (<c>PyreRenderer.cs:1025</c>) and its multiply by the shape's own coverage (<c>:1035</c>) are NOT carried
    /// across: the fixed feather is not authorable and does not scale with the shape's own softness, and the
    /// multiply is what makes the rim wash out on a soft edge. In particular the strip's coverage is <b>not</b>
    /// multiplied by the node's — it does not need to be, because BD-1.3 derives it from the same field, and
    /// multiplying would double-count the antialiasing at the shared edge.
    /// </summary>
    public static class ShaperBorder
    {
        /// <summary>
        /// Resolve a border's dials once, at compile time (FC-5.3, BD-1.4).
        ///
        /// <b>BD-1.5 — a zero or negative width is an EXACT no-op.</b> <c>width ≤ 0</c> produces no strip and no
        /// owner at all: not an empty strip, not a strip painted with zero alpha, and not a dilation by zero. It
        /// is tested here, before anything is compiled, so an author sweeping a width dial to zero gets back
        /// exactly the picture they had before the border existed, BIT FOR BIT — which is what BT-1 measures,
        /// and "exactly" is not something to leave to the arithmetic.
        ///
        /// This is the same house rule the shape engine already applies at three places
        /// (<c>ShaperOps.SmoothMinShaped</c>'s <c>k ≤ 0</c> early-out, <c>Combine</c>'s
        /// <c>carveStrength ≤ 0</c> early-out, and the identity checks in <c>ShaperFieldAudit.V1</c>/<c>V2</c>).
        ///
        /// A null <see cref="ShaperBorderDef.width"/> falls back to 0 and is therefore also a no-op — the same
        /// answer as a dial dragged to zero, rather than a silent default thickness nobody authored.
        /// </summary>
        public static ShaperResolvedBorder Resolve(ShaperBorderDef def, float phase01, uint seed)
        {
            if (def == null || !def.enabled) return ShaperResolvedBorder.Nothing;

            float w = ShaperValue.Sample(def.width, phase01, seed, 0f);
            if (!(w > 0f)) return ShaperResolvedBorder.Nothing;   // `!(w > 0)`, so a NaN dial is a no-op too

            return new ShaperResolvedBorder
            {
                live = true,
                width = w,
                reach = ShaperBorderDef.OutwardReach(def.alignment, w),
                alignment = def.alignment,
                joinsCoverage = def.joinsCoverage,
            };
        }

        /// <summary>
        /// Compile the STRIP program: the node's own program plus one <see cref="ShaperOpKind.Shell"/> op
        /// carrying the resolved width and alignment.
        ///
        /// <b>Why this appends rather than re-derives.</b> The strip's field must be
        /// <c>Shell(alignment, w, d)</c> where <c>d</c> is the node's finished field. The node's finished field
        /// is exactly what the node's compiled program computes, so the strip is that program with one more
        /// instruction — no second walk of the tree, no second copy of the three Shell expressions, and no way
        /// for the two to drift apart. BT-2 measures the agreement bit for bit for exactly that reason.
        ///
        /// <b>The one subtlety: the join must be undone first.</b> A node whose border joins has a trailing
        /// <see cref="ShaperOpKind.Dilate"/> op (see <see cref="ApplyJoin"/>), so its program publishes
        /// <c>d − reach</c>. The strip traces the ORIGINAL edge, not the dilated one — an outward strip that
        /// traced its own dilated silhouette would sit one full width further out on every frame, which is the
        /// runaway an author would read as "the outline detached from the shape". So when the caller says the
        /// program is joined, the trailing op is dropped. It is provably the node's own join and nothing else:
        /// the node is the ROOT of this program, and <see cref="ApplyJoin"/> emits the node's join as the very
        /// last instruction of the node's own subtree, so the last op of a joined node's program is that join.
        /// The kind is checked anyway rather than assumed, because a silently wrong drop would produce a strip
        /// with a missing operator and no error.
        ///
        /// <b>The support box.</b> The strip reaches <c>reach</c> past the node's boundary, so its box is the
        /// node's box grown by the reach on all four sides. When the program is joined the compiler has ALREADY
        /// grown that box by the same reach (BD-2.4), so it is used unchanged; when it is not, the same growth is
        /// applied here. Both paths are "the node's original box, grown by the reach" and neither can under-bound
        /// the strip — a bound that excludes real samples is a correctness failure, not a performance one.
        ///
        /// <b>The declared bound is inherited unchanged; the anchor box is the node's grown by the reach.</b>
        /// <c>abs</c> and a hard <c>max</c> both preserve gradient magnitude
        /// (<see cref="ShaperBound.Shell"/>), so the bound composes. The strip's own node-local ANCHOR box is
        /// the node's grown by <c>reach / σ_min</c>, because that is the strip's honest extent in the node's own
        /// frame — the same conversion <c>ShaperCompiler.EmitShell</c> makes for a shell.
        ///
        /// It is never read while BD-3.2 holds, and computing it correctly anyway is exactly what makes BD-3.2
        /// TESTABLE: this used to copy the node's box unchanged, which made the strip's anchor and the node's
        /// identical by construction on every fixture, so BT-6 would have passed even if the resolver had taken
        /// the anchor from the strip. A leg that cannot fail in the direction it exists to test is the failure
        /// T-0105 and T-0106 were each caught by twice.
        /// </summary>
        /// <param name="nodeProgram">The node's own compiled program.</param>
        /// <param name="border">The resolved border. A dead border returns null — there is no strip.</param>
        /// <param name="nodeProgramIsJoined">
        /// True when <paramref name="nodeProgram"/> already carries this node's join (i.e.
        /// <see cref="ShaperResolvedBorder.Joins"/> was true when it was compiled).
        /// </param>
        public static ShaperProgram CompileStrip(ShaperProgram nodeProgram, in ShaperResolvedBorder border,
                                                 bool nodeProgramIsJoined)
        {
            if (nodeProgram == null || !border.live) return null;

            ShaperOp[] src = nodeProgram.ops ?? System.Array.Empty<ShaperOp>();
            int keep = src.Length;
            if (nodeProgramIsJoined && keep > 0 && src[keep - 1].kind == ShaperOpKind.Dilate) keep--;

            // The node's ORIGINAL box: already grown by the compiler on the joined path, grown here otherwise.
            //
            // BD-2.4 — the growth is the reach in CANVAS units, which is `reach · supportSpread` and not the
            // bare reach: the field is σ_min-rescaled, so on an anisotropically scaled member a reported
            // distance of `reach` is reached up to `reach · σ_max/σ_min` canvas pixels out. Growing by the bare
            // reach put 808 strip samples outside the declared box on a member scaled (2.0, 0.5), overshooting
            // by 23.5 px. `supportSpread` is exactly 1 on any isotropic tree, so the common case is unchanged
            // bit for bit, and the compiler's joined path uses the identical product.
            float halfW = nodeProgram.supportHalfW;
            float halfH = nodeProgram.supportHalfH;
            float canvasReach = border.reach * (nodeProgram.supportSpread >= 1f ? nodeProgram.supportSpread : 1f);
            if (!nodeProgramIsJoined && border.reach > 0f)
            {
                halfW += canvasReach;
                halfH += canvasReach;
            }

            // BD-3.2's counterpart, and the reason it is written rather than copied. The strip's own node-local
            // ANCHOR box is the node's grown by the reach expressed in the node's LOCAL units — the same
            // `distance / σ_min` conversion `ShaperCompiler.EmitShell` makes for a shell's local box.
            //
            // It is never read while BD-3.2 holds: a border's fill anchors on the NODE's box, which the resolver
            // passes explicitly. Computing it correctly anyway is what makes that wiring TESTABLE. Copying the
            // node's box unchanged — which is what this did — made the strip's anchor and the node's identical
            // by construction, so BT-6 could not have failed even if the anchor had been taken from the strip:
            // the leg proved nothing in the direction it exists to prove. With the two genuinely different, a
            // fill anchored on the strip produces a visibly different ramp and BT-6 reports it.
            float localGrow = border.reach > 0f
                ? border.reach / (nodeProgram.rootSigmaMin > 1e-9f ? nodeProgram.rootSigmaMin : 1f)
                : 0f;

            var ops = new ShaperOp[keep + 1];
            System.Array.Copy(src, ops, keep);
            ops[keep] = new ShaperOp
            {
                kind = ShaperOpKind.Shell,
                shellAlignment = border.alignment,
                p0 = border.width,
                distanceScale = 1f,
                bound = ShaperBound.Shell(nodeProgram.bound),
                boxCx = nodeProgram.supportCx,
                boxCy = nodeProgram.supportCy,
                boxHalfW = halfW,
                boxHalfH = halfH,
            };

            return new ShaperProgram
            {
                ops = ops,
                stackDepth = nodeProgram.stackDepth,          // Shell is unary: it pops one and pushes one
                bound = ShaperBound.Shell(nodeProgram.bound),
                supportCx = nodeProgram.supportCx,
                supportCy = nodeProgram.supportCy,
                supportHalfW = halfW,
                supportHalfH = halfH,
                localSupportCx = nodeProgram.localSupportCx,
                localSupportCy = nodeProgram.localSupportCy,
                localSupportHalfW = nodeProgram.localSupportHalfW + localGrow,
                localSupportHalfH = nodeProgram.localSupportHalfH + localGrow,
                hasLocalSupport = nodeProgram.hasLocalSupport,
                rootInverse = nodeProgram.rootInverse,
                rootInvertible = nodeProgram.rootInvertible,
                supportSpread = nodeProgram.supportSpread,
                rootSigmaMin = nodeProgram.rootSigmaMin,
                phase01 = nodeProgram.phase01,
            };
        }

        /// <summary>
        /// BD-2.2 — <b>joining is a DILATION of the node's field, not a union with the strip's.</b>
        /// Appends one <see cref="ShaperOpKind.Dilate"/> op carrying <paramref name="reach"/> and returns the
        /// node's support box grown by the same number. Called by <see cref="ShaperCompiler"/> at the point a
        /// node's own content is finished, so that EVERY program containing the node — its own, and every
        /// ancestor's — sees the same published field, with no second rule about which one is authoritative.
        ///
        /// <b>The rule: when a border joins, the node's published distance becomes <c>d − reach</c>. Nothing is
        /// combined; a constant is subtracted.</b>
        ///
        /// <b>The obvious alternative is wrong, and wrong in a way that produces a visible artefact rather than
        /// an error.</b> Folding the strip in the way any other member folds in gives <c>min(d, s(d))</c>, and
        /// for an Outward strip that is <c>min(d, max(−d, d − w))</c>, which at <c>d = +0.1</c> evaluates to
        /// <c>−0.1</c>. The union of a shape with a band hugging its outside is the shape DILATED by <c>w</c>,
        /// whose true distance at that point is <c>0.1 − w</c>. The <c>min</c> form is not merely a loose
        /// approximation of it: <b>it has a spurious zero crossing exactly on the original silhouette</b>,
        /// because the strip's own field is zero at its inner edge and the node's field is zero at the same
        /// place. The coverage kernel turns that into a ring of coverage ≈ 0.5 following the original outline —
        /// a visible seam, one pixel wide, in the middle of what should be a solid dilated silhouette. It looks
        /// like an antialiasing bug and it is a topology bug. BT-8 runs the <c>min</c> form alongside the real
        /// one and requires it to FAIL the same measurement, so the test cannot be one that could never fail.
        ///
        /// <c>d − reach</c> is not an approximation either. For a strip derived from the node's own field, the
        /// union of node and strip <i>is</i> the set <c>{d ≤ reach}</c>, whose exact signed distance is
        /// <c>d − reach</c>. The join is therefore exact, is one subtraction, has no zero crossing anywhere but
        /// at the dilated boundary, and preserves the declared bound exactly because adding a constant does not
        /// change a gradient.
        ///
        /// <b>BD-2.4 — this grows the CULLING box and MUST NOT grow the ANCHOR box.</b> A joined outward strip
        /// makes the node bigger, so the support box must grow by the reach on all four sides; it is a bound, and
        /// a bound that excludes real samples is a correctness failure. The node-local ANCHOR box
        /// (<see cref="ShaperProgram.localSupportHalfW"/>, FC-1.5) must NOT grow: if it did, switching a border
        /// on would silently move every gradient on that node — a linear ramp authored across the shape would
        /// rescale by <c>(halfExtent + reach) / halfExtent</c> the moment an outline appeared, and rescale again
        /// on every frame in which the width animates. <b>Enabling an outline must never repaint the thing it
        /// outlines.</b> The two boxes are already separate fields on <see cref="ShaperProgram"/>, so this costs
        /// nothing but the discipline of touching one and not the other; BT-10 measures both halves.
        /// </summary>
        /// <returns>The op to append. The caller owns the box growth, which it holds in its own <c>Box</c> type.</returns>
        public static ShaperOp JoinOp(float reach, float childBound,
                                      float boxCx, float boxCy, float grownHalfW, float grownHalfH)
            => new ShaperOp
            {
                kind = ShaperOpKind.Dilate,
                p0 = reach,
                distanceScale = 1f,
                // Adding a constant does not change a gradient, so the child's Lipschitz bound is preserved
                // exactly — the same argument ShaperBound.Shell makes for `abs` and a hard `max`.
                bound = childBound,
                boxCx = boxCx,
                boxCy = boxCy,
                boxHalfW = grownHalfW,
                boxHalfH = grownHalfH,
            };

        /// <summary>
        /// The dilation, as one expression, so that the evaluator and any future consumer cannot disagree about
        /// it. <c>d − reach</c>, with the empty field left exactly empty.
        ///
        /// <b>Why the empty guard.</b> <see cref="ShaperField.Empty"/> is deliberately outside the metric
        /// contract — "not a distance to anything", with the one guarantee that it COMPOSES: <c>min(Empty, d)</c>
        /// is <c>d</c>, <c>max(Empty, d)</c> is <c>Empty</c>. An empty field dilated is still empty, and while
        /// <c>1e9 − 4</c> would still clear <see cref="ShaperField.EmptyThreshold"/> today, that is an accident
        /// of two constants' ratio rather than a property, and a border on a bag with a large reach is exactly
        /// the case that would erode it. The guard makes the composition exact instead of nearly exact.
        /// </summary>
        public static float Dilate(float reach, float d)
            => ShaperField.IsEmpty(d) ? d : d - reach;
    }
}
