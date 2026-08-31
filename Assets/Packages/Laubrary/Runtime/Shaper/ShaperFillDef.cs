using System;
using UnityEngine;

namespace Laubrary.Shaper
{
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

                default:
                    return ShaperQuantitySet.None;   // Solid, Texture
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
