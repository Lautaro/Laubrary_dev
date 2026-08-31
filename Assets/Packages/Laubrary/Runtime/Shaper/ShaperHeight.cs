using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The extrusion profile catalogue. HS-2.1.
    ///
    /// <b>APPEND-ONLY, serialized as an int</b>, and the ORDER IS LOAD-BEARING: it matches
    /// <c>EXTRUSION_TECHNIQUES</c> at <c>project_document.py:28</c>
    /// (<c>("flat","linear","stepped","dome","round","taper","pyramid")</c>) so a future importer of a
    /// reference-app document maps technique by INDEX and never by string. The same discipline
    /// <see cref="ShaperSolidForm"/>, <see cref="ShaperQuantity"/> and <see cref="ShaperFillKind"/> already
    /// carry. Never renumber; a new technique goes on the END.
    /// </summary>
    public enum ShaperExtrusionTechnique
    {
        /// <summary>Full-depth plateau. <c>E ≡ 1</c>. The exact identity in <c>t</c>. <c>index.html:1154</c>.</summary>
        Flat = 0,
        /// <summary>Tilted constant-thickness slab. <b>The one profile that does not read <c>t</c>.</b> <c>index.html:1148</c>.</summary>
        Linear = 1,
        /// <summary>Terraces. A floor function, so genuinely DISCONTINUOUS. <c>index.html:1149</c>.</summary>
        Stepped = 2,
        /// <summary>Spherical. <c>index.html:1150</c>.</summary>
        Dome = 3,
        /// <summary>Soft sqrt falloff. <c>index.html:1151</c>.</summary>
        Round = 4,
        /// <summary>Frustum. <c>index.html:1152</c>.</summary>
        Taper = 5,
        /// <summary>Falls to a point. <c>index.html:1153</c>.</summary>
        Pyramid = 6,
    }

    /// <summary>
    /// The bevel catalogue. HS-3.1 — <b>six authored entries, five functions plus the identity.</b>
    ///
    /// APPEND-ONLY, serialized as an int, order matching <c>BEVEL_TECHNIQUES</c> at
    /// <c>project_document.py:29</c> (<c>("none","linear","rounded","cove","ogee","stepped")</c>) for the same
    /// index-mapping reason as <see cref="ShaperExtrusionTechnique"/>.
    ///
    /// <see cref="None"/> is NOT dropped to make the count five: it is a real serialized value and the layer
    /// default (<c>index.html:2428</c> creates layers with <c>bevel:{type:'none'}</c>), and
    /// <c>index.html:2751</c> excludes it — by name — from the techniques that even show the <c>amount</c>
    /// slider. Count the shapes and you get five; count the enum and you get six. Both are right about
    /// different things (REF-HEIGHT-MATHS §B).
    /// </summary>
    public enum ShaperBevelTechnique
    {
        /// <summary>Sharp edge. <c>B ≡ 1</c>, a HARD identity taken before any arithmetic (<c>index.html:1161</c>).</summary>
        None = 0,
        /// <summary>Chamfer. <c>index.html:1163</c>.</summary>
        Linear = 1,
        /// <summary>Fillet. <c>index.html:1164</c>.</summary>
        Rounded = 2,
        /// <summary>Concave fillet. <c>index.html:1165</c>.</summary>
        Cove = 3,
        /// <summary>S-curve. <c>index.html:1166</c>.</summary>
        Ogee = 4,
        /// <summary>Micro-terraces. A DIFFERENT function from <see cref="ShaperExtrusionTechnique.Stepped"/> — HS-3.4.</summary>
        Stepped = 5,
    }

    /// <summary>
    /// The authored height stage of one layer: an extrusion profile, a bevel, and the dials both read. HS-2/HS-3.
    ///
    /// Every scalar is a <see cref="ZUIValue"/> sampled ONCE at compile through
    /// <see cref="ShaperValue.Sample"/> (LR-1.8 / BC-1.2) — the same funnel every other Shaper dial goes
    /// through, and never called from inside a per-sample loop.
    ///
    /// <b>Defaults are the reference app's, by value.</b> <c>angle 45</c> (<c>project_document.py:251</c>),
    /// <c>steps 4</c> (<c>:252</c>), <c>curve 1</c> (<c>:253</c>), <c>taper 1</c> (<c>:254</c>),
    /// <c>bevelAmount 0.25</c> (<c>:266</c>), <c>bevelSteps 3</c> (<c>:267</c>), and <c>depth 0</c>
    /// (<c>index.html:3805</c>'s <c>default:0</c>). A default document therefore has NO extrusion at all,
    /// which is the reference's own behaviour and is what makes HS-1.4's "publishes Height only when a height
    /// stage is present" a live path rather than a facility.
    /// </summary>
    [Serializable]
    public class ShaperHeightDef
    {
        /// <summary>Which extrusion profile. HS-2.1.</summary>
        public ShaperExtrusionTechnique technique = ShaperExtrusionTechnique.Flat;

        /// <summary>
        /// Extrusion THICKNESS, canvas pixels, NEVER negative — HS-1.3, <c>body = max(0, depth)</c>.
        ///
        /// <b>HS-7.4, the naming trap, recorded at the field it traps.</b> In the direct reference (3D Shaper)
        /// the dial labelled "Depth" is thickness, non-negative, <c>[0, 2048]</c> — "how far this layer
        /// extrudes from its face, in canvas units" (<c>index.html:3805</c>, <c>project_document.py:57</c>).
        /// In its ANCESTOR the slider labelled "Depth" is the OPPOSITE — <c>L.z</c>, the Z POSITION, range
        /// −8..32, with thickness on a separate slider called "Shape height"
        /// (<c>…draft6-1.html:26</c>). Shaper calls thickness <c>depth</c> and position
        /// <see cref="ShaperLayer.zOffset"/>, and never uses the word "depth" for position. A porting agent
        /// reading the ancestor's UI labels will get these two exactly backwards.
        /// </summary>
        public ZUIValue depth = new ZUIValue(0f);

        /// <summary>Linear only: tilt direction, degrees, <c>[−180, 180]</c>. <c>project_document.py:251</c>.</summary>
        public ZUIValue angle = new ZUIValue(45f);

        /// <summary>Stepped extrusion only: tread count, <c>[2, 32]</c> integer. <c>project_document.py:252</c>.</summary>
        public ZUIValue steps = new ZUIValue(4f);

        /// <summary>Dome/Round only: curvature, <c>[0.2, 4]</c>. <c>project_document.py:253</c>.</summary>
        public ZUIValue curve = new ZUIValue(1f);

        /// <summary>Taper/Pyramid only: <c>[0, 1]</c>. <c>project_document.py:254</c>.</summary>
        public ZUIValue taper = new ZUIValue(1f);

        /// <summary>Which bevel. HS-3.1. <c>None</c> is the default and is a real serialized value.</summary>
        public ShaperBevelTechnique bevel = ShaperBevelTechnique.None;

        /// <summary>
        /// Bevel band width as a FRACTION of <c>span</c>, <c>[0, 1]</c>, default 0.25
        /// (<c>project_document.py:266</c>). HS-3.2: because <c>a</c> scales <c>span</c> and <c>span</c> is
        /// the SHORTER half-extent, the band is a constant fraction of that half-extent and therefore NOT a
        /// constant-width rim on an elongated silhouette. That is the reference's behaviour and it is ported
        /// deliberately, not fixed.
        /// </summary>
        public ZUIValue bevelAmount = new ZUIValue(0.25f);

        /// <summary>
        /// Stepped BEVEL only: micro-terrace count, <c>[2, 16]</c> integer, default 3
        /// (<c>project_document.py:267</c>). <b>A different dial from <see cref="steps"/> with a different
        /// range and a different default</b> — HS-3.4.
        /// </summary>
        public ZUIValue bevelSteps = new ZUIValue(3f);
    }

    /// <summary>
    /// The compiled height stage: flat, blittable, every dial resolved, every constant the per-sample path
    /// would otherwise recompute precomputed. <see cref="ShaperSolidOp"/>'s form (FC-5.2), for the same
    /// reason — a height stage does not nest, so its "program" is one struct and a switch.
    ///
    /// It carries no managed reference of any kind, which is what lets it sit inside
    /// <see cref="ShaperNormalOp"/> without making that struct unblittable.
    /// </summary>
    public struct ShaperHeightOp
    {
        /// <summary>False when the layer has no height stage at all — HS-1.4's "and NOT when it is absent".</summary>
        public bool present;

        public ShaperExtrusionTechnique technique;
        public ShaperBevelTechnique bevel;

        /// <summary><c>max(0, depth)</c>, canvas pixels. HS-1.3.</summary>
        public float body;

        /// <summary>
        /// HS-1.2. <c>max(pixelSize, rootSigmaMin × min(localSupportHalfW, localSupportHalfH))</c>, or the
        /// canvas support box when the program has no local support box. A compile-time scalar; never
        /// recomputed per sample.
        /// </summary>
        public float span;
        /// <summary><c>1/span</c>. Never a divide in the loop.</summary>
        public float invSpan;

        /// <summary>The layer's base plane Z, canvas pixels. HS-7.2 — <c>i × layerSpacing + zOffset(i)</c>.</summary>
        public float baseZ;

        // ── Linear (HS-2.3) ──────────────────────────────────────────────────────────────────────────────
        /// <summary><c>cos θ</c>, <c>θ = angle·π/180</c>.</summary>
        public float cosAngle;
        /// <summary><c>sin θ</c>.</summary>
        public float sinAngle;
        /// <summary>Canvas → the ROOT NODE's own local space (<see cref="ShaperProgram.rootInverse"/>).</summary>
        public float m00, m01, m02, m10, m11, m12;
        /// <summary>The local support box centre, subtracted before normalising (FC-1.5a's own shape).</summary>
        public float localCx, localCy;
        /// <summary><c>1/localSupportHalfW</c>, or 0 when the box is degenerate — it never divides (FC-1.5b).</summary>
        public float invLocalHalfW, invLocalHalfH;

        // ── Stepped extrusion (HS-2.2) ───────────────────────────────────────────────────────────────────
        /// <summary><c>n = max(2, round(steps))</c>.</summary>
        public int n;
        /// <summary><c>1/(n−1)</c>.</summary>
        public float invN1;

        // ── Dome / Round (HS-2.2) ────────────────────────────────────────────────────────────────────────
        /// <summary><c>c = max(0.2, curve)</c>.</summary>
        public float curve;
        /// <summary>Dome's exponent <c>m = 1/(2c)</c> — the collapsed <c>sqrt</c>+<c>pow</c> of HS-2.2.</summary>
        public float domeExp;
        /// <summary>Round's exponent <c>p = 0.5/c</c>.</summary>
        public float roundExp;

        // ── Taper / Pyramid (HS-2.2) ─────────────────────────────────────────────────────────────────────
        /// <summary><c>τ = clamp01(taper)</c>.</summary>
        public float tau;
        /// <summary><c>T = max(0.05, 0.6τ)</c> — the clamp that caps taper's slope at 20 (REF §C.2).</summary>
        public float taperT;
        /// <summary><c>1/T</c>.</summary>
        public float invTaperT;

        // ── Bevel (HS-3.2) ───────────────────────────────────────────────────────────────────────────────
        /// <summary><c>a = clamp01(amount)</c>. The band is <c>t ∈ [0, a)</c>.</summary>
        public float a;
        /// <summary><c>1/a</c>, or 0 when <c>a ≤ 0</c>.</summary>
        public float invA;
        /// <summary><c>m = max(2, round(bevelSteps))</c>. Stepped bevel only.</summary>
        public int bevelN;
        /// <summary><c>1/m</c>.</summary>
        public float invBevelN;

        // ── Derived constants (HS-2.3, HS-4, HS-5) ───────────────────────────────────────────────────────
        /// <summary>
        /// <c>sup E</c> over the whole domain. <b>1 for every profile EXCEPT <see cref="ShaperExtrusionTechnique.Linear"/></b>,
        /// where it is <c>1 + 0.6(|cos θ| + |sin θ|) ≤ 1.84853</c> — HS-2.3, and the reason
        /// <see cref="body"/> is NOT a height budget on that one technique. Any code asserting
        /// <c>height ≤ body</c> is wrong on exactly <c>Linear</c>.
        /// </summary>
        public float supE;

        /// <summary>
        /// <c>inf E</c> over the whole domain — <b>1 for every profile EXCEPT
        /// <see cref="ShaperExtrusionTechnique.Linear"/></b>, where it is
        /// <c>1 − 0.6(|cos θ| + |sin θ|) ≥ 1 − 0.6√2 = 0.15147</c>.
        ///
        /// <b>T-0109 FIX F1 added this, and its absence was a live unsoundness.</b>
        /// <see cref="InverseLowerBound"/> inverts against <see cref="supE"/> to get a LOWER bound on
        /// <c>Ginv</c>, which makes the CONTAINING prism conservatively large — the safe direction. The march
        /// also skips SOLID space, and that mirrored skip needs the opposite bound: an UPPER bound on
        /// <c>Ginv</c>, i.e. the inverse taken at the LEAST permissive <c>E</c>. Before the fix the solid skip
        /// used the lower bound for both, so on <c>Linear</c> it could declare a point solid that was air and
        /// skip a real crossing pair. See <see cref="InverseUpperBound"/>.
        /// </summary>
        public float infE;

        /// <summary>
        /// An upper bound on <c>|∇E|</c> in CANVAS pixels, for <see cref="ShaperExtrusionTechnique.Linear"/>
        /// only; 0 for every other technique (where <c>E</c> does not depend on position at all).
        ///
        /// T-0109 FIX F1. The march's two-sided bracket needs to know how much <c>E</c> can change over one
        /// candidate step, so that <c>Linear</c> gets a bracket that SHRINKS with the step rather than the
        /// canvas-wide <c>[inf E, sup E]</c> one — without it, <c>Linear</c> would be the one technique whose
        /// ambiguous shell never closes and which therefore always fell back to the resolution floor.
        ///
        /// It is the magnitude of <c>∂E/∂(x,y)</c> through <see cref="LocalNormalised"/>'s affine map:
        /// <c>E = 1 + 0.6(cos θ·nx − sin θ·ny)</c>, <c>nx = (m00·x + m01·y + m02 − cx)/halfW</c>, and the
        /// clamp in <c>LocalNormalised</c> can only REDUCE the variation, never increase it.
        /// </summary>
        public float linearEGrad;

        /// <summary>
        /// <c>sup G = sup(E·B)</c>. Equal to <see cref="supE"/>, because <c>B ≡ 1</c> off the band and every
        /// profile reaches its own supremum there (<c>E(1) = 1</c> for the six <c>t</c>-profiles; <c>Linear</c>
        /// is constant in <c>t</c>). This is the top of the layer's Z extent in HS-5.3's march.
        /// </summary>
        public float supG;

        /// <summary><c>sup|dE/dt|</c>, HS-4.1. MAY be <c>+∞</c>, and that is a first-class answer.</summary>
        public float extrusionSlope;
        /// <summary><c>sup|dB/du|</c>, HS-4.1. MAY be <c>+∞</c>.</summary>
        public float bevelSlope;
        /// <summary>The HS-4.2 PRODUCT rule <c>L_E + supE·L_B/a</c> (off the band, just <c>L_E</c>). MAY be <c>+∞</c>.</summary>
        public float composedSlope;
    }

    /// <summary>
    /// The height stage: the extrusion/bevel catalogue, its composed profile, its analytic derivative, its
    /// inverse, its declared slope bounds and its breakpoint set.
    ///
    /// <b>The one statement everything else is a consequence of (HS-1.1).</b> A Silhouette layer is the
    /// implicit solid
    /// <code>
    /// S = { (x,y,z) : d(x,y) ≤ 0  and  base ≤ z ≤ base + body·G(t(x,y), nx, ny)  and  G > 0 }
    /// </code>
    /// <b>T-0109 FIX N3 added the <c>G &gt; 0</c> conjunct, and it is not a tolerance.</b> Without it the set
    /// contains a zero-thickness membrane on the base plane wherever <c>G</c> vanishes — and <c>G</c> vanishes
    /// over a BAND, not a point: <see cref="ShaperExtrusionTechnique.Stepped"/> has <c>E = 0</c> for the whole
    /// ring <c>t &lt; 1/n</c> around the silhouette, and a <see cref="ShaperBevelTechnique.Stepped"/> bevel does
    /// the same at the rim. The resolve then emitted crossing pairs of width exactly zero there (42 of them in
    /// H6's own fixture, all certified "real" by a check that shared the convention). This is the same sentence
    /// HS-2.2 already applies to a whole layer — "a zero-thickness layer publishes height 0 and no wall … there
    /// is no solid to cross" — applied POINTWISE, which is where it was always true. It changes no answer
    /// anywhere <c>G &gt; 0</c>, i.e. nowhere any surface is rendered.
    ///
    /// with <c>d</c> the shape tree's published signed distance in canvas pixels (negative inside,
    /// <c>ShaperField.cs:9-10</c>), <c>t</c> the normalised inside-distance of HS-1.2, <c>G = E·B</c> the
    /// composed height profile, <c>body</c> the authored thickness and <c>base</c> the layer's Z (HS-7).
    /// Every quantity in it is one the shape engine already publishes, which is why turning the solid is a
    /// change to the RAY and not to any primitive.
    ///
    /// <b>RULING ONE (HS-0.1) lives here, and it is why there is an <see cref="Inverse"/> at all.</b> The task
    /// this stage was built from assumed each technique declares a finite slope that the general resolve then
    /// marches by, with <c>Stepped</c> as the one exception. Measurement says the exception is most of the set:
    /// seven of the twelve techniques have NO finite bound (REF-HEIGHT-MATHS §C, §J), including <c>Round</c>
    /// and <c>Dome</c> AT THEIR OWN DEFAULT <c>curve = 1</c>, and the two figures that circulated as their
    /// maxima — <c>round 16.4</c>, <c>dome 23.1</c> — are one measurement grid's samples of a divergent
    /// function (their ratio is √2 to within 0.4%, which is the small-<c>t</c> ratio of the two derivatives).
    /// So the bound is declared honestly, INCLUDING <c>+∞</c> (HS-4), and the march is re-founded on a
    /// property every technique does have: <b><c>G</c> is monotone non-decreasing in <c>t</c></b> (HS-5.1),
    /// which makes every horizontal cross-section a LEVEL SET of the shape's own field offset by a constant —
    /// and offsetting a signed distance field by a constant preserves its declared bound exactly, the result
    /// <see cref="ShaperBound.Shell"/> and <c>BORDER-CONTRACT.md:108</c> already rest on. The marcher steps on
    /// the SHAPE's finite, already-proven bound and never reads a profile's slope.
    ///
    /// <b>Allocates nothing.</b> Every entry point here is a static over a struct and caller-owned arrays.
    /// </summary>
    public static class ShaperHeight
    {
        // ── constants ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="Inverse"/>'s answer when <c>ζ &gt; sup G</c> — there is no cross-section at that height.
        ///
        /// <c>+∞</c> rather than a magic negative or a NaN, because it COMPOSES: the marcher forms
        /// <c>d + τ·span</c> and an infinite <c>τ</c> makes that infinite, i.e. the containing prism is empty,
        /// which is exactly what "no cross-section" means. A NaN would have poisoned the comparison instead.
        /// </summary>
        public const float NoCrossSection = float.PositiveInfinity;

        /// <summary>True when <paramref name="tau"/> is <see cref="NoCrossSection"/>.</summary>
        public static bool IsNoCrossSection(float tau) => float.IsPositiveInfinity(tau);

        /// <summary>
        /// The bisection budget of HS-5.7. 40 halvings take <c>[0,1]</c> to <c>9.1e-13</c>, which is below
        /// float resolution, so the loop is bounded by the type rather than by the constant.
        /// </summary>
        public const int InverseIterations = 40;

        /// <summary>
        /// HS-5.5's SPEED dial: how many slabs a smooth profile's Z extent is cut into. It is a constant on
        /// the resolve and is authored NOWHERE — HS-5.4 is explicit that correctness holds for any count ≥ 1
        /// because the surface is located by bisection on the exact predicate, so this decides cost, not
        /// accuracy.
        /// </summary>
        public const int SlabQuality = 16;

        /// <summary>
        /// The capacity <see cref="Breakpoints"/> may need. The case to size for is HS-5.5's "both stepped":
        /// 32 tread values + 16 micro-terrace values + up to <see cref="SlabQuality"/> uniform + the two
        /// endpoints. 80 is that with headroom, and the entry point clamps rather than overruns.
        /// </summary>
        public const int MaxBreakpoints = 80;

        // ── clamps, verbatim from the reference, each with its line ──────────────────────────────────────

        /// <summary><c>n = max(2, round(steps))</c> — <c>index.html:1149</c>.</summary>
        public static int StepCount(float steps) => Mathf.Max(2, Mathf.RoundToInt(steps));

        /// <summary><c>m = max(2, round(steps))</c> — <c>index.html:1167</c>. Same clamp, DIFFERENT dial (HS-3.4).</summary>
        public static int BevelStepCount(float steps) => Mathf.Max(2, Mathf.RoundToInt(steps));

        /// <summary><c>c = max(0.2, curve)</c> — <c>index.html:1150</c>, <c>:1151</c>.</summary>
        public static float Curve(float curve) => Mathf.Max(0.2f, curve);

        /// <summary><c>τ = clamp01(taper)</c> — <c>index.html:1152</c>, <c>:1153</c>.</summary>
        public static float Taper(float taper) => Mathf.Clamp01(taper);

        /// <summary><c>a = clamp01(amount)</c> — <c>index.html:1160</c>.</summary>
        public static float Amount(float amount) => Mathf.Clamp01(amount);

        // ── t, the one number everything is a function of ────────────────────────────────────────────────

        /// <summary>
        /// HS-1.2 — <c>t = clamp01(−d/span)</c>, the normalised inside-distance. 0 at the silhouette, 1 at
        /// <c>span</c> canvas pixels in from it, saturating deeper than that.
        ///
        /// <c>span</c> is baked at compile (<see cref="ShaperHeightOp.span"/>); this is one multiply and a
        /// clamp.
        /// </summary>
        public static float T(in ShaperHeightOp op, float distance)
        {
            if (ShaperField.IsEmpty(distance)) return 0f;
            float t = -distance * op.invSpan;
            if (t <= 0f) return 0f;
            return t >= 1f ? 1f : t;
        }

        /// <summary>
        /// HS-2.3 — the node-LOCAL normalised coordinates <c>Linear</c> reads, and NOTHING else reads.
        ///
        /// They come from <see cref="ShaperProgram.rootInverse"/> and the LOCAL support half-extents
        /// (<c>localSupportHalfW/HalfH</c>), <b>not from <see cref="ShaperHeightOp.span"/></b>. The reference
        /// takes them from the screen-space bounding box (<c>index.html:1423</c> with
        /// <c>index.html:1339</c>'s <c>halfW/halfH</c>); we take them from the root-local box for exactly the
        /// reason FC-1.5a gave for the fill's anchor — the canvas box grows and shrinks as the node rotates,
        /// so a tilt normalised by it would breathe once per quarter turn with nothing authored changing.
        ///
        /// The box CENTRE is subtracted before dividing, matching <c>ShaperFillCompiler.BakeAnchor</c>
        /// (<c>ShaperFillCompiler.cs:342-345</c>): the local box is <c>(cx,cy,halfW,halfH)</c> and using only
        /// half of it would tilt about the node origin rather than about the shape.
        ///
        /// A degenerate box leaves the reciprocals at 0 and this returns <c>(0,0)</c> WITHOUT dividing
        /// (FC-1.5b).
        /// </summary>
        public static void LocalNormalised(in ShaperHeightOp op, float x, float y, out float nx, out float ny)
        {
            float lx = op.m00 * x + op.m01 * y + op.m02;
            float ly = op.m10 * x + op.m11 * y + op.m12;
            nx = (lx - op.localCx) * op.invLocalHalfW;
            ny = (ly - op.localCy) * op.invLocalHalfH;
            if (nx < -1f) nx = -1f; else if (nx > 1f) nx = 1f;
            if (ny < -1f) ny = -1f; else if (ny > 1f) ny = 1f;
        }

        // ── E, the extrusion profile ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>E</c>, the height as a MULTIPLE OF <c>body</c>. HS-2.2, transcribed from
        /// <c>index.html:1143-1155</c>.
        ///
        /// <c>Dome</c> is written <c>(t(2−t))^(1/(2c))</c> rather than the reference's
        /// <c>pow(sqrt(max(0, 1−(1−t)²)), 1/c)</c>: algebraically identical, one <c>sqrt</c> and one
        /// <c>pow</c> collapsed into one <c>pow</c>, and the <c>max(0,·)</c> guard becomes unnecessary because
        /// <c>t ∈ [0,1]</c> makes <c>t(2−t) ≥ 0</c> by construction (HS-2.2).
        ///
        /// <paramref name="nx"/>/<paramref name="ny"/> are read by <see cref="ShaperExtrusionTechnique.Linear"/>
        /// and by nothing else.
        /// </summary>
        public static float Profile(in ShaperHeightOp op, float t, float nx, float ny)
        {
            switch (op.technique)
            {
                case ShaperExtrusionTechnique.Linear:
                {
                    // index.html:1148, verbatim. The reference's Math.max(0, ...) guard is DEAD CODE — the
                    // minimum of 1 + 0.6(cosθ·nx − sinθ·ny) is 1 − 0.6√2 = 0.1515 > 0, so it can never fire
                    // (REF-HEIGHT-MATHS H4). It is kept because it is free and because it stops being dead the
                    // moment somebody changes the 0.6; it is NOT load-bearing and must not be treated as if it
                    // were.
                    //
                    // FRAME NOTE — <b>FLIPPED, and this is the settled decision, not an open question.</b>
                    //
                    // The reference rasterises y-DOWN (REF §H2) and writes this as
                    // `1 + 0.6(cos θ·nx − sin θ·ny)`. Shaper is +Y UP. Ported verbatim, the tilt for a given
                    // authored `angle` came out as the vertical MIRROR of the reference app's — which is not
                    // "the reference's look", it is a different look reached by the same number.
                    //
                    // T-0109 FIX F7 applies T-0105's already-established precedent, quoted: <i>"port Pyre
                    // angles unchanged, flip reference-app ones."</i> `Linear`'s angle is a REFERENCE-APP
                    // angle, so the sign on `sin θ` is flipped here, in <see cref="LinearGradient"/>, in the
                    // <c>Profile</c> normal provider's chain rule (<c>ShaperNormals.FillProfile</c>) and in
                    // <see cref="ShaperHeightOp.linearEGrad"/>'s compile — all four, or the height and its
                    // gradient would disagree about which way the slab leans.
                    //
                    // <b>Do not port it back.</b> A future reader diffing this line against
                    // <c>index.html:1148</c> will see a sign that does not match; that is deliberate and is
                    // what makes a 45° tilt lean the same way here as every other angle in the engine. The
                    // spec's HS-2.2/HS-4.4 still state the reference's sign verbatim and are now WRONG about
                    // it; that is recorded in FIX-REPORT.md §F7 rather than left for someone to "correct".
                    //
                    // The reference's Math.max(0, ...) guard is DEAD CODE either way — the minimum of
                    // 1 + 0.6(cos θ·nx + sin θ·ny) is 1 − 0.6√2 = 0.1515 > 0, and the flip does not change
                    // that because the extremes are symmetric in the sign (REF-HEIGHT-MATHS H4). It is kept
                    // because it is free; it is NOT load-bearing and must not be treated as if it were.
                    float e = 1f + 0.6f * (op.cosAngle * nx + op.sinAngle * ny);
                    return e > 0f ? e : 0f;
                }

                case ShaperExtrusionTechnique.Stepped:
                {
                    // index.html:1149. HS-3.4: this is NOT the bevel's stepped function and does not share it.
                    // E(0) = 0 here; B(0) = 1/m there.
                    float e = Mathf.Floor(t * op.n) * op.invN1;
                    return e > 1f ? 1f : e;
                }

                case ShaperExtrusionTechnique.Dome:
                {
                    float q = t * (2f - t);
                    if (q <= 0f) return 0f;
                    return (float)Math.Pow(q, op.domeExp);
                }

                case ShaperExtrusionTechnique.Round:
                {
                    if (t <= 0f) return 0f;
                    return (float)Math.Pow(t, op.roundExp);
                }

                case ShaperExtrusionTechnique.Taper:
                {
                    float e = t * op.invTaperT;
                    return e > 1f ? 1f : e;
                }

                case ShaperExtrusionTechnique.Pyramid:
                    return 1f - op.tau + op.tau * t;

                case ShaperExtrusionTechnique.Flat:
                default:
                    return 1f;
            }
        }

        // ── B, the bevel ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>B</c>, the bevel's multiplier. HS-3.2, transcribed from <c>index.html:1159-1173</c>.
        ///
        /// The band is <c>t ∈ [0, a)</c> and OUTSIDE it the factor is exactly 1 by early-out
        /// (<c>index.html:1161</c>) — a HARD identity, taken before any arithmetic, which is why
        /// <c>bevelAmount = 0</c> is bit-identical to <see cref="ShaperBevelTechnique.None"/> rather than
        /// merely numerically equal (audited as H5).
        ///
        /// All five non-identity bevels reach 1 at the band's inner edge, so there is no seam at <c>t = a</c>
        /// (HS-3.3). <see cref="ShaperBevelTechnique.Stepped"/> is the only one that does not reach 0 at the
        /// silhouette — <c>B(0) = 1/m</c>, a hard step of <c>1/m</c> at the outline itself — which is exactly
        /// why it is the only bevel that leaves a side wall standing (HS-6.4).
        /// </summary>
        public static float Bevel(in ShaperHeightOp op, float t)
        {
            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f || t >= op.a) return 1f;

            float u = t * op.invA;
            if (u < 0f) u = 0f; else if (u > 1f) u = 1f;

            switch (op.bevel)
            {
                case ShaperBevelTechnique.Linear:
                    return u;

                case ShaperBevelTechnique.Rounded:
                {
                    float q = u * (2f - u);
                    return q > 0f ? Mathf.Sqrt(q) : 0f;
                }

                case ShaperBevelTechnique.Cove:
                {
                    // B = 1 − √(1−u²), written as u²/(1 + √(1−u²)) — the SAME NUMBER, a different
                    // instrument. T-0109 FIX F2: the literal form catastrophically cancels below
                    // u ≈ 1.7e-4, where `1 − u²` is exactly 1.0 in float and the whole expression collapses
                    // to 0 (measured 5.9605e-8 against a true 1.5811e-8 at u = 1.78e-4 — 277% relative
                    // error). This is the same class the Dome inverse was rewritten for; the search that
                    // found Dome stopped there. The factorisation `1−u² = (1−u)(1+u)` is used inside the
                    // root so the ROOT is accurate near u = 1 as well.
                    float r = (1f - u) * (1f + u);
                    float s = r > 0f ? Mathf.Sqrt(r) : 0f;
                    return u * u / (1f + s);
                }

                case ShaperBevelTechnique.Ogee:
                {
                    // T-0109 FIX F2, same class: ½(1 − √(1−4u²)) → 2u²/(1 + √(1−4u²)), and the mirrored
                    // half's root factorised as (1−2w)(1+2w) so it stays accurate at the u = ½ inflection.
                    // Measured before the fix: 2.9802e-8 against a true 1.0000e-8 at u = 1e-4, 198%.
                    if (u < 0.5f)
                    {
                        float r = (1f - 2f * u) * (1f + 2f * u);
                        float s = r > 0f ? Mathf.Sqrt(r) : 0f;
                        return 2f * u * u / (1f + s);
                    }
                    else
                    {
                        float w = 1f - u;
                        float r = (1f - 2f * w) * (1f + 2f * w);
                        float s = r > 0f ? Mathf.Sqrt(r) : 0f;
                        return 1f - 2f * w * w / (1f + s);
                    }
                }

                case ShaperBevelTechnique.Stepped:
                {
                    // index.html:1167. NOT the extrusion's stepped formula: note the +1 and the /m rather than
                    // /(m−1), which is what makes B(0) = 1/m instead of 0 (HS-3.4).
                    float b = (Mathf.Floor(u * op.bevelN) + 1f) * op.invBevelN;
                    return b > 1f ? 1f : b;
                }

                default:
                    return 1f;
            }
        }

        // ── G = E·B ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// HS-3.3 — <b>the composed height is <c>G(t) = E · B(clamp01(t/a))</c></b>, a PRODUCT, exactly the
        /// reference's <c>index.html:1424</c>. The bevel multiplies the extrusion; it does not replace it, so
        /// a <c>Dome</c> with a <c>Rounded</c> bevel multiplies two rim falloffs and is much sharper at the
        /// edge than either alone (REF §H7).
        /// </summary>
        public static float Composed(in ShaperHeightOp op, float t, float nx, float ny)
            => Profile(op, t, nx, ny) * Bevel(op, t);

        /// <summary>
        /// The surface height above the layer's base plane, in canvas pixels: <c>body · G</c>.
        ///
        /// The <c>body ≤ 0</c> early-out of <c>index.html:1144</c> is kept (HS-2.2): a zero-thickness layer
        /// publishes height 0 and no wall, and <see cref="ShaperResolve"/> skips it entirely. It is retained
        /// as an EARLY-OUT and not left to <c>0 · G</c> so that H5's bit-identity claim is structural.
        /// </summary>
        public static float Height(in ShaperHeightOp op, float t, float nx, float ny)
            => op.body <= 0f ? 0f : op.body * Composed(op, t, nx, ny);

        // ── G′, analytically ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>dE/dt</c>, in closed form. Every technique supplies its own; nothing here differences anything.
        ///
        /// <c>Stepped</c> returns 0, which is correct ALMOST EVERYWHERE and is the only honest answer: a floor
        /// function has no derivative at its risers and 0 on every tread. HS-8.4 records what that means
        /// visually — the riser is a wall of zero screen width straight down, consistent with HS-6.2's wall
        /// normal in the limit.
        ///
        /// <c>Dome</c> and <c>Round</c> return <c>+∞</c> at <c>t → 0⁺</c> whenever <c>c &gt; 0.5</c> (which
        /// includes their DEFAULT). That is deliberate and must not be clamped — HS-8.4: let
        /// <c>normalize</c> do the work.
        /// </summary>
        public static float ProfileDerivative(in ShaperHeightOp op, float t)
        {
            switch (op.technique)
            {
                case ShaperExtrusionTechnique.Dome:
                {
                    // E = (t(2−t))^m ⟹ E′ = 2m(1−t)·(t(2−t))^(m−1).
                    float q = t * (2f - t);
                    double m = op.domeExp;
                    if (q <= 0f)
                    {
                        if (m < 1.0) return float.PositiveInfinity;   // the divergence at the silhouette
                        if (m > 1.0) return 0f;
                        return 2f * (1f - t);                          // m == 1 exactly: E′ = 2(1−t)
                    }
                    return (float)(2.0 * m * (1f - t) * Math.Pow(q, m - 1.0));
                }

                case ShaperExtrusionTechnique.Round:
                {
                    // E = t^p ⟹ E′ = p·t^(p−1).
                    double p = op.roundExp;
                    if (t <= 0f)
                    {
                        if (p < 1.0) return float.PositiveInfinity;
                        if (p > 1.0) return 0f;
                        return 1f;
                    }
                    return (float)(p * Math.Pow(t, p - 1.0));
                }

                case ShaperExtrusionTechnique.Taper:
                    // Piecewise linear: 1/T on the ramp, 0 after. At t == T exactly the right-derivative is
                    // taken, so the plateau wins the tie — which is what the min() in Profile does too.
                    return t < op.taperT ? op.invTaperT : 0f;

                case ShaperExtrusionTechnique.Pyramid:
                    return op.tau;

                case ShaperExtrusionTechnique.Stepped:
                case ShaperExtrusionTechnique.Flat:
                case ShaperExtrusionTechnique.Linear:
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// <c>dB/du</c>, in closed form, on the band. Zero off it (where <c>B ≡ 1</c> by early-out).
        ///
        /// Three of the five diverge, each somewhere different, and the audit demonstrates each divergence
        /// rather than reporting a number: <c>Rounded</c> at <c>u → 0⁺</c> (the silhouette), <c>Cove</c> at
        /// <c>u → 1⁻</c> (the INNER edge of the band), <c>Ogee</c> at <c>u → ½</c> (its mid-band inflection).
        /// REF §C.7.
        /// </summary>
        public static float BevelDerivative(in ShaperHeightOp op, float t)
        {
            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f || t >= op.a) return 0f;

            float u = t * op.invA;
            if (u < 0f) u = 0f; else if (u > 1f) u = 1f;

            switch (op.bevel)
            {
                case ShaperBevelTechnique.Linear:
                    return 1f;

                case ShaperBevelTechnique.Rounded:
                {
                    // B = √(u(2−u)) ⟹ B′ = (1−u)/√(u(2−u)).
                    float q = u * (2f - u);
                    if (q <= 0f) return float.PositiveInfinity;
                    return (1f - u) / Mathf.Sqrt(q);
                }

                case ShaperBevelTechnique.Cove:
                {
                    // B = 1 − √(1−u²) ⟹ B′ = u/√(1−u²).
                    //
                    // T-0109 FIX F2 (found by the sweep, not named in the defect list): `1 − u*u` cancels as
                    // u → 1⁻, which is exactly where this derivative DIVERGES and therefore exactly where its
                    // value is load-bearing for H3. `(1−u)(1+u)` forms no intermediate near 1.
                    float q = (1f - u) * (1f + u);
                    if (q <= 0f) return float.PositiveInfinity;
                    return u / Mathf.Sqrt(q);
                }

                case ShaperBevelTechnique.Ogee:
                {
                    // T-0109 FIX F2, same reason: Ogee's divergence is AT u = ½, which is precisely where
                    // `1 − 4u²` and `1 − 4w²` cancel. Factorised as (1∓2u)(1±2u).
                    if (u < 0.5f)
                    {
                        float q = (1f - 2f * u) * (1f + 2f * u);
                        if (q <= 0f) return float.PositiveInfinity;
                        return 2f * u / Mathf.Sqrt(q);
                    }
                    else
                    {
                        float w = 1f - u;
                        float q = (1f - 2f * w) * (1f + 2f * w);
                        if (q <= 0f) return float.PositiveInfinity;
                        return 2f * w / Mathf.Sqrt(q);
                    }
                }

                case ShaperBevelTechnique.Stepped:
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// HS-8.2 — <c>G′ = E′·B + E·B′/a</c> on the band, <c>E′</c> off it. ANALYTIC, from each technique's
        /// own closed form. <b>Nothing here differences <c>G</c>.</b>
        ///
        /// Where either factor is infinite the product is infinite (0·∞ is resolved as ∞ deliberately: an
        /// infinite slope multiplied by a vanishing factor is exactly the silhouette-edge case HS-8.4 wants to
        /// leave to <c>normalize</c>, and returning 0 there would flatten a genuinely vertical wall).
        /// </summary>
        public static float ComposedDerivative(in ShaperHeightOp op, float t, float nx, float ny)
        {
            float ep = ProfileDerivative(op, t);

            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f || t >= op.a) return ep;

            float e = Profile(op, t, nx, ny);
            float b = Bevel(op, t);
            float bp = BevelDerivative(op, t);

            if (float.IsPositiveInfinity(ep) || float.IsPositiveInfinity(bp)) return float.PositiveInfinity;

            return ep * b + e * bp * op.invA;
        }

        // ── the inverse, HS-5.7 ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// HS-5.2/HS-5.7 — <c>Ginv(ζ) = inf{ t ∈ [0,1] : G(t) ≥ ζ }</c>: the SMALLEST <c>t</c> whose composed
        /// height reaches <paramref name="zeta"/>. Returns 0 for <c>ζ ≤ G(0)</c> and
        /// <see cref="NoCrossSection"/> for <c>ζ &gt; sup G</c>.
        ///
        /// <b>Closed form above the band and wherever one exists; monotone bisection otherwise.</b> Above the
        /// band <c>B ≡ 1</c> so <c>Ginv = E⁻¹</c> and every profile has one. Inside the band the product
        /// <c>E·B</c> generally has none, so the shipped path bisects — EXCEPT where <c>E</c> is constant in
        /// <c>t</c> (<c>Flat</c> and <c>Linear</c>), where <c>G = E·B</c> is a scaled bevel and the bevel's own
        /// closed form applies. The <c>Ogee</c> bevel bisects even there.
        ///
        /// Every closed form is round-tripped against the bisection by the audit (H4) — that is the check that
        /// catches an algebra slip, and it is cheap.
        ///
        /// Allocates nothing; at most <see cref="InverseIterations"/> halvings.
        /// </summary>
        public static float Inverse(in ShaperHeightOp op, float zeta, float nx, float ny)
        {
            // Below or at the value the silhouette itself already reaches: the cross-section is the whole
            // silhouette, t = 0.
            float g0 = Composed(op, 0f, nx, ny);
            if (zeta <= g0) return 0f;

            // Above the top of the profile: nothing at this height.
            float gTop = Composed(op, 1f, nx, ny);
            if (zeta > gTop) return NoCrossSection;

            bool bevelled = op.bevel != ShaperBevelTechnique.None && op.a > 0f;

            if (!bevelled)
                return ProfileInverse(op, zeta, nx, ny);

            // STRICTLY above the band's inner edge, B ≡ 1 and the profile's own inverse is exact.
            //
            // The comparison is `>` and not `>=`, and the difference is a real defect the audit caught (H4).
            // At `ζ == E(a)` the answer can still lie INSIDE the band: a Stepped bevel reaches B = 1 at
            // u = (m−1)/m, i.e. at t = a(m−1)/m, so with a Flat profile and m = 3 the smallest t reaching
            // ζ = 1 is 2a/3 and not a. `>=` here clamped that to `a` and over-reported the inverse by a/3 —
            // which in the march means a containing prism SMALLER than the true cross-section, i.e. the one
            // error direction HS-5.4's "can only stop short, never late" does not cover.
            float eAtA = Profile(op, op.a, nx, ny);
            if (zeta > eAtA)
            {
                float t = ProfileInverse(op, zeta, nx, ny);
                if (IsNoCrossSection(t)) return NoCrossSection;
                return t >= op.a ? t : op.a;
            }

            // Inside the band. E constant in t ⟹ G is a scaled bevel and the bevel's closed form applies.
            if (op.technique == ShaperExtrusionTechnique.Flat || op.technique == ShaperExtrusionTechnique.Linear)
            {
                float e = Profile(op, 0f, nx, ny);
                if (e <= 0f) return NoCrossSection;
                float u = BevelInverseU(op, zeta / e);
                if (u >= 0f) return u * op.a;
                // Ogee (and anything without a closed form) falls through to the bisection below.
            }

            return Bisect(op, zeta, nx, ny, 0f, op.a);
        }

        /// <summary>
        /// <c>E⁻¹(ζ)</c> in closed form, per HS-5.7's list. Returns <see cref="NoCrossSection"/> when the
        /// profile never reaches <paramref name="zeta"/>.
        /// </summary>
        public static float ProfileInverse(in ShaperHeightOp op, float zeta, float nx, float ny)
        {
            switch (op.technique)
            {
                case ShaperExtrusionTechnique.Flat:
                    // E ≡ 1, so every t reaches ζ ≤ 1 and the smallest is 0. HS-5.7: "Flat → 0 for ζ ≤ 1".
                    return zeta <= 1f ? 0f : NoCrossSection;

                case ShaperExtrusionTechnique.Linear:
                {
                    // Constant in t, like Flat, but the constant depends on (nx,ny) — HS-2.3.
                    float e = Profile(op, 0f, nx, ny);
                    return zeta <= e ? 0f : NoCrossSection;
                }

                case ShaperExtrusionTechnique.Stepped:
                {
                    // E = min(1, ⌊t·n⌋/(n−1)) ≥ ζ ⟺ ⌊t·n⌋ ≥ ⌈ζ(n−1)⌉ =: k, and the smallest such t is k/n.
                    //
                    // T-0109 FIX F3 fixed the k = 0 case: E(0) = 0 for Stepped, so any ζ > 0 needs k ≥ 1 and
                    // returning t = 0 claimed a height the profile does not reach there.
                    //
                    // T-0109 FIX N6 removes the −1e-6 that used to sit inside the ceiling. It was an ABSOLUTE
                    // epsilon on a scale-free quantity: ζ(n−1) ranges over [0, n−1], so the effective ζ
                    // tolerance was 1e-6/(n−1) — about 17 ulps at n = 2 and about 1 ulp at n = 32, i.e. a
                    // different rule at every step count. It existed for a real reason (ζ landing one float
                    // rounding ABOVE a tread must not ceil a whole step up), but it answered that question by
                    // guessing a magnitude instead of asking. Measured at ζ one ulp above a tread — which
                    // neither of H4's grids lands on — it was still a WHOLE TREAD out: |Δt| = 3.333e-1 at
                    // n = 3, 2.5e-1 at n = 4, 3.125e-2 at n = 32, always exactly one tread.
                    //
                    // The replacement is not a smaller epsilon. It is to ASK THE FORWARD FUNCTION, which is
                    // exact by construction and scale-free by not having a scale: ceil, then walk the step
                    // index down while the step below already reaches ζ, and nudge up by ULPS (never by a
                    // tread) if the candidate falls short. That is the same verify-and-nudge idiom
                    // InverseUpperBound already uses, and it removes a defect generator rather than a symptom.
                    if (zeta > 1f) return NoCrossSection;
                    if (zeta <= 0f) return 0f;

                    int k = Mathf.CeilToInt(zeta * (op.n - 1));
                    if (k < 1) k = 1;
                    if (k > op.n) return NoCrossSection;
                    return SteppedProfileInverseExact(op, zeta, k);
                }

                case ShaperExtrusionTechnique.Dome:
                {
                    // (t(2−t))^m = ζ ⟹ (1−t)² = 1 − ζ^(1/m) ⟹ t = 1 − √(1 − ζ^(2c)),  1/m = 2c.
                    //
                    // Written as `w / (1 + √(1−w))` rather than as `1 − √(1−w)`, which is the SAME NUMBER and
                    // a different instrument. The literal form catastrophically cancels for small w: at
                    // c = 4 and ζ = 0.005, w = ζ^8 = 3.9e-19, √(1−w) rounds to exactly 1 even in double, and
                    // the subtraction returns 0 — so the inverse claimed t = 0 for a height it does not reach,
                    // and the audit's round-trip (H4) measured the resulting shortfall at 5e-3. The rewritten
                    // form returns 1.95e-19, which is exact and comfortably normal in float.
                    if (zeta > 1f) return NoCrossSection;
                    if (zeta <= 0f) return 0f;
                    double w = Math.Pow(zeta, 2.0 * op.curve);
                    double r = 1.0 - w;
                    if (r <= 0.0) return 1f;
                    return (float)(w / (1.0 + Math.Sqrt(r)));
                }

                case ShaperExtrusionTechnique.Round:
                {
                    // t^p = ζ ⟹ t = ζ^(1/p) = ζ^(2c).
                    if (zeta > 1f) return NoCrossSection;
                    if (zeta <= 0f) return 0f;
                    return (float)Math.Pow(zeta, 2.0 * op.curve);
                }

                case ShaperExtrusionTechnique.Taper:
                {
                    // min(1, t/T) = ζ ⟹ t = ζ·T.
                    if (zeta > 1f) return NoCrossSection;
                    if (zeta <= 0f) return 0f;
                    return zeta * op.taperT;
                }

                case ShaperExtrusionTechnique.Pyramid:
                {
                    // 1 − τ + τt = ζ ⟹ t = (ζ − 1 + τ)/τ. τ = 0 makes E ≡ 1, the Flat case.
                    if (zeta > 1f) return NoCrossSection;
                    if (op.tau <= 0f) return zeta <= 1f ? 0f : NoCrossSection;
                    float t = (zeta - 1f + op.tau) / op.tau;
                    return t <= 0f ? 0f : (t >= 1f ? 1f : t);
                }

                default:
                    return 0f;
            }
        }

        // ── T-0109 FIX N6: the two Stepped inverses, decided by the forward function ─────────────────────

        /// <summary><c>E</c>'s Stepped branch, bit-for-bit as <see cref="Profile"/> writes it. Duplicated
        /// deliberately: the verify below is only worth anything if it asks the SAME expression.</summary>
        static float SteppedE(in ShaperHeightOp op, float t)
        {
            float e = Mathf.Floor(t * op.n) * op.invN1;
            return e > 1f ? 1f : e;
        }

        /// <summary>
        /// The float at step index <paramref name="j"/> that <c>⌊t·n⌋</c> actually maps back to <c>j</c> —
        /// the riser itself rather than a float one ulp below it. T-0109 FIX V2.
        ///
        /// <c>j/n</c> is the obvious answer and is usually right, but over the authored range
        /// (<c>n ∈ [2,32]</c>, every <c>j</c>) there are exactly FOUR indices where <c>fl(fl(j/n)·n) &lt; j</c>
        /// — <c>n=22,j=13; n=23,j=7; n=23,j=14; n=29,j=15</c> — and there the quotient sits below its own
        /// riser, so <c>E</c> reads a whole tread short. Both Stepped inverse walks need the corrected float:
        /// the UP walk had its own private nudge, the DOWN walk did not, and the missing one was V2.
        ///
        /// The nudge is by ulps (a relative step of one ulp), bounded to eight, and stops the moment the
        /// floor agrees — so it can never cross a riser, a tread being <c>1/n ≥ 1/32</c>, seven orders of
        /// magnitude wider. In the overwhelmingly common case the loop runs zero times.
        /// </summary>
        static float SteppedRiser(in ShaperHeightOp op, int j)
        {
            float t = j / (float)op.n;
            for (int i = 0; i < 8 && Mathf.Floor(t * op.n) < j; i++) t *= 1.0000001f;
            return t;
        }

        /// <summary><c>B</c>'s Stepped branch in <c>u</c>, bit-for-bit as <see cref="Bevel"/> writes it.</summary>
        static float SteppedB(in ShaperHeightOp op, float u)
        {
            float b = (Mathf.Floor(u * op.bevelN) + 1f) * op.invBevelN;
            return b > 1f ? 1f : b;
        }

        /// <summary>
        /// The smallest <c>t</c> whose FORWARD <c>E</c> actually reaches <paramref name="zeta"/>, starting
        /// from the ceiling's step index <paramref name="k"/>. T-0109 FIX N6.
        ///
        /// Two corrections, and neither has a magnitude in it:
        ///
        /// <b>DOWN</b> — the ceiling over-shoots by one step whenever <c>ζ·(n−1)</c> lands a float rounding
        /// above an integer, which is the ordinary case for a ζ read back off a tread (ζ = 1/3 at n = 4 gives
        /// 1.0000001). The old code absorbed that with a −1e-6; this asks whether the step BELOW already
        /// reaches ζ, which is the question the epsilon was approximating.
        ///
        /// <b>UP</b> — <c>k/n</c> is not always a float that <c>⌊t·n⌋</c> maps back to <c>k</c>. Exhaustively
        /// over the authored range (<c>n ∈ [2,32]</c>, every <c>k</c>) there are exactly FOUR such indices —
        /// <c>n=22,k=13; n=23,k=7; n=23,k=14; n=29,k=15</c> — where <c>fl(fl(k/n)·n) &lt; k</c>, so the
        /// returned <c>t</c> sat one ulp below its own riser and <c>E(t)</c> came back a whole tread short.
        /// That is a SECOND instance of the same defect the epsilon was hiding, it is in the UNSAFE direction
        /// for the contained prism, and no epsilon on ζ could ever have fixed it because the error is in
        /// <c>t</c>. The nudge is by ulps (a relative step of one ulp), never by a tread, so it can never
        /// skip a riser: a tread is <c>1/n ≥ 1/32</c>, seven orders of magnitude wider.
        ///
        /// Both loops are bounded, allocate nothing, and in the overwhelmingly common case run zero times.
        /// </summary>
        static float SteppedProfileInverseExact(in ShaperHeightOp op, float zeta, int k)
        {
            while (k > 1)
            {
                // T-0109 FIX V2: through the RISER, not the raw quotient. The DOWN walk used to ask
                // SteppedE at the bare (k−1)/n — which is precisely the float the UP walk exists to nudge.
                // At the four latent indices that float sits one ulp below its own riser, so E came back a
                // whole tread short, the walk concluded the step below does not reach ζ and stopped one step
                // too high. That leaves τ_min ABOVE the true inverse, which is the unsafe direction: HS-5.2
                // requires {G ≥ ζ} ⊆ {t ≥ τ_min}, and a τ_min that is too large means the CONTAINING prism
                // does not contain, so the march can step clean through solid. Measured: 12 violations in
                // 520 800 checks, over-reporting by 3.4481e-2 t-units — exactly one whole tread at n = 29 —
                // and 1 of 58 rays aimed at the affected ζ lost a crossing, worst gap 7.1227 px.
                //
                // Only visible on a grid that includes the ±ulp neighbourhood of a tread boundary, which is
                // why two earlier passes measured zero here. The four pairs are named as regression traps.
                float below = SteppedRiser(op, k - 1);
                if (!(SteppedE(op, below) >= zeta)) break;
                k--;
            }

            float t = SteppedRiser(op, k);
            for (int i = 0; i < 8 && SteppedE(op, t) < zeta; i++) t *= 1.0000001f;

            if (SteppedE(op, t) < zeta)
            {
                if (k >= op.n) return NoCrossSection;
                t = (k + 1) / (float)op.n;
            }
            return t > 1f ? 1f : t;
        }

        /// <summary>
        /// The <c>u</c> mirror of <see cref="SteppedProfileInverseExact"/>. T-0109 FIX N6.
        ///
        /// The reciprocal form <c>k·(1/m)</c> is exhaustively exact over the authored range
        /// (<c>m ∈ [2,16]</c>, every <c>k</c>: zero round-trip failures), so the UP nudge never fires here
        /// today; it is kept because <c>m</c>'s clamp is a compiler constant and the property is not one
        /// anybody should have to re-derive if that clamp moves.
        /// </summary>
        static float SteppedBevelInverseExact(in ShaperHeightOp op, float zeta, int k)
        {
            while (k > 0)
            {
                float below = (k - 1) * op.invBevelN;
                if (!(SteppedB(op, below) >= zeta)) break;
                k--;
            }

            float u = k * op.invBevelN;
            for (int i = 0; i < 8 && SteppedB(op, u) < zeta; i++) u *= 1.0000001f;

            if (SteppedB(op, u) < zeta)
            {
                if (k + 1 >= op.bevelN) return 1f;
                u = (k + 1) * op.invBevelN;
            }
            return u > 1f ? 1f : u;
        }

        /// <summary>
        /// <c>B⁻¹(ζ)</c> in <c>u</c>, per HS-5.7's bevel list. Returns <b>−1</b> for
        /// <see cref="ShaperBevelTechnique.Ogee"/>, which has no closed inverse and is the declared bisection
        /// case; the caller falls through.
        /// </summary>
        public static float BevelInverseU(in ShaperHeightOp op, float zeta)
        {
            if (zeta <= 0f) return 0f;
            switch (op.bevel)
            {
                case ShaperBevelTechnique.Linear:
                    // B = u.
                    return zeta >= 1f ? 1f : zeta;

                case ShaperBevelTechnique.Rounded:
                {
                    // √(u(2−u)) = ζ ⟹ u = 1 − √(1−ζ²), written as ζ²/(1 + √(1−ζ²)).
                    //
                    // T-0109 FIX F2. The literal form returned EXACTLY 0 for every ζ below ≈2.4e-4 — D2's
                    // failure mode verbatim, in a different function: at Flat+Rounded, amount 0.05,
                    // ζ = 1e-4 the round trip measured a reach shortfall of 1.000e-4, i.e. the inverse
                    // claimed a height the profile does not reach at that t. Same rewrite as Dome's.
                    if (zeta >= 1f) return 1f;
                    float r = (1f - zeta) * (1f + zeta);
                    float s = r > 0f ? Mathf.Sqrt(r) : 0f;
                    return zeta * zeta / (1f + s);
                }

                case ShaperBevelTechnique.Cove:
                {
                    // 1 − √(1−u²) = ζ ⟹ u = √(1 − (1−ζ)²) = √(ζ(2−ζ)).
                    //
                    // T-0109 FIX F2, and this one erred in the UNSAFE direction — the one HS-5.4's "an
                    // over-large prism can only stop early" does not cover. Forming `w = 1−ζ` then
                    // `1 − w²` loses ζ's low bits twice: at ζ = 1e-7 it returned 4.88281e-4 against a true
                    // 4.47214e-4, +9.2%, and an OVER-large inverse shrinks the containing prism BELOW the
                    // true cross-section. `ζ(2−ζ)` never forms an intermediate near 1 and is exact to a
                    // rounding.
                    //
                    // The product is formed in DOUBLE and only then rounded. In float, ζ = 0.999999 gives
                    // ζ(2−ζ) = 1 − 6e-8 (one ulp below 1) purely from the rounding of the two factors, so
                    // the inverse came back one ulp short of u = 1 and the round trip lost 3.4e-4 of reach
                    // at the band's inner edge. In double the same product is 1 − 1e-12, which rounds to
                    // exactly 1f. Safe direction either way, but there is no reason to give away a
                    // breakpoint that the `t >= a` early-out makes exact.
                    if (zeta >= 1f) return 1f;
                    double q = (double)zeta * (2.0 - (double)zeta);
                    return q > 0.0 ? (float)Math.Sqrt(q) : 0f;
                }

                case ShaperBevelTechnique.Stepped:
                {
                    // min(1,(⌊um⌋+1)/m) ≥ ζ ⟺ ⌊um⌋ ≥ ⌈ζm − 1⌉ =: k, smallest such u is k/m.
                    //
                    // T-0109 FIX N6, the bevel's half. Two changes, both removing a guessed magnitude:
                    //
                    //  • `⌈ζm − 1⌉` is formed as `⌈ζm⌉ − 1`, which is the same integer in real arithmetic and
                    //    does not subtract 1 from a float that may be just above an integer.
                    //  • the −1e-6 is gone. It was absolute on ζm ∈ [0, m], so the effective ζ tolerance
                    //    varied with the step count exactly as the profile's did; measured at ζ one ulp above
                    //    a tread it was still a whole tread out, |Δt| = 5.000e-1 at m = 2. The forward
                    //    function decides instead — see SteppedBevelInverseExact.
                    if (zeta > 1f) return 1f;
                    int k = Mathf.CeilToInt(zeta * op.bevelN) - 1;
                    if (k < 0) k = 0;
                    if (k >= op.bevelN) return 1f;
                    return SteppedBevelInverseExact(op, zeta, k);
                }

                case ShaperBevelTechnique.None:
                    return 0f;

                default:
                    return -1f;   // Ogee: declared bisection case, HS-5.7.
            }
        }

        /// <summary>
        /// The declared fallback of HS-5.7: a monotone bisection on <c>G</c> over <c>[lo, hi]</c>, at most
        /// <see cref="InverseIterations"/> halvings, ZERO allocation. Sound precisely because HS-5.1 holds —
        /// which is why the audit proves that claim over all forty-two combinations rather than assuming it.
        /// </summary>
        public static float Bisect(in ShaperHeightOp op, float zeta, float nx, float ny, float lo, float hi)
        {
            if (Composed(op, lo, nx, ny) >= zeta) return lo;
            if (Composed(op, hi, nx, ny) < zeta) return hi;
            for (int i = 0; i < InverseIterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Composed(op, mid, nx, ny) >= zeta) hi = mid; else lo = mid;
            }
            return hi;
        }

        /// <summary>
        /// The CONSERVATIVE inverse the ray march uses: a LOWER bound on <c>Ginv(ζ)</c> valid at every
        /// <c>(nx,ny)</c>.
        ///
        /// <b>Why this exists and <see cref="Inverse"/> alone is not enough.</b> HS-5.2's containment argument
        /// treats <c>G</c> as a function of <c>t</c> alone. It is, for six of the seven profiles. It is NOT for
        /// <see cref="ShaperExtrusionTechnique.Linear"/>, whose <c>G(t,nx,ny) = E(nx,ny)·B(t/a)</c> has a
        /// different level set at every point of the canvas — so a single <c>τ_min</c> does not exist for it.
        /// The fix is to invert against <c>sup E</c>, the most permissive value: the resulting <c>τ_min</c> is
        /// the smallest over the whole canvas, so <c>{G ≥ ζ} ⊆ {t ≥ τ_min}</c> still holds and the containing
        /// prism is merely LARGER. HS-5.4 is explicit that an over-large prism can only make the marcher stop
        /// EARLY, never late, and the bisection on the exact predicate then finds the true surface — so this
        /// costs speed on one technique and nothing else.
        /// </summary>
        public static float InverseLowerBound(in ShaperHeightOp op, float zeta)
            => InverseAtE(op, zeta, op.supE);

        /// <summary>
        /// The mirror of <see cref="InverseLowerBound"/>: an UPPER bound on <c>Ginv(ζ)</c> valid at every
        /// <c>(nx,ny)</c>, obtained by inverting against <c>inf E</c> — the LEAST permissive value.
        ///
        /// <b>T-0109 FIX F1.</b> The march's solid-space skip is the mirror of its empty-space skip and needs
        /// the mirrored bound: to declare a point provably INSIDE the solid it must know that every point of
        /// the prism it is skipping through is inside, which needs <c>t ≥ τ ⟹ G(t) ≥ ζ</c> — an UPPER bound
        /// on the inverse. Using the lower bound there (which the pre-fix code did) is unsound on exactly
        /// <see cref="ShaperExtrusionTechnique.Linear"/>, the one technique whose <c>G</c> varies across the
        /// canvas: it can declare air to be solid and skip a whole crossing pair.
        /// </summary>
        public static float InverseUpperBound(in ShaperHeightOp op, float zeta)
        {
            float e = op.technique == ShaperExtrusionTechnique.Linear
                    ? (op.infE > 0f ? op.infE : Mathf.Max(0f, 1f - 0.6f * (Mathf.Abs(op.cosAngle) + Mathf.Abs(op.sinAngle))))
                    : 1f;
            return InverseAtEUpperBound(op, zeta, e);
        }

        /// <summary>
        /// <b>T-0109 FIX N4</b> — <see cref="InverseUpperBound"/>'s verify-and-nudge, generalised to any
        /// PINNED <c>E</c>, so the march's per-step contained prism gets the same guarantee its slab-wide
        /// sibling already had.
        ///
        /// The F1 hardening was applied to <see cref="InverseUpperBound"/> only. The march calls that once
        /// per slab, but the per-step adaptive bracket — the machinery the whole F1 rewrite exists for —
        /// called raw <see cref="InverseAtE"/>, which is short by up to a few float roundings and therefore
        /// is NOT an upper bound. Measured over 2 974 632 checks (42 combinations × 9 angles × 4 amounts ×
        /// 400 ζ × 41 t) driving exactly what the march drives: <b>2 526 violations, worst shortfall
        /// 3.3379e-6 in ζ</b>, against <see cref="InverseUpperBound"/>'s 40 / 1.1921e-7 on the same sweep.
        /// The harm is self-limiting (the bogus "provably solid" step is at most δτ·span ≈ 1.2e-5 px on a
        /// 200 px span, and is raised to <c>SurfaceResolution</c> anyway) and no behavioural failure was ever
        /// produced from it — but it is the same unsound-direction class that produced F1's 278-million-
        /// violation defect, and a bound that is not a bound is what T-0105 exists to eliminate.
        ///
        /// Returns the smallest <c>t</c> this function can PROVE reaches <paramref name="zeta"/> at
        /// <paramref name="e"/>: the closed form, checked, and bisected UP if the check fails. It can only
        /// move τ_max upward, i.e. shrink the contained prism, which is the conservative direction.
        /// </summary>
        public static float InverseAtEUpperBound(in ShaperHeightOp op, float zeta, float e)
        {
            float tau = InverseAtE(op, zeta, e);
            if (IsNoCrossSection(tau)) return NoCrossSection;

            if (LowerG(op, tau, e) >= zeta) return tau;

            float hi = 1f;
            if (LowerG(op, hi, e) < zeta) return NoCrossSection;   // nothing provably solid at this ζ
            float lo = tau;
            for (int i = 0; i < InverseIterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (LowerG(op, mid, e) >= zeta) hi = mid; else lo = mid;
            }
            return hi;
        }

        /// <summary>
        /// T-0109 FIX F1 — the LEAST value <c>G</c> can take at <paramref name="t"/> anywhere on the canvas.
        /// For every technique but <see cref="ShaperExtrusionTechnique.Linear"/> that is just <c>G(t)</c>,
        /// which does not depend on position; for <c>Linear</c> it is <c>inf E · B(t/a)</c>.
        /// </summary>
        static float LowerG(in ShaperHeightOp op, float t, float e)
            => op.technique == ShaperExtrusionTechnique.Linear ? e * Bevel(op, t) : Composed(op, t, 0f, 0f);

        /// <summary>
        /// <c>Ginv(ζ)</c> evaluated with <see cref="ShaperExtrusionTechnique.Linear"/>'s position-dependent
        /// <c>E</c> pinned to a SPECIFIED value. For every other technique <c>E</c> is a function of <c>t</c>
        /// and <paramref name="e"/> is ignored, so this is exactly <see cref="Inverse"/>.
        ///
        /// T-0109 FIX F1 — the shared body of <see cref="InverseLowerBound"/> and
        /// <see cref="InverseUpperBound"/>, and of the march's per-step LOCAL bracket, which pins <c>E</c> to
        /// the range it can reach over one candidate step rather than to the canvas-wide extremes.
        /// <c>Ginv</c> is non-increasing in <c>e</c>, so a larger <c>e</c> gives a smaller (more
        /// conservative, containing) <c>τ</c> and a smaller <c>e</c> gives a larger (contained) one.
        /// </summary>
        public static float InverseAtE(in ShaperHeightOp op, float zeta, float e)
        {
            if (op.technique != ShaperExtrusionTechnique.Linear)
                return Inverse(op, zeta, 0f, 0f);

            if (zeta <= 0f) return 0f;
            if (!(e > 0f)) return NoCrossSection;
            if (zeta > e) return NoCrossSection;
            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f) return 0f;

            float u = BevelInverseU(op, zeta / e);
            if (u >= 0f) return u * op.a;

            // Ogee: bisect on e·B(t) over the band.
            float lo = 0f, hi = op.a;
            for (int i = 0; i < InverseIterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (e * Bevel(op, mid) >= zeta) hi = mid; else lo = mid;
            }
            return hi;
        }

        // ── the declared slope bounds, HS-4 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>sup|dE/dt|</c> over the authored parameter range. HS-4.1's first table. <b>MAY return
        /// <c>+∞</c>, and that is a first-class answer, not a failure to measure.</b>
        ///
        /// Re-derived in REF-HEIGHT-MATHS §C, which also shows why the figures that circulated as
        /// <c>round 16.4</c> and <c>dome 23.1</c> are not bounds: both profiles diverge at <c>t → 0⁺</c> for
        /// every <c>c &gt; 0.5</c>, which includes their own default.
        /// </summary>
        public static float ExtrusionSlopeBound(in ShaperHeightOp op)
        {
            switch (op.technique)
            {
                case ShaperExtrusionTechnique.Flat:
                    return 0f;                                  // exact identity in t

                case ShaperExtrusionTechnique.Linear:
                    return 0f;                                  // exactly 0 in t; its slope lives in (nx,ny) — HS-4.4

                case ShaperExtrusionTechnique.Stepped:
                    return float.PositiveInfinity;              // a floor function: DISCONTINUOUS, REF §D

                case ShaperExtrusionTechnique.Dome:
                {
                    // Bounded only for c ≤ ½. The interior maximum solves 2(m−1)(1−t)² = t(2−t), giving
                    // t* = 1 − 1/√(2m−1) and L = 2m/√(2m−1) · (1 − 1/(2m−1))^(m−1). At m = 1 the expression
                    // degenerates correctly to 2 (the maximum moves to t = 0). REF §C.4.
                    if (op.curve > 0.5f) return float.PositiveInfinity;
                    double m = op.domeExp;
                    double r = 2.0 * m - 1.0;
                    return (float)(2.0 * m / Math.Sqrt(r) * Math.Pow(1.0 - 1.0 / r, m - 1.0));
                }

                case ShaperExtrusionTechnique.Round:
                    // E′ = p·t^(p−1) with p = 0.5/c: increasing and maximal at t = 1 when p ≥ 1 (c ≤ ½),
                    // divergent at t → 0⁺ otherwise. REF §C.3.
                    return op.curve > 0.5f ? float.PositiveInfinity : op.roundExp;

                case ShaperExtrusionTechnique.Taper:
                    // 1/max(0.05, 0.6τ). Range [1.6667, 20] over τ ∈ [0,1] — the worst case is 20 at τ ≤ 1/12,
                    // NOT the 16.7 that circulated for τ = 0.1 (REF §C.2). τ is authorable down to 0.
                    return op.invTaperT;

                case ShaperExtrusionTechnique.Pyramid:
                    return op.tau;                              // exact and tight, ≤ 1

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// <c>sup|dB/du|</c> over the authored parameter range. HS-4.1's second table. <b>Four of the five
        /// non-identity bevels are unbounded</b>, each somewhere different, and the fifth is bounded only in
        /// <c>u</c> — in <c>t</c> it carries a <c>1/a</c> amplifier, which HS-4.2's product rule applies.
        /// </summary>
        public static float BevelSlopeBound(in ShaperHeightOp op)
        {
            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f) return 0f;
            switch (op.bevel)
            {
                case ShaperBevelTechnique.Linear:  return 1f;                        // exact, tight
                case ShaperBevelTechnique.Rounded: return float.PositiveInfinity;    // u → 0⁺, the silhouette
                case ShaperBevelTechnique.Cove:    return float.PositiveInfinity;    // u → 1⁻, the inner band edge
                case ShaperBevelTechnique.Ogee:    return float.PositiveInfinity;    // u → ½, the mid-band inflection
                case ShaperBevelTechnique.Stepped: return float.PositiveInfinity;    // a floor function
                default: return 0f;
            }
        }

        /// <summary>
        /// HS-4.2 — <b>the composition rule is a PRODUCT rule, not a <c>max</c> rule</b>, and that is why none
        /// of <see cref="ShaperBound"/>'s four methods applies: <c>Transform</c>, <c>Combine</c>,
        /// <c>Sweep</c> and <c>Shell</c> all compose bounds on the SAME quantity, whereas this composes two
        /// DIFFERENT functions multiplied together.
        ///
        /// From <c>G = E·B(t/a)</c>, on the band: <c>sup|dG/dt| ≤ L_E·sup|B| + sup|E|·L_B/a = L_E + supE·L_B/a</c>
        /// (every bevel has <c>sup|B| = 1</c>); off the band simply <c>L_E</c>. Infinity ABSORBS — any
        /// infinite term makes the composition infinite.
        /// </summary>
        public static float ComposedSlopeBound(in ShaperHeightOp op)
        {
            float le = ExtrusionSlopeBound(op);
            if (op.bevel == ShaperBevelTechnique.None || op.a <= 0f) return le;
            float lb = BevelSlopeBound(op);
            if (float.IsPositiveInfinity(le) || float.IsPositiveInfinity(lb)) return float.PositiveInfinity;
            return le + op.supE * lb * op.invA;
        }

        /// <summary>
        /// HS-4.2's final line — the extruded solid's declared slope in CANVAS units:
        /// <c>sup|dh/d(canvas pixel)| ≤ (body/span) · sup|dG/dt|</c>.
        ///
        /// <b>This composed figure, never a primitive's, is what any consumer requiring a Lipschitz height
        /// must read.</b> The per-technique figures are per-unit-body and per-unit-span; comparing an
        /// unscaled "pyramid 1.00" against a primitive's "worst 3.1" compares two different quantities
        /// (REF §C.8, correction 10).
        ///
        /// HS-4.3, recorded plainly: <b>nothing on the rendering path consumes this.</b> Its purpose is to make
        /// refusal MECHANICAL — a consumer that genuinely requires a finite Lipschitz height asks
        /// <see cref="IsLipschitz"/> and is refused by name for seven of the twelve techniques instead of
        /// silently producing holes. <see cref="ShaperResolve"/> is explicitly NOT such a consumer; HS-5 is why.
        /// </summary>
        public static float SlopeBound(in ShaperHeightOp op)
        {
            if (!op.present || op.body <= 0f) return 0f;
            float g = ComposedSlopeBound(op);
            if (float.IsPositiveInfinity(g)) return float.PositiveInfinity;
            return (op.body * op.invSpan) * g;
        }

        /// <summary>
        /// HS-4.3 — whether this compiled height stage has a finite Lipschitz constant at all. <c>false</c> is
        /// a real, answerable, testable state and not an error: it is what lets a future consumer refuse a
        /// technique by ASKING rather than by KNOWING.
        /// </summary>
        public static bool IsLipschitz(in ShaperHeightOp op) => !float.IsPositiveInfinity(SlopeBound(op));

        /// <summary>
        /// HS-4.4 — <see cref="ShaperExtrusionTechnique.Linear"/>'s slope lives in a DIFFERENT SPACE and must
        /// never be summed with the <c>t</c>-space figure. It is declared here as its own 2-vector, in canvas
        /// pixels of height per canvas pixel of node-LOCAL position:
        /// <code>
        /// ∂h/∂lx = body·0.6·cos θ / localSupportHalfW      ∂h/∂ly = −body·0.6·sin θ / localSupportHalfH
        /// </code>
        /// Zero for every other technique. The two figures are never added, because they are gradients of the
        /// same height with respect to two different variables and adding them would be a category error.
        /// </summary>
        public static void LinearGradient(in ShaperHeightOp op, out float gx, out float gy)
        {
            if (!op.present || op.technique != ShaperExtrusionTechnique.Linear || op.body <= 0f)
            {
                gx = 0f; gy = 0f; return;
            }
            // T-0109 FIX F7 — `+` on sinθ, the flipped +Y-up frame. See the FRAME NOTE on
            // <see cref="Profile"/>'s Linear case; the declared gradient must match the forward formula.
            gx = op.body * 0.6f * op.cosAngle * op.invLocalHalfW;
            gy = op.body * 0.6f * op.sinAngle * op.invLocalHalfH;
        }

        // ── breakpoints, HS-5.5 ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// HS-5.5 — the slab boundaries in <c>ζ</c>, ascending, from 0 to <c>sup G</c> inclusive. Returns the
        /// number written; <c>count − 1</c> slabs. Writes into a CALLER-OWNED array and allocates nothing;
        /// size it at <see cref="MaxBreakpoints"/>.
        ///
        /// <list type="bullet">
        /// <item><b><see cref="ShaperExtrusionTechnique.Stepped"/></b> contributes its EXACT tread set
        /// <c>{k/(n−1)}</c>. Inside a tread <c>G</c> is exactly constant, so the containing prism is exact and
        /// the march is exact with ZERO conservatism — <b>the stepped case is the BEST case, not the excluded
        /// one</b>, which is the whole of why HS-0.1 needs no exclusion for it.</item>
        /// <item><b><see cref="ShaperBevelTechnique.Stepped"/></b> likewise at <c>{(k+1)/m}</c>.</item>
        /// <item><b><see cref="ShaperExtrusionTechnique.Flat"/> with no bevel</b> is one slab: the solid is a
        /// plain prism.</item>
        /// <item><b>Anything smooth</b> gets a uniform subdivision into <see cref="SlabQuality"/>, and
        /// correctness holds for any count ≥ 1 (HS-5.4).</item>
        /// <item><b>Both stepped</b> MERGES the two sets — the case to size for.</item>
        /// </list>
        /// </summary>
        public static int Breakpoints(in ShaperHeightOp op, float[] dst)
        {
            if (dst == null || dst.Length < 2) return 0;

            float top = op.supG;
            if (!(top > 0f)) { dst[0] = 0f; dst[1] = 0f; return 2; }

            int cap = Mathf.Min(dst.Length, MaxBreakpoints);
            int count = 0;
            dst[count++] = 0f;

            bool extrusionExact = op.technique == ShaperExtrusionTechnique.Flat ||
                                  op.technique == ShaperExtrusionTechnique.Linear ||
                                  op.technique == ShaperExtrusionTechnique.Stepped;
            bool bevelExact = op.bevel == ShaperBevelTechnique.None ||
                              op.a <= 0f ||
                              op.bevel == ShaperBevelTechnique.Stepped;

            if (op.technique == ShaperExtrusionTechnique.Stepped)
                for (int k = 1; k < op.n - 1 && count < cap; k++)
                    count = Insert(dst, count, cap, k * op.invN1 * top);

            if (op.bevel == ShaperBevelTechnique.Stepped && op.a > 0f)
                for (int k = 1; k < op.bevelN && count < cap; k++)
                    count = Insert(dst, count, cap, k * op.invBevelN * top);

            if (!extrusionExact || !bevelExact)
                for (int k = 1; k < SlabQuality && count < cap; k++)
                    count = Insert(dst, count, cap, (k / (float)SlabQuality) * top);

            count = Insert(dst, count, cap, top);
            return count;
        }

        /// <summary>Sorted insert of a distinct value. Linear, on an array of at most 80 — never a sort, never an alloc.</summary>
        static int Insert(float[] dst, int count, int cap, float v)
        {
            if (count >= cap) return count;
            if (v <= 0f || float.IsNaN(v)) return count;
            int i = 0;
            while (i < count && dst[i] < v) i++;
            if (i < count && Mathf.Abs(dst[i] - v) <= 1e-7f) return count;   // already present
            for (int j = count; j > i; j--) dst[j] = dst[j - 1];
            dst[i] = v;
            return count + 1;
        }

        // ── HS-6.4, mechanically ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// HS-6.4 — the height of the SIDE WALL at the silhouette, <c>body·G(0)</c>, in canvas pixels. Zero
        /// means the solid closes smoothly to the base plane at the rim and there is no wall at all.
        ///
        /// Five mechanical statements follow, and the audit asserts every one of them (H7): at their default
        /// parameters <c>Dome</c>, <c>Round</c>, <c>Taper</c> and <c>Stepped</c> all have <c>E(0) = 0</c>, and
        /// <c>Pyramid</c> has <c>E(0) = 1 − τ = 0</c> at <c>τ = 1</c>, so <b>five of the seven profiles have no
        /// side wall by default</b>; only <c>Flat</c> and <c>Linear</c> have full-height walls; any bevel other
        /// than <c>None</c> and <c>Stepped</c> forces <c>B(0) = 0</c> and removes the wall from EVERY profile;
        /// and <c>Stepped</c> bevel leaves a wall of exactly <c>body·E(0)/m</c>. Together they are why HS-0.2
        /// can be ruled now and its rendering deferred — for most authored content there is nothing to render.
        /// </summary>
        public static float WallHeight(in ShaperHeightOp op, float nx, float ny)
            => op.body <= 0f ? 0f : op.body * Composed(op, 0f, nx, ny);

        /// <summary>True when this compiled stage leaves a side wall standing anywhere. HS-6.4.</summary>
        public static bool HasWall(in ShaperHeightOp op, float nx, float ny) => WallHeight(op, nx, ny) > 0f;

        // ── HS-1.4, the published quantity set ───────────────────────────────────────────────────────────

        /// <summary>
        /// HS-1.4 — what a node carrying this stage publishes.
        /// <c>Coverage | EdgeDistance | Height</c> when a height stage is PRESENT, and exactly
        /// <see cref="ShaperQuantitySet.ShippedShapeEngine"/> when it is not.
        ///
        /// <b>Declared, not discovered</b> (BC-3.7a): the set follows from <see cref="ShaperHeightOp.present"/>,
        /// never from testing whether some array happened to be non-null. <c>Depth</c> (BC-3.3 #7) is
        /// deliberately NOT added — it becomes real only through <see cref="ShaperResolve"/> and is not
        /// published by the height stage directly. <c>Heat</c>, <c>Density</c>, <c>Soot</c> and <c>Age</c> stay
        /// unpublished and are out of scope.
        /// </summary>
        public static ShaperQuantitySet Publishes(in ShaperHeightOp op)
            => op.present
                ? ShaperQuantitySet.ShippedShapeEngine | ShaperQuantitySet.Height
                : ShaperQuantitySet.ShippedShapeEngine;

        // ── the block entry, mirroring ShaperFillOps.FillTile / ShaperNormals.FillTile exactly ───────────

        /// <summary>
        /// Fill a rectangular tile of the HEIGHT sheet in ONE call: 1 float per sample, canvas pixels,
        /// measured from the layer's own base plane (HS-1.3, BC-3.3 #2 — "0 = the base plane"). The layer's
        /// <c>base</c> is HS-7's and belongs to <see cref="ShaperResolve"/>; it is deliberately NOT folded in
        /// here, because a sheet that already contained it could not be summed with a fill's height delta
        /// under FC-2.5 without double-counting the base.
        ///
        /// The signature mirrors <c>ShaperFillOps.FillTile</c> (<c>ShaperFillOps.cs:48-53</c>) and
        /// <c>ShaperNormals.FillTile</c> (<c>ShaperNormals.cs:121-126</c>) EXACTLY, including
        /// <paramref name="srcOffset"/>/<paramref name="srcStride"/> and
        /// <paramref name="dstOffset"/>/<paramref name="dstStride"/>, so shape, height, normal and fill all
        /// tile identically and one host loop drives all four.
        ///
        /// It reads only its OWN sample of <paramref name="distance"/> — never <c>[i−1]</c>, never
        /// <c>[i+width]</c> — which is what makes tile independence structural rather than a convention
        /// (BC-1.6, BC-2.1). The host owns, allocates and sizes every array; this never allocates, replaces,
        /// resizes or frees one (BC-3.7f). <b>Allocates nothing.</b>
        ///
        /// Samples OUTSIDE the silhouette (<c>d &gt; 0</c>) are written as 0 rather than as a sentinel. The
        /// reference simply skips them (<c>index.html:1421</c>, <c>if (distance>0) continue;</c>) and carries
        /// occupancy in a <c>−9999</c> sentinel; Shaper carries occupancy in <c>coverage</c>, which already
        /// exists, so no sentinel is introduced — HS-7.3. Samples exactly ON the boundary (<c>d == 0</c>) ARE
        /// written, at <c>E(0)·B(0)</c>, matching the reference.
        /// </summary>
        /// <param name="x0">Absolute sample index of the tile's left column.</param>
        /// <param name="y0">Absolute sample index of the tile's bottom row.</param>
        /// <param name="dstOffset">Index in the destination array of the tile's first sample.</param>
        /// <param name="dstStride">Row stride of the destination array; pass <paramref name="width"/> for a packed tile.</param>
        public static void FillTile(in ShaperHeightOp op, in ShaperSampleGrid grid,
                                    int x0, int y0, int width, int height,
                                    float[] distance, float[] heightOut,
                                    int srcOffset, int srcStride,
                                    int dstOffset, int dstStride)
        {
            if (heightOut == null) return;

            // The zero-thickness and no-stage cases are one memory clear rather than a per-sample branch, and
            // are structurally exact rather than arithmetically so (H5).
            if (!op.present || op.body <= 0f || distance == null)
            {
                for (int j = 0; j < height; j++)
                    Array.Clear(heightOut, dstOffset + j * dstStride, width);
                return;
            }

            bool linear = op.technique == ShaperExtrusionTechnique.Linear;

            for (int j = 0; j < height; j++)
            {
                float y = grid.originY + (y0 + j) * grid.pixelSize;
                int srow = srcOffset + j * srcStride;
                int drow = dstOffset + j * dstStride;
                for (int i = 0; i < width; i++)
                {
                    float d = distance[srow + i];
                    if (d > 0f || ShaperField.IsEmpty(d)) { heightOut[drow + i] = 0f; continue; }

                    float t = -d * op.invSpan;
                    if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

                    float nx = 0f, ny = 0f;
                    if (linear)
                    {
                        float x = grid.originX + (x0 + i) * grid.pixelSize;
                        LocalNormalised(op, x, y, out nx, out ny);
                    }

                    heightOut[drow + i] = op.body * Profile(op, t, nx, ny) * Bevel(op, t);
                }
            }
        }
    }
}
