using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0110, B6 — one hand-painted slot of an <see cref="ShaperFillKind.IndexedStrip"/> fill's palette: a
    /// colour AND a height (protrusion), together, on ONE authored row. That pairing is the whole point of the
    /// fill — "one hand-painted strip paints and sculpts at once" — so the two are never split into parallel
    /// arrays the author has to keep in sync by index.
    ///
    /// A plain <c>[Serializable]</c> class in a <c>List&lt;&gt;</c>, not a <c>SerializeReference</c> hierarchy —
    /// a slot does not nest and never will, the same reasoning <see cref="ShaperFillDef"/> itself gives.
    /// </summary>
    [Serializable]
    public class ShaperStripSlot
    {
        /// <summary>
        /// The slot's colour. Its ALPHA CHANNEL IS NOT AUTHORED AND NOT READ (FC-2.2, same rule as
        /// <see cref="ShaperFillDef.solidColor"/>): <see cref="ShaperFillCompiler.BakeStrip"/> decodes only R,
        /// G, B into the baked palette. Stored as a <see cref="Color"/> only because that is what Unity's
        /// picker gives, not because alpha means anything here.
        /// </summary>
        public Color color = Color.white;

        /// <summary>
        /// Layer-local height units (the same units as <see cref="ShaperFillDef.heightDelta"/>), signed. B6:
        /// "a positive or negative protrusion over a wide range". Zero is a legal, common value — a slot that
        /// only paints.
        /// </summary>
        public float height = 0f;
    }

    /// <summary>
    /// The authored fill. One <c>[Serializable]</c> class carrying every kind's dials, exactly the shape
    /// <see cref="ShaperPrimitiveDef"/> already uses for the seven primitives — not a
    /// <c>SerializeReference</c> class hierarchy. A fill does not nest (FC-5.2), so there is nothing a
    /// hierarchy would buy, and the flat class avoids the managed-reference-nulls-against-a-broken-assembly
    /// hazard the project has already been bitten by.
    ///
    /// Dial types are <see cref="ZUIValue"/> and <see cref="ZuiGradient"/>, used BY VALUE on the same terms the
    /// shape stage already stores <c>ZUIValue</c> dials on a primitive (<c>ShaperPrimitives.cs:96-100</c>) and
    /// samples them through <c>ShaperValue.Sample</c> rather than through ZUI's own <c>Evaluate</c>.
    /// FC-0.1: this stage MUST NOT reference, subclass, wrap or add a member to <c>ZuiFill</c> — that is the
    /// library-wide abstraction used in 20 files, and it is the blast radius the prohibition protects.
    /// </summary>
    [Serializable]
    public class ShaperFillDef
    {
        public ShaperFillKind kind = ShaperFillKind.Solid;

        // ── common to all four (FC-6, "common to all four and not repeated per kind") ──────────────────────

        /// <summary>
        /// Multiplies the shape's published coverage; clamped to [0,1] at bake (FC-2.4). It can never REPLACE
        /// the shape's edge — see the clamp in <see cref="ShaperFillCompiler"/> for why the multiplication
        /// alone is only half of that guarantee.
        /// </summary>
        public ZUIValue veil = new ZUIValue(1f);

        /// <summary>
        /// Layer-local height units, ADDED to the shape's own height and weighted by <c>coverageEff</c>
        /// (FC-2.5). A fill DECLARES at compile time whether it emits height — <c>emitsHeight</c> is
        /// <c>heightDelta != 0</c> — so a Solid with 0 costs no sheet and a Solid with 3 does.
        /// Nothing consumes it in T-0106; it is T-0110's whole interface.
        /// </summary>
        public ZUIValue heightDelta = new ZUIValue(0f);

        /// <summary>
        /// FC-2.6. Per-kind default is <c>Over</c> for all four, because paint is the common case; the heat and
        /// soot ramps B4 identifies as the fire and explosion palettes are usually authored to <c>Add</c>, and
        /// that is one dial the author sets, visibly, once (FC-2.6d).
        /// </summary>
        public ShaperFillComposite composite = ShaperFillComposite.Over;

        /// <summary>
        /// FC-1.6. Shared by Gradient and Texture because it means exactly the same thing to both; splitting it
        /// into two identically-defined fields would be the mirror image of the "one field, two quantities"
        /// fault <see cref="ShaperBlend"/> was split to avoid. Solid and Ramp ignore it — they are
        /// non-positional and never read the coordinate.
        /// </summary>
        public ShaperFillSpace space = ShaperFillSpace.Stamped;

        /// <summary>FC-1.5. Shared by Gradient and Texture, for the same reason as <see cref="space"/>.</summary>
        public ShaperFillFit fit = ShaperFillFit.Uniform;

        // ── Solid (FC-6.1) ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The flat colour. Its ALPHA CHANNEL IS NOT AUTHORED AND NOT READ (FC-2.2): a colour dial is
        /// three-channel in the UI, because if it were left four-channel an author would set alpha, see nothing
        /// happen, and file a bug. It is stored as a <see cref="Color"/> only because that is what
        /// <see cref="ZuiGradient"/> returns and what Unity's picker gives.
        /// </summary>
        public Color solidColor = Color.white;

        // ── Gradient (FC-6.2) ─────────────────────────────────────────────────────────────────────────────

        public ShaperGradientMode gradientMode = ShaperGradientMode.Linear;

        /// <summary>
        /// Baked to a 256-entry LINEAR RGB LUT at compile time (FC-5.4), never dereferenced per sample —
        /// BC-1.3 names "per-pixel dereference of a managed ZuiFill holding a Gradient" as an offender by name.
        /// Null falls back to <see cref="gradientTint"/> as a Solid AND records a diagnostic (FC-6.5).
        /// </summary>
        public ZuiGradient gradient = new ZuiGradient();

        /// <summary>The fallback colour when <see cref="gradient"/> is null — "a half-configured fill never renders empty".</summary>
        public Color gradientTint = Color.white;

        /// <summary><c>Linear</c>: the axis direction. <c>Angular</c>: the phase where <c>t = 0</c>. Unused by the other two.</summary>
        public ZUIValue gradientAngleDegrees = new ZUIValue(0f);

        /// <summary>In the normalised anchor space. <c>Linear</c>: the axis passes through it. <c>Radial</c>/<c>Angular</c>: the origin.</summary>
        public ZUIValue gradientCentreX = new ZUIValue(0f);
        public ZUIValue gradientCentreY = new ZUIValue(0f);

        /// <summary>
        /// A <b>SIZE, not a frequency</b> — bigger means the pattern spreads FURTHER. Taken by value from
        /// <c>ZuiFill.cs:83-88</c>, where the field had to be migrated because "the maths used to multiply by
        /// it, so raising 'zoom' made the pattern SMALLER". This contract starts on the fixed side: the
        /// authored dial is a size and the reciprocal is taken ONCE at compile time. Unused by
        /// <c>ByEdgeDistance</c>.
        /// </summary>
        public ZUIValue gradientSize = new ZUIValue(1f);

        /// <summary><c>ByEdgeDistance</c> only: how far in from the edge the ramp reaches, in CANVAS PIXELS.</summary>
        public ZUIValue gradientDepthPixels = new ZUIValue(8f);

        // ── Ramp-by-quantity (FC-6.3) ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE PICK DRIVES THE AVAILABILITY GATE (FC-4.1a). Default <see cref="ShaperQuantity.Coverage"/>
        /// because it is the one quantity guaranteed available on every node (FC-4.1b), so a freshly created
        /// Ramp never greys out on creation. <see cref="ShaperQuantity.SurfaceDirection"/> is not offerable at
        /// all — refused on TYPE (FC-6.3c), with a different message from the availability one.
        /// </summary>
        public ShaperQuantity rampQuantity = ShaperQuantity.Coverage;

        public ZuiGradient rampGradient = new ZuiGradient();
        public Color rampTint = Color.white;

        /// <summary>The value of the quantity that maps to <c>t = 0</c>.</summary>
        public ZUIValue rampInputLow = new ZUIValue(0f);
        /// <summary>
        /// The value that maps to <c>t = 1</c>. Inverted windows (<c>low &gt; high</c>) are LEGAL (FC-6.3b) —
        /// a negative denominator gives a reversed ramp, which is the natural way to invert without a second
        /// dial and needs no special case.
        /// </summary>
        public ZUIValue rampInputHigh = new ZUIValue(1f);

        // ── Texture (FC-6.4) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pixels are COPIED into host-owned bulk data at compile time and decoded to linear (FC-5.5). The op
        /// never holds this reference — a compiled fill op may reference bulk data by index only, never a
        /// <c>Texture2D</c>, <c>Sprite</c>, <c>Gradient</c> or any other managed object.
        /// </summary>
        public Texture2D texture;

        public ShaperTextureMapping textureMapping = ShaperTextureMapping.Fitted;

        /// <summary><c>Tiled</c> only: the repeat density, independent of the node's size.</summary>
        public ZUIValue textureTilesX = new ZUIValue(1f);
        public ZUIValue textureTilesY = new ZUIValue(1f);

        /// <summary>In UV units; animatable, which is how a texture scrolls (FC-6.4e — that is why an animated texture is not a fifth fill).</summary>
        public ZUIValue textureOffsetU = new ZUIValue(0f);
        public ZUIValue textureOffsetV = new ZUIValue(0f);

        /// <summary>Rotation of the UV frame about the anchor centre.</summary>
        public ZUIValue textureAngleDegrees = new ZUIValue(0f);

        /// <summary>
        /// MULTIPLIES the sampled albedo. Alpha not authored (FC-2.2). Also the fallback colour when the
        /// texture is null, missing or not readable (FC-6.4d).
        /// </summary>
        public Color textureTint = Color.white;

        // ── Indexed strip (FC-6.6, T-0110) ────────────────────────────────────────────────────────────────

        /// <summary>Angle-around or projection-across (B6). See <see cref="ShaperStripParameterisation"/>.</summary>
        public ShaperStripParameterisation stripParameterisation = ShaperStripParameterisation.Angular;

        /// <summary>
        /// The hand-painted strip itself, in authored order. Selected by INDEX, never interpolated — this is
        /// what "indexed" means and what keeps it a strip of discrete slots rather than a second gradient.
        /// Empty is a legal, degenerate authoring state: the fill falls back to <see cref="stripPlainColor"/>
        /// as a flat Solid (FC-6.5's "a half-configured fill never renders empty").
        /// </summary>
        public List<ShaperStripSlot> stripSlots = new List<ShaperStripSlot>();

        /// <summary>
        /// Whole-number repeats of the strip around the parameter's full cycle — 3D Shaper's improvement over
        /// the reference (B6: "more slots, whole-number repeats, true arc-length"). Rounded and clamped to
        /// >= 1 at compile; a non-integer authored value is not a partial repeat, it is the nearest whole one.
        /// </summary>
        public ZUIValue stripRepeats = new ZUIValue(1f);

        /// <summary><c>Angular</c>: the phase where the parameter is 0. <c>Projection</c>: the axis direction.</summary>
        public ZUIValue stripOrientationDegrees = new ZUIValue(0f);

        /// <summary>A 0..1 phase shift of the whole strip along its parameter, wrapping. Position, in B6's words.</summary>
        public ZUIValue stripOffset = new ZUIValue(0f);

        /// <summary>
        /// How far in from the outline the strip reaches, as a FRACTION of the node's own local half-extent
        /// (never raw canvas pixels — a pixel count would mean a different fraction of the shape at every
        /// size). <c>0</c> reaches no distance at all — the strip lives only on the silhouette's outermost
        /// edge. <c>1</c> (the default) reaches the shape's own shorter local half-extent, which for a
        /// symmetric primitive is close enough to the centre that B6's "turn it to maximum and it covers the
        /// WHOLE shape" reads as intended — an angular strip becomes a full sunburst converging near the
        /// centre rather than an outlined ring. Values above 1 are legal and simply saturate sooner.
        /// </summary>
        public ZUIValue stripReach = new ZUIValue(1f);

        /// <summary>
        /// The flat colour painted beyond the reach — B6's "plain fill". A single authored colour, not a slot
        /// of the strip: the interior is deliberately NOT a multicoloured continuation of the pattern, only a
        /// faint relief of it (via the height's quarter weight there) — see <see cref="ShaperFillOps.Sample"/>'s
        /// <see cref="ShaperFillKind.IndexedStrip"/> case (STRIP-SPEC.md SS-7.4).
        ///
        /// Its ALPHA CHANNEL IS NOT AUTHORED AND NOT READ either (FC-2.2, same rule as
        /// <see cref="ShaperStripSlot.color"/> and <see cref="solidColor"/>).
        /// </summary>
        public Color stripPlainColor = Color.white;

        // ── Height field (FC-6.7, T-0111) ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The imported/authored field, an <c>RFloat</c>, non-sRGB, Read/Write-enabled <see cref="Texture2D"/>
        /// carrying the SIGNED raw height value per texel — see <see cref="ShaperHeightFieldPreset"/> for how
        /// the 245 Kiln presets are shipped. Pixels are copied into host-owned bulk data at compile time,
        /// exactly like <see cref="texture"/> (FC-5.5) — the op never holds the Texture2D reference.
        /// </summary>
        public Texture2D heightField;

        /// <summary>
        /// Multiplies the field's RAW sampled value before it becomes <see cref="ShaperFillEmit.heightDelta"/>.
        /// Deliberately NOT a per-field auto-normalise: the 245 shipped presets are unnormalised (measured
        /// range roughly −1.65..+2.21 across the set, and each field has its own local range within that), and
        /// silently rescaling on import would make the SAME preset asset produce a different relief depending
        /// on when it was imported. <see cref="ShaperHeightFieldPreset.measuredMin"/>/<c>measuredMax</c> are
        /// informational only — this dial is the one and only place magnitude is decided, authored, on the
        /// fill, the same way <see cref="heightDelta"/> already is.
        /// </summary>
        public ZUIValue heightFieldScale = new ZUIValue(1f);

        /// <summary>
        /// The flat albedo this kind paints — "a fill that emits height and no colour" (SHAPER_THE_DESIGN.md
        /// B5) means no PATTERN in the colour, not a missing channel (FC-2.1 requires every fill to emit
        /// albedo). Its alpha is not authored or read, same rule as every other flat-colour dial (FC-2.2).
        /// </summary>
        public Color heightFieldTint = Color.white;

        // ── Tapestry Steel (FC-6.8, T-0111) ───────────────────────────────────────────────────────────────

        /// <summary>Base grid resolution the fBm base tone hashes at, in <see cref="ShaperTapestryCanvas.Fbm"/>'s <c>baseCells</c>. Bigger = finer mottling.</summary>
        public ZUIValue steelCells = new ZUIValue(6f);

        /// <summary>fBm octave count for the base tone. Kiln's own default order of magnitude (its shade() pipeline uses 3-4).</summary>
        public ZUIValue steelOctaves = new ZUIValue(4f);

        /// <summary>Deterministic seed, rounded to a uint at compile — same role as <see cref="ShaperFillInputs.seed"/> but authored per-fill so two Steel fills on one document can differ.</summary>
        public ZUIValue steelSeed = new ZUIValue(0f);

        /// <summary>The base tone at the low end of the fBm sweep (Kiln's "dry" entry, simplified to one authored colour rather than a hue/sat/val palette table).</summary>
        public Color steelBaseLow = new Color(0.30f, 0.31f, 0.33f);

        /// <summary>The base tone at the high end of the fBm sweep.</summary>
        public Color steelBaseHigh = new Color(0.55f, 0.56f, 0.58f);

        /// <summary>Kiln's "rust growing out of the low ground of whatever shape it was handed" — the tint blended in with weight <see cref="steelRustAmount"/>, biased toward the shape's interior via <see cref="ShaperQuantity.EdgeDistance"/>.</summary>
        public Color steelRustColor = new Color(0.42f, 0.20f, 0.10f);

        /// <summary>How strongly the rust tint reaches in from the edge, and its overall weight. 0 = no rust.</summary>
        public ZUIValue steelRustAmount = new ZUIValue(0.25f);

        /// <summary>How far in from the silhouette (canvas pixels) the rust bias saturates. Larger = rust reaches further toward the centre.</summary>
        public ZUIValue steelRustReachPixels = new ZUIValue(24f);

        /// <summary>Additive fine-grain variation from a second, higher-frequency fBm octave set — Kiln's "grain" term, simplified to one amount dial.</summary>
        public ZUIValue steelGrain = new ZUIValue(0.06f);

        /// <summary>
        /// T-0111's new palette-quantise stage (FC-6.9) — per-CHANNEL posterise in LINEAR space to N levels,
        /// applied last. 0 or 1 = off (the fill stays continuous-tone). This is the "output stays pixel art
        /// rather than photographic" requirement, and it did not exist anywhere in the fill contract before
        /// this task — see <see cref="ShaperFillOps.Quantise"/>.
        /// </summary>
        public ZUIValue quantiseLevels = new ZUIValue(0f);

        // ── declarations (FC-4.1) ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The set of quantities this fill REQUIRES, as a function of its authored settings — NOT a per-kind
        /// constant (FC-4.1a). A per-kind constant cannot express any of the three real cases: Solid and
        /// Texture require nothing regardless of settings; Gradient requires <c>edgeDistance</c> in ONE of its
        /// four modes and nothing in the other three; Ramp requires whichever quantity is picked.
        ///
        /// Evaluated ONCE at compile time, which is what keeps it out of the loop (BC-1.2).
        /// </summary>
        public ShaperQuantitySet RequiredSet()
        {
            switch (kind)
            {
                case ShaperFillKind.Gradient:
                    return gradientMode == ShaperGradientMode.ByEdgeDistance
                        ? ShaperQuantitySet.EdgeDistance
                        : ShaperQuantitySet.None;

                case ShaperFillKind.RampByQuantity:
                    // A non-rampable pick is refused on type before it ever reaches the availability gate, so it
                    // must not also be reported as an unpublished requirement — the two failures have different
                    // remedies and conflating them sends an author looking for a shape change that never helps.
                    return ShaperQuantities.IsRampable(rampQuantity)
                        ? ShaperQuantities.Of(rampQuantity)
                        : ShaperQuantitySet.None;

                case ShaperFillKind.IndexedStrip:
                    // The reach test always reads the node's own edge distance (T-0110) — unconditionally,
                    // unlike Gradient where only ONE of its four modes needs it. Every node publishes it
                    // (ShaperQuantitySet.ShippedShapeEngine), so this never greys out on creation, matching
                    // FC-4.1b's reasoning for Ramp's own Coverage default.
                    return ShaperQuantitySet.EdgeDistance;

                case ShaperFillKind.TapestrySteel:
                    // T-0111: the rust bias always reads edge distance, unconditionally, same reasoning as
                    // IndexedStrip's reach test above — every node publishes it, so this never greys out.
                    return ShaperQuantitySet.EdgeDistance;

                default:
                    return ShaperQuantitySet.None;   // Solid, Texture, HeightField
            }
        }

        /// <summary>
        /// The set this fill OPTIONALLY reads. Empty for all four kinds T-0106 ships. It exists as a declared
        /// set rather than being omitted because FT-12 asserts <c>read set == Required ∪ Optional</c>, and an
        /// absent concept cannot be asserted against.
        /// </summary>
        public ShaperQuantitySet OptionalSet() => ShaperQuantitySet.None;

        /// <summary>
        /// True when the authored settings are refused on TYPE rather than on availability (FC-6.3c). The
        /// caller emits <see cref="ShaperQuantities.SurfaceDirectionRefusal"/>, which is deliberately a
        /// different sentence from the FC-4.3 availability one.
        /// </summary>
        public bool HasTypeRefusal()
            => kind == ShaperFillKind.RampByQuantity && !ShaperQuantities.IsRampable(rampQuantity);

        /// <summary>The root fill of a brand-new layer: Solid, opaque white, zero height delta, <c>Over</c> (FC-3.2).</summary>
        public static ShaperFillDef DefaultRootFill()
            => new ShaperFillDef
            {
                kind = ShaperFillKind.Solid,
                solidColor = Color.white,
                composite = ShaperFillComposite.Over,
            };
    }
}
