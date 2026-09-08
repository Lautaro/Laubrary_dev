using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The authored border. One flat <c>[Serializable]</c> class carrying every dial, exactly the shape
    /// <see cref="ShaperFillDef"/> takes and for the same reason: a border does not nest — it is one object on
    /// one node with no children — so a <c>SerializeReference</c> class hierarchy would buy nothing and would
    /// import the managed-reference-nulls-against-a-broken-assembly hazard this project has already been bitten
    /// by once.
    ///
    /// <b>What a border IS</b> (BORDER-CONTRACT BD-1.1): a strip derived from ONE shape node's own signed
    /// distance field. A border on each member outlines each blob separately; a border on the bag outlines the
    /// fused silhouette; both at once gives an outer outline plus interior division lines, and the two do not
    /// fight because they are different nodes tracing different fields.
    ///
    /// <b>A node owns at most one border, and that is not a missing feature.</b> Two strips on one node is a bag
    /// with one child — the bag's border and the child's border are the two strips. That keeps B2's "one nesting
    /// mechanism used at every level" intact rather than adding a per-node list which duplicates it.
    ///
    /// <b>A border is optional on EVERY node, including the root.</b> Deliberately unlike FC-3.2, which makes
    /// the root's <i>fill</i> compulsory. FC-3.2's argument is that a compulsory root fill makes the
    /// nearest-ancestor search total, so there is no "no fill found" branch anywhere. Borders have no
    /// inheritance and therefore no search to make total: a node without a border simply has no strip. Nothing
    /// is gained by forcing one, and a permanent unwanted outline would be lost.
    /// </summary>
    [Serializable]
    public class ShaperBorderDef
    {
        /// <summary>
        /// The border's one structural identity. <c>false</c> emits nothing at all — no strip program, no owner,
        /// no dilation of the node's published field (BD-1.5). It is tested BEFORE the strip is compiled, so it
        /// is an exact no-op rather than a strip painted with zero alpha.
        ///
        /// OFF by default, and this default is load-bearing: <c>ShaperNode.border</c> is a plain [Serializable]
        /// class field, and Unity's serializer never writes null for one, so every node saved to an asset comes
        /// back carrying a border object nobody authored. With the old default of <c>true</c> that phantom was a
        /// live 2 px strip on every saved shape — and on a Solid it traced the solid's carrier box instead of the
        /// solid, painting a flat rectangle over the lit facets, lines and glow (a Pyramid fell from 238 colours
        /// to 4). The window's "Add edge" sets this true at the moment a person asks for an edge.
        /// </summary>
        public bool enabled = false;

        /// <summary>
        /// Where the strip sits relative to the node's edge.
        /// <see cref="ShaperShellAlignment.Centred"/> reads as <b>STRADDLING</b> in the border vocabulary: half
        /// the width inside the edge and half outside. The enum is shared with
        /// <see cref="ShaperShell"/> rather than duplicated, because BD-1.3 rules that the strip field IS
        /// <see cref="ShaperOps.Shell"/> — a second enum would be a second name for the same three expressions.
        ///
        /// Inward is the default because it is the only one today's Pyre can do
        /// (<c>PyreRenderer.cs:1021</c>), so an author porting a look gets the same picture before touching a
        /// dial; the other two are the new capability, not the new default.
        /// </summary>
        public ShaperShellAlignment alignment = ShaperShellAlignment.Inward;

        /// <summary>
        /// The <b>TOTAL</b> thickness of the strip, in canvas pixels, in ALL THREE alignments (BD-1.4).
        ///
        /// An Inward border of width 4 occupies four pixels inside the edge, an Outward border of width 4
        /// occupies four outside it, and a <b>Straddling border of width 4 occupies two on each side</b>. Stating
        /// this matters because Straddling is the one where the natural implementation — <c>abs(d) − w</c> —
        /// silently means DOUBLE. <see cref="ShaperOps.Shell"/> already writes <c>abs(d) − w/2</c>, which is why
        /// reusing it rather than re-deriving it is the safe half of BD-1.3.
        ///
        /// Units are canvas pixels at every nesting depth, which is free: the compiler's <c>σ_min</c> rescale
        /// already guarantees that a distance means pixels at any depth or scale
        /// (<see cref="ShaperMatrix.SigmaMin"/>), so a 2px outline on a member scaled to a quarter size is still
        /// 2px on screen.
        ///
        /// It is a <see cref="ZUIValue"/>, so it animates over the layer's life, and it is resolved AT COMPILE
        /// TIME through <see cref="ShaperValue.Sample"/> like every other dial (FC-5.3). Animating the width
        /// therefore costs a recompile of one node's strip program per frame and NOTHING per sample.
        /// </summary>
        public ZUIValue width = new ZUIValue(2f);

        /// <summary>
        /// BD-2.3. <c>true</c> (the default, per design C5) means an outward strip JOINS the node's published
        /// coverage, so a bag containing the node sees the outline as part of the member and a mask made from it
        /// includes the outline. The join is a DILATION and not a union — see
        /// <see cref="ShaperBorder.ApplyJoin"/> for why the obvious <c>min</c> produces a visible seam.
        ///
        /// <c>false</c> means <b>drawn, not counted</b> — and any UI over this dial must say it in those words,
        /// because the consequence is that painted alpha and published coverage then disagree: a mask built from
        /// this layer will not contain the outline that is visibly on screen. That is what the switch is for and
        /// it is the only thing it does, but it is exactly the sort of divergence that reads as a bug six months
        /// later.
        ///
        /// A second consequence follows from BD-3.4 rather than from this dial: on a NON-ROOT node, opting out
        /// also means the part of the strip lying outside the parent's silhouette is clipped away, because the
        /// parent's coverage clips the strip and the parent never learned about it. On the root — where the use
        /// case actually lives — there is no ancestor and nothing is clipped.
        ///
        /// It has no effect at all on an Inward border, whose outward reach is zero.
        /// </summary>
        public bool joinsCoverage = true;

        /// <summary>
        /// The strip's fill — the same type, the same four kinds, the same compiler, the same blittable op and
        /// the same tile call as any other fill (BD-3.1). Gradient outlines, textured outlines, animated
        /// outlines and ramp-by-quantity outlines are all free, and this stage adds NO new fill machinery.
        ///
        /// <b>Null is the default and it binds anyway.</b> A border with no fill authored is a border with no
        /// colour, which is nothing. Rather than inventing a second default, a null fill resolves to
        /// <see cref="ShaperFillDef.DefaultRootFill"/> — the same guaranteed-binding Solid FC-3.2 uses for the
        /// root — so a border which is switched on always draws something and can never be silently inert.
        ///
        /// Two rules bind this field and neither is enforced here, because a border does not know where its node
        /// sits: BD-3.7 (a Subtract member may not own a border at all) and BD-3.8 (a fill that refuses on
        /// availability falls back to the default Solid rather than vanishing). Both live in
        /// <see cref="ShaperFillResolver"/>, which is the one place that knows the tree.
        /// </summary>
        public ShaperFillDef fill;

        /// <summary>
        /// BD-2.1 — the <b>outward reach</b>: how far past the node's boundary the strip goes.
        /// <c>w</c> for Outward, <c>w/2</c> for Straddling, <c>0</c> for Inward.
        ///
        /// Only the reach matters to publication; the rest of the strip is inside the node and changes nothing
        /// a parent, a mask or a culling box can see. It is the single number BD-2.2's dilation subtracts and
        /// the single number BD-2.4 grows the culling box by.
        ///
        /// Written as a static taking a resolved width because the width is a <see cref="ZUIValue"/> that must
        /// be sampled exactly once per compile (FC-5.3) — a convenience overload that sampled it again here
        /// would be a second, differently-phased read of the same dial.
        /// </summary>
        public static float OutwardReach(ShaperShellAlignment alignment, float resolvedWidth)
        {
            if (resolvedWidth <= 0f) return 0f;
            switch (alignment)
            {
                case ShaperShellAlignment.Outward: return resolvedWidth;
                case ShaperShellAlignment.Inward: return 0f;
                default: return resolvedWidth * 0.5f;   // Centred == Straddling
            }
        }

        /// <summary>This border's outward reach at an already-resolved width. See <see cref="OutwardReach"/>.</summary>
        public float ReachAt(float resolvedWidth) => OutwardReach(alignment, resolvedWidth);
    }
}
