using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The anchor a fill normalises its sampling point by (FC-1.5). Produced by the shape compiler and handed
    /// to the fill compiler; never authored.
    ///
    /// <b>Why the node-LOCAL box and not the canvas one.</b> <c>ShaperProgram.supportCx/…/supportHalfH</c>
    /// exists and is tempting, and it is the wrong number: it is built by mapping the primitive's local
    /// half-extents through the FORWARD matrix with an absolute-value corner sum
    /// (<c>ShaperCompiler.cs:178-179</c>), so it is an axis-aligned box in canvas space that GROWS AND SHRINKS
    /// AS THE NODE ROTATES. Normalising by it would make a gradient on a spinning shape breathe once per
    /// quarter turn with nothing authored changing — B5's swim problem wearing a different hat.
    ///
    /// The local box is the primitive's own declared numbers, fixed before any transform touches them, so a
    /// node that rotates carries its pattern round with it and the pattern does not breathe. That is what
    /// "stamped onto the shape" means.
    /// </summary>
    public struct ShaperFillAnchor
    {
        /// <summary>
        /// Canvas → this node's own local space: the full accumulated root→node inverse, one 2×3. This is the
        /// same number <c>ShaperCompiler.EmitLeaf</c> already bakes per leaf (<c>ShaperCompiler.cs:171-172</c>)
        /// so a leaf is reached with one 2×3 and no point stack. Nothing new is computed; one number is exposed.
        /// </summary>
        public ShaperMatrix inverse;

        /// <summary>Node-local support box centre and half-extents (FC-1.5a). Valid only when <see cref="localValid"/>.</summary>
        public float localCx, localCy, localHalfW, localHalfH;
        public bool localValid;

        /// <summary>The layer canvas half-extents, for <see cref="ShaperFillSpace.Fixed"/> (FC-1.6).</summary>
        public float canvasHalfW, canvasHalfH;

        /// <summary>The anchor of a compiled shape program, with the canvas extents the host is rendering at.</summary>
        public static ShaperFillAnchor From(ShaperProgram program, float canvasHalfW, float canvasHalfH)
            => new ShaperFillAnchor
            {
                inverse = program.rootInverse,
                localCx = program.localSupportCx,
                localCy = program.localSupportCy,
                localHalfW = program.localSupportHalfW,
                localHalfH = program.localSupportHalfH,
                localValid = program.hasLocalSupport,
                canvasHalfW = canvasHalfW,
                canvasHalfH = canvasHalfH,
            };
    }

    /// <summary>
    /// One compiled fill: a flat blittable op plus the host-owned bulk data it indexes into.
    ///
    /// FC-5.2: a fill compiles to a flat, blittable op form invoked through a static entry point that switches
    /// on the op kind, exactly as <c>ShaperEvaluator.Distance</c> switches on <c>ShaperOp.kind</c>
    /// (<c>ShaperEvaluator.cs:57</c>). Read literally, BC-1.1 would PERMIT a per-tile virtual call — it forbids
    /// invocation "once per sample" through a managed callback, and once per tile is not once per sample. What
    /// decides it is BC-1.2's Burst-addressability clause plus BC-1.4's "if BC-1 is broken, BC-2 is
    /// decorative": a virtual method on a managed class cannot be Burst-compiled at all, so an
    /// object-per-tile design satisfies BC-1.1's letter and forfeits BC-1.4's purpose.
    /// </summary>
    public sealed class ShaperFillProgram
    {
        public ShaperFillOp op;

        /// <summary>
        /// FC-5.5: the ONE slot for things that are not floats. A compiled op may reference host-owned bulk
        /// data by index — an offset, a width, a height and a stride into a flat host array — and MUST NOT hold
        /// a <c>Texture2D</c>, <c>Sprite</c>, <c>Gradient</c> or any other managed reference. Gradient uses it
        /// for its LUT, Texture for its pixels, and T-0111's cached Tapestry surfaces will use it with no
        /// contract change.
        /// </summary>
        public float[] bulk = System.Array.Empty<float>();

        /// <summary>
        /// The FC-6.5 fallback diagnostic, or null. "A half-configured fill NEVER renders empty and NEVER
        /// renders silently": the first half is <c>ZuiFill.cs:163-166</c> by value, the second half is BC-3.1,
        /// and it is the half <c>ZuiFill</c> structurally cannot have.
        /// </summary>
        public string diagnostic;

        /// <summary>The normalised node clock this fill was compiled for. Part of the cache key — see <see cref="ShaperFillCompiler"/>.</summary>
        public float phase01;
    }

    /// <summary>
    /// One instruction's worth of compiled fill. Flat numbers only: the per-sample loop reads this struct and
    /// the bulk array and nothing else, so there is no virtual dispatch, no delegate, no managed dereference
    /// and no boxing. Booleans are stored as ints because <c>bool</c> has no guaranteed blittable width.
    /// </summary>
    public struct ShaperFillOp
    {
        public ShaperFillKind kind;
        public ShaperFillComposite composite;
        public ShaperGradientMode gradientMode;
        public ShaperTextureMapping mapping;
        public ShaperQuantity rampQuantity;

        /// <summary>1 when this fill reads the anchor coordinate at all. Solid and Ramp are non-positional (FC-1.6).</summary>
        public int positional;
        /// <summary>1 for <see cref="ShaperFillSpace.Fixed"/>.</summary>
        public int fixedSpace;
        /// <summary>1 when <c>heightDelta != 0</c> at compile time — the FC-2.5 declaration.</summary>
        public int emitsHeight;

        /// <summary>Canvas → node-local, the accumulated root→node inverse (FC-1.5).</summary>
        public float m00, m01, m02, m10, m11, m12;
        /// <summary>The node-local support box centre, subtracted before the divide.</summary>
        public float anchorCx, anchorCy;
        /// <summary>
        /// Reciprocal anchor half-extents, with <see cref="ShaperFillFit"/> already applied — <c>Uniform</c>
        /// puts <c>1/max(hx,hy)</c> in both, <c>Stretch</c> puts <c>1/hx</c> and <c>1/hy</c>.
        /// <b>ZERO when the anchor is degenerate</b> (FC-1.5b): the coordinate becomes (0,0), the fill still
        /// evaluates and binds, and nothing ever divides. Mirrors the shipped engine's singular-transform rule,
        /// which publishes the empty field and flags rather than dividing (<c>ShaperCompiler.cs:131-142</c>).
        /// </summary>
        public float invHx, invHy;

        /// <summary>Solid's colour, and every kind's fallback colour. LINEAR (FC-2.3).</summary>
        public float colR, colG, colB;
        /// <summary>Texture tint, LINEAR. Multiplies the sampled albedo.</summary>
        public float tintR, tintG, tintB;

        /// <summary>The veil dial, resolved and clamped to [0,1] at compile (FC-2.4a).</summary>
        public float veil;
        /// <summary>The height-delta dial, resolved at compile. Layer-local height units.</summary>
        public float height;

        // Gradient
        public float centreX, centreY;
        public float cosTheta, sinTheta;
        /// <summary>The RECIPROCAL of the authored size, taken once here — the dial is a size, not a frequency.</summary>
        public float invSize;
        /// <summary>The reciprocal of <c>max(depthPixels, ε)</c>, canvas pixels, for <c>ByEdgeDistance</c>.</summary>
        public float invDepthPixels;

        /// <summary>Index into <see cref="ShaperFillProgram.bulk"/> of LUT entry 0, or -1 when there is no LUT.</summary>
        public int lutOffset;

        // Ramp
        public float inputLow;
        /// <summary>1/(high−low), or 0 when the window is degenerate.</summary>
        public float invInputSpan;
        /// <summary>1 when the window is degenerate and the ramp becomes a hard step (FC-6.3a).</summary>
        public int hardStep;

        // Texture
        /// <summary>Index into <see cref="ShaperFillProgram.bulk"/> of texel 0, or -1 when there is no texture.</summary>
        public int texOffset;
        public int texWidth, texHeight;
        public float tilesX, tilesY;
        public float offsetU, offsetV;
        public float texCosTheta, texSinTheta;
        /// <summary>
        /// T-0172, animated sprite sheet: the selected frame's sub-rectangle in 0..1 UV, applied AFTER the
        /// existing Fitted/Tiled mapping computes <c>uu, vv</c> — so a frame is treated as its own whole image
        /// for fit/tile purposes. Default (0, 0, 1, 1) is the identity mapping — every pre-T-0172 Texture fill
        /// is byte-identical.
        /// </summary>
        public float animCellU0, animCellV0, animCellScaleU, animCellScaleV;

        // IndexedStrip (T-0110)
        /// <summary>1 = <see cref="ShaperStripParameterisation.Angular"/>, 0 = <see cref="ShaperStripParameterisation.Projection"/>.</summary>
        public int stripAngular;
        /// <summary><c>Angular</c>: the phase origin. <c>Projection</c>: the axis direction.</summary>
        public float stripCosTheta, stripSinTheta;
        /// <summary>Whole-number repeat count, >= 1.</summary>
        public int stripRepeats;
        /// <summary>0..1 phase offset, wrapping.</summary>
        public float stripOffset;
        /// <summary>Index into <see cref="ShaperFillProgram.bulk"/> of slot 0's R, or -1 when the strip is empty.</summary>
        public int stripSlotOffset;
        public int stripSlotCount;
        /// <summary>Reciprocal of the reach depth, already resolved to canvas pixels at compile time.</summary>
        public float stripInvReach;
        /// <summary>The "plain fill" colour beyond the reach, LINEAR.</summary>
        public float plainColR, plainColG, plainColB;

        // HeightField (T-0111)
        /// <summary>Index into <see cref="ShaperFillProgram.bulk"/> of texel 0 (one float per texel, raw signed height), or -1 when there is no field.</summary>
        public int heightFieldOffset;
        public int heightFieldWidth, heightFieldHeight;
        /// <summary>Resolved <see cref="ShaperFillDef.heightFieldScale"/>.</summary>
        public float heightFieldScale;

        // Tapestry Steel (T-0111)
        public int steelCells;
        public int steelOctaves;
        public uint steelSeed;
        public float steelBaseLowR, steelBaseLowG, steelBaseLowB;
        public float steelBaseHighR, steelBaseHighG, steelBaseHighB;
        public float steelRustR, steelRustG, steelRustB;
        public float steelRustAmount;
        public float steelInvRustReach;
        public float steelGrain;
        /// <summary>0 or 1 = off. FC-6.9.</summary>
        public int quantiseLevels;

        // Procedural: Noise / Grid / Dots (T-0172). Positional — reuses centreX/centreY/cosTheta/sinTheta/
        // invSize above for its own offset/rotation/scale (the same "centre + rotation + reciprocal size"
        // triple Gradient already bakes; a second set of fields for the identical concept would be the
        // duplication FC-6's "common to all four" already argues against).
        /// <summary>0 = <see cref="ShaperProceduralKind.Noise"/>, 1 = Grid, 2 = Dots.</summary>
        public int procKind;
        /// <summary>Noise only: 0 = Value, 1 = Ridged, 2 = Steps.</summary>
        public int procNoiseKind;
        /// <summary>Grid only: line half-width and soft-edge band, in cell units.</summary>
        public float procGridHalfW, procGridSoft;
        /// <summary>Grid only: which axes draw lines.</summary>
        public int procGridVertical, procGridHorizontal;
        /// <summary>Dots only: disc radius and soft-edge band, in cell units.</summary>
        public float procDotRadius, procDotSoft;
        /// <summary>Dots only: 1 = stagger alternating rows by half a cell.</summary>
        public int procDotStagger;
    }

    /// <summary>
    /// Bakes an authored <see cref="ShaperFillDef"/> into the flat form the per-sample loop reads.
    ///
    /// <b>Every dial is sampled here, once per compile, through <c>ShaperValue.Sample</c></b> (FC-5.3) — never
    /// from inside the tile loop. BC-1.2: "the value funnel must be evaluated INTO the parameter block up
    /// front, not called FROM inside it". Using <c>ShaperValue</c> rather than <c>ZUIValue.Evaluate</c>
    /// directly is not a style preference: <c>ZUIValue.Evaluate(t)</c> divides the time by the value's
    /// <c>duration</c> (default 4 s) and would sweep only the curve's first quarter
    /// (<c>ShaperValue.cs:9-13</c>).
    ///
    /// <b>The gradient LUT bake is valid, and this was checked rather than assumed.</b> The fill contract flags
    /// FC-5.4 as resting on an unread inference — that <c>ZuiGradient.Evaluate</c>'s transforms are
    /// parameter-dependent rather than position-dependent — and says the bake is invalid if not. Its body was
    /// read (<c>ZuiGradient.cs:85-125</c>): it is a pure function of <c>(t, phase, life)</c> and the gradient's
    /// own serialized fields. It touches no screen or spatial position, and its one stochastic-looking mode is
    /// not stochastic — <c>EvalTransform</c> returns a <c>MinMax</c> value's STABLE MIDPOINT
    /// (<c>ZuiGradient.cs:187</c>) precisely so a shared gradient does not shimmer. So a fixed-<c>life</c> bake
    /// is exact for that <c>life</c>. The consequence for the cache key: the LUT depends on <c>life</c>, and
    /// <c>life</c> here is the node clock, so the LUT is baked <b>per compile</b> — which is what FC-5.4 asks
    /// for anyway, since a compile already happens once per frame-time.
    /// </summary>
    public static class ShaperFillCompiler
    {
        /// <summary>
        /// LUT entries. 256, justified rather than assumed: the LUT quantises the ramp's POSITION, not its
        /// colour, so a hard stop lands within 1/256 of its true position. B9's canvas is authorable 32–256
        /// wide and capped at 68 000 cells, so 1/256 of the anchor span is at or below one output pixel on any
        /// canvas the tool allows. More entries cannot be seen; fewer can.
        /// </summary>
        public const int LutEntries = 256;

        /// <summary>Floats per LUT entry — linear R, G, B. No alpha: albedo has no alpha channel (FC-2.2).</summary>
        public const int LutChannels = 3;

        /// <summary>Floats per texel — linear R, G, B and a NON-decoded alpha (alpha is a coverage, not a colour).</summary>
        public const int TexelChannels = 4;

        /// <summary>Below this the anchor is degenerate and the coordinate is (0,0) (FC-1.5b).</summary>
        const float DegenerateExtent = 1e-6f;

        /// <summary>Below this the ramp window is degenerate and becomes a hard step (FC-6.3a).</summary>
        const float DegenerateWindow = 1e-6f;

        /// <summary>Guards the <c>ByEdgeDistance</c> and size reciprocals. Never a divide by zero.</summary>
        const float MinPositive = 1e-6f;

        /// <summary>Compile one authored fill for one node, at one normalised node clock.</summary>
        public static ShaperFillProgram Compile(ShaperFillDef def, in ShaperFillAnchor anchor,
                                                float phase01, uint seed)
        {
            var prog = new ShaperFillProgram { phase01 = Mathf.Clamp01(phase01) };
            if (def == null)
            {
                // Not reachable through the resolver (the root's fill is non-nullable, FC-3.2) but a null here
                // must still paint something visible rather than crash — FC-6.5's first half.
                prog.op = SolidOp(Color.white, 1f, 0f, ShaperFillComposite.Over);
                prog.diagnostic = "No fill authored; painting opaque white.";
                return prog;
            }

            float p = prog.phase01;
            var op = new ShaperFillOp
            {
                kind = def.kind,
                composite = def.composite,
                gradientMode = def.gradientMode,
                mapping = def.textureMapping,
                rampQuantity = def.rampQuantity,
                lutOffset = -1,
                texOffset = -1,
                heightFieldOffset = -1,
                // T-0172: identity mapping — a non-animated Texture fill reads its whole sheet as before.
                animCellScaleU = 1f,
                animCellScaleV = 1f,
            };

            // ── the two common dials (FC-6, "common to all four") ─────────────────────────────────────────

            // THE CLAMP IS LOAD-BEARING. "The shape always owns its own edge. A fill can veil it but never
            // replace it" (SHAPER_THE_DESIGN.md:321). Multiplication alone gives only the EASY half of that:
            // veil × 0 = 0, so no veil can create paint where the shape published no coverage. But the SOFT
            // edge is not protected by multiplication — at a half-covered antialiasing sample coverage is 0.5,
            // and a veil of 2.0 would give coverageEff = 1.0, silently thickening the shape's edge by half a
            // pixel and hardening its antialiasing. The [0,1] clamp is what actually enforces the rule.
            //
            // Deliberately ASYMMETRIC with coverage (FC-2.4a): incoming coverage is treated as unbounded above
            // per BC-3.3, because a fog accumulating past 1 is meaningful; a FILL claiming more than total
            // opacity is not. A fill that wants something brighter than opaque uses Add compositing.
            op.veil = Clamp01OrZero(ShaperValue.Sample(def.veil, p, seed, 1f));

            op.height = FiniteOrZero(ShaperValue.Sample(def.heightDelta, p, seed, 0f));
            // DECLARED, not discovered (BC-3.7a): a Solid with heightDelta 0 costs no sheet and a Solid with
            // heightDelta 3 does. The host allocates the height sheet only if some fill declared it.
            op.emitsHeight = op.height != 0f ? 1 : 0;

            // T-0277 — Posterise is sampled HERE, in the shared block beside veil and heightDelta, because it
            // is a shared dial: ShaperFillDef declares it outside every per-kind group (:413) and the card
            // draws it outside the per-kind switch. It used to be sampled inside BakeTapestrySteel alone, so
            // op.quantiseLevels was 0 for the other eight kinds however the document was authored, and
            // ShaperFillOps' quantise step could never fire for them.
            op.quantiseLevels = Mathf.RoundToInt(ShaperValue.Sample(def.quantiseLevels, p, seed, 0f));

            // ── the anchor (FC-1.5 / FC-1.6) ──────────────────────────────────────────────────────────────

            op.positional = (def.kind == ShaperFillKind.Gradient &&
                             def.gradientMode != ShaperGradientMode.ByEdgeDistance) ||
                            def.kind == ShaperFillKind.Texture ||
                            def.kind == ShaperFillKind.IndexedStrip ||
                            def.kind == ShaperFillKind.HeightField ||
                            def.kind == ShaperFillKind.TapestrySteel ||
                            def.kind == ShaperFillKind.Procedural
                ? 1 : 0;
            op.fixedSpace = def.space == ShaperFillSpace.Fixed ? 1 : 0;
            BakeAnchor(def, anchor, ref op);

            // ── per kind ──────────────────────────────────────────────────────────────────────────────────

            switch (def.kind)
            {
                case ShaperFillKind.Solid:
                    ShaperSrgb.Decode(def.solidColor, out op.colR, out op.colG, out op.colB);
                    break;

                case ShaperFillKind.Gradient:
                    BakeGradient(def, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.RampByQuantity:
                    BakeRamp(def, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.Texture:
                    BakeTexture(def, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.IndexedStrip:
                    BakeStrip(def, anchor, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.HeightField:
                    BakeHeightField(def, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.TapestrySteel:
                    BakeTapestrySteel(def, p, seed, prog, ref op);
                    break;

                case ShaperFillKind.OverPhase:
                    BakeOverPhase(def, p, prog, ref op);
                    break;

                case ShaperFillKind.Procedural:
                    BakeProcedural(def, p, seed, prog, ref op);
                    break;
            }

            prog.op = op;
            return prog;
        }

        /// <summary>A bare Solid op — the fallback every other kind degenerates to, and the root fill's shape.</summary>
        /// <summary>
        /// Clamp to [0,1] with <b>NaN mapped to 0</b>, which <c>Mathf.Clamp01</c> does not do:
        /// <c>Mathf.Clamp01</c> is <c>v &lt; 0 ? 0 : v &gt; 1 ? 1 : v</c>, and every comparison against NaN is
        /// false, so NaN survives it unchanged. Written as `v &gt; 0` first for the same reason — the NaN
        /// branch falls out of the ordering rather than needing a `float.IsNaN` test.
        ///
        /// This matters because the veil is a <see cref="ZUIValue"/> dial, so it is animatable and its value
        /// is arithmetic the author does not see. A NaN veil is not a theoretical case: before this guard, a
        /// single NaN veil measured 16384/16384 non-finite floats in the destination, encoding to a garbage
        /// byte with no diagnostic — the exact "renders silently" failure FC-6.5 forbids.
        /// </summary>
        static float Clamp01OrZero(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

        /// <summary>Pass a finite value through; map NaN and both infinities to 0. Same reasoning as <see cref="Clamp01OrZero"/>, for the unbounded height delta.</summary>
        static float FiniteOrZero(float v) => (v > float.NegativeInfinity && v < float.PositiveInfinity) ? v : 0f;

        static ShaperFillOp SolidOp(Color c, float veil, float height, ShaperFillComposite composite)
        {
            var op = new ShaperFillOp
            {
                kind = ShaperFillKind.Solid,
                composite = composite,
                veil = Clamp01OrZero(veil),
                height = FiniteOrZero(height),
                emitsHeight = height != 0f ? 1 : 0,
                lutOffset = -1,
                texOffset = -1,
            };
            ShaperSrgb.Decode(c, out op.colR, out op.colG, out op.colB);
            return op;
        }

        // ── the anchor ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FC-1.5 / FC-1.6, baked. In <c>Stamped</c> space the reciprocals divide the node-LOCAL box; in
        /// <c>Fixed</c> space the inverse is dropped to identity and the reciprocals divide the layer canvas,
        /// so the shape becomes a window onto a stationary backdrop.
        ///
        /// FC-8.4: the anchor box is ALWAYS the NODE's box, never the painted region's box. When T-0107 hands
        /// this stage a border strip to paint, a gradient must still normalise across the SHAPE, not across the
        /// ring — otherwise a linear ramp on an outline runs across the outline's own thickness instead of
        /// across the shape it traces, which is almost never what anyone wants and is nearly impossible to
        /// diagnose after the fact.
        /// </summary>
        static void BakeAnchor(ShaperFillDef def, in ShaperFillAnchor anchor, ref ShaperFillOp op)
        {
            float hx, hy, cx, cy;

            if (def.space == ShaperFillSpace.Fixed)
            {
                // Canvas-anchored: the node's inverse transform is NOT applied (FC-1.6).
                op.m00 = 1f; op.m01 = 0f; op.m02 = 0f;
                op.m10 = 0f; op.m11 = 1f; op.m12 = 0f;
                hx = anchor.canvasHalfW; hy = anchor.canvasHalfH; cx = 0f; cy = 0f;
            }
            else
            {
                op.m00 = anchor.inverse.m00; op.m01 = anchor.inverse.m01; op.m02 = anchor.inverse.m02;
                op.m10 = anchor.inverse.m10; op.m11 = anchor.inverse.m11; op.m12 = anchor.inverse.m12;
                hx = anchor.localValid ? anchor.localHalfW : 0f;
                hy = anchor.localValid ? anchor.localHalfH : 0f;
                cx = anchor.localValid ? anchor.localCx : 0f;
                cy = anchor.localValid ? anchor.localCy : 0f;
            }

            op.anchorCx = cx;
            op.anchorCy = cy;

            // FC-1.5b: an empty bag, a node whose every member was skipped, or a Box.Invalid gives (0,0). The
            // fill STILL evaluates and still binds — it must not divide, must not produce NaN and must not
            // refuse. Storing 0 in the reciprocal is what makes that structural rather than a branch.
            float big = Mathf.Max(hx, hy);
            if (big <= DegenerateExtent)
            {
                op.invHx = 0f; op.invHy = 0f;
                return;
            }

            if (def.fit == ShaperFillFit.Stretch)
            {
                op.invHx = hx > DegenerateExtent ? 1f / hx : 0f;
                op.invHy = hy > DegenerateExtent ? 1f / hy : 0f;
            }
            else
            {
                // Uniform: ONE divisor for both axes, so a radial gradient on a wide box stays a true circle.
                float inv = 1f / big;
                op.invHx = inv; op.invHy = inv;
            }
        }

        // ── gradient ──────────────────────────────────────────────────────────────────────────────────────

        static void BakeGradient(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog,
                                 ref ShaperFillOp op)
        {
            // The fallback colour is decoded either way, so a null gradient degenerates to Solid-of-tint with
            // no second code path (FC-6.2, FC-6.5).
            ShaperSrgb.Decode(def.gradientTint, out op.colR, out op.colG, out op.colB);

            op.centreX = ShaperValue.Sample(def.gradientCentreX, p, seed, 0f);
            op.centreY = ShaperValue.Sample(def.gradientCentreY, p, seed, 0f);

            float theta = ShaperValue.Sample(def.gradientAngleDegrees, p, seed, 0f) * Mathf.Deg2Rad;
            op.cosTheta = Mathf.Cos(theta);
            op.sinTheta = Mathf.Sin(theta);

            // The dial is a SIZE, not a frequency: bigger means the pattern spreads further. The reciprocal is
            // taken ONCE, here (ZuiFill.cs:83-88 by value — that field had to be migrated because the maths
            // multiplied by it, so raising "zoom" made the pattern smaller).
            float size = ShaperValue.Sample(def.gradientSize, p, seed, 1f);
            op.invSize = Mathf.Abs(size) > MinPositive ? 1f / size : 0f;

            float depthPixels = ShaperValue.Sample(def.gradientDepthPixels, p, seed, 8f);
            op.invDepthPixels = 1f / Mathf.Max(Mathf.Abs(depthPixels), MinPositive);

            if (def.gradient == null)
            {
                op.kind = ShaperFillKind.Solid;   // degenerate to the flat colour, bit-identically
                prog.diagnostic = "Gradient fill has no gradient authored; painting flat tint instead.";
                return;
            }

            op.lutOffset = 0;
            prog.bulk = BakeLut(def.gradient, prog.phase01);
        }

        /// <summary>
        /// FC-5.4: 256 linear RGB triples, read per sample by POINT lookup with no interpolation.
        ///
        /// <b>Point lookup, not lerp, is a real decision.</b> Interpolating between LUT entries re-softens
        /// exactly the hard stops a pixel-art gradient exists to create — B5 lists "a palette-quantise step so
        /// the output stays pixel art rather than photographic" as one of three things a ported surface needs.
        /// Since the LUT is already at or above output resolution, interpolation buys nothing and costs the
        /// hard stop.
        ///
        /// <b>The conflation to avoid.</b> BC's criticism of Kiln's composite forms baking "a 1-D LUT by
        /// sampling at (0.5, 0.5)" (<c>PyreShade.cs:209</c>) is NOT a criticism of LUTs — it is a criticism of
        /// collapsing a SPATIAL fill to a single sample point, so every spatial mode silently becomes a
        /// constant. Baking a GRADIENT, which is 1-D by definition, loses nothing. This bakes only the colour
        /// lookup; the spatial parameter <c>t</c> is computed per sample from the anchor coordinate.
        /// </summary>
        static float[] BakeLut(ZuiGradient gradient, float life)
        {
            var lut = new float[LutEntries * LutChannels];
            for (int i = 0; i < LutEntries; i++)
            {
                float t = i / (float)(LutEntries - 1);
                // phase = 0: the gradient's OWN authored scroll (phaseAnim, animated over life) still applies
                // inside Evaluate; the second argument is an external cycle driver this stage does not have.
                Color c = gradient.Evaluate(t, 0f, life);
                int o = i * LutChannels;
                lut[o + 0] = ShaperSrgb.DecodeChannel(c.r);
                lut[o + 1] = ShaperSrgb.DecodeChannel(c.g);
                lut[o + 2] = ShaperSrgb.DecodeChannel(c.b);
            }
            return lut;
        }

        // ── ramp ──────────────────────────────────────────────────────────────────────────────────────────

        static void BakeRamp(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog, ref ShaperFillOp op)
        {
            ShaperSrgb.Decode(def.rampTint, out op.colR, out op.colG, out op.colB);

            float lo = ShaperValue.Sample(def.rampInputLow, p, seed, 0f);
            float hi = ShaperValue.Sample(def.rampInputHigh, p, seed, 1f);
            op.inputLow = lo;

            float span = hi - lo;
            if (Mathf.Abs(span) < DegenerateWindow)
            {
                // FC-6.3a: MUST NOT divide. The meaningful limit of an infinitely narrow window is a hard step,
                // which is a useful two-colour threshold to be able to author on purpose.
                op.hardStep = 1;
                op.invInputSpan = 0f;
            }
            else
            {
                // FC-6.3b: an inverted window (low > high) gives a negative denominator and a reversed ramp.
                // That is the natural way to invert without a second dial and needs no special case.
                op.hardStep = 0;
                op.invInputSpan = 1f / span;
            }

            if (def.rampGradient == null)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Ramp fill has no gradient authored; painting flat tint instead.";
                return;
            }

            op.lutOffset = 0;
            prog.bulk = BakeLut(def.rampGradient, prog.phase01);
        }

        // ── texture ───────────────────────────────────────────────────────────────────────────────────────

        static void BakeTexture(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog,
                                ref ShaperFillOp op)
        {
            ShaperSrgb.Decode(def.textureTint, out op.tintR, out op.tintG, out op.tintB);
            // The tint is also the fallback colour, so the null/unreadable path is the same three floats.
            op.colR = op.tintR; op.colG = op.tintG; op.colB = op.tintB;

            op.tilesX = ShaperValue.Sample(def.textureTilesX, p, seed, 1f);
            op.tilesY = ShaperValue.Sample(def.textureTilesY, p, seed, 1f);
            op.offsetU = ShaperValue.Sample(def.textureOffsetU, p, seed, 0f);
            op.offsetV = ShaperValue.Sample(def.textureOffsetV, p, seed, 0f);

            float theta = ShaperValue.Sample(def.textureAngleDegrees, p, seed, 0f) * Mathf.Deg2Rad;
            op.texCosTheta = Mathf.Cos(theta);
            op.texSinTheta = Mathf.Sin(theta);

            if (def.texture == null)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Texture fill has no texture assigned; painting flat tint instead.";
                return;
            }

            // FC-6.4d: THE NON-READABLE CASE IS THE ONE THAT WILL ACTUALLY HAPPEN, because it depends on the
            // texture's import setting rather than on the asset being present — the asset looks fine in the
            // inspector and renders as a flat colour. So the reason string names the import setting.
            if (!def.texture.isReadable)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Texture '" + def.texture.name +
                                  "' is not marked Read/Write Enabled; painting flat tint instead.";
                return;
            }

            Color32[] px;
            try { px = def.texture.GetPixels32(); }
            catch (System.Exception e)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Texture '" + def.texture.name + "' could not be read (" + e.GetType().Name +
                                  "); painting flat tint instead.";
                return;
            }

            int w = def.texture.width, h = def.texture.height;
            if (px == null || px.Length < w * h || w <= 0 || h <= 0)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Texture '" + def.texture.name + "' returned no pixels; painting flat tint instead.";
                return;
            }

            // Pixels are copied ONCE, here, into host-owned bulk data and decoded to linear. That is what makes
            // ZuiFill's per-sample null-cache (ZuiFill.cs:260 — "never re-throws per pixel") structurally
            // unnecessary: there is no per-pixel path left that can throw.
            var texels = new float[w * h * TexelChannels];
            for (int i = 0; i < w * h; i++)
            {
                Color32 c = px[i];
                int o = i * TexelChannels;
                texels[o + 0] = ShaperSrgb.DecodeChannel(c.r * (1f / 255f));
                texels[o + 1] = ShaperSrgb.DecodeChannel(c.g * (1f / 255f));
                texels[o + 2] = ShaperSrgb.DecodeChannel(c.b * (1f / 255f));
                // Alpha is NOT sRGB-decoded: it is a coverage, not a colour. It multiplies into the VEIL
                // (FC-6.4c) — albedo has no alpha channel, so there is nowhere else for it to go, and the veil
                // is exactly the right place: a transparent texel means "this pattern does not cover here".
                texels[o + 3] = c.a * (1f / 255f);
            }

            op.texOffset = 0;
            op.texWidth = w;
            op.texHeight = h;
            prog.bulk = texels;

            // T-0172: an animated sprite sheet steps to one grid cell, chosen from the node's own phase —
            // resolved here, once per compile, exactly like every other compile-time scalar (FC-5.3). The
            // frame does not interpolate between cells: a phase-driven texture STEPS, the same reasoning
            // BakeStrip's palette indexes a slot rather than lerping two (FC-5.4's "point lookup, not lerp"
            // applies to a discrete frame exactly as it does to a discrete ramp band).
            if (def.textureAnimated)
            {
                int columns = Mathf.Max(1, Mathf.RoundToInt(ShaperValue.Sample(def.textureFrameColumns, p, seed, 1f)));
                int rows = Mathf.Max(1, Mathf.RoundToInt(ShaperValue.Sample(def.textureFrameRows, p, seed, 1f)));
                int frameCount = Mathf.Clamp(
                    Mathf.RoundToInt(ShaperValue.Sample(def.textureFrameCount, p, seed, 1f)), 1, columns * rows);

                // floor + clamp-to-last, not wrap: a stepped sheet plays once across the node's own 0..1
                // cycle (FC-1.4), it does not loop mid-cycle the way a spatial Tiled pattern repeats.
                int frame = Mathf.Clamp(Mathf.FloorToInt(p * frameCount), 0, frameCount - 1);
                int col = frame % columns;
                int row = frame / columns;

                // Row-major from texel row 0, which GetPixels32 lays out BOTTOM-up — frame 0 is the sheet's
                // bottom-left cell, the same convention the texel buffer above already uses (no new one).
                op.animCellScaleU = 1f / columns;
                op.animCellScaleV = 1f / rows;
                op.animCellU0 = col * op.animCellScaleU;
                op.animCellV0 = row * op.animCellScaleV;
            }
        }

        // ── indexed strip (T-0110, B6, FC-6.6) ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes an <see cref="ShaperFillKind.IndexedStrip"/> fill. Every dial through
        /// <see cref="ShaperValue.Sample"/> once, exactly as every other kind (FC-5.3); the palette is copied
        /// into <see cref="ShaperFillProgram.bulk"/> once, exactly as Gradient's LUT and Texture's texels are
        /// (FC-5.5) — four floats per slot (linear R, G, B, height), no interpolation between entries: the
        /// per-sample path indexes a slot, it never lerps two.
        /// </summary>
        static void BakeStrip(ShaperFillDef def, in ShaperFillAnchor anchor, float p, uint seed,
                              ShaperFillProgram prog, ref ShaperFillOp op)
        {
            ShaperSrgb.Decode(def.stripPlainColor, out op.plainColR, out op.plainColG, out op.plainColB);

            op.stripAngular = def.stripParameterisation == ShaperStripParameterisation.Angular ? 1 : 0;

            float theta = ShaperValue.Sample(def.stripOrientationDegrees, p, seed, 0f) * Mathf.Deg2Rad;
            op.stripCosTheta = Mathf.Cos(theta);
            op.stripSinTheta = Mathf.Sin(theta);

            op.stripRepeats = Mathf.Max(1, Mathf.RoundToInt(ShaperValue.Sample(def.stripRepeats, p, seed, 1f)));

            float offset = ShaperValue.Sample(def.stripOffset, p, seed, 0f);
            op.stripOffset = offset - Mathf.Floor(offset);

            // The reach is a FRACTION of the node's own local half-extent (never raw canvas pixels — see
            // ShaperFillDef.stripReach), resolved to canvas pixels once, here, exactly like every other
            // compile-time scalar. Falls back to the canvas half-extent on a degenerate/absent local box, the
            // same fallback FC-1.5b's anchor already uses for the positional coordinate.
            float span = anchor.localValid
                ? Mathf.Min(anchor.localHalfW, anchor.localHalfH)
                : Mathf.Max(anchor.canvasHalfW, anchor.canvasHalfH);
            float reachFraction = Mathf.Max(0f, ShaperValue.Sample(def.stripReach, p, seed, 1f));
            float reachPixels = reachFraction * Mathf.Max(span, MinPositive);
            op.stripInvReach = 1f / Mathf.Max(reachPixels, MinPositive);

            int count = def.stripSlots != null ? def.stripSlots.Count : 0;
            if (count <= 0)
            {
                // FC-6.5: a half-configured fill never renders empty. An empty strip degenerates to a flat
                // Solid of the plain colour, bit-identically to Gradient's null-gradient and Texture's
                // null-texture fallbacks.
                op.kind = ShaperFillKind.Solid;
                op.colR = op.plainColR; op.colG = op.plainColG; op.colB = op.plainColB;
                op.stripSlotOffset = -1;
                op.stripSlotCount = 0;
                prog.diagnostic = "Indexed strip has no palette slots authored; painting the plain colour instead.";
                return;
            }

            op.stripSlotOffset = 0;
            op.stripSlotCount = count;

            var bulk = new float[count * 4];
            bool anySlotHeight = false;
            for (int i = 0; i < count; i++)
            {
                ShaperStripSlot slot = def.stripSlots[i];
                Color c = slot != null ? slot.color : Color.white;
                float h = slot != null ? FiniteOrZero(slot.height) : 0f;
                if (h != 0f) anySlotHeight = true;

                int o = i * 4;
                ShaperSrgb.Decode(c, out bulk[o + 0], out bulk[o + 1], out bulk[o + 2]);
                bulk[o + 3] = h;
            }
            prog.bulk = bulk;

            // DECLARED, not discovered (BC-3.7a): the strip's own per-slot heights are a SECOND source of
            // height alongside the common heightDelta dial (FC-2.5) — Sample() adds the two — so the sheet
            // must be allocated when EITHER is live, not only when the common dial is.
            if (anySlotHeight) op.emitsHeight = 1;
        }

        // ── height field (T-0111, FC-6.7) ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes a <see cref="ShaperFillKind.HeightField"/> fill. The field's raw signed texels are copied
        /// ONCE into <see cref="ShaperFillProgram.bulk"/> — one float per texel, no decode (FC-5.5, mirrors
        /// <see cref="BakeTexture"/>'s copy of Texture's pixels, minus the sRGB step since this is not a
        /// colour). Always declares <c>emitsHeight</c>: unlike the common <see cref="ShaperFillDef.heightDelta"/>
        /// dial (which costs a sheet only when non-zero), a HeightField fill's entire purpose is height, so
        /// costing the sheet unconditionally is the honest declaration rather than a false economy.
        /// </summary>
        static void BakeHeightField(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog, ref ShaperFillOp op)
        {
            ShaperSrgb.Decode(def.heightFieldTint, out op.colR, out op.colG, out op.colB);
            op.heightFieldScale = ShaperValue.Sample(def.heightFieldScale, p, seed, 1f);

            if (def.heightField == null)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Height field fill has no field assigned; painting flat tint instead.";
                return;
            }
            if (!def.heightField.isReadable)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Height field '" + def.heightField.name +
                                  "' is not marked Read/Write Enabled; painting flat tint instead.";
                return;
            }

            int w = def.heightField.width, h = def.heightField.height;
            float[] texels;
            try
            {
                var raw = def.heightField.GetPixelData<float>(0);   // NativeArray<float>, RFloat texel = one float
                texels = new float[raw.Length];
                for (int i = 0; i < raw.Length; i++) texels[i] = raw[i];
            }
            catch (System.Exception e)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Height field '" + def.heightField.name + "' could not be read (" +
                                  e.GetType().Name + "); painting flat tint instead.";
                return;
            }
            if (texels == null || texels.Length < w * h || w <= 0 || h <= 0)
            {
                op.kind = ShaperFillKind.Solid;
                prog.diagnostic = "Height field '" + def.heightField.name + "' returned no data; painting flat tint instead.";
                return;
            }

            op.heightFieldOffset = 0;
            op.heightFieldWidth = w;
            op.heightFieldHeight = h;
            op.emitsHeight = 1;
            prog.bulk = texels;
        }

        // ── Tapestry Steel (T-0111, FC-6.8) ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes a <see cref="ShaperFillKind.TapestrySteel"/> fill. Every dial through
        /// <see cref="ShaperValue.Sample"/> once (FC-5.3), exactly like every other kind — the noise itself
        /// is evaluated per sample by <see cref="ShaperTapestryCanvas"/> from the baked scalars here, so
        /// there is no LUT and no bulk data at all for this kind.
        /// </summary>
        static void BakeTapestrySteel(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog, ref ShaperFillOp op)
        {
            op.steelCells = Mathf.Max(1, Mathf.RoundToInt(ShaperValue.Sample(def.steelCells, p, seed, 6f)));
            op.steelOctaves = Mathf.Clamp(Mathf.RoundToInt(ShaperValue.Sample(def.steelOctaves, p, seed, 4f)), 1, 8);
            op.steelSeed = (uint)Mathf.RoundToInt(ShaperValue.Sample(def.steelSeed, p, seed, 0f));

            ShaperSrgb.Decode(def.steelBaseLow, out op.steelBaseLowR, out op.steelBaseLowG, out op.steelBaseLowB);
            ShaperSrgb.Decode(def.steelBaseHigh, out op.steelBaseHighR, out op.steelBaseHighG, out op.steelBaseHighB);
            ShaperSrgb.Decode(def.steelRustColor, out op.steelRustR, out op.steelRustG, out op.steelRustB);
            // The fallback colour (a null case cannot arise for this kind — no asset dependency — but
            // op.colR/G/B is read by the shared Solid-degeneration path elsewhere, so keep it sane).
            op.colR = op.steelBaseLowR; op.colG = op.steelBaseLowG; op.colB = op.steelBaseLowB;

            op.steelRustAmount = Mathf.Clamp01(ShaperValue.Sample(def.steelRustAmount, p, seed, 0.25f));
            float reachPixels = Mathf.Max(1e-3f, ShaperValue.Sample(def.steelRustReachPixels, p, seed, 24f));
            op.steelInvRustReach = 1f / reachPixels;
            op.steelGrain = Mathf.Max(0f, ShaperValue.Sample(def.steelGrain, p, seed, 0.06f));

            // T-0277: quantiseLevels is sampled once in the shared block above, for every kind.
        }

        // ── OverPhase (T-0172, FC-6.10) ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes an <see cref="ShaperFillKind.OverPhase"/> fill: ONE colour, sampled from the gradient at the
        /// node's own phase, decoded straight into <c>colR/G/B</c> — the same three floats <see cref="ShaperFillKind.Solid"/>
        /// writes, so the per-sample path needs no new case (see <see cref="ShaperFillOps.Sample"/>'s
        /// <c>OverPhase</c> case, which documents why it is bit-identical to Solid's).
        /// </summary>
        static void BakeOverPhase(ShaperFillDef def, float p, ShaperFillProgram prog, ref ShaperFillOp op)
        {
            // Decoded either way, so a null gradient degenerates to Solid-of-tint with no second code path
            // (FC-6.5), exactly like Gradient's own null fallback.
            ShaperSrgb.Decode(def.overPhaseTint, out op.colR, out op.colG, out op.colB);

            if (def.overPhaseGradient == null || !def.overPhaseGradient.HasRamp)
            {
                prog.diagnostic = "Colour-over-phase fill has no gradient authored; painting flat tint instead.";
                return;
            }

            // T-0139's "existing colour-over-time control": ZuiGradient.Evaluate's ramp POSITION and its
            // LIFE-driven colour transforms (hue/sat/brightness/contrast/phaseAnim) are both set to the SAME
            // node phase, exactly the "ramp position IS the life" trick ZuiGradient.ToLut's
            // lifeFollowsPosition already names for an OverLife-shaped fill (ZuiGradient.cs:130-135). One
            // sample, not a LUT — the whole shape is one colour at this compile, so there is nothing to look
            // up per pixel.
            Color c = def.overPhaseGradient.Evaluate(p, 0f, p);
            ShaperSrgb.Decode(c, out op.colR, out op.colG, out op.colB);
        }

        // ── Procedural: Noise / Grid / Dots (T-0172, FC-6.11) ────────────────────────────────────────────

        /// <summary>
        /// Bakes an <see cref="ShaperFillKind.Procedural"/> fill. Shares Gradient's own centre/rotation/size
        /// fields (<c>centreX/centreY/cosTheta/sinTheta/invSize</c>) for its offset/angle/scale — the same
        /// "position + rotation + reciprocal size" triple, baked the same way, for the same reason (FC-5.3).
        /// Noise bakes a LUT exactly like Gradient/Ramp (FC-5.4); Grid and Dots bake no bulk data at all — an
        /// ink-and-mask kind, like TapestrySteel, needs none.
        /// </summary>
        static void BakeProcedural(ShaperFillDef def, float p, uint seed, ShaperFillProgram prog, ref ShaperFillOp op)
        {
            op.procKind = (int)def.proceduralKind;
            op.procNoiseKind = (int)def.noiseKind;

            // Fallback (Noise) / ink (Grid, Dots) colour, decoded either way (FC-6.5 pattern; also literally
            // the ink ZuiFill.cs:68 calls "Ink = color" for Grid and Dots, by value).
            ShaperSrgb.Decode(def.proceduralTint, out op.colR, out op.colG, out op.colB);

            op.centreX = ShaperValue.Sample(def.proceduralOffsetU, p, seed, 0f);
            op.centreY = ShaperValue.Sample(def.proceduralOffsetV, p, seed, 0f);

            float theta = ShaperValue.Sample(def.proceduralAngleDegrees, p, seed, 0f) * Mathf.Deg2Rad;
            op.cosTheta = Mathf.Cos(theta);
            op.sinTheta = Mathf.Sin(theta);

            // A SIZE, not a frequency — same convention as Gradient's own gradientSize (ZuiFill.cs:83-88 by
            // value). The reciprocal is taken once, here.
            float scale = ShaperValue.Sample(def.proceduralScale, p, seed, 1f);
            op.invSize = Mathf.Abs(scale) > MinPositive ? 1f / scale : 0f;

            switch (def.proceduralKind)
            {
                case ShaperProceduralKind.Grid:
                {
                    // ZuiFill.cs:315-316 by value: half-width and a soft edge band, both in CELL units
                    // (lineWidth is a fraction of a cell).
                    float lineWidth = Mathf.Max(0f, ShaperValue.Sample(def.gridLineWidth, p, seed, 0.08f));
                    op.procGridHalfW = lineWidth * 0.5f;
                    op.procGridSoft = 0.1f * op.procGridHalfW + 0.02f;
                    op.procGridVertical = def.gridVertical ? 1 : 0;
                    op.procGridHorizontal = def.gridHorizontal ? 1 : 0;
                    return;
                }

                case ShaperProceduralKind.Dots:
                {
                    // ZuiFill.cs:338,347 by value: disc radius and its soft edge, both in CELL units (dotSize
                    // is a diameter fraction of a cell).
                    float r = Mathf.Clamp01(ShaperValue.Sample(def.dotSize, p, seed, 0.5f)) * 0.5f;
                    op.procDotRadius = r;
                    op.procDotSoft = 0.15f * r + 0.02f;
                    op.procDotStagger = def.dotStagger ? 1 : 0;
                    return;
                }

                default:   // Noise
                    if (def.proceduralGradient == null || !def.proceduralGradient.HasRamp)
                    {
                        op.kind = ShaperFillKind.Solid;
                        prog.diagnostic = "Noise fill has no gradient authored; painting flat tint instead.";
                        return;
                    }
                    op.lutOffset = 0;
                    prog.bulk = BakeLut(def.proceduralGradient, prog.phase01);
                    return;
            }
        }
    }
}
