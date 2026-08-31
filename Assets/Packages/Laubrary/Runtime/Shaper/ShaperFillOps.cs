using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The per-sample fill expressions and the block entry point.
    ///
    /// The inner loop is a flat walk over a struct and a flat float array with a switch: no virtual method, no
    /// interface call, no delegate, no allocation, no boxing, no LINQ and no <c>System.Random</c> (FC-5.1).
    /// Every dial was resolved into the op at compile time (FC-5.3), so no value funnel is called from inside
    /// it (BC-1.2). It reads only its OWN sample — never <c>[i−1]</c> or <c>[i+width]</c> — which is what makes
    /// tile independence structural rather than a convention (BC-1.6, BC-2.1).
    ///
    /// <b>Order of operations within one sample is FIXED (FC-5.6), so two implementations cannot disagree:</b>
    /// <list type="number">
    /// <item>Compute the anchor coordinate (positional fills only).</item>
    /// <item>Read the declared input sheets at this sample.</item>
    /// <item>Compute the fill's own parameter <c>t</c> — clamped or wrapped per kind.</item>
    /// <item>Look up or compute albedo, in LINEAR.</item>
    /// <item>Compute the veil; multiply in any kind-specific mask; clamp to [0,1].</item>
    /// <item>Compute the height delta, if declared.</item>
    /// <item>Write all three. <b>THE FILL DOES NOT COMPOSITE.</b></item>
    /// </list>
    /// Step 7's last sentence is load-bearing: keeping the composite out of the fill is what lets the resolver
    /// decide ownership once per pixel and lets Over and Add be a two-line branch in ONE place rather than
    /// duplicated into every fill kind. Compositing lives in <see cref="ShaperFillResolver"/>.
    /// </summary>
    public static class ShaperFillOps
    {
        /// <summary>Two pi, for the angular mode's wrap. Named because an anonymous 6.2831855 is a magic number.</summary>
        const float TwoPi = 6.2831853071795865f;

        /// <summary>
        /// T-0110, B6 — the weight a strip slot's height is added at when the pixel is in the "plain fill"
        /// zone (beyond the reach), against a weight of 1 when "patterned" (within it): "the renderer adds
        /// that height into the pixel's depth at full weight where the strip is patterned and at a quarter
        /// weight in plain fill." <see cref="Sample"/>'s <c>IndexedStrip</c> case interpolates this weight
        /// continuously by the SAME <c>edgeCoverage</c> the colour blends by, so the height and the colour
        /// never disagree about where the pattern is — a hard step in one and a soft ramp in the other would
        /// show as a seam in the relief that the paint does not have.
        /// </summary>
        const float StripPlainHeightWeight = 0.25f;

        /// <summary>
        /// Fill a rectangular tile in ONE call, writing into host-supplied flat arrays.
        ///
        /// The signature mirrors <c>ShaperEvaluator.FillTile</c> (<c>ShaperEvaluator.cs:133-136</c>) including
        /// <paramref name="dstOffset"/> and <paramref name="dstStride"/>, so the fill stage and the shape stage
        /// tile identically and a host can drive both from one loop. The host owns, allocates and sizes every
        /// array; the fill never allocates, replaces, resizes or frees one (BC-3.7f, extended to outputs).
        ///
        /// Any output array may be null — a consumer allocates only what it asked for, exactly as the shape
        /// stage lets either sheet be null (<c>ShaperEvaluator.cs:148-149</c>).
        /// </summary>
        /// <param name="x0">Absolute sample index of the tile's left column.</param>
        /// <param name="y0">Absolute sample index of the tile's bottom row.</param>
        /// <param name="dstOffset">Index in the destination arrays of the tile's first sample.</param>
        /// <param name="dstStride">Row stride of the destination arrays; pass <paramref name="width"/> for a packed tile.</param>
        public static void FillTile(ShaperFillProgram program, in ShaperSampleGrid grid,
                                    int x0, int y0, int width, int height,
                                    in ShaperFillSheets sheets, in ShaperFillEmit emit,
                                    int srcOffset, int srcStride,
                                    int dstOffset, int dstStride)
        {
            if (program == null) return;

            ShaperFillOp op = program.op;
            float[] bulk = program.bulk;

            // Resolved ONCE per tile, not per sample: which scalar sheet the ramp reads, and whether the edge
            // sheet the ByEdgeDistance mode needs is actually present. A fill that declared a quantity the host
            // did not supply should never have bound (the gate is FC-4.4, in the resolver) — this is a
            // defensive read guard, never the declaration (BC-3.7a).
            float[] rampSheet = op.kind == ShaperFillKind.RampByQuantity ? sheets.Sheet(op.rampQuantity) : null;
            // FC-1.2 / FT-12: a fill reads ONLY the sheets it DECLARED. `ByEdgeDistance` is the single mode
            // whose RequiredSet contains edgeDistance, so every other kind and mode must not index the array
            // at all. Taking it unconditionally used to be invisible to FT-12 — that test measures reads
            // BEHAVIOURALLY (perturb a sheet, see whether the output moves), and a read whose value is
            // discarded moves nothing. It was still a real undeclared read: handing a Solid fill a shorter
            // edgeDistance array threw IndexOutOfRange, which is what a host allocating strictly by the
            // declared set (BC-3.7a, "declared-then-allocated") is entitled to do.
            // T-0110: IndexedStrip's RequiredSet() is unconditionally EdgeDistance (its reach test always reads
            // it, unlike Gradient's four modes where only ByEdgeDistance does) — so it belongs in this gate on
            // exactly the same terms Gradient's one mode does.
            float[] edgeSheet = (op.kind == ShaperFillKind.Gradient &&
                                 op.gradientMode == ShaperGradientMode.ByEdgeDistance) ||
                                op.kind == ShaperFillKind.IndexedStrip
                ? sheets.edgeDistance : null;

            float[] albedo = emit.albedo;
            float[] veilOut = emit.veil;
            float[] heightOut = emit.heightDelta;

            for (int j = 0; j < height; j++)
            {
                float y = grid.originY + (y0 + j) * grid.pixelSize;
                int srcRow = srcOffset + j * srcStride;
                int dstRow = dstOffset + j * dstStride;

                for (int i = 0; i < width; i++)
                {
                    // FC-1.6: computed from the ABSOLUTE sample index, exactly as ShaperSampleGrid.X(ix) does
                    // (ShaperEvaluator.cs:31-32) — never from a per-tile local origin. Fixed space is the case
                    // that passes every visual check on a whole-grid render and seams on the first decomposed
                    // one, which is exactly what BC-1.6 exists to catch.
                    float x = grid.originX + (x0 + i) * grid.pixelSize;

                    int s = srcRow + i;
                    int d = dstRow + i;

                    float edge = edgeSheet != null ? edgeSheet[s] : 0f;
                    float q = rampSheet != null ? rampSheet[s] : 0f;

                    // `sampleHeight`, not `height` — this method's own parameter `height` is the TILE's height
                    // in samples, and C# forbids a nested local from shadowing it.
                    Sample(in op, bulk, x, y, edge, q,
                           out float r, out float g, out float b, out float veil, out float sampleHeight);

                    if (albedo != null)
                    {
                        int a3 = d * 3;
                        albedo[a3 + 0] = r;
                        albedo[a3 + 1] = g;
                        albedo[a3 + 2] = b;
                    }
                    if (veilOut != null) veilOut[d] = veil;
                    // A fill that did not DECLARE height still writes zero when the host allocated the sheet,
                    // so FT-13's "every output sample is written" holds for the whole tile rather than for a
                    // kind-dependent subset. T-0110: the height is now computed BY Sample() itself, per sample —
                    // every kind but IndexedStrip returns the same `op.emitsHeight != 0 ? op.height : 0f` this
                    // line used to compute inline, bit-identically; IndexedStrip is the one kind whose height
                    // genuinely varies per sample (FC-2.5's "T-0110's whole interface").
                    if (heightOut != null) heightOut[d] = sampleHeight;
                }
            }
        }

        /// <summary>
        /// One sample of one fill. A static switch on the op kind, exactly as <c>ShaperSdf.Evaluate</c> is
        /// called per sample with a switch on the primitive kind — the flat form is a struct and a switch,
        /// which for a fill (one object on one node, no children) is strictly SIMPLER than an abstract class
        /// with four subclasses rather than a concession to performance.
        /// </summary>
        /// <param name="edge">The signed edge distance at this sample. <b>NEGATIVE INSIDE</b> — see the polarity note in <see cref="ByEdgeDistanceT"/>.</param>
        /// <param name="q">The ramp's picked quantity at this sample, already fetched from the right sheet.</param>
        /// <param name="height">
        /// T-0110 — the height delta at THIS sample, layer-local units. For every kind but
        /// <see cref="ShaperFillKind.IndexedStrip"/> this is the same compile-time constant
        /// <c>ShaperFillOps.FillTile</c> used to compute inline (<c>op.emitsHeight != 0 ? op.height : 0f</c>),
        /// bit-identically — moving it into <c>Sample</c> is what lets IndexedStrip be the one kind whose
        /// height genuinely varies per sample without touching <c>FillTile</c>'s loop or its signature.
        /// </param>
        public static void Sample(in ShaperFillOp op, float[] bulk, float x, float y, float edge, float q,
                                  out float r, out float g, out float b, out float veil, out float height)
        {
            veil = op.veil;
            height = op.emitsHeight != 0 ? op.height : 0f;

            switch (op.kind)
            {
                case ShaperFillKind.Solid:
                {
                    // Three constants resolved at compile time; the loop is three stores. This fill ALWAYS
                    // BINDS — it requires nothing — which is what makes it the mandatory root fill (FC-3.2)
                    // and the fallback target of the availability gate (FC-4.4).
                    r = op.colR; g = op.colG; b = op.colB;
                    return;
                }

                case ShaperFillKind.Gradient:
                {
                    float t;
                    if (op.gradientMode == ShaperGradientMode.ByEdgeDistance)
                    {
                        t = ByEdgeDistanceT(edge, op.invDepthPixels);
                    }
                    else
                    {
                        Anchor(in op, x, y, out float u, out float v);
                        float du = u - op.centreX;
                        float dv = v - op.centreY;
                        switch (op.gradientMode)
                        {
                            case ShaperGradientMode.Linear:
                                // ZuiFill.cs:197-198 by value: project onto the axis through the centre, scale
                                // by the reciprocal SIZE, then remap −1..1 to 0..1.
                                t = Mathf.Clamp01(((du * op.cosTheta + dv * op.sinTheta) * op.invSize + 1f) * 0.5f);
                                break;

                            case ShaperGradientMode.Radial:
                                // ZuiFill.cs:215-217 by value: measured FROM the centre, so an off-centre
                                // gradient drifts toward a border rather than squashing.
                                t = Mathf.Clamp01(Mathf.Sqrt(du * du + dv * dv) * op.invSize);
                                break;

                            default:   // Angular
                                // frac, NOT clamp01: an angular ramp is CYCLIC and clamping would pin a hard
                                // band at the wrap. The wrap seam is visible unless the gradient's first and
                                // last stops match — that is inherent, and the UI says so rather than the fill
                                // trying to hide it.
                                float a = Mathf.Atan2(dv, du) - Mathf.Atan2(op.sinTheta, op.cosTheta);
                                t = a / TwoPi;
                                t -= Mathf.Floor(t);
                                break;
                        }
                    }
                    Lut(in op, bulk, t, out r, out g, out b);
                    return;
                }

                case ShaperFillKind.RampByQuantity:
                {
                    float t = op.hardStep != 0
                        // FC-6.3a: the meaningful limit of an infinitely narrow window — a two-colour threshold.
                        ? (q >= op.inputLow ? 1f : 0f)
                        : Mathf.Clamp01((q - op.inputLow) * op.invInputSpan);
                    Lut(in op, bulk, t, out r, out g, out b);
                    return;
                }

                case ShaperFillKind.Texture:
                {
                    Anchor(in op, x, y, out float u, out float v);

                    // Rotate the UV frame about the anchor centre (the anchor coordinate is already centred).
                    float ru = u * op.texCosTheta + v * op.texSinTheta;
                    float rv = -u * op.texSinTheta + v * op.texCosTheta;

                    float uu, vv;
                    if (op.mapping == ShaperTextureMapping.Tiled)
                    {
                        // The image repeats at an authored density independent of the node's size.
                        uu = ru * op.tilesX + op.offsetU;
                        vv = rv * op.tilesY + op.offsetV;
                        uu -= Mathf.Floor(uu);
                        vv -= Mathf.Floor(vv);
                    }
                    else
                    {
                        // Fitted: the normalised anchor box (±1) maps to the full 0..1 UV exactly once, so the
                        // image stretches to the node and never repeats. Outside the box the edge texel is
                        // held, because "never repeats" is the whole meaning of Fitted. Default, because a
                        // first-time author dropping a texture on a shape expects to see the whole image.
                        uu = Mathf.Clamp01(ru * 0.5f + 0.5f + op.offsetU);
                        vv = Mathf.Clamp01(rv * 0.5f + 0.5f + op.offsetV);
                    }

                    // FC-6.4b: POINT filtering only, and there is no filter dial. This is a pixel-art tool;
                    // bilinear on a pixel-art texture is the defect rather than the feature, and a dial that is
                    // always set to one value is clutter. If smoothing is ever wanted it is a new dial then, on
                    // evidence.
                    int tx = (int)(uu * op.texWidth);
                    int ty = (int)(vv * op.texHeight);
                    if (tx < 0) tx = 0; else if (tx >= op.texWidth) tx = op.texWidth - 1;
                    if (ty < 0) ty = 0; else if (ty >= op.texHeight) ty = op.texHeight - 1;

                    int o = op.texOffset + (ty * op.texWidth + tx) * ShaperFillCompiler.TexelChannels;
                    r = bulk[o + 0] * op.tintR;
                    g = bulk[o + 1] * op.tintG;
                    b = bulk[o + 2] * op.tintB;

                    // FC-6.4c: the texture's alpha multiplies into the VEIL, not into the albedo. Albedo has no
                    // alpha (FC-2.2) so there is nowhere else for it to go, and the veil is exactly right: a
                    // transparent texel means "this pattern does not cover here", which is what a veil says.
                    veil = Mathf.Clamp01(op.veil * bulk[o + 3]);
                    return;
                }

                case ShaperFillKind.IndexedStrip:
                {
                    if (op.stripSlotOffset < 0 || op.stripSlotCount <= 0)
                    {
                        // Empty strip degenerated to Solid(plainColour) at compile time (BakeStrip); op.kind is
                        // already Solid in that case, so this branch is unreachable in practice and exists only
                        // as a defensive guard against a hand-built op.
                        r = op.colR; g = op.colG; b = op.colB;
                        return;
                    }

                    Anchor(in op, x, y, out float u, out float v);

                    // B6: the parameter is the angle around the shape, or a projection across it.
                    float t;
                    if (op.stripAngular != 0)
                    {
                        // Angular — identical construction to Gradient's Angular mode (ShaperGradientMode.Angular
                        // above): frac, not clamp, because the parameter is cyclic.
                        float a = Mathf.Atan2(v, u) - Mathf.Atan2(op.stripSinTheta, op.stripCosTheta);
                        t = a / TwoPi;
                        t -= Mathf.Floor(t);
                    }
                    else
                    {
                        // Projection — a linear projection across the node's own ±1 anchor box, unscaled (the
                        // strip always spans the shape edge-to-edge; there is no separate "size" dial the way
                        // Gradient's Linear mode has, because whole-number `stripRepeats` is what subdivides it).
                        float proj = u * op.stripCosTheta + v * op.stripSinTheta;
                        t = Mathf.Clamp01(proj * 0.5f + 0.5f);
                    }

                    // Whole-number repeats (3D Shaper's improvement, B6) and the position offset, both wrapping.
                    float raw = t * op.stripRepeats + op.stripOffset;
                    float frac = raw - Mathf.Floor(raw);

                    int slot = (int)(frac * op.stripSlotCount);
                    if (slot < 0) slot = 0; else if (slot >= op.stripSlotCount) slot = op.stripSlotCount - 1;

                    int o = op.stripSlotOffset + slot * 4;
                    float slotR = bulk[o + 0], slotG = bulk[o + 1], slotB = bulk[o + 2], slotHeight = bulk[o + 3];

                    // T-0110 fix pass: the constant here was 1, which put the START of the soft transition
                    // exactly at the reach depth (edge == -reachPixels) rather than the END of it, so at the
                    // documented, authored default reach == 1 (reachPixels == span) the shape's OWN deepest
                    // point (e.g. a disc's centre, depthInto == 1 == reachFraction) landed exactly on
                    // edgeCoverage == 0 — fully PLAIN — which contradicts both B6 ("turn it to maximum and it
                    // covers the WHOLE shape") and this op's own BakeStrip/ShaperFillDef doc comments, which
                    // already claimed the opposite. Measured before this fix: a 40px-radius disc at the default
                    // reach, sampled at its exact centre, returned the plain colour and a quarter-weight height,
                    // not the strip's.
                    //
                    // The correct constant is 2, derived from matching the reference's HARD rule
                    // (`depthInto <= depth` -- REF-HEIGHT-MATHS.md SS-2.4) at its own transition point and then
                    // softening ONLY on the far (plain) side, never pulling the near (patterned) side inward:
                    // writing x = depthInto/reachFraction = -edge/reachPixels, the reference's hard rule is
                    // "patterned iff x <= 1". The soft version used here is `coverage = clamp01(2 - x)`, which
                    // is IDENTICALLY 1 (fully patterned, matching the hard rule exactly) for the whole core
                    // region x in [0,1], and ramps linearly down to 0 over x in [1,2] -- a transition band one
                    // reach-width wide, entirely beyond the hard rule's own cutoff. Substituting
                    // x = -edge*invReach: `coverage = clamp01(2 - (-edge*invReach)) = clamp01(2 + edge*invReach)`.
                    // At edge == 0 (the silhouette): 2 + 0 = 2, clamps to 1 (patterned) -- unchanged from before.
                    // At the default reach == 1, for ANY point inside the shape (depthInto in [0,1], so x in
                    // [0,1] too since reachFraction == 1): coverage == 1 identically -- the disc's centre now
                    // reads fully patterned, matching B6.
                    float edgeCoverage = Mathf.Clamp01(2f + edge * op.stripInvReach);

                    r = Mathf.Lerp(op.plainColR, slotR, edgeCoverage);
                    g = Mathf.Lerp(op.plainColG, slotG, edgeCoverage);
                    b = Mathf.Lerp(op.plainColB, slotB, edgeCoverage);

                    // B6: full weight ("patterned") / a quarter weight ("plain fill") — interpolated by the SAME
                    // edgeCoverage the colour blends by (StripPlainHeightWeight's doc explains why), and ADDED
                    // to the common heightDelta dial rather than replacing it (FC-2.5: "ADDED to the shape's own
                    // height"), so the common dial still works as a uniform nudge on top of the strip's relief.
                    float weight = Mathf.Lerp(StripPlainHeightWeight, 1f, edgeCoverage);
                    height = (op.emitsHeight != 0 ? op.height : 0f) + slotHeight * weight;
                    return;
                }

                default:
                    r = op.colR; g = op.colG; b = op.colB;
                    return;
            }
        }

        /// <summary>
        /// FC-1.5 / FC-1.6, per sample: map the canvas point through the baked matrix (the node's accumulated
        /// inverse in Stamped space, identity in Fixed space), recentre on the anchor box, and divide by the
        /// baked reciprocal half-extents.
        ///
        /// A degenerate anchor stored ZERO reciprocals (FC-1.5b), so this returns (0,0) and never divides,
        /// never produces NaN and never refuses to bind.
        /// </summary>
        public static void Anchor(in ShaperFillOp op, float x, float y, out float u, out float v)
        {
            float lx = op.m00 * x + op.m01 * y + op.m02;
            float ly = op.m10 * x + op.m11 * y + op.m12;
            u = (lx - op.anchorCx) * op.invHx;
            v = (ly - op.anchorCy) * op.invHy;
        }

        /// <summary>
        /// <c>t = clamp01(−edgeDistance / max(depthPixels, ε))</c>.
        ///
        /// <b>THE NEGATION IS THE POLARITY, AND IT IS A STATED TRAP.</b> The shipped field is signed and
        /// NEGATIVE INSIDE (<c>ShaperField.cs:9-10</c>), so <c>−edgeDistance</c> is depth-into-the-shape in
        /// canvas pixels: <c>t = 0</c> at the boundary, <c>t = 1</c> at <c>depthPixels</c> deep, <c>t = 0</c>
        /// outside. BC records that Pyre's existing <c>BorderInsideDistance</c> runs the OPPOSITE direction —
        /// unsigned and growing inward — and that the band expression it feeds "inverts if it is ported
        /// literally onto a negative-inside quantity" (<c>BUFFER_CONTRACT.md:190</c>). Anything ported from
        /// Pyre into this expression needs the sign flipped.
        /// </summary>
        public static float ByEdgeDistanceT(float edge, float invDepthPixels)
            => Mathf.Clamp01(-edge * invDepthPixels);

        /// <summary>
        /// The 256-entry point lookup. <paramref name="t"/> is already in [0,1) or [0,1].
        /// When there is no LUT (a null gradient degenerated the op to Solid at compile time) this is never
        /// reached, but the guard keeps a hand-built op from indexing a null array.
        /// </summary>
        static void Lut(in ShaperFillOp op, float[] bulk, float t, out float r, out float g, out float b)
        {
            if (op.lutOffset < 0 || bulk == null)
            {
                r = op.colR; g = op.colG; b = op.colB;
                return;
            }
            int idx = (int)(t * ShaperFillCompiler.LutEntries);
            if (idx < 0) idx = 0;
            else if (idx >= ShaperFillCompiler.LutEntries) idx = ShaperFillCompiler.LutEntries - 1;
            int o = op.lutOffset + idx * ShaperFillCompiler.LutChannels;
            r = bulk[o + 0]; g = bulk[o + 1]; b = bulk[o + 2];
        }
    }
}
