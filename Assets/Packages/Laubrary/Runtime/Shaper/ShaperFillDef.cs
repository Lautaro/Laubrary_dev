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
        /// <summary>
        /// T-0271 — <b>did a person ask for this fill, or did Unity's serializer conjure it?</b>
        ///
        /// <see cref="ShaperNode.fill"/> is a plain <c>[Serializable]</c> class field, and Unity NEVER writes
        /// null for one: on save it materialises a DEFAULT-CONSTRUCTED instance in its place (measured on a
        /// saved document — the phantom comes back with <c>veil</c> static 1 and <c>solidColor</c> white, the
        /// C# initialiser values, not zeroes). So every node of every SAVED document comes back owning a fill
        /// nobody authored. That is not cosmetic: a child that owns a fill wins inside its own coverage, so on
        /// a saved bag each member is painted by its own phantom white Solid and the bag's authored fill paints
        /// nothing at all — measured 1754 changed pixels in memory, 0 after a save and reload (T-0277).
        ///
        /// The border closed the same hole with <see cref="ShaperBorderDef.enabled"/> defaulting to false,
        /// which works there because a border is meaningless until switched on. A fill is not: there is no
        /// "off" state for one, and the phantom is bit-identical to a legitimately authored white Solid.
        /// Hence an explicit flag, false by default so a phantom carries false, set true by every place that
        /// creates a fill because someone asked for one — <see cref="DefaultRootFill"/>, the window's
        /// "Add fill" and its seeded border fill.
        ///
        /// <b>Documents authored before this flag existed keep their fills.</b> <see cref="IsAuthored"/>
        /// treats a deserialized fill whose serialized state DIFFERS from a default-constructed one as real
        /// authored data and promotes it in place, so nothing is lost and nothing needs re-saving. The one
        /// case that cannot be told apart — a pre-flag fill that was added and then left at every default —
        /// is read as absent; on a layer ROOT that is invisible (FC-3.2 substitutes the identical
        /// <see cref="DefaultRootFill"/>), and on a child it means the child inherits its owner's paint
        /// instead of covering it with white, which is what the author saw anyway on every kind of document
        /// this project could find. Turning the field into <c>[SerializeReference]</c> would tell them apart
        /// perfectly and is the wrong trade: it changes the field's storage, and every fill in every existing
        /// document would deserialize as null.
        /// </summary>
        [HideInInspector] public bool authored = false;

        /// <summary>
        /// Set once <see cref="IsAuthored"/> has answered "phantom" for this instance, so the JSON comparison
        /// below is paid at most once per deserialized object rather than once per node per rendered frame.
        /// Not serialized, and deliberately only caches the NEGATIVE answer: the positive one promotes
        /// <see cref="authored"/> itself, which is both the cache and the migration.
        /// </summary>
        [NonSerialized] bool m_phantomChecked;

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

        /// <summary>
        /// T-0172 — a SPRITE-SHEET texture stepped by the node's own phase, rather than the continuous UV
        /// scroll <see cref="textureOffsetU"/>/<see cref="textureOffsetV"/> already give (FC-6.4e's "that is
        /// why an animated texture is not a fifth fill" — this is why it is a MODE on Texture instead: it
        /// shares every other Texture dial, including the tint/mapping/tiling above, and differs only in which
        /// sub-rectangle of <see cref="texture"/> is sampled). When false, every field below is inert and the
        /// kind renders exactly as before T-0172 (byte-identical).
        /// </summary>
        public bool textureAnimated = false;

        /// <summary>The sheet's grid, columns × rows. Rounded and clamped to >= 1 at compile.</summary>
        public ZUIValue textureFrameColumns = new ZUIValue(1f);
        public ZUIValue textureFrameRows = new ZUIValue(1f);

        /// <summary>
        /// How many of the grid's <c>columns × rows</c> cells are actually used frames, in ROW-MAJOR order
        /// starting at the sheet's texel row 0 (Unity's <c>GetPixels32</c> is BOTTOM-up, so frame 0 is the
        /// BOTTOM-left cell — the same convention <see cref="ShaperFillOps"/>'s existing texel indexing already
        /// uses, not a new one). Clamped to <c>[1, columns·rows]</c> at compile. The frame shown is
        /// <c>floor(phase01 · frameCount)</c>, clamped to the last frame at <c>phase01 == 1</c> rather than
        /// wrapping past it — a stepped sprite-sheet plays once per node cycle, the same way every other
        /// phase-driven Shaper dial reads the node's own clock once per cycle (FC-1.4).
        /// </summary>
        public ZUIValue textureFrameCount = new ZUIValue(1f);

        // ── OverPhase (FC-6.10, T-0172) ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// The ramp this fill's single flat colour is drawn from — see <see cref="ShaperFillKind.OverPhase"/>.
        /// Null falls back to <see cref="overPhaseTint"/> as a Solid, same rule as every other kind's gradient
        /// (FC-6.5).
        ///
        /// <b>Deliberately NOT <c>new ZuiGradient()</c> (W6.3).</b> Every other positional kind samples this
        /// ramp at many spatial <c>t</c> values across one shape, so its shared black→white default still
        /// shows both ends somewhere on screen. OverPhase samples ONE fixed <c>t</c> — the node's own phase —
        /// for the WHOLE shape, so a still frame (a contact sheet, or simply phase 0 on a freshly authored
        /// node) shows exactly the ramp's FIRST stop. With the shared default that stop is black: a fresh
        /// OverPhase fill rendered as a solid black shape, indistinguishable from nothing painted at all. See
        /// <see cref="DefaultOverPhaseGradient"/>.
        /// </summary>
        public ZuiGradient overPhaseGradient = DefaultOverPhaseGradient();

        /// <summary>
        /// W6.3 — OverPhase's own default ramp. Red-to-blue matches this fill kind's own doc comment example
        /// ("a flash of red at phase 0 sliding to blue at phase 1"), so a fresh fill is recognisably itself at
        /// ANY single phase — not merely correct on average across a sweep nobody may ever watch.
        /// </summary>
        static ZuiGradient DefaultOverPhaseGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(new Color(0.25f, 0.45f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ZuiGradient { gradient = g };
        }

        /// <summary>The fallback colour when <see cref="overPhaseGradient"/> is null.</summary>
        public Color overPhaseTint = Color.white;

        // ── Procedural: Noise / Grid / Dots (FC-6.11, T-0172) ─────────────────────────────────────────────

        /// <summary>Which procedural pattern this fill draws — see <see cref="ShaperFillKind.Procedural"/>.</summary>
        public ShaperProceduralKind proceduralKind = ShaperProceduralKind.Noise;

        /// <summary><see cref="ShaperProceduralKind.Noise"/> only — see <see cref="ShaperNoiseKind"/>.</summary>
        public ShaperNoiseKind noiseKind = ShaperNoiseKind.Value;

        /// <summary>Noise only: the ramp the noise value is mapped through. Null falls back to <see cref="proceduralTint"/> (FC-6.5).</summary>
        public ZuiGradient proceduralGradient = new ZuiGradient();

        /// <summary>
        /// Noise's fallback colour when <see cref="proceduralGradient"/> is null; Grid/Dots' INK colour
        /// (<c>ZuiFill.cs:68</c>'s "Ink = color" by value — the pattern's alpha-equivalent mask multiplies the
        /// common <see cref="veil"/> dial instead of a fourth channel, per FC-2.2).
        /// </summary>
        public Color proceduralTint = Color.white;

        /// <summary>
        /// A SIZE, not a frequency — same convention as <see cref="gradientSize"/> (bigger spreads the pattern
        /// further; the reciprocal is taken once at compile). For Grid/Dots this is the cell size; for Noise
        /// the noise wavelength.
        ///
        /// <b>Default 0.5, not 1.</b> The anchor coordinate this divides is already ±1 across the whole node
        /// box (the same <c>u,v</c> <see cref="gradientSize"/> itself divides), so a scale of 1 leaves only
        /// ONE grid line/dot cell inside the box — measured on a PM by-eye pass of T-0172's contact sheet as
        /// "one solid block" for Grid and "invisible" for Dots. 0.5 reproduces <c>ZuiFill</c>'s own Grid
        /// default EXACTLY (<c>ZuiFill.cs:145</c>'s <c>gridSpacing = 0.5f</c>, divided into the same ±1 range),
        /// giving ~4 cells across the box for Grid/Dots and, with Noise's own ×3 sample-time multiplier
        /// (<c>ShaperFillOps.Sample</c>'s <c>ValueNoise2Octave(px·3, py·3)</c>), a comparable few-cells-across
        /// frequency for Noise too — all three sub-modes read as a pattern rather than a flat average colour.
        /// </summary>
        public ZUIValue proceduralScale = new ZUIValue(0.5f);

        /// <summary>Animatable — slides the pattern, which is how it scrolls/drifts over the node's phase.</summary>
        public ZUIValue proceduralOffsetU = new ZUIValue(0f);
        public ZUIValue proceduralOffsetV = new ZUIValue(0f);

        /// <summary>Rotates the pattern about its offset.</summary>
        public ZUIValue proceduralAngleDegrees = new ZUIValue(0f);

        /// <summary>Grid only: line thickness as a fraction of a cell (<c>ZuiFill.cs:146</c> by value).</summary>
        public ZUIValue gridLineWidth = new ZUIValue(0.08f);

        /// <summary>Grid only: which axes draw lines (<c>ZuiFill.cs:147-148</c> by value).</summary>
        public bool gridVertical = true;
        public bool gridHorizontal = true;

        /// <summary>Dots only: disc diameter as a fraction of a cell (<c>ZuiFill.cs:152</c> by value).</summary>
        public ZUIValue dotSize = new ZUIValue(0.5f);

        /// <summary>Dots only: offset alternating rows by half a cell (<c>ZuiFill.cs:154</c> by value).</summary>
        public bool dotStagger = true;

        // ── Indexed strip (FC-6.6, T-0110) ────────────────────────────────────────────────────────────────

        /// <summary>Angle-around or projection-across (B6). See <see cref="ShaperStripParameterisation"/>.</summary>
        public ShaperStripParameterisation stripParameterisation = ShaperStripParameterisation.Angular;

        /// <summary>
        /// The hand-painted strip itself, in authored order. Selected by INDEX, never interpolated — this is
        /// what "indexed" means and what keeps it a strip of discrete slots rather than a second gradient.
        /// Empty is still a legal authoring state (an author can clear every slot) — the fill falls back to
        /// <see cref="stripPlainColor"/> as a flat Solid then (FC-6.5's "a half-configured fill never renders
        /// empty") — but a FRESH fill is seeded with two contrasting slots (W6.3): a zero-slot default made
        /// every new IndexedStrip fill degenerate to that same flat Solid, so the contact sheet's "every cell
        /// must visibly show the kind's character" requirement failed for this kind specifically — switching
        /// to IndexedStrip showed no strip at all until an author added a slot by hand.
        /// </summary>
        public List<ShaperStripSlot> stripSlots = new List<ShaperStripSlot>
        {
            new ShaperStripSlot { color = new Color(0.92f, 0.92f, 0.92f) },
            new ShaperStripSlot { color = new Color(0.32f, 0.32f, 0.32f) },
        };

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
                    return ShaperQuantitySet.None;   // Solid, Texture, HeightField, OverPhase, Procedural
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
                // T-0271 — the engine's own substituted default IS a real fill, and the window hands this same
                // object to the author as the fill of a new layer (ShaperWindow.NewLayer), so it is authored.
                authored = true,
            };

        /// <summary>
        /// T-0271 — the ONE test for "does this node own a fill?", replacing every <c>fill != null</c>.
        ///
        /// A saved document's null fill comes back as a default-constructed phantom (see <see cref="authored"/>),
        /// so null-checking answers the wrong question on exactly the documents an author actually has. This
        /// answers it for both: an unauthored instance whose serialized state equals a default-constructed
        /// one's is the phantom and is absent; anything else is real.
        ///
        /// The comparison is <see cref="JsonUtility.ToJson"/> against a cached default rather than a
        /// hand-written field list ON PURPOSE: this class carries 60-odd authorable fields and gains more, and
        /// a hand-written comparison silently stops migrating the day someone adds one it does not know about.
        /// JsonUtility walks exactly the fields Unity serializes — the same set that produced the phantom —
        /// so the two can never drift apart. Cost is paid at most once per instance (see
        /// <see cref="m_phantomChecked"/>), on the main thread, where every caller already is.
        /// </summary>
        public static bool IsAuthored(ShaperFillDef def)
        {
            if (def == null) return false;
            if (def.authored) return true;
            if (def.m_phantomChecked) return false;
            if (def.DiffersFromDefault())
            {
                // Migration, in place: a pre-flag authored fill is promoted the first time it is read, so it
                // survives being re-saved and never has to be examined again.
                def.authored = true;
                return true;
            }
            def.m_phantomChecked = true;
            return false;
        }

        /// <summary>
        /// T-0271 — <see cref="IsAuthored"/> as a null-coalescible value, for the call sites that want the def
        /// or nothing: <c>ShaperFillDef.Authored(node.fill)</c> is the phantom-safe <c>node.fill</c>.
        /// </summary>
        public static ShaperFillDef Authored(ShaperFillDef def) => IsAuthored(def) ? def : null;

        static string s_defaultJson;

        bool DiffersFromDefault()
        {
            s_defaultJson ??= JsonUtility.ToJson(new ShaperFillDef());
            return JsonUtility.ToJson(this) != s_defaultJson;
        }
    }
}
