using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>APPEND-ONLY: serialized as an int. LR-6.1.</summary>
    public enum ShaperSolidForm
    {
        Box = 0,
        Pyramid = 1,
        Can = 2,
        Orb = 3,
        Gem = 4,
        Ring = 5,
    }

    /// <summary>
    /// An authored Solids generator. LR-6.1.
    ///
    /// Every scalar is a <see cref="ZUIValue"/> sampled ONCE at compile through
    /// <see cref="ShaperValue.Sample"/>, the same funnel every other Shaper dial goes through (LR-1.8).
    ///
    /// <b>What is NOT here, and why.</b> No light, no ambient, no diffuse strength, no specular exponent, no
    /// material colour. The light and ambient are the document's (LR-1.1); the specular exponent and tint are
    /// the layer's response block (LR-4.4); the material colour is an ORDINARY SHAPER FILL on the Solids node
    /// (LR-6.3), which is a strict gain — Solids acquires Gradient, Ramp-by-quantity and Texture, plus the
    /// availability gate, for free. Pyre's <c>shapeFill</c> (<c>PyreRenderer.cs:4356</c>, <c>:4520</c>) has no
    /// equivalent field here on purpose.
    /// </summary>
    [Serializable]
    public class ShaperSolidDef
    {
        public ShaperSolidForm form = ShaperSolidForm.Box;

        /// <summary>The size envelope, in canvas pixels — Pyre's <c>R</c> (<c>PyreRenderer.cs:4157</c>).</summary>
        public ZUIValue size = new ZUIValue(20f);

        /// <summary>Canvas position of the solid's centre, canvas pixels (LR-1.5).</summary>
        public ZUIValue centreX = new ZUIValue(0f);
        public ZUIValue centreY = new ZUIValue(0f);

        /// <summary><c>Pyre.solidAspect</c> — the Y half-extent multiplier.</summary>
        public ZUIValue aspect = new ZUIValue(1f);
        /// <summary><c>Pyre.solidDepth</c> — the Z half-extent multiplier. Unused by Can (circular section).</summary>
        public ZUIValue depth = new ZUIValue(1f);

        /// <summary>Gem only: girdle sides, clamped 3..8 as <c>BuildGemGeometry</c> does.</summary>
        public ZUIValue gemSides = new ZUIValue(6f);
        /// <summary>Gem only: crown height as a fraction of R.</summary>
        public ZUIValue gemCrown = new ZUIValue(0.55f);
        /// <summary>Gem only: pavilion depth as a fraction of R.</summary>
        public ZUIValue gemPavilion = new ZUIValue(0.85f);

        /// <summary>Ring only: hole radius as a fraction of R, clamped 0.1..0.92 (<c>PyreRenderer.cs:4617</c>).</summary>
        public ZUIValue ringInner = new ZUIValue(0.55f);

        /// <summary>Yaw about Y, degrees — Pyre's <c>particleSpin</c>.</summary>
        public ZUIValue yaw = new ZUIValue(0f);
        /// <summary>Tilt about X, degrees — Pyre's <c>gemTilt</c>.</summary>
        public ZUIValue tilt = new ZUIValue(0f);
        /// <summary>Roll about Z in model space, applied FIRST — Pyre's <c>gemRoll</c>.</summary>
        public ZUIValue roll = new ZUIValue(0f);

        /// <summary>
        /// FACET EDGE LINE half-width, canvas pixels. <b>Called a facet edge line and never a border</b>
        /// (BD-4.1): the lines trace INTERIOR FACET SEAMS, which are nowhere in the zero set of any 2D field,
        /// so no border stage can produce them.
        /// </summary>
        public ZUIValue lineWidth = new ZUIValue(1.1f);

        /// <summary>The facet edge line's colour, sRGB, decoded once at compile.</summary>
        public Color lineColour = Color.white;

        /// <summary>Halo strength, 0..1 — Pyre's <c>gemEdgeGlow</c>.</summary>
        public ZUIValue edgeGlow = new ZUIValue(0f);
        public Color edgeGlowColour = Color.white;

        /// <summary>Inner-glow strength, 0..1 — Pyre's <c>gemInnerGlow</c>.</summary>
        public ZUIValue innerGlow = new ZUIValue(0f);
        public Color innerGlowColour = Color.white;
    }

    /// <summary>
    /// Every authored dial on a Solids generator, named so a diagnostic and a UI can talk about the same
    /// thing. APPEND-ONLY, like <c>ShaperQuantity</c> and <c>ShaperFillKind</c>.
    /// </summary>
    public enum ShaperSolidDial
    {
        Size = 0, Centre = 1, Aspect = 2, Depth = 3,
        GemSides = 4, GemCrown = 5, GemPavilion = 6, RingInner = 7,
        Yaw = 8, Tilt = 9, Roll = 10, LineWidth = 11, EdgeGlow = 12, InnerGlow = 13,
    }

    /// <summary>The compiled Solids generator: flat, blittable, every dial resolved (FC-5.2's form).</summary>
    public struct ShaperSolidOp
    {
        public ShaperSolidForm form;
        public float r;
        public float centreX, centreY;
        public float aspect, depth;
        public int gemSides;
        public float gemCrown, gemPavilion;
        public float ringInner;
        /// <summary>Radians.</summary>
        public float yaw, tilt, roll;
        public float lineWidth;
        public float lineR, lineG, lineB;                  // LINEAR
        public float edgeGlow, innerGlow;
        public float edgeGlowR, edgeGlowG, edgeGlowB;      // LINEAR
        public float innerGlowR, innerGlowG, innerGlowB;   // LINEAR
    }

    /// <summary>
    /// Where a Solids generator writes. LR-6.1: it publishes COVERAGE and a SURFACE NORMAL, into the same
    /// sheets any other provider writes, and then goes through the ordinary fill and light pipeline like
    /// every other generator.
    /// </summary>
    public struct ShaperSolidEmit
    {
        /// <summary>1 per sample. HARD 0 or 1 in Wave 2 — LR-6.4's declared limitation, owner T-0114.</summary>
        public float[] coverage;

        /// <summary>
        /// 1 per sample: the signed screen-space distance to the SILHOUETTE (negative inside), so the
        /// <c>edgeDistance</c> quantity means the same thing here as it does for a 2D field
        /// (<c>ShaperField.cs:181-184</c>). It is measured to the silhouette edge set — the visible edges
        /// belonging to exactly one visible face — and deliberately NOT to the facet seams, which are an
        /// interior feature and not the shape's edge.
        /// </summary>
        public float[] distance;

        /// <summary>3 per sample, UNIT, canvas frame (LR-3.5). The same array <see cref="ShaperNormals"/> writes.</summary>
        public float[] normal;

        /// <summary>
        /// 1 per sample, 0 or 1: this sample is on a FACET EDGE LINE (LR-6.3). The host substitutes the line
        /// colour as the sample's albedo while KEEPING THE FACE'S NORMAL, so the line is lit by the shared law
        /// exactly like the face it sits on.
        /// </summary>
        public float[] lineMask;

        /// <summary>
        /// 3 per sample: the halo and inner glow, as an ADDITIVE, UNLIT linear triple (LR-6.4 keeps both in
        /// the generator). Unlit for LR-5.3's reason applied one level down — a glow is light, not paint, and
        /// multiplying emitted light by an incident-light term is backwards.
        /// </summary>
        public float[] glow;

        /// <summary>
        /// 1 per sample: the Z of the published surface point, in canvas pixels, +Z toward the viewer
        /// (LR-1.5). Its X and Y need no sheet — the family is ORTHOGRAPHIC (LR-6.5d), so the surface point's
        /// screen x and y ARE the sample's own canvas position, which the host already has.
        /// </summary>
        public float[] pointZ;
    }

    /// <summary>
    /// Prebuilt, host-owned geometry for one compiled solid: the rotated vertices, the culled visible faces,
    /// their normals, the line-edge segments and the silhouette-edge segments.
    ///
    /// <b>Everything that allocates happens HERE, in <see cref="ShaperSolids.Build"/>, once per compile.</b>
    /// <see cref="ShaperSolids.FillTile"/> then allocates nothing (LT-2) — which is the exact discipline
    /// BC-3.7f sets and the exact thing <c>PyreRenderer.cs:1803</c> and <c>:1857</c>
    /// (<c>light = new float[W * H]</c>, per layer per frame) violate today.
    /// </summary>
    public sealed class ShaperSolidGeometry
    {
        public int visCount;
        public float[] visAx, visAy, visBx, visBy, visCx, visCy;   // screen triangle, canvas units, centred on the solid
        public float[] visP;                                        // 9 floats per visible face: the three 3D verts
        public float[] visN;                                        // 3 floats per visible face: the outward unit normal

        public int lineCount;
        public float[] lineAx, lineAy, lineBx, lineBy;

        public int silCount;
        public float[] silAx, silAy, silBx, silBy;

        public float minX, maxX, minY, maxY;

        /// <summary>Orb / Ring: the constant published normal, or the Ring's plane normal.</summary>
        public float planeNx, planeNy, planeNz;
        /// <summary>Ring: the forward/inverse plane-map constants.</summary>
        public float cyw, syw, ct, st, sywst;
        /// <summary>Ring: true when the edge-on degeneracy fired and the solid draws nothing.</summary>
        public bool degenerate;
        /// <summary>Orb: the forward rotation matrix applied to the published NORMAL. Row-major 3x3.</summary>
        public float m00, m01, m02, m10, m11, m12, m20, m21, m22;
    }

    /// <summary>
    /// Solids, re-expressed for Shaper. LR-6.1: <b>a GENERATOR that publishes coverage and a surface normal,
    /// going through the ordinary fill and light pipeline like everything else — not a composite.</b>
    ///
    /// <b>Why a generator and not a composite.</b> B8 says Solids publishes "real 3D geometry with a genuine
    /// surface direction at every dot and real hiding of one part behind another"
    /// (<c>SHAPER_THE_DESIGN.md:190</c>) — that sentence is literally coverage plus normal. A composite would
    /// be exempt from the fill contract (so Solids would keep its private colour ramps forever), exempt from
    /// the availability gate, exempt from borders, and would land in the category design Part A reserves for
    /// effects "not worth taking apart". Solids IS worth taking apart, because its inline lighting block is
    /// the exact thing this task exists to share: leaving it a composite would mean shipping "one shared law"
    /// with one of its two callers structurally unable to call it.
    ///
    /// <b>This file does not modify Pyre and is not a refactor of it.</b> It is a RE-EXPRESSION, reading
    /// <c>PyreRenderer.cs</c> as the reference. Pyre keeps working exactly as it does today.
    ///
    /// <b>LR-6.2 — what comes across unchanged</b> (verbatim in substance, because it is correct and
    /// rewriting it would be a risk for no gain): the four geometry builders (<c>:4796</c>, <c>:4825</c>,
    /// <c>:4869</c>, <c>:4903</c>) and <c>EdgeKey</c> (<c>:4793</c>); the rotation <c>Rot</c> — roll about Z in
    /// model space, then yaw about Y, then world tilt about X (<c>:4193-4198</c>); face resolution — the
    /// centroid outward-normal test (<c>:4234</c>), the backface cull (<c>:4235</c>), the normalise
    /// (<c>:4236</c>), the deduped line-edge gather (<c>:4238-4254</c>); <c>InTri</c> (<c>:4936</c>) and
    /// <c>DistSeg</c> (<c>:4949</c>); Orb's analytic normal <c>N = P/R</c> (<c>:4533</c>); Ring's
    /// forward/inverse plane map and its gradient-magnitude correction.
    ///
    /// <b>LR-6.3 — what is re-expressed.</b> The inline lighting block at <c>:4341-4360</c> (facet),
    /// <c>:4534-4553</c> (Orb) and <c>:4720-4740</c> (Ring) — three copies of
    /// <c>lit = ambient + diffuse*ndl*atten</c> plus a Blinn-Phong half-vector term — <b>is deleted here and
    /// is not reproduced anywhere in this file.</b> All three become one <see cref="ShaperLightLaw.Shade"/>
    /// call, made by the host in <c>ShaperFillResolver.PaintTile</c>. <c>gemAmbient</c> becomes the document
    /// ambient; <c>gemDiffuse</c> becomes light intensity; <c>gemSpecular</c>/<c>gemSpecPower</c> become the
    /// response block; the light position and range become the rig (LR-1.6). <c>shapeFill</c> becomes an
    /// ordinary Shaper fill. And <c>float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f)</c> (<c>:4349</c>) is
    /// <b>DELETED</b> — a crude hand-approximation of "lit, with a floor and a ceiling", where the shared law
    /// does the lit part properly. Expected visual change, stated: an edge line in deep shadow now falls to
    /// ambient instead of stopping at 0.25, and a fully lit one is no longer clamped at 1.15 so it can blow
    /// out. If the floor turns out to be wanted it returns as a <c>lineAmbientBoost</c> dial — NAMED AS
    /// DEFERRED, NOT BUILT, because a floor nobody has asked for is a dial nobody can explain.
    ///
    /// <b>LR-6.5 — the four constraints, written down here rather than rediscovered.</b>
    /// <list type="number">
    /// <item><b>a — convexity, backface-cull-only, no depth sort.</b> <c>:4139-4140</c> "backface culling ONLY
    /// (no depth sort)"; <c>:4213</c> "Convex + culled implies front faces tile the silhouette with no
    /// overlap"; <c>:4362</c> "break; // convex + culled: first hit wins". The family cannot render a
    /// non-convex solid, cannot render two solids that interpenetrate, and cannot render a fused silhouette of
    /// several solids — reusing it on one requires a per-pixel depth compare, which is a different rasteriser
    /// rather than a parameter. Worse and easy to miss: the outward-normal resolution depends on the ORIGIN
    /// BEING STRICTLY INSIDE the solid (<c>:4234</c>, <c>:4787</c>), and a fused shape has no such point in
    /// general, so even the normals would be wrong.</item>
    /// <item><b>b — Orb does not rotate geometrically.</b> <c>:4412</c> "NEVER squashed by spin/tilt";
    /// <c>:4442-4455</c> the lighting-frame rotation. Orb cannot participate in any shared 3D transform: a
    /// document-level camera tilt would turn every facet solid and leave the Orb's silhouette exactly
    /// unchanged. And the equivalence the trick rests on holds ONLY FOR ROTATIONS — a non-uniform scale or a
    /// perspective divide breaks it silently, producing a plausible-looking but wrong hotspot. See
    /// <see cref="BuildOrb"/> for the one place this file departs from Pyre's formulation and why.</item>
    /// <item><b>c — Ring breaks (a) outright.</b> <c>:4601</c> "a flat TWO-SIDED tilted annulus"; <c>:4607</c>
    /// "no backface cull"; <c>:4633</c> the hard early return at <c>|cos tilt| &lt; 0.02</c>. An annulus is not
    /// a convex set and Ring is not culled; it survives only because it is a SINGLE FLAT PLANE, so a ray meets
    /// it exactly once and "first hit wins" is trivially true with one hit available. Ring cannot be combined
    /// with any other solid in one pass, and under a shared camera passing through edge-on it POPS OUT OF
    /// EXISTENCE DISCONTINUOUSLY rather than fading. LT-14c measures that discontinuity and locks it as a
    /// known defect rather than pretending it is a design.</item>
    /// <item><b>d — the whole family is orthographic and the light is a screen-space object.</b>
    /// <c>:4936-4938</c>, <c>InTri</c>'s own header: "Barycentric point-in-triangle (ORTHOGRAPHIC: screen bary
    /// == plane bary)". Introducing any perspective breaks the barycentric interpolation as well as Orb's
    /// rotation identity, so a future camera is a rewrite of the interpolation and not a matrix change.
    /// Recorded because "add a camera later" reads cheap and is not.</item>
    /// </list>
    ///
    /// <b>LR-6.4 — what is deliberately left behind.</b> Coverage stays HARD (0 or 1) for every member; the
    /// Orb COULD be softened for free (it has an exact SDF and <c>ShaperField.Coverage</c> would take it
    /// directly) and is deliberately NOT, because an Orb and a Can in one document with visibly different edge
    /// quality is the "not one scene" complaint restated about edges instead of light. Declared limitation,
    /// owner T-0114. Geometry modifiers stay excluded, as they already are (<c>:4149-4150</c>).
    /// </summary>
    public static class ShaperSolids
    {
        /// <summary>
        /// What a Solids node publishes (BC-3.7a: DECLARED, not discovered). Coverage and edge distance, like
        /// the shipped shape engine, PLUS the surface direction that is the whole reason this generator
        /// exists.
        /// </summary>
        public const ShaperQuantitySet Published =
            ShaperQuantitySet.Coverage | ShaperQuantitySet.EdgeDistance | ShaperQuantitySet.SurfaceDirection;

        // ── compile ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve every dial once, through <see cref="ShaperValue.Sample"/> and never
        /// <c>ZUIValue.Evaluate</c> (the duration trap, <c>ShaperValue.cs:9-13</c>). Compile time only.
        /// </summary>
        /// <summary>
        /// <b>The declared-inertness table: which dial does nothing on which form, and why.</b> Returns
        /// <c>null</c> when the dial is live on that form, and the reason sentence when it is not.
        ///
        /// LR-7.3's standard is that "a control that silently does nothing is the failure B4 says must not
        /// survive the rebuild"; a control that does nothing AND SAYS SO IS NOT THAT FAILURE. This method is
        /// the machine-readable half of saying so — <c>ShaperLightProgram.hasInertDial</c> raises it at
        /// compile in exactly the shape <c>hasTooManyLights</c> and <c>hasUnavailableFill</c> take, so the
        /// future window can grey the control and show the reason without re-deriving any of this.
        ///
        /// <b>Measured, not assumed.</b> Rendering each form twice at rotation (yaw 35, tilt 28, roll 12) and
        /// counting differing ENCODED pixels for a 1 -> 0.4 sweep gave, for aspect / depth: Box 3470 / 3014,
        /// Pyramid 2589 / 2006, Can 2623 / <b>0</b>, Orb <b>0</b> / <b>0</b>, Gem <b>0</b> / <b>0</b>, Ring
        /// <b>0</b> / <b>0</b>. Only Can's <c>depth</c> was documented; the other five zeroes were silent.
        ///
        /// <b>Why the dials are declared rather than wired.</b> Pyre's own <c>BuildGemGeometry</c>
        /// (<c>PyreRenderer.cs:4796-4818</c>) reads neither <c>solidAspect</c> nor <c>solidDepth</c> — a gem's
        /// proportions are <c>gemCrown</c> and <c>gemPavilion</c> — and <c>DrawOrb</c>'s silhouette is the
        /// plain circle <c>d &lt;= R</c> by LR-6.5b's ruling. Wiring either would be INVENTING GEOMETRY the
        /// reference does not have, against LR-6.2's "verbatim, because it is correct" and against the brief's
        /// own "do not invent geometry".
        /// </summary>
        public static string InertReason(ShaperSolidForm form, ShaperSolidDial dial)
        {
            switch (dial)
            {
                case ShaperSolidDial.Aspect:
                    if (form == ShaperSolidForm.Orb)
                        return "Aspect does nothing on an Orb. LR-6.5b rules the sphere's silhouette is the " +
                               "plain circle d <= R and is never squashed, because a sphere looks identical " +
                               "from every angle (PyreRenderer.cs:4412).";
                    if (form == ShaperSolidForm.Gem)
                        return "Aspect does nothing on a Gem. A gem's proportions are Crown and Pavilion, " +
                               "which set its height above and below the girdle; the reference builder " +
                               "(PyreRenderer.cs:4796) reads neither aspect nor depth. Use Crown and Pavilion.";
                    if (form == ShaperSolidForm.Ring)
                        return "Aspect does nothing on a Ring. A ring is a flat annulus in one plane, so it " +
                               "has no Y half-extent to multiply; its shape comes from Size, Inner and the " +
                               "tilt that foreshortens it.";
                    return null;

                case ShaperSolidDial.Depth:
                    if (form == ShaperSolidForm.Can)
                        return "Depth does nothing on a Can. Its section is circular, so the X and Z " +
                               "half-extents are both Size and there is no separate Z to scale.";
                    if (form == ShaperSolidForm.Orb)
                        return "Depth does nothing on an Orb, for LR-6.5b's reason: the sphere's silhouette " +
                               "is the plain circle d <= R at every rotation, so no half-extent multiplier " +
                               "can reach it.";
                    if (form == ShaperSolidForm.Gem)
                        return "Depth does nothing on a Gem. A gem's proportions are Crown and Pavilion; the " +
                               "reference builder (PyreRenderer.cs:4796) reads neither aspect nor depth.";
                    if (form == ShaperSolidForm.Ring)
                        return "Depth does nothing on a Ring. A ring is a flat annulus with no thickness in " +
                               "its own plane's normal direction.";
                    return null;

                case ShaperSolidDial.GemSides:
                case ShaperSolidDial.GemCrown:
                case ShaperSolidDial.GemPavilion:
                    return form == ShaperSolidForm.Gem ? null
                         : "Sides, Crown and Pavilion describe a gem's girdle and its two apexes. They do " +
                           "nothing on a " + form + ".";

                case ShaperSolidDial.RingInner:
                    return form == ShaperSolidForm.Ring ? null
                         : "Inner is the ring's hole radius. It does nothing on a " + form + ", which has no hole.";

                case ShaperSolidDial.Roll:
                    return form == ShaperSolidForm.Ring
                         ? "Roll does nothing on a Ring. Roll is a spin about Z in MODEL space and a ring is " +
                           "rotationally symmetric about its own axis, so rolling it maps the annulus onto " +
                           "itself; the reference builder (PyreRenderer.cs:4609) takes only yaw and tilt."
                         : null;

                default:
                    return null;   // Size, Centre, Yaw, Tilt, LineWidth, EdgeGlow and InnerGlow are live on every form.
            }
        }

        /// <summary>The neutral value of a dial — the value at which authoring it is not a claim about anything.</summary>
        static float Neutral(ShaperSolidDial dial)
        {
            switch (dial)
            {
                case ShaperSolidDial.Aspect: return 1f;
                case ShaperSolidDial.Depth: return 1f;
                case ShaperSolidDial.GemSides: return 6f;
                case ShaperSolidDial.GemCrown: return 0.55f;
                case ShaperSolidDial.GemPavilion: return 0.85f;
                case ShaperSolidDial.RingInner: return 0.55f;
                default: return 0f;   // Roll's neutral is 0 degrees.
            }
        }

        // ── the legal range of every Solids dial ──────────────────────────────────────────────────────────
        //
        // Same gate, same reason as ShaperLightCompiler's: a NaN or Infinity here does not stay in the
        // generator. A NaN `aspect` or `depth` reaches the surface point through the barycentric
        // interpolation at SampleFacet, so `pz` goes NaN, so `dvz` goes NaN inside Shade, and the law writes
        // NaN into `dst` exactly as an unclamped `rimPower` did. The coverage tests happen to reject NaN by
        // accident (`w0 >= 0f` is false for NaN); the surface point does not.

        const float MaxSolidCoord = 1e6f;
        const float MaxSolidScale = 1e4f;

        static float Dial(float v, float lo, float hi, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = fallback;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public static ShaperSolidOp Compile(ShaperSolidDef def, float phase01, uint seed,
                                            ShaperLightProgram prog = null)
        {
            var op = new ShaperSolidOp();
            if (def == null) return op;

            op.form = def.form;
            op.r = Dial(ShaperValue.Sample(def.size, phase01, seed + 101u, 20f), 0f, MaxSolidCoord, 20f);
            op.centreX = Dial(ShaperValue.Sample(def.centreX, phase01, seed + 102u), -MaxSolidCoord, MaxSolidCoord, 0f);
            op.centreY = Dial(ShaperValue.Sample(def.centreY, phase01, seed + 103u), -MaxSolidCoord, MaxSolidCoord, 0f);
            op.aspect = Dial(ShaperValue.Sample(def.aspect, phase01, seed + 104u, 1f), 0f, MaxSolidScale, 1f);
            op.depth = Dial(ShaperValue.Sample(def.depth, phase01, seed + 105u, 1f), 0f, MaxSolidScale, 1f);
            op.gemSides = Mathf.Clamp(Mathf.RoundToInt(Dial(ShaperValue.Sample(def.gemSides, phase01, seed + 106u, 6f), 3f, 8f, 6f)), 3, 8);
            op.gemCrown = Dial(ShaperValue.Sample(def.gemCrown, phase01, seed + 107u, 0.55f), 0f, MaxSolidScale, 0.55f);
            op.gemPavilion = Dial(ShaperValue.Sample(def.gemPavilion, phase01, seed + 108u, 0.85f), 0f, MaxSolidScale, 0.85f);
            op.ringInner = Dial(ShaperValue.Sample(def.ringInner, phase01, seed + 109u, 0.55f), 0.1f, 0.92f, 0.55f);
            op.yaw = Dial(ShaperValue.Sample(def.yaw, phase01, seed + 110u), -MaxSolidCoord, MaxSolidCoord, 0f) * Mathf.Deg2Rad;
            op.tilt = Dial(ShaperValue.Sample(def.tilt, phase01, seed + 111u), -MaxSolidCoord, MaxSolidCoord, 0f) * Mathf.Deg2Rad;
            op.roll = Dial(ShaperValue.Sample(def.roll, phase01, seed + 112u), -MaxSolidCoord, MaxSolidCoord, 0f) * Mathf.Deg2Rad;
            op.lineWidth = Dial(ShaperValue.Sample(def.lineWidth, phase01, seed + 113u, 1.1f), 0f, MaxSolidCoord, 1.1f);
            op.edgeGlow = Mathf.Clamp01(Dial(ShaperValue.Sample(def.edgeGlow, phase01, seed + 114u), 0f, 1f, 0f));
            op.innerGlow = Mathf.Clamp01(Dial(ShaperValue.Sample(def.innerGlow, phase01, seed + 115u), 0f, 1f, 0f));

            // The one place a Solids colour is decoded, and it happens ONCE, at compile — FC-2.3's single
            // decode discipline. The law never decodes and never encodes (LR-2.6).
            ShaperSrgb.Decode(def.lineColour, out op.lineR, out op.lineG, out op.lineB);
            ShaperSrgb.Decode(def.edgeGlowColour, out op.edgeGlowR, out op.edgeGlowG, out op.edgeGlowB);
            ShaperSrgb.Decode(def.innerGlowColour, out op.innerGlowR, out op.innerGlowG, out op.innerGlowB);
            op.lineR = Dial(op.lineR, 0f, MaxSolidScale, 1f); op.lineG = Dial(op.lineG, 0f, MaxSolidScale, 1f); op.lineB = Dial(op.lineB, 0f, MaxSolidScale, 1f);
            op.edgeGlowR = Dial(op.edgeGlowR, 0f, MaxSolidScale, 1f); op.edgeGlowG = Dial(op.edgeGlowG, 0f, MaxSolidScale, 1f); op.edgeGlowB = Dial(op.edgeGlowB, 0f, MaxSolidScale, 1f);
            op.innerGlowR = Dial(op.innerGlowR, 0f, MaxSolidScale, 1f); op.innerGlowG = Dial(op.innerGlowG, 0f, MaxSolidScale, 1f); op.innerGlowB = Dial(op.innerGlowB, 0f, MaxSolidScale, 1f);

            // LR-7.3 — DECLARE every dial the author moved that this form cannot use. Only a dial moved OFF
            // ITS NEUTRAL raises the diagnostic, so a default aspect of 1 on a Gem is silent and an aspect of
            // 0.4 on a Gem is not: the complaint is about a control the author operated and got nothing from,
            // which is the failure LR-7.3 names, and not about a field's existence.
            if (prog != null) DeclareInertDials(def, op, prog);
            return op;
        }

        static void DeclareInertDials(ShaperSolidDef def, in ShaperSolidOp op, ShaperLightProgram prog)
        {
            Check(op.form, ShaperSolidDial.Aspect, op.aspect, prog);
            Check(op.form, ShaperSolidDial.Depth, op.depth, prog);
            Check(op.form, ShaperSolidDial.GemSides, op.gemSides, prog);
            Check(op.form, ShaperSolidDial.GemCrown, op.gemCrown, prog);
            Check(op.form, ShaperSolidDial.GemPavilion, op.gemPavilion, prog);
            Check(op.form, ShaperSolidDial.RingInner, op.ringInner, prog);
            Check(op.form, ShaperSolidDial.Roll, op.roll * Mathf.Rad2Deg, prog);
        }

        static void Check(ShaperSolidForm form, ShaperSolidDial dial, float value, ShaperLightProgram prog)
        {
            string reason = InertReason(form, dial);
            if (reason == null) return;
            float neutral = Neutral(dial);
            if (Mathf.Abs(value - neutral) <= 1e-6f) return;

            if (!prog.hasInertDial)
            {
                prog.hasInertDial = true;
                prog.inertDialName = dial.ToString();
                prog.inertDialForm = form.ToString();
                prog.inertDialReason = reason;
            }
            prog.inertDialCount++;
        }

        // ── geometry, built once ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the rotated, culled, resolved geometry for one compiled solid. Allocates; called once per
        /// compile, never from a tile loop.
        /// </summary>
        public static ShaperSolidGeometry Build(in ShaperSolidOp op)
        {
            switch (op.form)
            {
                case ShaperSolidForm.Orb: return BuildOrb(op);
                case ShaperSolidForm.Ring: return BuildRing(op);
                default: return BuildFacet(op);
            }
        }

        static ShaperSolidGeometry BuildOrb(in ShaperSolidOp op)
        {
            var g = new ShaperSolidGeometry
            {
                minX = -op.r, maxX = op.r, minY = -op.r, maxY = op.r,
                planeNx = 0f, planeNy = 0f, planeNz = 1f,
            };

            // LR-6.5b, and THE ONE PLACE THIS FILE DEPARTS FROM PYRE'S FORMULATION — flagged rather than
            // smuggled, because the departure is forced by LR-1.1 and the result is provably the same
            // shading.
            //
            // Pyre rotates the LIGHT by the inverse of the sphere's rotation and shades with the true
            // N = P/R (PyreRenderer.cs:4442-4455), justified by the identity N.(Rot^-1.L) == (Rot.N).L, which
            // holds because a rotation preserves dot products and lengths. Under a SHARED rig that
            // formulation is not available: it is a per-particle private light, and LR-1.1 says no light is
            // owned by a generator and no stage may add one at render time. So this generator takes the OTHER
            // side of Pyre's own identity and rotates the published NORMAL FORWARD, leaving the one shared
            // light exactly where the author put it.
            //
            // The one divergence from Pyre that follows, named rather than left to be discovered: Pyre also
            // rotates the light for the ATTENUATION term (|Rot^-1.L - P| == |L - Rot.P|), i.e. it measures
            // distance from the ROTATED surface point. This generator measures it from the TRUE canvas
            // position of the sample, which is what LR-1.5 requires of every surface point in the document.
            // Diffuse direction, specular direction and rim are identical; only a POINT light's falloff
            // differs, by the distance between P and Rot.P, and for a directional light the two are
            // bit-identical because there is no falloff at all.
            float cro = Mathf.Cos(op.roll), sro = Mathf.Sin(op.roll);
            float cyw = Mathf.Cos(op.yaw), syw = Mathf.Sin(op.yaw);
            float ct = Mathf.Cos(op.tilt), st = Mathf.Sin(op.tilt);

            // The forward model rotation, ROLL(Z) then YAW(Y) then TILT(X) — PyreRenderer.cs:4193-4198's Rot,
            // written out as a matrix because this one is applied per sample to a per-sample normal.
            // Roll:  (x cos - y sin, x sin + y cos, z)
            // Yaw:   (x cyw + z syw, y, -x syw + z cyw)
            // Tilt:  (x, y ct - z st, y st + z ct)
            // Composed, row by row:
            g.m00 = cro * cyw; g.m01 = -sro * cyw; g.m02 = syw;
            g.m10 = cro * syw * st + sro * ct; g.m11 = -sro * syw * st + cro * ct; g.m12 = -cyw * st;
            g.m20 = -cro * syw * ct + sro * st; g.m21 = sro * syw * ct + cro * st; g.m22 = cyw * ct;
            return g;
        }

        static ShaperSolidGeometry BuildRing(in ShaperSolidOp op)
        {
            var g = new ShaperSolidGeometry();
            float cyw = Mathf.Cos(op.yaw), syw = Mathf.Sin(op.yaw);
            float ct = Mathf.Cos(op.tilt), st = Mathf.Sin(op.tilt);

            // LR-6.5c — the edge-on degeneracy is a HARD EARLY RETURN, not a fade (PyreRenderer.cs:4633).
            // Carried across verbatim, including its discontinuity, because LT-14c locks it as a KNOWN DEFECT
            // and a regression guard. If a later task fades the ring out instead, that test must be updated
            // deliberately — which is the whole reason it exists.
            if (Mathf.Abs(ct) < 0.02f || Mathf.Abs(cyw) < 0.02f) { g.degenerate = true; return g; }

            g.cyw = cyw; g.syw = syw; g.ct = ct; g.st = st; g.sywst = syw * st;

            // The screen-space plane normal is Rot(0,0,1) = (syw, -cyw*st, cyw*ct); pick the viewer-facing
            // side (N.z > 0) — the ring is TWO-SIDED so it is never culled, only flipped (:4636-4640). N is
            // CONSTANT (the plane is flat), so it is computed once here and not per sample.
            float nx = syw, ny = -cyw * st, nz = cyw * ct;
            if (nz < 0f) { nx = -nx; ny = -ny; nz = -nz; }
            float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; }
            else { nx = 0f; ny = 0f; nz = 1f; }                     // LR-3.5's degenerate answer, never zero
            g.planeNx = nx; g.planeNy = ny; g.planeNz = nz;

            float haloR = Mathf.Max(2.5f, 0.24f * op.r);
            g.maxX = op.r * Mathf.Abs(cyw) + haloR; g.minX = -g.maxX;
            g.maxY = op.r * (Mathf.Abs(g.sywst) + Mathf.Abs(ct)) + haloR; g.minY = -g.maxY;
            return g;
        }

        static ShaperSolidGeometry BuildFacet(in ShaperSolidOp op)
        {
            Vector3[] model;
            int[][] faces;
            HashSet<int> lineEdges;
            switch (op.form)
            {
                case ShaperSolidForm.Pyramid: BuildPyramidGeometry(op, out model, out faces, out lineEdges); break;
                case ShaperSolidForm.Can: BuildCanGeometry(op, out model, out faces, out lineEdges); break;
                case ShaperSolidForm.Gem: BuildGemGeometry(op, out model, out faces, out lineEdges); break;
                default: BuildBoxGeometry(op, out model, out faces, out lineEdges); break;
            }

            // LR-6.2: the rotation, verbatim in substance from PyreRenderer.cs:4193-4198. ROLL about Z in
            // model space FIRST, then yaw about Y, then the world tilt about X. +Z toward the viewer.
            float cro = Mathf.Cos(op.roll), sro = Mathf.Sin(op.roll);
            float cyw = Mathf.Cos(op.yaw), syw = Mathf.Sin(op.yaw);
            float ct = Mathf.Cos(op.tilt), st = Mathf.Sin(op.tilt);
            bool doRoll = op.roll != 0f;

            var verts = new Vector3[model.Length];
            for (int k = 0; k < model.Length; k++)
            {
                Vector3 p = model[k];
                if (doRoll) p = new Vector3(p.x * cro - p.y * sro, p.x * sro + p.y * cro, p.z);
                var q = new Vector3(p.x * cyw + p.z * syw, p.y, -p.x * syw + p.z * cyw);
                verts[k] = new Vector3(q.x, q.y * ct - q.z * st, q.y * st + q.z * ct);
            }

            int F = faces.Length;
            var g = new ShaperSolidGeometry
            {
                visAx = new float[F], visAy = new float[F],
                visBx = new float[F], visBy = new float[F],
                visCx = new float[F], visCy = new float[F],
                visP = new float[F * 9], visN = new float[F * 3],
                lineAx = new float[F * 3], lineAy = new float[F * 3],
                lineBx = new float[F * 3], lineBy = new float[F * 3],
                silAx = new float[F * 3], silAy = new float[F * 3],
                silBx = new float[F * 3], silBy = new float[F * 3],
            };

            // Visible faces: outward normal (flip against the centroid so the origin-inside-the-solid
            // convention holds), then the backface cull n.z <= 0, then the normalise. LR-6.2, from
            // PyreRenderer.cs:4234-4236 — and that normalise is what LT-15 asserts and what its stated
            // mutation removes.
            var seenLine = new HashSet<int>();
            var visibleEdgeUse = new Dictionary<int, int>();
            var visFaceEdges = new List<int>(F * 3);

            for (int fi = 0; fi < F; fi++)
            {
                int[] f = faces[fi];
                Vector3 p0 = verts[f[0]], p1 = verts[f[1]], p2 = verts[f[2]];
                Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 centroid = (p0 + p1 + p2) / 3f;
                if (Vector3.Dot(nrm, centroid) < 0f) nrm = -nrm;    // force outward (origin is inside)
                if (nrm.z <= 0f) continue;                           // backface: not seen
                if (nrm.sqrMagnitude < 1e-20f) continue;             // degenerate triangle: no direction to publish
                nrm = nrm.normalized;

                int v = g.visCount;
                g.visAx[v] = p0.x; g.visAy[v] = p0.y;
                g.visBx[v] = p1.x; g.visBy[v] = p1.y;
                g.visCx[v] = p2.x; g.visCy[v] = p2.y;
                g.visP[v * 9 + 0] = p0.x; g.visP[v * 9 + 1] = p0.y; g.visP[v * 9 + 2] = p0.z;
                g.visP[v * 9 + 3] = p1.x; g.visP[v * 9 + 4] = p1.y; g.visP[v * 9 + 5] = p1.z;
                g.visP[v * 9 + 6] = p2.x; g.visP[v * 9 + 7] = p2.y; g.visP[v * 9 + 8] = p2.z;
                g.visN[v * 3 + 0] = nrm.x; g.visN[v * 3 + 1] = nrm.y; g.visN[v * 3 + 2] = nrm.z;
                g.visCount++;

                for (int e = 0; e < 3; e++)
                {
                    int i0 = f[e], i1 = f[(e + 1) % 3];
                    int key = EdgeKey(i0, i1);
                    visFaceEdges.Add(key);
                    visibleEdgeUse.TryGetValue(key, out int used);
                    visibleEdgeUse[key] = used + 1;

                    // lineEdges == null means every edge is a line (Gem's original behaviour). A non-null set
                    // names the LINE edges only, so quad diagonals, barrel seams and cap spokes are skipped —
                    // they neither draw a hard line nor seed the inner glow, which measures distance to this
                    // same edge set (PyreRenderer.cs:4238-4254).
                    if (lineEdges != null && !lineEdges.Contains(key)) continue;
                    if (!seenLine.Add(key)) continue;
                    g.lineAx[g.lineCount] = verts[i0].x; g.lineAy[g.lineCount] = verts[i0].y;
                    g.lineBx[g.lineCount] = verts[i1].x; g.lineBy[g.lineCount] = verts[i1].y;
                    g.lineCount++;
                }
            }

            // The SILHOUETTE edge set: a visible edge used by exactly ONE visible face is on the outline;
            // one used by two visible faces is an interior seam. That distinction is what lets this generator
            // publish a real `edgeDistance` (the shape's edge) rather than a distance to the facet seams,
            // which are an interior feature. It is derived here rather than assumed, and it is the reason
            // Solids can carry a border and a ByEdgeDistance ramp at all.
            var seenSil = new HashSet<int>();
            for (int vi = 0; vi < visFaceEdges.Count; vi++)
            {
                int key = visFaceEdges[vi];
                if (visibleEdgeUse[key] != 1) continue;
                if (!seenSil.Add(key)) continue;
                int i0 = key / 64, i1 = key % 64;
                g.silAx[g.silCount] = verts[i0].x; g.silAy[g.silCount] = verts[i0].y;
                g.silBx[g.silCount] = verts[i1].x; g.silBy[g.silCount] = verts[i1].y;
                g.silCount++;
            }

            float mnx = float.MaxValue, mxx = -float.MaxValue, mny = float.MaxValue, mxy = -float.MaxValue;
            for (int k = 0; k < verts.Length; k++)
            {
                if (verts[k].x < mnx) mnx = verts[k].x;
                if (verts[k].x > mxx) mxx = verts[k].x;
                if (verts[k].y < mny) mny = verts[k].y;
                if (verts[k].y > mxy) mxy = verts[k].y;
            }
            float halo = Mathf.Max(2.5f, 0.24f * op.r);
            g.minX = mnx - halo; g.maxX = mxx + halo; g.minY = mny - halo; g.maxY = mxy + halo;
            if (g.visCount == 0) g.degenerate = true;
            return g;
        }

        // ── the tile pass ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Publish coverage, edge distance, the surface normal, the facet-edge-line mask and the glow for one
        /// tile. The tile mapping is a pure function of the ABSOLUTE sample index, exactly as
        /// <c>ShaperEvaluator.FillTile</c>'s is, which is what makes tile independence structural rather than
        /// a convention (BC-1.6, LT-3).
        ///
        /// <b>Allocates nothing</b> (LT-2): the geometry is prebuilt by <see cref="Build"/> and the host owns
        /// every array. This is the discipline <c>PyreRenderer.cs:1803</c> and <c>:1857</c> violate today with
        /// a <c>new float[W * H]</c> per layer per frame.
        /// </summary>
        public static void FillTile(ShaperSolidGeometry geo, in ShaperSolidOp op, in ShaperSampleGrid grid,
                                    int x0, int y0, int width, int height,
                                    in ShaperSolidEmit emit,
                                    int dstOffset, int dstStride)
        {
            if (geo == null) return;

            float haloR = Mathf.Max(2.5f, 0.24f * op.r);
            float innerR = Mathf.Max(3f, 0.30f * op.r);

            for (int j = 0; j < height; j++)
            {
                float cy = grid.originY + (y0 + j) * grid.pixelSize;
                int row = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    float cx = grid.originX + (x0 + i) * grid.pixelSize;
                    int t = row + i;

                    // Solid-local canvas coordinates. The solid's centre is an absolute canvas position
                    // (LR-1.5), so the surface point published below is in canvas units at the canvas origin
                    // and NOT in a per-particle frame — which is the whole content of LR-1.6's break.
                    float lx = cx - op.centreX, ly = cy - op.centreY;

                    float cov = 0f, dist = 1e9f, line = 0f;
                    float nx = 0f, ny = 0f, nz = 1f;                 // LR-3.5's degenerate answer
                    float px = lx, py = ly, pz = 0f;
                    float gr = 0f, gg = 0f, gb = 0f;

                    if (!geo.degenerate)
                    {
                        switch (op.form)
                        {
                            case ShaperSolidForm.Orb:
                                SampleOrb(geo, op, lx, ly, ref cov, ref dist, ref line,
                                          ref nx, ref ny, ref nz, ref px, ref py, ref pz);
                                break;
                            case ShaperSolidForm.Ring:
                                SampleRing(geo, op, lx, ly, ref cov, ref dist, ref line,
                                           ref nx, ref ny, ref nz, ref px, ref py, ref pz);
                                break;
                            default:
                                SampleFacet(geo, op, lx, ly, ref cov, ref dist, ref line,
                                            ref nx, ref ny, ref nz, ref px, ref py, ref pz);
                                break;
                        }

                        // Halo and inner glow, kept in the generator per LR-6.4. Emitted as an ADDITIVE,
                        // UNLIT triple, because a glow is light rather than paint — LR-5.3's argument applied
                        // one level down. `dist` here is the distance to the SILHOUETTE for the glow's
                        // purposes, matching Pyre's `edist`/`rimDist` for Orb and Ring; the facet path uses
                        // its line-edge distance instead, which is what Pyre measures (:4327-4330).
                        if (cov > 0f && (op.edgeGlow > 0f || op.innerGlow > 0f))
                        {
                            float ed = op.form == ShaperSolidForm.Box || op.form == ShaperSolidForm.Pyramid ||
                                       op.form == ShaperSolidForm.Can || op.form == ShaperSolidForm.Gem
                                     ? NearestLine(geo, lx, ly)
                                     : Mathf.Abs(dist);

                            float h = 1f - ed / haloR; if (h < 0f) h = 0f;
                            float haloAmt = 0.85f * op.edgeGlow * h * h;
                            gr += op.edgeGlowR * haloAmt; gg += op.edgeGlowG * haloAmt; gb += op.edgeGlowB * haloAmt;

                            if (line == 0f)
                            {
                                float core = Mathf.Pow(Mathf.Clamp01(ed / innerR), 1.4f);
                                float innerAmt = 0.75f * op.innerGlow * core;
                                gr += op.innerGlowR * innerAmt; gg += op.innerGlowG * innerAmt; gb += op.innerGlowB * innerAmt;
                            }
                        }
                    }

                    if (emit.coverage != null) emit.coverage[t] = cov;
                    if (emit.distance != null) emit.distance[t] = dist;
                    if (emit.lineMask != null) emit.lineMask[t] = line;
                    if (emit.normal != null)
                    {
                        emit.normal[t * 3 + 0] = nx;
                        emit.normal[t * 3 + 1] = ny;
                        emit.normal[t * 3 + 2] = nz;
                    }
                    if (emit.glow != null)
                    {
                        emit.glow[t * 3 + 0] = gr;
                        emit.glow[t * 3 + 1] = gg;
                        emit.glow[t * 3 + 2] = gb;
                    }
                    if (emit.pointZ != null) emit.pointZ[t] = pz;

                    // px and py are the sample's own canvas position by construction (orthographic, LR-6.5d),
                    // which is exactly what the host will pass to the law — so they are asserted here rather
                    // than published. A form that ever breaks this would have to publish them too.
                    _ = px; _ = py;
                }
            }
        }

        static void SampleFacet(ShaperSolidGeometry g, in ShaperSolidOp op, float lx, float ly,
                                ref float cov, ref float dist, ref float line,
                                ref float nx, ref float ny, ref float nz,
                                ref float px, ref float py, ref float pz)
        {
            // Silhouette distance first — it is published whether or not the sample is covered.
            float sd = float.MaxValue;
            for (int e = 0; e < g.silCount; e++)
            {
                float d = DistSeg(lx, ly, g.silAx[e], g.silAy[e], g.silBx[e], g.silBy[e]);
                if (d < sd) sd = d;
            }

            for (int v = 0; v < g.visCount; v++)
            {
                if (!InTri(lx, ly, g.visAx[v], g.visAy[v], g.visBx[v], g.visBy[v], g.visCx[v], g.visCy[v],
                           out float w0, out float w1, out float w2)) continue;

                cov = 1f;                                            // LR-6.4: HARD coverage
                int b = v * 9;
                px = g.visP[b + 0] * w0 + g.visP[b + 3] * w1 + g.visP[b + 6] * w2;
                py = g.visP[b + 1] * w0 + g.visP[b + 4] * w1 + g.visP[b + 7] * w2;
                pz = g.visP[b + 2] * w0 + g.visP[b + 5] * w1 + g.visP[b + 8] * w2;
                nx = g.visN[v * 3 + 0]; ny = g.visN[v * 3 + 1]; nz = g.visN[v * 3 + 2];
                break;                                               // LR-6.5a: convex + culled, first hit wins
            }

            dist = cov > 0f ? -sd : sd;

            // The FACET EDGE LINE (LR-6.3). Where it fires, the host substitutes the line colour as the
            // sample's albedo AND KEEPS THIS FACE'S NORMAL, so the line is lit by the shared law exactly like
            // the face it sits on. Pyre's `k = Clamp(0.25 + lit, 0, 1.15)` (:4349) is deleted and is not
            // reproduced here or anywhere else in this file.
            if (cov > 0f && op.lineWidth > 0f && NearestLine(g, lx, ly) <= op.lineWidth) line = 1f;
        }

        static float NearestLine(ShaperSolidGeometry g, float lx, float ly)
        {
            float e = float.MaxValue;
            for (int k = 0; k < g.lineCount; k++)
            {
                float d = DistSeg(lx, ly, g.lineAx[k], g.lineAy[k], g.lineBx[k], g.lineBy[k]);
                if (d < e) e = d;
            }
            return e == float.MaxValue ? 1e9f : e;
        }

        static void SampleOrb(ShaperSolidGeometry g, in ShaperSolidOp op, float lx, float ly,
                              ref float cov, ref float dist, ref float line,
                              ref float nx, ref float ny, ref float nz,
                              ref float px, ref float py, ref float pz)
        {
            float R = op.r;
            float d = Mathf.Sqrt(lx * lx + ly * ly);

            // LR-6.5b — the silhouette is the plain circle d <= R and is NEVER squashed by spin or tilt
            // (PyreRenderer.cs:4412: "a sphere looks identical from every angle"). LT-14b asserts exactly
            // this: the alpha channel is bit-identical across a rotation while the RGB is not.
            dist = d - R;                                            // the exact SDF; LR-6.4 keeps coverage hard anyway
            if (d > R) return;

            cov = 1f;
            float z = Mathf.Sqrt(Mathf.Max(0f, R * R - d * d));
            px = lx; py = ly; pz = z;

            // Orb's analytic normal N = P/R (PyreRenderer.cs:4533), then rotated FORWARD by the model
            // rotation — see BuildOrb for why this file takes that side of Pyre's own identity rather than
            // rotating the light.
            float ux = lx / R, uy = ly / R, uz = z / R;
            nx = g.m00 * ux + g.m01 * uy + g.m02 * uz;
            ny = g.m10 * ux + g.m11 * uy + g.m12 * uz;
            nz = g.m20 * ux + g.m21 * uy + g.m22 * uz;
            float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-6f) { float inv = 1f / len; nx *= inv; ny *= inv; nz *= inv; }
            else { nx = 0f; ny = 0f; nz = 1f; }

            // The orb's ONLY edge is the silhouette rim, so the "facet edge line" is that rim — the same
            // read Pyre makes at :4541.
            if (op.lineWidth > 0f && Mathf.Abs(d - R) <= op.lineWidth) line = 1f;
        }

        static void SampleRing(ShaperSolidGeometry g, in ShaperSolidOp op, float lx, float ly,
                               ref float cov, ref float dist, ref float line,
                               ref float nx, ref float ny, ref float nz,
                               ref float px, ref float py, ref float pz)
        {
            float R = op.r, innerR = op.r * op.ringInner;

            // Invert the forward map: u = lx/cyw ; v = (ly - u*syw*st)/ct  (PyreRenderer.cs:4692-4693).
            float u = lx / g.cyw;
            float v = (ly - u * g.sywst) / g.ct;
            float rho = Mathf.Sqrt(u * u + v * v);

            float dBand = Mathf.Max(innerR - rho, rho - R);

            // The gradient-magnitude correction, LR-6.2. The tilt and spin compress the ellipse in screen
            // space, so a fixed ring-plane distance spans FEWER screen pixels near the ellipse's flat sides;
            // dividing by |grad rho| restores a uniform SCREEN thickness all the way around. Floored at 0.2
            // so a near-zero gradient cannot blow the distance up.
            float drdx = rho > 1e-4f ? (u / g.cyw - v * g.sywst / (g.cyw * g.ct)) / rho : 0f;
            float drdy = rho > 1e-4f ? (v / g.ct) / rho : 0f;
            float gradMag = Mathf.Max(0.2f, Mathf.Sqrt(drdx * drdx + drdy * drdy));

            dist = dBand / gradMag;
            if (dBand > 0f) return;

            cov = 1f;
            // The 3D point on the tilted plane at this sample (orthographic, so its screen x/y ARE lx/ly;
            // z from the forward map's P.z = v*st - u*syw*ct) — PyreRenderer.cs:4717.
            px = lx; py = ly; pz = v * g.st - u * g.syw * g.ct;
            nx = g.planeNx; ny = g.planeNy; nz = g.planeNz;

            if (op.lineWidth > 0f && Mathf.Abs(dist) <= op.lineWidth) line = 1f;
        }

        // ── LR-6.2: verbatim-in-substance ports ───────────────────────────────────────────────────────────

        /// <summary>
        /// <c>PyreRenderer.cs:4793</c>. Valid while every vertex index is below 64 — true here: Gem is at most
        /// 10, Box 8, Pyramid 5, Can 34.
        /// </summary>
        static int EdgeKey(int i, int j) => i < j ? i * 64 + j : j * 64 + i;

        /// <summary>
        /// Barycentric point-in-triangle. ORTHOGRAPHIC: screen bary == plane bary (<c>:4936-4938</c>), which
        /// is LR-6.5d's constraint made concrete — introducing perspective breaks this function's premise, not
        /// just its constants.
        /// </summary>
        static bool InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy,
                          out float w0, out float w1, out float w2)
        {
            w0 = 0f; w1 = 0f; w2 = 0f;
            float d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (d < 1e-6f && d > -1e-6f) return false;
            w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
            w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
            w2 = 1f - w0 - w1;
            return w0 >= 0f && w1 >= 0f && w2 >= 0f;
        }

        /// <summary><c>PyreRenderer.cs:4949</c>, in loose floats so no <c>Vector2</c> enters the loop.</summary>
        static float DistSeg(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float len2 = abx * abx + aby * aby;
            float t = ((px - ax) * abx + (py - ay) * aby) / (len2 > 1e-6f ? len2 : 1e-6f);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float dx = px - (ax + abx * t), dy = py - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Gem: n girdle verts + a crown apex + a pavilion apex (<c>PyreRenderer.cs:4796</c>).
        /// <c>lineEdges == null</c> means every visible facet boundary is a line — the gem's original look.
        /// </summary>
        static void BuildGemGeometry(in ShaperSolidOp op, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            int n = Mathf.Clamp(op.gemSides, 3, 8);
            float R = op.r;
            float crownH = R * op.gemCrown;
            float pavD = R * op.gemPavilion;

            model = new Vector3[n + 2];
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * k / n;
                model[k] = new Vector3(R * Mathf.Cos(a), 0f, R * Mathf.Sin(a));
            }
            model[n] = new Vector3(0f, crownH, 0f);
            model[n + 1] = new Vector3(0f, -pavD, 0f);

            faces = new int[2 * n][];
            for (int i = 0; i < n; i++) faces[i] = new[] { n, i, (i + 1) % n };
            for (int i = 0; i < n; i++) faces[n + i] = new[] { n + 1, (i + 1) % n, i };
            lineEdges = null;
        }

        /// <summary>
        /// Box: a real cuboid, half-extents <c>(R, R*aspect, R*depth)</c>, 8 SHARED verts
        /// (<c>PyreRenderer.cs:4825</c>). The genuinely-shared vertices are what make the silhouette CLOSE.
        /// Each quad's splitting diagonal is non-line, so the line set is exactly the 12 cube edges.
        /// </summary>
        static void BuildBoxGeometry(in ShaperSolidOp op, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float hx = op.r, hy = op.r * op.aspect, hz = op.r * op.depth;
            model = new[]
            {
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz),
            };
            int[][] quads =
            {
                new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 },
                new[] { 0, 1, 5, 4 }, new[] { 3, 2, 6, 7 },
                new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 },
            };
            faces = new int[12][];
            lineEdges = new HashSet<int>();
            for (int q = 0; q < quads.Length; q++)
            {
                int a = quads[q][0], b = quads[q][1], c = quads[q][2], d = quads[q][3];
                faces[q * 2] = new[] { a, b, c };
                faces[q * 2 + 1] = new[] { a, c, d };
                lineEdges.Add(EdgeKey(a, b));
                lineEdges.Add(EdgeKey(b, c));
                lineEdges.Add(EdgeKey(c, d));
                lineEdges.Add(EdgeKey(d, a));
            }
        }

        /// <summary>Pyramid (<c>PyreRenderer.cs:4869</c>). Line set: 4 apex spokes + 4 base perimeter edges.</summary>
        static void BuildPyramidGeometry(in ShaperSolidOp op, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float R = op.r;
            float apexY = R * op.aspect;
            float baseY = -R * op.aspect * 0.35f;
            float hz = R * op.depth;
            model = new[]
            {
                new Vector3(0f, apexY, 0f),
                new Vector3(-R, baseY, -hz), new Vector3(R, baseY, -hz),
                new Vector3(R, baseY, hz), new Vector3(-R, baseY, hz),
            };
            faces = new[]
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 4 }, new[] { 0, 4, 1 },
                new[] { 1, 2, 3 }, new[] { 1, 3, 4 },
            };
            lineEdges = new HashSet<int>
            {
                EdgeKey(0, 1), EdgeKey(0, 2), EdgeKey(0, 3), EdgeKey(0, 4),
                EdgeKey(1, 2), EdgeKey(2, 3), EdgeKey(3, 4), EdgeKey(4, 1),
            };
        }

        /// <summary>
        /// Can: a 16-sided prism (<c>PyreRenderer.cs:4903</c>); <c>depth</c> is unused because the section is
        /// circular. LINE edges = the two cap rims ONLY. Flat per-face barrel normals band into 16 strips —
        /// accepted retro banding, carried across as-is.
        /// </summary>
        static void BuildCanGeometry(in ShaperSolidOp op, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            const int seg = 16;
            float R = op.r;
            float hy = R * op.aspect;
            model = new Vector3[seg * 2 + 2];
            for (int k = 0; k < seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                float vx = R * Mathf.Cos(a), vz = R * Mathf.Sin(a);
                model[k] = new Vector3(vx, hy, vz);
                model[seg + k] = new Vector3(vx, -hy, vz);
            }
            int topC = seg * 2, botC = seg * 2 + 1;
            model[topC] = new Vector3(0f, hy, 0f);
            model[botC] = new Vector3(0f, -hy, 0f);

            var faceList = new List<int[]>(seg * 4);
            lineEdges = new HashSet<int>();
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                int t0 = k, t1 = k1, b0 = seg + k, b1 = seg + k1;
                faceList.Add(new[] { t0, t1, b1 });
                faceList.Add(new[] { t0, b1, b0 });
                faceList.Add(new[] { topC, t0, t1 });
                faceList.Add(new[] { botC, b1, b0 });
                lineEdges.Add(EdgeKey(t0, t1));
                lineEdges.Add(EdgeKey(b0, b1));
            }
            faces = faceList.ToArray();
        }
    }
}
