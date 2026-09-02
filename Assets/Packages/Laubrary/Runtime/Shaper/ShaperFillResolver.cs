using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// One node that owns a fill, resolved: its own subtree compiled standalone in the layer frame, its fill
    /// compiled, and the index of the nearest ancestor owner whose coverage clips its claim.
    /// </summary>
    public sealed class ShaperFillOwner
    {
        public ShaperNode node;
        public string name;

        /// <summary>Index into <see cref="ShaperFillDocument.owners"/> of the nearest BINDING ancestor owner, or −1 for the root.</summary>
        public int ancestorOwner = -1;

        /// <summary>This node's subtree, compiled standalone but seeded with the node's accumulated parent transform (FC-3.9).</summary>
        public ShaperProgram shape;

        /// <summary>The compiled fill.</summary>
        public ShaperFillProgram fill;

        /// <summary>What this node publishes, per FC-4.2, and what its fill required.</summary>
        public ShaperQuantitySet published, required;

        /// <summary>This owner's own evaluation stack. Allocated once here, never per sample.</summary>
        public float[] stack;

        /// <summary>True when this owner's fill was substituted for the FC-3.2 default because the authored one refused to bind.</summary>
        public bool isSubstitutedRootFill;

        /// <summary>
        /// T-0191 — non-null when this owner is a COMPOSITE node painting with its own finished picture rather
        /// than with a fill. Its four clauses — an authored fill wins, only where the composite itself owns a
        /// fill, no height, unlit — are stated in full on <see cref="ShaperCompositeAlbedo"/>. Null on every
        /// other owner, and the paint pass then takes the pre-T-0191 path unchanged.
        /// </summary>
        public ShaperCompositeAlbedo compositeAlbedo;

        /// <summary>
        /// BORDER-CONTRACT BD-3.5 — true when this owner is a node's BORDER STRIP rather than a node's fill.
        ///
        /// A border owner is deliberately <b>NOT part of the exclusivity partition</b>. It contributes nothing to
        /// any owner's <c>descendantClaim</c>, no owner's <c>descendantClaim</c> excludes it, and its
        /// <c>paint</c> is its own <c>claim</c> with no descendant subtraction. It composites <c>Over</c> (or
        /// <c>Add</c>, if its fill declares it) into its host node's subtree accumulator after that subtree is
        /// otherwise complete and before that accumulator folds into its parent.
        ///
        /// <b>Why not simply make it an owner in the partition.</b> That is the intuitive reading of "recolour
        /// the band", and it does not work. To be on top of a bag's members the bag's border would have to be a
        /// DESCENDANT of every member's fill owner, and a member's owner is a descendant of the bag's — the two
        /// requirements are contradictory in a tree. Any tree placement gives the wrong answer for one of B3's
        /// two documented cases, and the case it breaks is the interesting one: an outer outline on a bag would
        /// be EATEN wherever a member happened to own its own fill (BT-11 measures exactly that).
        ///
        /// The price, which is honest and must be said in any UI over this: a border with a partially transparent
        /// fill lets the fill underneath show through, because it is a STROKE ON TOP rather than a substitution.
        /// That is what every drawing tool does with a stroke, and it is what Pyre's border does today — while
        /// Pyre's own documentation claims the opposite (<c>Pyre.cs:518</c>, "recoloured to the Border fill").
        /// The implementation is the sane half; the documentation is the half that was wrong.
        /// </summary>
        public bool isBorder;

        /// <summary>
        /// For a border owner: the index of the owner for the NODE this strip traces, or −1 when that node bound
        /// no fill of its own. −1 on every non-border owner.
        ///
        /// It is the accumulator the strip composites into. When it is −1 the node has no accumulator of its own
        /// — its interior is painted by the nearest binding ancestor — so the strip goes into
        /// <see cref="ancestorOwner"/>'s accumulator instead, which is the only accumulator that exists to hold
        /// it. That placement is right against the ancestor's own paint (the thing the strip must sit on top of)
        /// and WRONG against the ancestor's other descendants, which is what <see cref="borderSubtreeEnd"/>
        /// corrects.
        /// </summary>
        public int borderHost = -1;

        /// <summary>
        /// For a HOSTLESS border owner (<see cref="borderHost"/> = −1): one past the last owner index belonging
        /// to the bordered node's own subtree. −1 on every other owner.
        ///
        /// <b>The defect it exists to fix, measured.</b> A hostless strip composites into its nearest binding
        /// ancestor's accumulator at that ancestor's turn — which is necessarily AFTER the ancestor's own paint
        /// (it must be, or the ancestor's fill would be summed on top of the strip and wash it out) and
        /// therefore also after every one of the ancestor's descendants has folded in. So a member's outline
        /// landed above a LATER sibling that owned a fill, which BD-3.6 forbids: "a member's border folds into
        /// the member's subtree, which folds into the bag." Measured on a bag with member A (magenta inward
        /// border, no fill of its own) below member B (opaque red fill): the sample where A's rim crosses B read
        /// magenta (1.000, 0.000, 1.000); giving A a fill of its own — an unrelated dial — flipped the same
        /// sample to red. An author cannot predict a z-order that depends on whether an outlined shape also
        /// happens to carry its own colour.
        ///
        /// <b>What it corrects, and how.</b> Owners are in pre-order, so the owners with an index at or above
        /// this one are exactly the LATER siblings and their subtrees — the things that must end up on top —
        /// while the owners between the border's own index and this one are the bordered node's own descendants,
        /// which must stay UNDER it. The paint pass walks owners in decreasing index, so at the turn of owner
        /// <c>borderSubtreeEnd − 1</c> the ancestor's accumulator holds the later siblings and nothing else.
        /// Its alpha is snapshotted there, and the strip's coverage is later scaled by <c>1 − that</c>.
        ///
        /// <b>Exact where it matters, approximate where it cannot be.</b> Under an opaque later sibling the
        /// strip contributes nothing, which is precisely "the sibling is on top"; with no later sibling the
        /// snapshot is 0 and nothing changes at all. Under a PARTIALLY transparent later sibling the result is a
        /// blend rather than the exact <c>sibling Over strip</c>, because the accumulator has already mixed the
        /// sibling with the ancestor's other content and the two can no longer be separated. Making that case
        /// exact needs the ancestor's own paint and its descendants kept as separate layers, which is a change
        /// to FC-3.5a's SUM and would reopen the alpha deficit T-0106 closed. The approximation is monotone,
        /// agrees at both limits, and is strictly closer than the unclamped strip it replaces.
        /// </summary>
        public int borderSubtreeEnd = -1;
    }

    /// <summary>
    /// A whole layer's fills, resolved: the owners in PAINT ORDER, plus the program-level diagnostics.
    ///
    /// Every diagnostic block mirrors <c>ShaperProgram</c>'s flags exactly (<c>ShaperProgram.cs:82-102</c>) —
    /// a boolean, the FIRST offender's name, the full reason sentence, and A COUNT. The count is not
    /// decoration: <c>ShaperProgram.leadingNonAddCount</c> exists because recording only the first name meant
    /// "a tree with three offending bags would have surfaced one and silently hidden two — and the flag exists
    /// precisely so the UI can point at the problem" (<c>ShaperProgram.cs:90-95</c>). That correction is
    /// applied here from the start rather than after the same mistake is made a second time (FC-4.3a).
    /// </summary>
    public sealed class ShaperFillDocument
    {
        /// <summary>
        /// PAINT ORDER, and paint order IS FOLD ORDER (FC-3.4). No stage may reorder it, for the same reason
        /// R1 gives for the fold: "No stage may reorder a bag's members. Not to batch the additive ones, not to
        /// group by generator, not to improve cache locality, not to skip ahead." Two lists which look
        /// identical must not disagree about which end is which.
        ///
        /// A node appears before its own members, and members appear bottom-most first.
        /// </summary>
        public List<ShaperFillOwner> owners = new List<ShaperFillOwner>();

        // ── FC-4.4: a fill that required a quantity its node does not publish ─────────────────────────────
        public bool hasUnavailableFill;
        public string unavailableFillReason;
        public string unavailableFillNode;
        public int unavailableFillCount;

        // ── FC-3.3: a Subtract member that authored a fill ────────────────────────────────────────────────
        public bool hasSubtractFill;
        public string subtractFillReason;
        public string subtractFillNode;
        public int subtractFillCount;

        // ── FC-6.3c: a Ramp picking a quantity that is refused on TYPE, not on availability ───────────────
        public bool hasTypeRefusedFill;
        public string typeRefusedFillReason;
        public string typeRefusedFillNode;
        public int typeRefusedFillCount;

        // ── FC-6.5: a half-configured fill that fell back (null gradient, null/unreadable texture) ────────
        public bool hasFallbackFill;
        public string fallbackFillReason;
        public string fallbackFillNode;
        public int fallbackFillCount;

        // ── BD-3.7: a Subtract member that authored a BORDER ──────────────────────────────────────────────
        //
        // The fifth block, mirroring the four above exactly — a boolean, the FIRST offender's name, the full
        // reason sentence, and A COUNT. Not a reuse of the subtract-FILL block: the two have different remedies
        // and different fixes, and a UI that greys one control must not point the author at the other.
        //
        // BD-3.7 mirrors FC-3.3 for the same reason and through the same machinery: a Subtract member deposits
        // nothing, and a strip is a deposit. It is WORSE than a fill, in fact — a Subtract member's own edge is
        // not an edge of the finished silhouette at all, so its rim would trace a boundary that is not visible
        // anywhere, wherever a later member happened to put the shape back.
        //
        // THE WAY TO OUTLINE A HOLE IS A BORDER ON THE BAG, and it already works with no extra feature: the
        // bag's finished field is zero on the hole's boundary just as it is on the outer boundary, so an Inward
        // or Straddling border on the bag traces both at once. That is not a workaround, it is the correct
        // reading of BD-1.1 — the hole is part of the bag's edge.
        //
        // An INTERSECT member may own a border, mirroring FC-3.3, with the caveat that it traces the member's
        // own edge rather than the intersection's — the same caveat the fill stage already carries.
        public bool hasSubtractBorder;
        public string subtractBorderReason;
        public string subtractBorderNode;
        public int subtractBorderCount;

        /// <summary>Every diagnostic in the document, one line each, for a report or a UI list.</summary>
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            if (hasUnavailableFill)
                sb.AppendLine("unavailable x" + unavailableFillCount + " (first: " + unavailableFillNode + ") — " + unavailableFillReason);
            if (hasSubtractFill)
                sb.AppendLine("subtract-fill x" + subtractFillCount + " (first: " + subtractFillNode + ") — " + subtractFillReason);
            if (hasTypeRefusedFill)
                sb.AppendLine("type-refused x" + typeRefusedFillCount + " (first: " + typeRefusedFillNode + ") — " + typeRefusedFillReason);
            if (hasFallbackFill)
                sb.AppendLine("fallback x" + fallbackFillCount + " (first: " + fallbackFillNode + ") — " + fallbackFillReason);
            if (hasSubtractBorder)
                sb.AppendLine("subtract-border x" + subtractBorderCount + " (first: " + subtractBorderNode + ") — " + subtractBorderReason);
            if (sb.Length == 0) sb.AppendLine("(no fill diagnostics)");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// The per-tile scratch and output the paint pass needs. HOST-ALLOCATED AND HOST-OWNED, on exactly the
    /// terms BC-3.7f sets for sheets, extended to the fill stage's own intermediates: the resolver never
    /// allocates, replaces, resizes or frees one of these inside a paint call.
    /// </summary>
    public sealed class ShaperFillBuffers
    {
        public int sampleCapacity;
        public int ownerCapacity;

        /// <summary>Per owner, per sample: that owner's own subtree coverage.</summary>
        public float[] ownCoverage;
        /// <summary>
        /// Per owner, per sample: that owner's own subtree signed distance — the <c>edgeDistance</c> quantity.
        /// Kept per owner, not per document, because FC-1.2 says a fill reads "the sheets ITS OWNING NODE
        /// published". A Gradient in <c>ByEdgeDistance</c> on a member must ramp from that member's own edge,
        /// not from the layer root's.
        /// </summary>
        public float[] ownDistance;
        /// <summary>
        /// <b>T-0109 FIX F4a.</b> Per owner, per sample: <c>height_shape</c> in canvas pixels above the
        /// layer's own base plane — the layer's compiled height stage evaluated on THAT OWNER's own
        /// <see cref="ownDistance"/>, written by <see cref="ShaperHeight.FillTile"/>.
        ///
        /// It sits alongside <see cref="ownCoverage"/>/<see cref="ownDistance"/> and is slabbed the same way
        /// for the same FC-1.2 reason: a fill reads the sheets its OWN node published, so a member's height
        /// must be measured from that member's own edge and not from the layer root's. The layer-level sum
        /// FC-2.5 asks for is seeded from the ROOT owner's slab, which is the one HS-1.1 defines the layer's
        /// solid from; the per-owner slabs additionally feed <c>ShaperLightScene.pointZ</c>, which is itself
        /// per owner because the surface point being shaded belongs to the owner being shaded.
        ///
        /// Before this fix <see cref="ShaperHeight.FillTile"/> had NO caller outside the audit,
        /// <c>buf.height</c> was <c>0 + heightDelta·coverageEff</c>, and <c>pointZ</c> was
        /// <c>Array.Clear</c>ed — so every point lamp shaded every layer as a flat sheet on the base plane.
        /// </summary>
        public float[] ownHeight;
        /// <summary>Per owner, per sample: <c>min(coverage_N, coverage_A)</c> — the FC-3.1 claim.</summary>
        public float[] claim;
        /// <summary>Per owner, per sample: the largest claim anywhere in that owner's DESCENDANT owners.</summary>
        public float[] descendantClaim;
        /// <summary>Per owner, per sample: the claim after descendant exclusion — what this owner actually paints.</summary>
        public float[] paint;

        /// <summary>One owner's fill output at a time. 3 floats per sample.</summary>
        public float[] albedo;
        public float[] veil;
        public float[] heightDelta;

        /// <summary>The accumulated destination: PREMULTIPLIED linear RGB and alpha, 4 floats per sample.</summary>
        public float[] dst;
        /// <summary>The accumulated height, summed regardless of composite mode (FC-2.6c).</summary>
        public float[] height;

        /// <summary>
        /// Per owner, per sample: that owner's whole SUBTREE result, premultiplied linear RGB + alpha, 4 floats
        /// per sample. The one buffer the FC-3.5a accumulation needs and the reason it costs anything at all.
        ///
        /// <b>Why a per-owner accumulator and not one destination.</b> An owner's own paint region is DISJOINT
        /// from every descendant's (FC-3.5a), so the two accumulate by SUM; two SIBLING owners' regions genuinely
        /// overlap, so they accumulate by <c>Over</c> (FC-3.4). One flat destination cannot tell the two apart
        /// once they are mixed into it — which is exactly how the 244-sample alpha deficit measured at T-0106's
        /// verification arose. Giving each owner its own slab lets a subtree be finished before it meets a
        /// sibling, which is the only place the distinction is still visible.
        ///
        /// Host-allocated once, on the same BC-3.7f terms as every other array here: never allocated, resized or
        /// freed inside a paint call. Costs <c>ownerCapacity · sampleCapacity · 4</c> floats — four times one of
        /// the per-owner scalar sheets above, and the honest price of the fix.
        /// </summary>
        public float[] subtree;

        public ShaperFillBuffers(int sampleCapacity, int ownerCapacity)
        {
            this.sampleCapacity = Mathf.Max(1, sampleCapacity);
            this.ownerCapacity = Mathf.Max(1, ownerCapacity);
            int n = this.sampleCapacity, k = this.ownerCapacity;

            ownCoverage = new float[n * k];
            ownDistance = new float[n * k];
            ownHeight = new float[n * k];
            claim = new float[n * k];
            descendantClaim = new float[n * k];
            paint = new float[n * k];

            albedo = new float[n * 3];
            veil = new float[n];
            heightDelta = new float[n];

            dst = new float[n * 4];
            height = new float[n];
            subtree = new float[n * k * 4];
        }

        /// <summary>
        /// Clear the destination, the height accumulator and every owner's subtree accumulator for a fresh
        /// tile. Never called per sample — once per <see cref="ShaperFillResolver.PaintTile"/>, which is where
        /// BC-1.2's "per invocation, not per sample" line sits.
        /// </summary>
        public void ClearDestination(int sampleCount)
        {
            Array.Clear(dst, 0, sampleCount * 4);
            Array.Clear(height, 0, sampleCount);
            for (int o = 0; o < ownerCapacity; o++)
                Array.Clear(subtree, o * sampleCapacity * 4, sampleCount * 4);
        }
    }

    /// <summary>
    /// Resolves WHICH fill paints WHICH pixel, gates a fill that requires a quantity its node does not publish,
    /// and composites. This is Part F3 and Part F4 of the fill contract.
    ///
    /// <b>The resolution procedure (FC-3.1).</b> For each fill-owning node N, N's paint region is
    /// <c>min(coverage_N, coverage_A)</c>, where <c>coverage_N</c> is the coverage of N's own subtree evaluated
    /// standalone and <c>coverage_A</c> is the finished coverage of the nearest ancestor that also owns a fill.
    /// Regions are painted in FOLD ORDER, bottom-most member first, each compositing by its own declared mode.
    /// Ownership is exclusive per pixel: an ancestor's fill does NOT paint underneath a descendant's (FC-3.5).
    ///
    /// The <c>min</c> is load-bearing and not decoration. In the worked example the bag folds
    /// <c>((Torso ∪ Head) ∩ Belt) ∪ Iris</c>; a Solid fill on Head has a non-empty CLAIM outside Belt, and only
    /// the <c>min</c> against the bag's zero coverage there stops it painting outside the silhouette. A
    /// member's fill claim can be entirely annihilated by a LATER member.
    ///
    /// <b>What "wins" means, and what it does not.</b> C4's "a child that owns its own fill wins inside its own
    /// coverage" resolves OWNERSHIP — whose fill applies — not z-order between siblings. Two sibling fills do
    /// not fight for ownership; they own disjoint claims and paint in order, and where both cover, the later
    /// one composites OVER the earlier. Reading "wins" as "replaces" would make a translucent veil on the upper
    /// sibling produce a hole rather than a blend.
    ///
    /// <b>A disabled node needs no rule and deliberately gets none (FC-3.6).</b> A disabled child is skipped
    /// outright by the compiler (<c>ShaperCompiler.cs:208</c>), so its coverage is empty, so
    /// <c>min(coverage_N, coverage_A) = 0</c> and its fill paints nothing — with no fill-stage check at all.
    /// Adding a redundant check is how the two hand-synced allow-lists in <c>PyreRenderer.cs:950-952</c> and
    /// <c>PyreWindow.cs:2670-2672</c> came to exist.
    /// </summary>
    public static class ShaperFillResolver
    {
        /// <summary>
        /// Guards the un-premultiply in <see cref="Encode"/>. Never divides by zero — and the branch it guards
        /// is the additive-over-transparent sample, so it is a defined case rather than a safety net.
        /// </summary>
        const float MinAlpha = 1e-6f;

        // ── resolution ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Walk the tree, decide ownership, gate availability, and compile one shape program and one fill
        /// program per BINDING owner.
        /// </summary>
        /// <param name="leafPublished">
        /// What a PRIMITIVE node publishes. Defaults to <see cref="ShaperQuantitySet.ShippedShapeEngine"/> —
        /// coverage and edge distance and nothing else, because that is what <c>ShaperEvaluator.FillTile</c>
        /// actually writes (<c>ShaperEvaluator.cs:148-149</c>). Injectable so the audit can exercise the gate
        /// against a hypothetical publisher without mocking the engine.
        /// </param>
        /// <param name="perLeafPublished">
        /// An optional per-node override, for the day R6's coverage-only generators land and two leaves in one
        /// tree publish different sets. Called at RESOLVE time, once per node — never from a loop.
        /// </param>
        public static ShaperFillDocument Resolve(
            ShaperNode root, float phase01, uint seed,
            float canvasHalfW, float canvasHalfH,
            ShaperQuantitySet leafPublished = ShaperQuantitySet.ShippedShapeEngine,
            Func<ShaperNode, ShaperQuantitySet> perLeafPublished = null)
        {
            var doc = new ShaperFillDocument();
            if (root == null) return doc;

            float p = Mathf.Clamp01(phase01);
            Walk(doc, root, ShaperMatrix.Identity, -1, true, p, seed,
                 canvasHalfW, canvasHalfH, leafPublished, perLeafPublished);
            return doc;
        }

        static void Walk(ShaperFillDocument doc, ShaperNode node, in ShaperMatrix parentForward,
                         int ancestorOwner, bool isRoot, float phase01, uint seed,
                         float canvasHalfW, float canvasHalfH,
                         ShaperQuantitySet leafPublished, Func<ShaperNode, ShaperQuantitySet> perLeaf)
        {
            if (node == null || !node.enabled) return;

            ShaperFillDef def = node.fill;

            // FC-3.2: A SHAPE LAYER'S ROOT NODE ALWAYS OWNS A FILL, and it cannot be removed, only edited.
            // Design B2 settles this without appearing to — "Add a layer. You get a disc in one flat colour" —
            // so a new layer PAINTS with nothing authored, and "nothing/transparent" contradicts the design.
            // The structural payoff is worth more than the aesthetic one: it makes the nearest-ancestor search
            // TOTAL. It can never fail, so there is no "no fill found" branch anywhere, no null owner and no
            // undefined pixel inside a covered silhouette — an entire error class removed by one field.
            if (isRoot && def == null) def = ShaperFillDef.DefaultRootFill();

            int myOwnerIndex = ancestorOwner;
            // The index of the owner this NODE bound for its own fill, or −1. Kept separately from
            // `myOwnerIndex` because the two answer different questions: `myOwnerIndex` is what a CHILD inherits
            // as its clipping ancestor (and is the ancestor's index when this node bound nothing), while this is
            // the accumulator a border on THIS node composites into (BD-3.5), which must be null when the node
            // has no accumulator of its own.
            int myFillOwner = -1;

            if (def != null)
            {
                ShaperQuantitySet published = Published(node, leafPublished, perLeaf);
                string refusal = Refuse(doc, node, def, published, isRoot, leafPublished, perLeaf);

                if (refusal == null)
                {
                    myOwnerIndex = Bind(doc, node, def, published, parentForward, ancestorOwner,
                                        phase01, seed, canvasHalfW, canvasHalfH, false);
                    myFillOwner = myOwnerIndex;
                }
                else if (isRoot)
                {
                    // The root's authored fill refused to bind, and the root MUST bind — it is the fallback
                    // target of FC-4.4 and there is no ancestor above it. Substituting the guaranteed-binding
                    // FC-3.2 default is the only total answer; the diagnostic already carries the reason.
                    myOwnerIndex = Bind(doc, node, ShaperFillDef.DefaultRootFill(), published, parentForward,
                                        ancestorOwner, phase01, seed, canvasHalfW, canvasHalfH, true);
                    myFillOwner = myOwnerIndex;
                }
                // T-0191 — CLAUSE 1 of the composite-albedo rule: WHERE A COMPOSITE OWNS THE PAINT, IT PAINTS
                // WITH ITS OWN PICTURE. Unconditional, because a composite has no Shaper fill to lose to: the
                // window does not offer a Fill or Border card on a composite node at all (the owner's ruling,
                // T-0191), so the FC-3.2 default this owner is holding is a structural placeholder rather than
                // anything an author chose. It is still resolved, because what it writes to `veil` is what
                // gates this owner's coverage; only its colour is replaced, in PaintTile.
                //
                // CLAUSE 2, "only where the composite is itself a fill owner", needs no code of its own: it is
                // exactly where this line sits — inside the `myFillOwner >= 0` case. FC-3.2 gives a default
                // fill to the LAYER ROOT and to nothing else, and no card can put one on a nested composite,
                // so a composite inside a Bag never becomes an owner and never reaches here. It therefore
                // still publishes coverage and nothing else (B1) and its colour cannot leak into the bag that
                // fused it — the nearest-ancestor ownership rule doing the work, not a second rule beside it.
                if (myFillOwner >= 0 && node.kind == ShaperNodeKind.Composite)
                {
                    doc.owners[myFillOwner].compositeAlbedo =
                        ShaperCompositeAlbedo.From(doc.owners[myFillOwner].shape);
                }

                // Otherwise: NO owner is created, so this node's region falls through to the nearest BINDING
                // ancestor — which is at worst the root's Solid. FC-4.4: it never falls back to a different
                // quantity, never substitutes a default value for the missing one, and NEVER PAINTS NOTHING.
                // Painting nothing is "silently inert", which BC-3.1 names as the single most-reported
                // confusion in the existing tool, because a hole inside a silhouette looks like a shape bug
                // rather than a fill bug.
            }

            // ── the border, AFTER the node's own fill has been considered (BORDER-CONTRACT Part B3) ────────
            //
            // After, and not before, for two reasons that both matter. Its `borderHost` is the owner index the
            // node's own fill just produced, so that index has to exist. And paint order IS fold order
            // (FC-3.4): a node's border sits directly above that node's finished subtree, so it belongs
            // immediately after the node in the owner list and before any of the node's children — which is
            // where appending here puts it.
            int borderIndex = BindBorder(doc, node, parentForward, ancestorOwner, myFillOwner, isRoot,
                                         phase01, seed, canvasHalfW, canvasHalfH, leafPublished, perLeaf);

            if (node.kind == ShaperNodeKind.Bag && node.children != null)
            {
                ShaperMatrix forward = ShaperMatrix.Mul(parentForward,
                                                        (node.transform ?? new ShaperTransformBlock()).ToMatrix(phase01, seed));
                for (int i = 0; i < node.children.Count; i++)
                {
                    Walk(doc, node.children[i], forward, myOwnerIndex, false, phase01, seed,
                         canvasHalfW, canvasHalfH, leafPublished, perLeaf);
                }
            }

            // BD-3.6 — a HOSTLESS border needs to know where its node's own subtree ends, because that is the
            // boundary between "the node's own descendants, which the outline is above" and "the later siblings,
            // which it is below". It can only be known here, after the children have been walked. Recorded ONLY
            // for a hostless border: a border with a host rides its node's own accumulator and is already
            // ordered correctly by construction. See ShaperFillOwner.borderSubtreeEnd for the measurement.
            if (borderIndex >= 0 && doc.owners[borderIndex].borderHost < 0)
                doc.owners[borderIndex].borderSubtreeEnd = doc.owners.Count;
        }

        /// <summary>
        /// Create this node's BORDER owner, if it has a live one. BORDER-CONTRACT BD-3.1 through BD-3.8.
        ///
        /// Everything the strip needs already exists; this method is the wiring, and each of the five decisions
        /// it makes is a clause that could plausibly have gone the other way, so each is named at its site.
        /// </summary>
        static int BindBorder(ShaperFillDocument doc, ShaperNode node, in ShaperMatrix parentForward,
                              int ancestorOwner, int myFillOwner, bool isRoot, float phase01, uint seed,
                              float canvasHalfW, float canvasHalfH,
                              ShaperQuantitySet leafPublished, Func<ShaperNode, ShaperQuantitySet> perLeaf)
        {
            if (node.border == null) return -1;

            // T-0191 — A COMPOSITE NODE HAS NO BORDER, and this is a structural fact rather than a refusal to
            // report. A border is ShaperOps.Shell applied to the node's field (BD-1.3), and a composite's field
            // is not a distance function at all: it is a coverage raster inverted into a pseudo-distance that
            // ShaperEvaluator's own CompositeSample case documents as "valid ONLY within roughly one texel of
            // the generator's own edge". A strip traced on that saturates immediately, so a border on a
            // composite could only ever draw the wrong band. §6.2's "may not be re-filled" says the same thing
            // about its colour, and the window now offers neither card on a composite node.
            //
            // It has to be refused HERE and not only in the window, because `ShaperNode.border` is a plain
            // [Serializable] class field: Unity's serializer never writes null for one, so a composite node
            // saved into an asset comes back with a default border object whether or not anyone authored it.
            // Measured on a freshly saved single-Gem document: two owners instead of one, the phantom strip
            // painting the FC-3.2 default over the generator's own picture and cutting a 608-colour gem down
            // to 51 flat-looking ones.
            if (node.kind == ShaperNodeKind.Composite) return -1;

            // BD-1.5 — null, disabled or zero-width is an EXACT no-op: no owner, no strip, nothing recorded.
            ShaperResolvedBorder border = ShaperBorder.Resolve(node.border, phase01, seed);
            if (!border.live) return -1;

            // BD-3.7 — a Subtract member may NOT own a border. Refused before anything is compiled, and the
            // compiler refuses the matching dilation on the identical test (ShaperCompiler.EmitBorderJoin), so
            // the strip and the published field cannot disagree about whether the border exists.
            if (!isRoot && node.mode == ShaperCombineMode.Subtract)
            {
                string reason = "Border on '" + node.name + "' cannot be attached: " +
                                ShaperQuantities.SubtractRefusal + ". Outline the hole with a border on the bag.";
                doc.subtractBorderCount++;
                if (!doc.hasSubtractBorder)
                {
                    doc.hasSubtractBorder = true;
                    doc.subtractBorderNode = node.name;
                    doc.subtractBorderReason = reason;
                }
                return -1;
            }

            // The node's own compiled program. Reused from the fill owner when the node bound one — it is the
            // same node, the same parent transform, the same phase and the same seed, so a second compile would
            // be the same numbers at twice the cost — and compiled here when it did not.
            ShaperProgram nodeProgram = myFillOwner >= 0
                ? doc.owners[myFillOwner].shape
                : ShaperCompiler.Compile(node, parentForward, phase01, seed);

            // BD-1.3 — the strip IS ShaperOps.Shell applied to the node's field. `border.Joins` tells CompileStrip
            // that the node's program already carries its own dilation, which must be undone before the shell is
            // applied: the strip traces the ORIGINAL edge, not the dilated one.
            ShaperProgram strip = ShaperBorder.CompileStrip(nodeProgram, border, border.Joins);
            if (strip == null) return -1;

            // BD-3.1 — a border whose fill is null uses the FC-3.2 default Solid rather than a second default of
            // its own, so a border which is switched on always draws something and can never be silently inert.
            ShaperFillDef def = node.border.fill ?? ShaperFillDef.DefaultRootFill();

            // BD-3.8 — the border's FILL goes through the ordinary availability gate, unchanged, against the
            // NODE's published set (a border does not change what its node publishes). The label makes the
            // diagnostic name the BORDER rather than the node, because "Ramp-by-quantity on 'Torso' needs heat"
            // would otherwise be indistinguishable from the same sentence about the node's own fill.
            //
            // The strip ITSELF never refuses on availability: it needs only the node's distance, which every
            // shape node publishes unconditionally (ShaperQuantitySet.ShippedShapeEngine).
            string label = node.name + " border";
            ShaperQuantitySet published = Published(node, leafPublished, perLeaf);
            string refusal = Refuse(doc, node, def, published, isRoot, leafPublished, perLeaf, label);
            bool substituted = false;
            if (refusal != null)
            {
                // NOT a fall back to the node's fill, and NOT a fall back to nothing — the FC-3.2 default Solid.
                // A border that was switched on is always visible, so a refusal reads as "the outline is the
                // wrong colour, why?" rather than as "the outline vanished, is this a shape bug?". That is the
                // same reasoning FC-4.4 gives for never painting nothing. Recorded in the FALLBACK block as well
                // as in whichever refusal block already fired, because the two say different things: one says
                // why it refused, this one says what is on screen instead.
                def = ShaperFillDef.DefaultRootFill();
                substituted = true;
                doc.fallbackFillCount++;
                if (!doc.hasFallbackFill)
                {
                    doc.hasFallbackFill = true;
                    doc.fallbackFillNode = label;
                    doc.fallbackFillReason = refusal + " Painted with the default Solid instead (BD-3.8).";
                }
            }

            // BD-3.2 / FC-8.4 — THE ANCHOR BOX IS THE NODE'S, NEVER THE PAINTED REGION'S. So a Linear, Radial or
            // Angular gradient painting a border ramps ACROSS THE SHAPE IT TRACES, not across the ring's own
            // thickness. The failure it prevents is a linear ramp on an outline running across two pixels of
            // outline thickness instead of across the shape — almost never what anyone wants, and nearly
            // impossible to diagnose after the fact. BT-6 measures it by widening the border 20x and requiring
            // the same colour at the same canvas point.
            //
            // Note this is the OPPOSITE direction from BD-3.3 one paragraph down, and both are deliberate.
            ShaperFillAnchor anchor = ShaperFillAnchor.From(nodeProgram, canvasHalfW, canvasHalfH);

            int index = Bind(doc, node, def, published, parentForward,
                             // BD-3.4 — the claim is clipped by the nearest binding ancestor OF THE NODE, never
                             // by the node itself. Not the node itself, because for an Outward strip that would
                             // clip the entire border away: the strip is BY CONSTRUCTION outside the node's
                             // coverage. The ancestor clip is what stops a member's rim appearing in a region a
                             // later Subtract member carved out of the bag — FC-3.3's problem in a new costume.
                             ancestorOwner,
                             phase01, seed, canvasHalfW, canvasHalfH, substituted,
                             strip, anchor, label);

            ShaperFillOwner owner = doc.owners[index];
            owner.isBorder = true;
            owner.borderHost = myFillOwner;

            // BD-3.3 needs no code here and that is worth recording, because "no code" is easy to mistake for
            // "not done". The `edgeDistance` sheet handed to a fill is buf.ownDistance at the OWNER's own slab
            // (ShaperFillResolver.PaintTile step 1), and this owner's shape program is the STRIP — so a
            // ByEdgeDistance ramp on a border automatically reads s(d), the strip's own field, and ramps across
            // the strip's thickness. That is the only sane meaning on a border and it is what makes a bevelled
            // or double outline expressible at all. Its shape differs from a fill's: a fill's region has one
            // edge and its ramp is monotone, a strip has two and its ramp is a RIDGE, peaking at the centre line
            // and returning to 0 at both faces. That is the honest geometry of a band, not a defect. BT-7.

            return index;
        }

        /// <summary>
        /// The three refusals, in the order they must be tested, returning the reason sentence or null.
        ///
        /// Order matters: a Subtract member's slot is refused before its quantity is even considered, and a
        /// type refusal is reported as a type refusal rather than as an availability failure — the two have
        /// DIFFERENT REMEDIES (one is fixed by adding a member that publishes the quantity, the other cannot be
        /// fixed at all from this fill), and conflating them would send an author looking for a shape change
        /// that would never help.
        /// </summary>
        /// <param name="label">
        /// What the diagnostic calls the thing that refused. Null means the node's own name, which is every
        /// pre-border caller and keeps their sentences byte-identical; a border passes "<c>&lt;node&gt; border</c>"
        /// so that BD-3.8's diagnostic names the BORDER rather than the node it traces.
        /// </param>
        static string Refuse(ShaperFillDocument doc, ShaperNode node, ShaperFillDef def,
                             ShaperQuantitySet published, bool isRoot,
                             ShaperQuantitySet leafPublished, Func<ShaperNode, ShaperQuantitySet> perLeaf,
                             string label = null)
        {
            string who = label ?? node.name;
            // FC-3.3 — a Subtract member MAY NOT own a fill. R3: "A Subtracted member contributes no values at
            // all. It removes coverage; it does not deposit heat." Paint is a deposit.
            //
            // It must be FORBIDDEN rather than left to the arithmetic, and that is the whole argument: the
            // product min(coverage_N, coverage_A) is NOT identically zero. Where a LATER member re-added over
            // the hole the bag's coverage is non-zero, so a fill on a Subtract member would paint exactly the
            // parts of the subtractor that something else put back — a region nobody authored, nobody can
            // predict, and which changes shape when an unrelated member above it is edited.
            if (!isRoot && node.mode == ShaperCombineMode.Subtract)
            {
                string reason = FillKindName(def.kind) + " on '" + who + "' cannot be attached: " +
                                ShaperQuantities.SubtractRefusal + ".";
                doc.subtractFillCount++;
                if (!doc.hasSubtractFill)
                {
                    doc.hasSubtractFill = true;
                    doc.subtractFillNode = who;
                    doc.subtractFillReason = reason;
                }
                return reason;
            }

            // FC-6.3c — refused ON TYPE, with a deliberately different sentence from the availability one.
            if (def.HasTypeRefusal())
            {
                string reason = FillKindName(def.kind) + " on '" + who + "' cannot use " +
                                ShaperQuantities.Name(def.rampQuantity) + ": " +
                                ShaperQuantities.SurfaceDirectionRefusal + ".";
                doc.typeRefusedFillCount++;
                if (!doc.hasTypeRefusedFill)
                {
                    doc.hasTypeRefusedFill = true;
                    doc.typeRefusedFillNode = who;
                    doc.typeRefusedFillReason = reason;
                }
                return reason;
            }

            // FC-4.4 — the availability gate.
            ShaperQuantitySet required = def.RequiredSet();
            int missing = ShaperQuantities.FirstMissing(published, required);
            if (missing >= 0)
            {
                string reason = UnavailableReason(node, def, (ShaperQuantity)missing, leafPublished, perLeaf, who);
                doc.unavailableFillCount++;
                if (!doc.hasUnavailableFill)
                {
                    doc.hasUnavailableFill = true;
                    doc.unavailableFillNode = who;
                    doc.unavailableFillReason = reason;
                }
                return reason;
            }

            return null;
        }

        /// <summary>
        /// FC-4.3: a COMPLETE SENTENCE naming the fill kind, the node the fill is on, the missing quantity, and
        /// WHERE IT WENT MISSING. Two shapes and no others:
        /// <list type="bullet">
        /// <item>Leaf — "Ramp-by-quantity on 'Torso' needs heat, which this shape does not publish."</item>
        /// <item>Bag — "Ramp-by-quantity on 'Body' needs heat, which member 'Belt' does not publish."</item>
        /// </list>
        /// Naming the MEMBER is what makes intersection (FC-4.2) defensible at all: intersection is a cliff,
        /// not a slope — one member that does not publish heat disables every heat-driven fill on the whole bag
        /// and every bag above it — and the mitigation is that the author is told exactly which node to fix.
        /// Without the named member, intersection would be indefensible.
        /// </summary>
        static string UnavailableReason(ShaperNode node, ShaperFillDef def, ShaperQuantity missing,
                                        ShaperQuantitySet leafPublished,
                                        Func<ShaperNode, ShaperQuantitySet> perLeaf, string label = null)
        {
            string head = FillKindName(def.kind) + " on '" + (label ?? node.name) + "' needs " +
                          ShaperQuantities.Name(missing) + ", which ";

            // "until extrusion exists" rather than a bare "not published": depth is the one unavailable
            // quantity with a SCHEDULED answer (BC-3.5 — identically zero until T-0109).
            string tail = missing == ShaperQuantity.Depth
                ? "no shape publishes until extrusion exists."
                : "this shape does not publish.";

            if (node.kind == ShaperNodeKind.Bag && node.children != null)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    ShaperNode m = node.children[i];
                    if (m == null || !m.enabled) continue;
                    if (m.mode == ShaperCombineMode.Subtract) continue;   // excluded from the intersection
                    if (!ShaperQuantities.Contains(Published(m, leafPublished, perLeaf), missing))
                        return head + "member '" + m.name + "' does not publish.";
                }
                if (!HasContributingMember(node)) return head + "this bag has no contributing member.";
            }

            return head + tail;
        }

        static bool HasContributingMember(ShaperNode bag)
        {
            if (bag.children == null) return false;
            for (int i = 0; i < bag.children.Count; i++)
            {
                ShaperNode m = bag.children[i];
                if (m != null && m.enabled && m.mode != ShaperCombineMode.Subtract) return true;
            }
            return false;
        }

        /// <summary>
        /// FC-4.2: <c>published(bag) = ⋂ { published(M) : M is an ENABLED member whose mode is Add or
        /// Intersect }</c>. A bag with no such member publishes the empty set. Subtract members are excluded
        /// from the intersection entirely.
        ///
        /// <b>Why intersection and not union.</b> Take a bag with member A publishing heat and member B
        /// publishing nothing. Under a union declaration a Ramp-by-heat on that bag BINDS and then reads
        /// something at every pixel B contributed — a zero, a stale value, whatever the sheet was initialised
        /// to. R3 says continuous quantities combine as a coverage-weighted average of the CONTRIBUTING members
        /// at that pixel, and where only B contributes there are no contributors, so the average is undefined.
        /// A union declaration therefore produces a fill that binds and is wrong across part of its own shape —
        /// BC-3.1's "silently inert" failure in its more damaging variant, because it is not inert, it is
        /// CONFIDENTLY INCORRECT. Intersection means the quantity is meaningful everywhere the bag has
        /// coverage, which is the only guarantee a fill can actually use.
        ///
        /// <b>Why Subtract members are excluded.</b> They deposit nothing, so their published set is irrelevant
        /// to what can be read at any pixel. Including them would let a subtractor that publishes nothing
        /// disable a quantity for a bag it only ever removed from — a pure false negative.
        ///
        /// Union is the reversal if intersection proves too aggressive in practice, and it is one line — but it
        /// must not be taken without also deciding what a fill reads in a non-contributing member's region,
        /// which is a real design question and not a default.
        /// </summary>
        public static ShaperQuantitySet Published(ShaperNode node, ShaperQuantitySet leafPublished,
                                                  Func<ShaperNode, ShaperQuantitySet> perLeaf)
        {
            if (node == null || !node.enabled) return ShaperQuantitySet.None;

            if (node.kind != ShaperNodeKind.Bag)
                return perLeaf != null ? perLeaf(node) : leafPublished;

            var children = node.children;
            int n = children != null ? children.Count : 0;
            bool any = false;
            ShaperQuantitySet acc = ShaperQuantitySet.None;

            for (int i = 0; i < n; i++)
            {
                ShaperNode m = children[i];
                if (m == null || !m.enabled) continue;
                if (m.mode == ShaperCombineMode.Subtract) continue;

                ShaperQuantitySet ms = Published(m, leafPublished, perLeaf);
                if (!any) { acc = ms; any = true; }
                else acc &= ms;
            }

            return any ? acc : ShaperQuantitySet.None;
        }

        /// <param name="shapeOverride">
        /// The region this owner paints, when it is NOT the node's own subtree. A border passes its STRIP
        /// program (BD-1.3); everything else passes null and gets the node compiled here.
        /// </param>
        /// <param name="anchorOverride">
        /// The fill anchor, when it is not derived from the region being painted. A border passes the NODE's
        /// anchor — BD-3.2/FC-8.4, "the anchor box is always the NODE's box, never the painted region's box" —
        /// and that is the one place the two come apart.
        /// </param>
        /// <param name="label">The owner's name for diagnostics; null means the node's own name.</param>
        static int Bind(ShaperFillDocument doc, ShaperNode node, ShaperFillDef def,
                        ShaperQuantitySet published, in ShaperMatrix parentForward, int ancestorOwner,
                        float phase01, uint seed, float canvasHalfW, float canvasHalfH, bool substituted,
                        ShaperProgram shapeOverride = null, ShaperFillAnchor? anchorOverride = null,
                        string label = null)
        {
            // FC-3.9 route (a): one compiled program per fill-owning node, seeded with the node's accumulated
            // PARENT transform so the subtree lands in the layer frame rather than in its own local frame.
            ShaperProgram shape = shapeOverride ?? ShaperCompiler.Compile(node, parentForward, phase01, seed);

            ShaperFillAnchor anchor = anchorOverride ?? ShaperFillAnchor.From(shape, canvasHalfW, canvasHalfH);
            ShaperFillProgram fill = ShaperFillCompiler.Compile(def, anchor, phase01, seed);

            if (fill.diagnostic != null)
            {
                // FC-6.5: a half-configured fill never renders empty AND never renders silently. The first half
                // is ZuiFill's own rule by value (ZuiFill.cs:163-166); the second half is the one ZuiFill
                // structurally cannot have.
                doc.fallbackFillCount++;
                if (!doc.hasFallbackFill)
                {
                    doc.hasFallbackFill = true;
                    doc.fallbackFillNode = label ?? node.name;
                    doc.fallbackFillReason = fill.diagnostic;
                }
            }

            var owner = new ShaperFillOwner
            {
                node = node,
                name = label ?? node.name,
                ancestorOwner = ancestorOwner,
                shape = shape,
                fill = fill,
                published = published,
                required = def.RequiredSet(),
                stack = shape.NewStack(),
                isSubstitutedRootFill = substituted,
            };
            doc.owners.Add(owner);
            return doc.owners.Count - 1;
        }

        static string FillKindName(ShaperFillKind k)
        {
            switch (k)
            {
                case ShaperFillKind.Solid: return "Solid";
                case ShaperFillKind.Gradient: return "Gradient";
                case ShaperFillKind.RampByQuantity: return "Ramp-by-quantity";
                case ShaperFillKind.Texture: return "Texture";
                case ShaperFillKind.IndexedStrip: return "Indexed strip";
                case ShaperFillKind.HeightField: return "Height field";
                case ShaperFillKind.TapestrySteel: return "Tapestry Steel";
                default: return "Fill";
            }
        }

        // ── the paint pass ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve ownership and composite one tile. The host owns every array; nothing is allocated here.
        ///
        /// Six passes, each over the tile, none over a neighbour sample:
        /// <list type="number">
        /// <item>Each owner's own subtree coverage, via the shipped <c>ShaperEvaluator.FillTile</c>.</item>
        /// <item><c>claim = min(coverage_N, coverage_A)</c> — FC-3.1.</item>
        /// <item>The largest DESCENDANT claim, propagated up in reverse paint order.</item>
        /// <item><c>paint = max(0, claim − descendantClaim)</c> — FC-3.5's exclusivity, as a set difference.</item>
        /// <item>Per owner, in REVERSE paint order: the fill, then
        /// <c>coverageEff = clamp01(paint) · clamp01(veil)</c>, then the owner's own contribution SUMMED into
        /// its own subtree accumulator, then that finished subtree folded <c>Over</c> into its parent's.</item>
        /// <item>The height accumulation, which is a SUM regardless of composite mode (FC-2.6c).</item>
        /// </list>
        ///
        /// <b>On step 4.</b> FC-3.5 states exclusivity as a binary rule — exactly one owner per pixel. Coverage
        /// is not binary at an antialiased edge, so the binary rule needs a continuous extension. The extension
        /// is SET DIFFERENCE, <c>max(0, claim − descendantClaim)</c>, and deliberately not the independent-
        /// probability product <c>claim · (1 − descendantClaim)</c>: <c>claim</c> is an AREA FRACTION and the
        /// min-chain guarantees a descendant's claim is a SUBSET of its ancestor's, so subtraction is the
        /// geometrically right split. The two agree at both limits and differ only where the ancestor's own
        /// claim is fractional — where the product leaves <c>c·(1−c)</c>, a 0.15..0.25 fringe of the ANCESTOR's
        /// colour tracing the whole silhouette (measured on the FC-3.8 tree: 119 leaking samples, now 0).
        ///
        /// <b>On step 5 — why the loop runs BACKWARDS, and why that is not a reordering of the fold.</b>
        /// FC-3.4's "no stage may reorder a bag's members" governs the COMPOSITE order, and the composite order
        /// here is carried by the tree structure, not by the loop counter: a child's finished subtree is folded
        /// into its parent with <c>parentAcc = parentAcc Over childAcc</c>, and because the children are reached
        /// in DECREASING index the highest-index member is placed first and therefore stays on top — which is
        /// fold order exactly, by the associativity of <c>Over</c>. Reverse pre-order is simply the order in
        /// which a subtree is guaranteed finished before its parent needs it, the same reason step 3 already
        /// iterates backwards. Evaluating a fill has no effect on any other owner, so the evaluation order is
        /// free; only the composite order is contractual, and it is preserved.
        ///
        /// <b>On step 5 — SUM along the chain, <c>Over</c> between siblings (FC-3.5a / FC-2.6a).</b> An owner's
        /// paint region is disjoint from every descendant's by step 4, so the two are two parts of ONE pixel's
        /// area and their alphas ADD; sibling claims genuinely overlap, so siblings composite <c>Over</c>. The
        /// previous build ran <c>Over</c> for both, which under-reported alpha at every internal fill boundary —
        /// measured at 244/16384 samples, total deficit 29.288, worst 0.2484, i.e. a one-pixel ring up to 25%
        /// transparent wherever one fill's region met another's inside the same silhouette. <c>Over</c> is the
        /// rule for INDEPENDENT coverage and a partition is not independent; asking it that question is what
        /// produced the seam.
        /// </summary>
        /// <b>T-0108 adds the last parameter and nothing else to this signature.</b> It is an OPTIONAL
        /// parameter and deliberately NOT a second overload: <c>ShaperBorderAudit.BT14_NoAllocation</c>
        /// reflects on this method by name alone (<c>ShaperBorderAudit.cs:1707</c>,
        /// <c>typeof(ShaperFillResolver).GetMethod("PaintTile")</c>), and a second overload makes that throw
        /// <c>AmbiguousMatchException</c> — measured, on the first regression run of T-0108. One method with a
        /// defaulted argument keeps every existing caller and every existing reflection probe working
        /// untouched, which is what "additive" has to mean for a signature.
        ///
        /// <b>LR-5.1 — lighting runs INSIDE this method, per owner, immediately after that owner's fill has
        /// emitted albedo/veil/heightDelta and BEFORE the <c>coverageEff</c> weighting and the subtree
        /// accumulation.</b> It is not a stage between the fill stage and the border stage; it is a per-sample
        /// transform applied at the moment each owner emits, and it applies to borders and non-borders by the
        /// same code.
        ///
        /// <b>LR-5.2 — why exactly there, and not one line later.</b> Three reasons, each ruling out a
        /// placement someone would otherwise pick.
        /// <list type="bullet">
        /// <item><b>Not after premultiplication.</b> <c>dst</c> and <c>subtree</c> are PREMULTIPLIED linear
        /// RGBA. Multiplying a premultiplied destination by <c>L</c> scales its ALPHA too, which is silently
        /// wrong and produces exactly the transparent-seam class of defect T-0106's fix pass measured at
        /// 244/16384 samples. LT-9 is the test, and its mutation reproduces that number.</item>
        /// <item><b>Not after the <c>coverageEff</c> weighting.</b> Lighting scales colour; coverage is a
        /// separate authority (FC-2.4). Applying <c>L</c> to <c>albedo * ce</c> gives the same number for the
        /// multiplicative half but a WRONG one for the additive half, because <c>S</c> would be weighted once
        /// instead of gated by coverage — an additive rim would spill outside the shape.</item>
        /// <item><b>Not once per document after all owners.</b> The response block is per layer and the normal
        /// provider is per owner. Once the owners are mixed into <c>dst</c> they are not separable — the same
        /// argument <c>subtree</c>'s own doc comment already makes for the exclusivity fix.</item>
        /// </list>
        ///
        /// <b>LR-5.5 — the exclusivity partition is untouched by construction.</b> Lighting is a per-sample
        /// transform of one owner's colour before that colour enters the partition arithmetic. It reads the
        /// scene's normal sheet and writes nothing but two local colour triples; <c>claim</c>,
        /// <c>descendantClaim</c>, <c>paint</c> and every alpha path are untouched, so FC-3.5a's measured
        /// result — 0 of 16384 samples below published coverage, total deficit 0.000 — is preserved.
        ///
        /// <b>Passing <c>null</c> for <paramref name="scene"/> is the pre-T-0108 build, bit for bit</b>, and
        /// so is a scene whose owner has <c>receive == 0</c> and no Solids overlay: both take the ORIGINAL
        /// accumulate expression by an explicit branch rather than by relying on <c>x*1 + 0 == x</c>. LR-4.3
        /// says "bit for bit" and that is the only way to mean it — the same structural-identity discipline
        /// <c>ShaperEvaluator</c>'s Sweep early-out already uses (<c>ShaperEvaluator.cs:88-91</c>).
        /// </summary>
        public static void PaintTile(ShaperFillDocument doc, in ShaperSampleGrid grid,
                                     int x0, int y0, int width, int height,
                                     ShaperFillBuffers buf, in ShaperFillSheets sheets,
                                     ShaperLightScene scene = null)
        {
            if (doc == null || buf == null) return;
            int n = width * height;
            int k = doc.owners.Count;
            if (n <= 0 || k <= 0) return;

            buf.ClearDestination(n);

            // 1 — each owner's own subtree distance AND coverage. Both, because those are the two quantities
            //     the shipped shape engine publishes (ShaperEvaluator.cs:148-149) and a fill must read the
            //     sheets ITS OWN node published (FC-1.2), not the layer root's.
            for (int o = 0; o < k; o++)
            {
                ShaperFillOwner ow = doc.owners[o];
                int slab = o * buf.sampleCapacity;

                // LR-6.1 — a Solids owner's coverage, edge distance AND surface normal come from the
                // generator, which REPLACES the shape stage for that owner and nothing else. Everything
                // downstream — the claim, the exclusivity partition, the fill, the border, the composite — is
                // identical to any other owner's, which is what "goes through the ordinary fill and light
                // pipeline like every other generator" has to mean if it is to mean anything.
                ShaperSolidGeometry sg = scene != null && o < scene.ownerCapacity ? scene.solid[o] : null;
                if (sg != null)
                {
                    // T-0127 — `ow.fill` and `scene.normalOp[o]` are passed so a HeightField fill on THIS
                    // Solids node can perturb the analytic normal FillTile just wrote (see its own doc comment
                    // and the perturbation block inside it). Both are trailing/optional and every OTHER call to
                    // ShaperSolids.FillTile (there are none today besides this one) would still compile and
                    // behave identically without them.
                    ShaperSolids.FillTile(sg, scene.solidOp[o], grid, x0, y0, width, height,
                                          new ShaperSolidEmit
                                          {
                                              coverage = buf.ownCoverage,
                                              distance = buf.ownDistance,
                                              normal = scene.normal,
                                              lineMask = scene.lineMask,
                                              glow = scene.glow,
                                              pointZ = scene.pointZ,
                                          },
                                          slab, width,
                                          ow.fill, scene.normalOp[o]);
                    continue;
                }

                ShaperEvaluator.FillTile(ow.shape, grid, x0, y0, width, height,
                                         buf.ownDistance, buf.ownCoverage,
                                         slab, width, ow.stack);

                // T-0109 FIX F4a — height_shape, HS-1.3: canvas pixels above the layer's own base plane,
                // from this owner's OWN edge distance (FC-1.2). This is the call ShaperHeight.FillTile did
                // not have; without it FC-2.5's sum had no `height_shape` term to be a sum OF.
                ShaperHeightOp hopO = scene != null && o < scene.ownerCapacity && scene.heightOp != null
                                    ? scene.heightOp[o] : default(ShaperHeightOp);
                ShaperHeight.FillTile(hopO, grid, x0, y0, width, height,
                                      buf.ownDistance, buf.ownHeight,
                                      slab, width, slab, width);

                // LR-3.1 — the SECOND provider, and it is genuinely different machinery: it writes an
                // authored constant and consumes no sheet, where the Solids branch above derives its vectors
                // from real geometry. That is what makes the interface real rather than a
                // single-implementation fiction, and it is what lets T-0109 add a `Profile` case to
                // ShaperNormals.FillTile while touching neither ShaperLightLaw nor one line of Solids.
                //
                // T-0109 FIX F4b — `ow.shape` and `ow.stack` ARE passed now. They used to be omitted, which
                // sent an authored `ShaperNormalKind.Profile` down FillTile's LR-3.5 fallback on every
                // sample: (0,0,1) written silently, so selecting Profile was indistinguishable from
                // selecting Constant(0,0,1). `distance` and `heightSheet` stay null on purpose — the Profile
                // case central-differences ShaperEvaluator.Distance at the sample POINT, which is a pure
                // function of a canvas point, and reads no screen-space neighbour of any buffer (LR-3.3).
                // The return value is the count of samples that still took the fallback; it is recorded
                // rather than swallowed.
                //
                // T-0109 FIX F4a — pointZ is the SURFACE point's Z (LR-1.5), which for a layer with a height
                // stage is `base + height_shape`, not 0. It used to be Array.Clear'ed with the comment "the
                // surface point's Z is 0 for a Silhouette layer", so every point lamp in the document shaded
                // every layer as a flat sheet on the base plane no matter what the height stage computed.
                if (scene != null && o < scene.ownerCapacity)
                {
                    scene.normalDegenerate[o] =
                        ShaperNormals.FillTile(scene.normalOp[o], grid, x0, y0, width, height,
                                               null, null, scene.normal, slab, width, slab, width,
                                               ow.shape, ow.stack);

                    if (hopO.present && hopO.body > 0f)
                        for (int i = 0; i < n; i++) scene.pointZ[slab + i] = hopO.baseZ + buf.ownHeight[slab + i];
                    else
                        Array.Clear(scene.pointZ, slab, n);

                    Array.Clear(scene.lineMask, slab, n);
                    Array.Clear(scene.glow, slab * 3, n * 3);
                }
            }

            // 1b — T-0109 FIX F4a: SEED the layer's height accumulator with `height_shape`, so that FC-2.5's
            //      `height_final = height_shape + heightDelta · coverageEff` is the real sum HEIGHT-SPEC
            //      Part 10 says it becomes here. Until this line `buf.height` was cleared to 0 and the only
            //      writes were the `+= heightDelta · ce` below, i.e. `0 + heightDelta · ce`.
            //
            //      The layer's own solid is HS-1.1's, defined from the LAYER's field, so the seed is the
            //      ROOT owner's slab — the owner with no binding ancestor. The per-owner slabs are still all
            //      filled (they feed `pointZ`, which is per owner because the surface point being shaded
            //      belongs to the owner being shaded), and a future per-owner height sum is then a change to
            //      this one loop rather than a re-shape of the buffers.
            if (scene != null && scene.heightOp != null)
            {
                int rootOwner = -1;
                for (int o = 0; o < k; o++) if (doc.owners[o].ancestorOwner < 0) { rootOwner = o; break; }
                if (rootOwner >= 0 && rootOwner < scene.ownerCapacity &&
                    scene.heightOp[rootOwner].present && scene.heightOp[rootOwner].body > 0f)
                {
                    int rbase = rootOwner * buf.sampleCapacity;
                    Array.Copy(buf.ownHeight, rbase, buf.height, 0, n);
                }
            }

            // 2 — the FC-3.1 claim. The root has no ancestor owner, so its cap is itself.
            for (int o = 0; o < k; o++)
            {
                int baseO = o * buf.sampleCapacity;
                int a = doc.owners[o].ancestorOwner;
                if (a < 0)
                {
                    Array.Copy(buf.ownCoverage, baseO, buf.claim, baseO, n);
                }
                else
                {
                    int baseA = a * buf.sampleCapacity;
                    for (int i = 0; i < n; i++)
                        buf.claim[baseO + i] = Mathf.Min(buf.ownCoverage[baseO + i], buf.claim[baseA + i]);
                }
                Array.Clear(buf.descendantClaim, baseO, n);
            }

            // 3 — the largest claim anywhere in each owner's DESCENDANT owners. Owners are in pre-order, so
            //     iterating in reverse guarantees every descendant is finished before its ancestor is read.
            for (int o = k - 1; o >= 1; o--)
            {
                // BD-3.5 — A BORDER IS NOT PART OF THE EXCLUSIVITY PARTITION. It contributes nothing to any
                // owner's descendantClaim, so a node's own fill is NOT punched out underneath its own outline:
                // the outline is a stroke laid on top, and an ancestor yielding its pixels to it would make a
                // translucent outline a HOLE rather than a blend — the exact misreading FC-3.5's "wins" note
                // warns about, in a new place.
                //
                // The other half of the rule needs no code and is worth naming so that "no code" is not read as
                // "not done": nothing ever writes INTO a border's own descendantClaim slab either, because a
                // border is never any owner's ancestorOwner (only a node's own fill can be inherited), and step 2
                // cleared that slab. So step 4 computes `paint = claim − 0 = claim` for a border with no branch
                // at all, which is exactly BD-3.5's "its paint is its own claim with no descendant subtraction".
                if (doc.owners[o].isBorder) continue;

                int a = doc.owners[o].ancestorOwner;
                if (a < 0) continue;
                int baseO = o * buf.sampleCapacity, baseA = a * buf.sampleCapacity;
                for (int i = 0; i < n; i++)
                {
                    // The SUBTREE claim, not just this owner's own — an ancestor must yield wherever anything
                    // below it claims, at any depth.
                    float sub = Mathf.Max(buf.claim[baseO + i], buf.descendantClaim[baseO + i]);
                    if (sub > buf.descendantClaim[baseA + i]) buf.descendantClaim[baseA + i] = sub;
                }
            }

            // 4 — exclusivity.
            for (int o = 0; o < k; o++)
            {
                int baseO = o * buf.sampleCapacity;
                for (int i = 0; i < n; i++)
                {
                    // FC-3.5 exclusivity, as a SUBTRACTION and deliberately not as `c * (1 - e)`.
                    //
                    // Both agree at the two limits (e = 0 gives c; e = 1 forces c = 1 by the min chain and
                    // gives 0), so the choice only shows where the ancestor's OWN claim is fractional — that
                    // is, along the ancestor's antialiased edge. There the two disagree and the product is
                    // wrong, because `claim` is an AREA FRACTION and the descendant's covered sub-area is a
                    // SUBSET of the ancestor's (guaranteed: claim[o] = min(ownCoverage[o], claim[ancestor]),
                    // so claim[descendant] <= claim[ancestor] transitively). Exclusive ownership of nested
                    // areas is set difference, not independent probability.
                    //
                    // MEASURED, on the FC-3.8 worked tree: where an Intersect member's claim EQUALS the bag's
                    // claim (which is the normal case at a soft edge, since the bag's coverage there IS the
                    // intersected member's), the product leaves c*(1-c) — a 0.15..0.25 fringe of the BAG's
                    // colour tracing the whole silhouette, 58 samples on that fixture. The difference gives
                    // exactly 0, which is the answer FC-3.5 states in words: "exactly ONE owner per pixel".
                    float c = buf.claim[baseO + i];
                    float e = buf.descendantClaim[baseO + i];
                    float p = c - e;
                    buf.paint[baseO + i] = p > 0f ? p : 0f;
                }
            }

            // 5/6 — the fills. REVERSE paint order, so a subtree is finished before its parent folds it in;
            //        the composite order is still fold order, carried by the tree rather than by the counter.
            //        See the "why the loop runs BACKWARDS" paragraph on this method.
            for (int o = k - 1; o >= 0; o--)
            {
                // BD-3.6 — snapshot, for every HOSTLESS border whose node's subtree ends here, the alpha its
                // ancestor's accumulator holds RIGHT NOW. At this instant that accumulator holds exactly the
                // LATER siblings' finished subtrees: every owner with an index at or above `borderSubtreeEnd`
                // has folded in, and every owner belonging to the bordered node's own subtree (indices from the
                // border's own up to `borderSubtreeEnd − 1`, this turn included) has not. That is the one moment
                // the two are separable, and the strip's coverage is scaled by `1 − it` when it is finally
                // applied at the ancestor's turn, so a member's outline stops landing on top of a later sibling
                // that owns a fill. Measured before: magenta (1.000, 0.000, 1.000) where a red opaque sibling
                // should have been.
                //
                // It is stored in the BORDER'S OWN subtree slab, which is otherwise dead — a border is never
                // painted into its own accumulator and never folds one — so this costs no allocation and no new
                // buffer, and ClearDestination already zeroes it per tile. Zero is the correct "no later
                // sibling" value, so a document without one is untouched, bit for bit.
                // `b <= o`, deliberately: the border's own index is the FIRST owner of its node's subtree and
                // `o` is the LAST, so the border sits at or below this turn, never above it.
                for (int b = o; b >= 0; b--)
                {
                    ShaperFillOwner sb = doc.owners[b];
                    if (!sb.isBorder || sb.borderHost >= 0 || sb.borderSubtreeEnd != o + 1) continue;
                    int a = sb.ancestorOwner;
                    if (a < 0) continue;
                    int snap = b * buf.sampleCapacity * 4, from = a * buf.sampleCapacity * 4;
                    for (int i = 0; i < n; i++) buf.subtree[snap + i * 4 + 3] = buf.subtree[from + i * 4 + 3];
                }

                ShaperFillOwner ow = doc.owners[o];
                // A border is applied BY ITS HOST, a few lines down, and never on its own turn. Its turn comes
                // BEFORE its host's in this reverse walk (a border is appended immediately after the node it
                // traces, so its index is higher), and at that moment the host's own contribution is not yet in
                // its accumulator — which is precisely the thing BD-3.5 requires the border to sit on top of.
                if (ow.isBorder) continue;

                int baseO = o * buf.sampleCapacity;
                int accO = baseO * 4;

                // The owner's OWN published sheets replace whatever the host passed for coverage and edge
                // distance, because those two are computed here from that owner's own compiled program. The
                // other seven come from `sheets` and are read at the SAME per-owner offset, so a host that
                // supplies one of them must lay it out as `ownerIndex * sampleCapacity + tileIndex` — the same
                // scheme buf.ownCoverage uses. No such producer exists in T-0106 (FC-4.5), so nothing depends
                // on it yet; it is stated so the first producer does not have to guess.
                ShaperFillSheets own = sheets;
                own.coverage = buf.ownCoverage;
                own.edgeDistance = buf.ownDistance;

                ShaperFillOps.FillTile(ow.fill, grid, x0, y0, width, height, own,
                                       new ShaperFillEmit { albedo = buf.albedo, veil = buf.veil,
                                                            heightDelta = buf.heightDelta },
                                       baseO, width, 0, width);

                // T-0191 — A COMPOSITE ROOT'S PICTURE IS ITS ALBEDO. The fill above still runs, and is still
                // the FC-3.2 default by clause 1, because what it writes to `veil` is what gates this owner's
                // coverage and must not be skipped; only the COLOUR it wrote is replaced. Placed here, before
                // the lighting branch and a very long way before premultiplication, because that is where an
                // albedo is defined to exist (LR-5.2) — this is a different SOURCE for the albedo, not a new
                // stage after it, so nothing downstream needs a branch.
                if (ow.compositeAlbedo != null)
                {
                    ShaperEvaluator.FillCompositeAlbedoTile(ow.compositeAlbedo, grid, x0, y0, width, height,
                                                            buf.albedo, 0, width);

                    // CLAUSE 3 — no height. IShaperCompositeSource publishes ONE thing, a picture; there is no
                    // height channel to read, so the delta is zero rather than whatever the substituted default
                    // fill happened to leave here. The LAYER's own height stage is untouched: it is driven by
                    // this node's edge distance (step 1's ShaperHeight.FillTile) and is a separate authority.
                    Array.Clear(buf.heightDelta, 0, n);
                }

                bool add = ow.fill.op.composite == ShaperFillComposite.Add;

                // ── LR-5.1 / LR-5.3: is this owner lit, and does it carry a Solids overlay? ───────────────
                //
                // LR-5.3 — an ADDITIVE fill is NOT lit. `Add` means "this is light, not paint"
                // (ShaperFillContract.cs:169-172), and multiplying emitted light by an incident-light term is
                // backwards: a lamp does not get dimmer because you put it in a dark room. FC-2.6d records
                // that the heat and soot ramps B4 identifies as the fire and explosion palettes "are usually
                // authored to Add", so this ruling is what keeps a fire emissive under a rig that would
                // otherwise plunge it into shadow. The branch is free — `add` is already computed one line up.
                //
                // The HEIGHT DELTA is unaffected (FC-2.6c): it is summed in both modes, so an additive fill
                // can still raise a surface that an Over fill on the same node gets lit by. Worth the line
                // because "Add mode" reads like it should change everything.
                bool solidOwner = scene != null && o < scene.ownerCapacity && scene.solid[o] != null;

                // T-0191 CLAUSE 4 — A COMPOSITE ROOT'S OWN PICTURE IS NOT LIT AGAIN. It arrives finished: Pyre's
                // Gem already carries its lit facets, its edge lines and its two glows, computed by Pyre's own
                // light model at bake time. Multiplying that by an incident-light term shades an image of a lit
                // object as though it were a flat albedo, which is double-lighting and reads as a mid-grey
                // wash over exactly the detail the generator was hosted for. It is the same objection LR-5.3
                // already makes of an ADDITIVE fill one branch down, for the same reason: the value is already
                // an outgoing radiance, not a reflectance.
                //
                // It overrides the layer's `receiveLighting`, which defaults to ON (ShaperLightRig.cs:217) —
                // honouring it would make the default double-light every composite in the project. The escape
                // hatch is clause 1 and costs no dial: author a fill on the composite and that fill is an
                // ordinary albedo, lit like every other owner's.
                bool doLight = scene != null && o < scene.ownerCapacity && !add &&
                               ow.compositeAlbedo == null && scene.response[o].receive != 0;
                int nslab = o * buf.sampleCapacity;

                for (int i = 0; i < n; i++)
                {
                    // coverageEff = clamp01(coverage) · clamp01(veil), and the coverage clamp happens HERE and
                    // only here — BC-3.3's "consumers clamp at use" applied at the last possible moment, so a
                    // future fog generator publishing above 1 is handled without the publisher clamping.
                    float ce = Mathf.Clamp01(buf.paint[baseO + i]) * Mathf.Clamp01(buf.veil[i]);
                    // `!(ce > 0)` and not `ce <= 0`: the two differ on NaN, and only the first rejects it.
                    // Mathf.Clamp01 is `v < 0 ? 0 : v > 1 ? 1 : v`, so it passes NaN straight through — a
                    // single NaN dial anywhere upstream would otherwise write NaN into every destination
                    // float from here on (measured: 16384/16384 before this guard). FC-6.5 says a
                    // half-configured fill never renders empty and never renders silently; it must also never
                    // render NOT-A-NUMBER, which encodes to a garbage byte with no diagnostic at all.
                    if (!(ce > 0f)) continue;

                    int a3 = i * 3, s4 = accO + i * 4;

                    // This owner's OWN contribution, SUMMED into its own subtree accumulator (FC-3.5a). It is
                    // a sum and not an Over because step 4 already made this region disjoint from every
                    // descendant's, and the accumulator holds nothing else yet but those descendants — two
                    // parts of one pixel's area, whose alphas add. Compositing them Over is what produced the
                    // 244-sample transparent seam this build fixes.
                    //
                    // LR-5.1 puts the light HERE: on the albedo, before `ce`, before the accumulation, and a
                    // very long way before premultiplication. The unlit branch is taken literally — the
                    // original three lines, unchanged — so a null scene and a receive-off layer are
                    // bit-identical to the pre-T-0108 build rather than arithmetically equal to it (LR-4.3).
                    if (!doLight && !solidOwner)
                    {
                        buf.subtree[s4 + 0] += buf.albedo[a3 + 0] * ce;
                        buf.subtree[s4 + 1] += buf.albedo[a3 + 1] * ce;
                        buf.subtree[s4 + 2] += buf.albedo[a3 + 2] * ce;
                    }
                    else
                    {
                        LightSample(scene, buf, o, nslab, i, a3, grid, x0, y0, width,
                                    doLight, solidOwner, out float cr, out float cg, out float cb);
                        buf.subtree[s4 + 0] += cr * ce;
                        buf.subtree[s4 + 1] += cg * ce;
                        buf.subtree[s4 + 2] += cb * ce;
                    }

                    if (!add)
                    {
                        float na = buf.subtree[s4 + 3] + ce;
                        // Clamped because alpha past total opacity has no meaning — FC-2.4a's argument, applied
                        // at use per BC-3.3. It CAN overshoot legitimately: descendantClaim is a MAX over the
                        // descendants (siblings may overlap, so a sum there would double-count), so two
                        // spatially disjoint siblings at 0.5 leave their parent 0.5 of its own claim while
                        // together folding to 0.75. RGB is deliberately NOT rescaled to match — an additive
                        // result carries more light than its alpha on purpose (FC-2.6b), and rescaling would
                        // dim it.
                        buf.subtree[s4 + 3] = na < 1f ? na : 1f;
                    }
                    // FC-2.6b. ADD DOES NOT INCREASE ALPHA — an additive glow over nothing stays
                    // transparent-but-bright, which is what makes it read as light rather than as paint;
                    // adding to alpha is exactly how a glow ends up looking like paint. And the veil STILL
                    // applies: coverageEff gates the additive contribution, so veil 0 contributes nothing. A
                    // veil is not an opacity in Add mode, it is a strength.
                    //
                    // FC-3.5b — the mixed case, defined rather than left to fall out: where one owner of a
                    // pixel is Over and another is Add, the pixel's ALPHA is the sum of the Over owners' claims
                    // alone and its RGB is the claim-weighted sum of EVERY owner's albedo. An Add owner
                    // contributes light and never opacity, whether it meets the destination, a sibling, or an
                    // ancestor that shares the pixel with it. That falls out of the two lines above and is
                    // stated because a reader has to be able to predict it without deriving it.

                    // FC-2.6c: THE SWITCH GOVERNS COLOUR ONLY. The height delta is ALWAYS summed, in both
                    // modes. There is no "over" meaning for a height DELTA — a delta by definition adds — and
                    // giving the Over/Add dial a second meaning on a second output is exactly the double
                    // meaning FC-2.2 refused for alpha. Stated here because "Add mode" reads like it should
                    // change everything.
                    buf.height[i] += buf.heightDelta[i] * ce;
                }

                // ── the borders hosted on THIS accumulator (BD-3.5, BD-3.6) ────────────────────────────────
                //
                // The subtree is otherwise complete: every descendant folded in on an earlier turn, and this
                // owner's own contribution went in immediately above. This is the one moment at which "above
                // everything inside the node, below anything above the node" is expressible, and it is why
                // model (b) of BD-3.5 was chosen — the ordering is correct BY CONSTRUCTION rather than by a
                // placement in the tree that has to be right for two contradictory cases at once.
                //
                // DESCENDING index, and that is BD-3.6 exactly: borders fold in fold order, so a member's border
                // folds in below the bag's own. Where several borders share one accumulator — which happens when
                // a bordered node bound no fill of its own, so its strip lands on its nearest binding ancestor's
                // slab — the deeper node has the HIGHER owner index (pre-order), so applying in descending index
                // lays the deeper outline down first and the bag's outline on top of it. The outline of the thing
                // you drew around everything is on top, which is also the answer an author predicts.
                for (int b = k - 1; b > o; b--)
                {
                    ShaperFillOwner bo = doc.owners[b];
                    if (!bo.isBorder) continue;
                    if ((bo.borderHost >= 0 ? bo.borderHost : bo.ancestorOwner) != o) continue;

                    int baseB = b * buf.sampleCapacity;

                    ShaperFillSheets bown = sheets;
                    // BD-3.3: this owner's shape program is the STRIP, so ownDistance at its own slab is the
                    // strip's field s(d) and a ByEdgeDistance ramp on the border ramps across the strip's own
                    // thickness — a ridge, peaking at the centre line. Not the node's field, which is what
                    // BD-3.2 uses for POSITIONAL inputs. The two inputs deliberately point opposite ways.
                    bown.coverage = buf.ownCoverage;
                    bown.edgeDistance = buf.ownDistance;

                    ShaperFillOps.FillTile(bo.fill, grid, x0, y0, width, height, bown,
                                           new ShaperFillEmit { albedo = buf.albedo, veil = buf.veil,
                                                                heightDelta = buf.heightDelta },
                                           baseB, width, 0, width);

                    bool badd = bo.fill.op.composite == ShaperFillComposite.Add;

                    // ── LR-5.4: A BORDER IS LIT, BY ITS HOST'S RESPONSE BLOCK AND ITS HOST'S NORMAL ──────
                    //
                    // `o` — the HOST's owner index, not `b` — indexes both. A border carries no response
                    // block of its own and no provider of its own.
                    //
                    // Why lit at all: BD-3.1 makes a border's fill an ordinary fill and BD-3.5 makes the
                    // border "a finished thing meeting what is beneath it". An unlit outline on a lit shape
                    // is the "not one scene" failure at the smallest possible scale — a bright rim on a shape
                    // lying in shadow. (This also supersedes BD-4.1's reason #2, which argued the facet edge
                    // lines could not be a border because "it is lit, and a fill may not be": under LR-5.1 a
                    // border's fill IS lit, downstream of the fill stage, with nothing smuggled anywhere.
                    // BD-4.1's ruling is unchanged, because its reasons #1 and #3 are each independently
                    // sufficient — flagged, not edited, since BD-4.1 is signed off.)
                    //
                    // Why the host's NORMAL and not the strip's — THE TRAP. BD-3.3 rules that the
                    // edgeDistance sheet handed to a border's fill is the STRIP's, and this method implements
                    // that a few lines below. The normal is the OPPOSITE, and symmetry with BD-3.3 is the
                    // obvious wrong answer: the strip is not a separate surface, it is a band painted ON the
                    // node's surface, so it faces the way the node faces. A normal derived from the strip's
                    // own field would make an outline read as a raised welt around every shape, which nobody
                    // authored.
                    //
                    // LR-5.3 applies here unchanged: a border that declares Add is not lit either.
                    bool bLight = scene != null && o < scene.ownerCapacity && !badd &&
                                  scene.response[o].receive != 0;
                    int bnslab = o * buf.sampleCapacity;

                    // BD-3.6 — a HOSTLESS strip is scaled by `1 − the later siblings' alpha`, snapshotted at the
                    // top of this loop at the one turn where that alpha is separable. `hostless == false` skips
                    // the read entirely, so a border riding its own node's accumulator is untouched; and the
                    // snapshot is 0 whenever there is no later sibling, so the overwhelmingly common document is
                    // bit-for-bit what it was.
                    bool hostless = bo.borderHost < 0 && bo.borderSubtreeEnd >= 0;
                    int snapB = b * buf.sampleCapacity * 4;

                    for (int i = 0; i < n; i++)
                    {
                        float ce = Mathf.Clamp01(buf.paint[baseB + i]) * Mathf.Clamp01(buf.veil[i]);
                        if (hostless)
                        {
                            // NaN-safe: `Mathf.Clamp01` passes NaN through, so the multiply is written to make a
                            // NaN snapshot collapse `ce` to a value the `!(ce > 0)` guard below rejects.
                            float above = buf.subtree[snapB + i * 4 + 3];
                            ce *= 1f - (above > 0f ? (above < 1f ? above : 1f) : 0f);
                        }
                        if (!(ce > 0f)) continue;                       // `!(ce > 0)` rejects NaN; `ce <= 0` does not

                        int a3 = i * 3, t = accO + i * 4;

                        // LR-5.1's identical substitution, made in the border block too — "it applies to
                        // borders and non-borders by the same code". The unlit branch is again taken
                        // literally so a null scene is bit-identical (LR-4.3).
                        float br, bg, bb;
                        if (!bLight) { br = buf.albedo[a3 + 0]; bg = buf.albedo[a3 + 1]; bb = buf.albedo[a3 + 2]; }
                        else
                        {
                            LightSample(scene, buf, o, bnslab, i, a3, grid, x0, y0, width,
                                        true, false, out br, out bg, out bb);
                        }

                        float sr = br * ce;
                        float sg = bg * ce;
                        float sb2 = bb * ce;

                        if (badd)
                        {
                            // FC-2.6b, unchanged for a border: ADD DOES NOT RAISE ALPHA. An additive outline is
                            // light laid over the subtree, not paint, and adding to alpha is exactly how a glow
                            // ends up looking like paint. BD-3.5's last paragraph settles that a border MAY
                            // declare Add and that it means there what it means everywhere else: Over-versus-Add
                            // governs how a FINISHED thing meets what is beneath it, and a border is a finished
                            // thing meeting what is beneath it.
                            buf.subtree[t + 0] += sr;
                            buf.subtree[t + 1] += sg;
                            buf.subtree[t + 2] += sb2;
                        }
                        else
                        {
                            // SOURCE-over-DESTINATION, with the border as the SOURCE — the opposite direction
                            // from the fold below, where the target is what stays on top. That asymmetry is the
                            // whole content of BD-3.5: a border is the one thing that goes ON TOP of a subtree
                            // rather than underneath the siblings already accumulated above it.
                            float inv = 1f - ce;
                            buf.subtree[t + 0] = sr + buf.subtree[t + 0] * inv;
                            buf.subtree[t + 1] = sg + buf.subtree[t + 1] * inv;
                            buf.subtree[t + 2] = sb2 + buf.subtree[t + 2] * inv;
                            float na = ce + buf.subtree[t + 3] * inv;
                            buf.subtree[t + 3] = na < 1f ? na : 1f;
                        }

                        // FC-2.6c: the switch governs COLOUR only; a height delta is always summed. A bevelled
                        // outline is a border whose fill emits height, lit later by the light rig — which is the
                        // whole reason the border stage emits no light of its own.
                        buf.height[i] += buf.heightDelta[i] * ce;
                    }
                }

                // The subtree is finished. Fold it into its parent's accumulator — or into the destination,
                // for an owner with no binding ancestor (FC-3.2 guarantees exactly one of those, the root).
                //
                // THIS is where FC-2.6a's Over lives, and it is the only place it belongs: it describes how a
                // FINISHED thing meets what is beneath it. `target Over source` — the target already holds the
                // higher-index siblings, which were reached first and must stay on top. `dst` is cleared per
                // tile, so the root's fold is an identity and the destination is the root subtree exactly.
                {
                    int p = ow.ancestorOwner;
                    float[] tgt = p >= 0 ? buf.subtree : buf.dst;
                    int baseT = p >= 0 ? p * buf.sampleCapacity * 4 : 0;

                    for (int i = 0; i < n; i++)
                    {
                        int s = accO + i * 4, t = baseT + i * 4;
                        float ta = tgt[t + 3];
                        float inv = 1f - (ta > 0f ? (ta < 1f ? ta : 1f) : 0f);   // NaN-safe clamp01, then 1−a
                        if (inv <= 0f) continue;                                  // fully occluded from above
                        tgt[t + 0] += buf.subtree[s + 0] * inv;
                        tgt[t + 1] += buf.subtree[s + 1] * inv;
                        tgt[t + 2] += buf.subtree[s + 2] * inv;
                        float na = ta + buf.subtree[s + 3] * inv;
                        tgt[t + 3] = na < 1f ? na : 1f;
                    }
                }
            }
        }

        /// <summary>
        /// One sample's finished, lit colour — LR-5.1's per-sample transform, factored out so the owner block
        /// and the border block run THE SAME CODE (LR-5.1 requires exactly that) and so there is one call site
        /// per stage to <see cref="ShaperLightLaw.Shade"/> and no second shading site anywhere (LT-1b).
        ///
        /// Allocates nothing, reads no neighbour, and reads the sheets only at this sample's own index
        /// (BC-2.1). The surface point is built from the ABSOLUTE sample index — <c>x0 + i%width</c>, not
        /// <c>i%width</c> — which is what makes tile independence structural: LT-3's second stated mutation is
        /// precisely computing it from a tile-local index, after which a point light's falloff restarts at
        /// every tile boundary.
        /// </summary>
        /// <param name="respOwner">
        /// The owner whose RESPONSE BLOCK and Solids op apply. For a border this is its HOST (LR-5.4).
        /// </param>
        /// <param name="nslab">
        /// The base index of the owner slab the NORMAL, line-mask, glow and point-Z sheets are read from. For
        /// a border this is its HOST's slab, for the same reason (LR-5.4).
        /// </param>
        static void LightSample(ShaperLightScene scene, ShaperFillBuffers buf,
                                int respOwner, int nslab, int i, int a3,
                                in ShaperSampleGrid grid, int x0, int y0, int width,
                                bool doLight, bool solidOwner,
                                out float cr, out float cg, out float cb)
        {
            int t = nslab + i;

            float ar = buf.albedo[a3 + 0], ag = buf.albedo[a3 + 1], ab = buf.albedo[a3 + 2];

            // LR-6.3 — the FACET EDGE LINE. Where the generator flagged one, the line colour is SUBSTITUTED
            // as this sample's albedo while the FACE'S NORMAL is kept, so the line is lit by the shared law
            // exactly like the face it sits on. Pyre's `k = Clamp(0.25 + lit, 0, 1.15)` (PyreRenderer.cs:4349)
            // is deleted: it is a crude hand-approximation of "lit, with a floor and a ceiling" and the shared
            // law does the lit part properly. Expected visual change, stated rather than discovered: an edge
            // line in deep shadow now falls to ambient instead of stopping at 0.25, and a fully lit one is no
            // longer clamped at 1.15 so it can blow out. A `lineAmbientBoost` dial would bring the floor back
            // and is NAMED AS DEFERRED, NOT BUILT.
            if (solidOwner && scene.lineMask[t] != 0f)
            {
                ar = scene.solidOp[respOwner].lineR;
                ag = scene.solidOp[respOwner].lineG;
                ab = scene.solidOp[respOwner].lineB;
            }

            if (doLight)
            {
                int ix = x0 + (i % width), iy = y0 + (i / width);
                float px = grid.originX + ix * grid.pixelSize;
                float py = grid.originY + iy * grid.pixelSize;
                float pz = scene.pointZ[t];                     // 0 for a Silhouette layer: LR-1.5's base plane

                int t3 = t * 3;
                ShaperLightLaw.Shade(scene.rig, scene.response[respOwner],
                                     px, py, pz,
                                     scene.normal[t3 + 0], scene.normal[t3 + 1], scene.normal[t3 + 2],
                                     scene.vx, scene.vy, scene.vz,
                                     out float lr, out float lg, out float lb,
                                     out float sr, out float sg, out float sb);

                // final = albedo * L + S — LR-2.2's two-output form, applied. The law does not clamp and
                // neither does this: the only clamp is at the byte (ShaperSrgb.EncodeToByte).
                cr = ar * lr + sr;
                cg = ag * lg + sg;
                cb = ab * lb + sb;
            }
            else { cr = ar; cg = ag; cb = ab; }

            // LR-6.4 — the halo and inner glow stay in the generator, and they are ADDITIVE and UNLIT for
            // LR-5.3's reason applied one level down: a glow is light rather than paint. They are added after
            // the law and before the coverage weighting, so they are gated by coverage like everything else.
            if (solidOwner)
            {
                int t3 = t * 3;
                cr += scene.glow[t3 + 0];
                cg += scene.glow[t3 + 1];
                cb += scene.glow[t3 + 2];
            }
        }

        // ── the one encode boundary (FC-2.3) ──────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>THE</b> <c>Color32</c> write — FC-2.3's single encode boundary, and the only one. The compositor's
        /// destination is linear and PREMULTIPLIED (a representation choice inside the compositor, FC-2.6a);
        /// this un-premultiplies it in linear, then encodes to sRGB once, producing STRAIGHT-ALPHA bytes.
        ///
        /// <b>Straight, because there is no other option a consumer can read.</b> FC-2.3 says albedo is linear
        /// and NON-premultiplied and that the encode happens once at this write; a PNG is straight-alpha by
        /// definition, with no premultiplied mode to declare; and Unity's sprite blend is
        /// <c>SrcAlpha OneMinusSrcAlpha</c>, which is the straight-alpha blend. Every shipped consumer wants
        /// this form.
        ///
        /// <b>A premultiplied encoder was deleted here, and the reason is worth keeping.</b> The previous build
        /// shipped <c>EncodePremultiplied</c>, which premultiplied in LINEAR and then sRGB-encoded the
        /// premultiplied value — <c>encode(rgb·α)</c>. That is neither straight alpha nor the conventional
        /// premultiplied-sRGB (<c>encode(rgb)·α</c>), so NEITHER kind of consumer reads it back: measured at
        /// α = 0.5 on a linear-white fill, a premultiplied consumer reconstructs 188/128 = 1.47× too bright and
        /// a straight-alpha consumer sees 188 where it should see 255, 26% too dark. Its justification was that
        /// only premultiplied can represent an additive glow over nothing (α = 0, RGB &gt; 0), and that
        /// justification is sound about premultiplication and wrong about this function: the CONVENTIONAL
        /// premultiplied encoding of that sample is <c>encode(rgb)·0 = 0</c>, which deletes the glow just as
        /// thoroughly. No 8-bit RGBA convention in a Gamma-space project carries an additive-over-transparent
        /// sample correctly, so keeping a second encoder would have traded a documented limitation for an
        /// undocumented misencoding. It had no caller.
        ///
        /// <b>Where the additive case actually lives, then.</b> Two places, both real: read the float
        /// destination directly (it is linear premultiplied and carries the glow exactly — this is what a light
        /// stage or a further composite should do), or composite over an opaque backdrop BEFORE this encode, at
        /// which point the glow is ordinary brightness and every byte is meaningful (worked example:
        /// <c>ShaperFillAudit.CompositeOverBackdrop</c>, which is how the contact sheet shows an additive fill
        /// at all). This function's own degenerate branch is deliberate and not a third answer: at
        /// <c>α ≤ 1e-6</c> it does not divide, so an additive sample keeps its COLOUR bytes and reports α = 0 —
        /// honest, and recoverable by a premultiplied blend, but most blenders will discard it.
        /// </summary>
        public static void Encode(float[] dst, Color32[] outPixels, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int d4 = i * 4;
                float a = Mathf.Clamp01(dst[d4 + 3]);
                float inv = a > MinAlpha ? 1f / a : 1f;
                outPixels[i] = new Color32(
                    ShaperSrgb.EncodeToByte(dst[d4 + 0] * inv),
                    ShaperSrgb.EncodeToByte(dst[d4 + 1] * inv),
                    ShaperSrgb.EncodeToByte(dst[d4 + 2] * inv),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }
    }
}
