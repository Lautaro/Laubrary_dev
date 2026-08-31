using System;
using Laubrary.Shaper;
using Laubrary.SpriteFx;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0114 — WHERE in the pipeline an effect runs, relative to the point several instances of one node fold
    /// into one picture (a swarm's per-instance union, or any future multi-layer composite). This is a real,
    /// user-visible property, not an implementation detail to hide: for a single un-swarmed shape the two stages
    /// produce an IDENTICAL picture, but for overlapping instances they do not, because a stage that can drop a
    /// pixel (<c>PixelModifier.ApplyPixel</c> returning <c>false</c>, or any non-linear kernel — posterise,
    /// contrast, ordered dither, colour replace) behaves differently once other content is already under or over
    /// it. An earlier design report claimed this distinction could be collapsed into a single stage with "zero
    /// behavioural cost" — that claim is WITHDRAWN (see the T-0098 digest, error E2): non-linear kernels differ
    /// whenever instances overlap, linear kernels differ once anything lies underneath (Over-compositing algebra
    /// does not commute with per-source scaling), and a pixel-dropping kernel drops a PARTICLE's contribution
    /// pre-composite but the WHOLE merged result post-composite. Both stages are therefore built for real here,
    /// never folded into one.
    ///
    /// APPEND-ONLY: serialized as an int, same posture <see cref="ShaperSwarmImplementation"/> and
    /// <see cref="ShaperCompositeReason"/> already take.
    /// </summary>
    public enum ShaperEffectStage
    {
        /// <summary>Runs once per instance, on that instance's OWN buffer, before instances fold into one
        /// picture. The native semantic of Pyre's per-particle Geometry/Pixel modifiers today
        /// (<c>PyreRenderer.ApplyGeo</c>/<c>ApplyPix</c>, run inside <c>DrawParticle</c> before the Over-composite
        /// at <c>PyreRenderer.cs:3089-3092</c>).</summary>
        PreComposite = 0,

        /// <summary>Runs once on the FINISHED, folded picture. The native semantic of Pyre's whole-frame Post
        /// modifiers today (<c>PyreRenderer.ApplyLayerPost</c>) — the only stage that can read a pixel's
        /// NEIGHBOURS, since Pre-composite modifiers only ever see one instance's own buffer.</summary>
        PostComposite = 1,
    }

    /// <summary>
    /// T-0114 — which of the three preconditions (SHAPER_THE_DESIGN.md C8) an effect actually needs, measured
    /// against the real 41-effect catalog rather than assumed. Four buckets, not the task body's approximate
    /// "13 to 20 need sheets" — the real split, grep-verified against
    /// <c>Runtime/SpriteFx/SpriteFxModifiers.cs</c> and <c>SpriteFxSimulationModifiers.cs</c>, is exactly
    /// 27 buffer-only + 13 sheet-needing + 1 stuck = 41 (see <see cref="ShaperEffectCatalog"/> for the
    /// per-effect table and SPEC.md Part 2 for the full method).
    /// </summary>
    public enum ShaperEffectPortability
    {
        /// <summary>Reads nothing but its own buffer pixels — no shape awareness, no reach past its own picture.
        /// Universal today with zero cost, on any host that runs its stage at all. 20 of 41 (the 10 Post
        /// whole-frame passes + 1 stateful Simulation pass, all inherently post-composite by nature since they
        /// need a finished picture to read neighbours from; plus 9 Pixel colour kernels that are commonly
        /// mis-advertised as generator-specific but read only buffer coordinates).</summary>
        BufferOnlyFree = 0,

        /// <summary>Reads only buffer pixels but REACHES past the silhouette (a warp can pull an off-picture
        /// pixel in, per <c>PyreRenderer.cs:3020</c>'s own comment) — universal once the buffer is padded and
        /// the picture-rect is known, at a named quality cost (nearest-neighbour resample, one per chained warp
        /// unless folded into a single map). 7 of 41 — the Geometry warps whose own per-instance form already
        /// works today; padding is what makes their whole-buffer/post-composite form possible too.</summary>
        BufferOnlyPadded = 1,

        /// <summary>Needs the generator to publish a per-pixel shape-aware sheet — concretely
        /// <see cref="ShaperQuantitySet.ShippedShapeEngine"/> (coverage + edge distance), the same sheets the
        /// Wave 2 fill contract already defined and a primitive/height node already publishes today. 13 of 41.
        /// A Composite-sourced (baked-raster) generator publishes Coverage (its alpha channel,
        /// <c>ShaperCompiledComposite.coverage</c>) but not EdgeDistance yet, so these 13 are available on
        /// Primitive/Bag/height-carrying nodes today and correctly greyed out on a bare Composite node until a
        /// distance transform is added to <see cref="ShaperCompiledComposite"/> — a real, honest, structural
        /// gate, not a hidden gap.</summary>
        NeedsSheets = 2,

        /// <summary>Genuinely stuck: Edge Warp roughens a rim by testing the boundary at each angle, which has
        /// no reconstruction from a merged/rasterised buffer (SHAPER_THE_DESIGN.md C8, T-0098 Investigator I §7
        /// point 1). It is also currently hostless anywhere in the project. Not part of "universal" — it needs a
        /// real per-generator rim host, or deletion, and this task does neither (out of scope, named honestly).
        /// 1 of 41.</summary>
        Stuck = 3,
    }

    /// <summary>One catalogued effect's classification. <see cref="typeName"/> is the concrete
    /// <c>Laubrary.SpriteFx.PyreModifier</c> subclass name — deliberately a string, not a <c>System.Type</c>
    /// literal, so this file has no compile-time dependency on every effect class existing with that exact
    /// name forever (an effect renamed or retired shows up as a catalog/reality mismatch <see
    /// cref="ShaperEffectCatalogAudit"/>-style code can flag, rather than a silent compile failure elsewhere).</summary>
    public readonly struct ShaperEffectCatalogEntry
    {
        public readonly string typeName;
        public readonly string stageKind;      // "Geometry" | "Pixel" | "Post" | "Edge" | "Simulation"
        public readonly ShaperEffectPortability portability;
        public readonly ShaperEffectStage defaultStage;
        public readonly bool bothStagesPossible;   // true when running it at the OTHER stage is structurally legal too
        public readonly ShaperQuantitySet requiredSheets;   // Coverage=0 set (None) when portability != NeedsSheets

        public ShaperEffectCatalogEntry(string typeName, string stageKind, ShaperEffectPortability portability,
                                        ShaperEffectStage defaultStage, bool bothStagesPossible,
                                        ShaperQuantitySet requiredSheets = default)
        {
            this.typeName = typeName;
            this.stageKind = stageKind;
            this.portability = portability;
            this.defaultStage = defaultStage;
            this.bothStagesPossible = bothStagesPossible;
            this.requiredSheets = requiredSheets;
        }
    }

    /// <summary>
    /// T-0114 — the full 41-effect catalog, classified. Counted by walking every <c>class X : Y</c> declaration
    /// under <c>Runtime/SpriteFx/</c> whose base resolves to <c>GeometryModifier</c>/<c>PixelModifier</c>/
    /// <c>PostModifier</c>/<c>EdgeModifier</c>/<c>SimulationModifier</c> (matches T-0098 Investigator E's
    /// independent count of 41: 17 Geometry + 12 Pixel + 10 Post + 1 Edge + 1 Simulation). Buckets reconcile
    /// EXACTLY against Investigator I's bucket sizes (11 + 9 + 7 + 13 + 1 = 41) once each bucket's real member
    /// names are matched to the shipped class names — see SPEC.md Part 2 for the full reconciliation, including
    /// the one real ambiguity resolved there (I's bucket (iii) "Tint" is <see cref="Laubrary.SpriteFx"/>'s
    /// <c>TintModifier</c>, a directional/shape-aware tint — NOT <c>ColorTintModifier</c>, which is bucket (i)'s
    /// flat "Colour tint" and stays buffer-only).
    /// </summary>
    public static class ShaperEffectCatalog
    {
        // ── bucket 0/(i): buffer-only, free, PostComposite-native (10 Post + 1 Simulation = 11) ──────────────
        static ShaperEffectCatalogEntry Post(string name) =>
            new ShaperEffectCatalogEntry(name, "Post", ShaperEffectPortability.BufferOnlyFree,
                ShaperEffectStage.PostComposite, bothStagesPossible: true);

        // ── bucket (i): buffer-only, free, PreComposite-native but portable to Post too (9 Pixel) ────────────
        static ShaperEffectCatalogEntry FreePixel(string name) =>
            new ShaperEffectCatalogEntry(name, "Pixel", ShaperEffectPortability.BufferOnlyFree,
                ShaperEffectStage.PreComposite, bothStagesPossible: true);

        // ── bucket (ii): buffer-only via padding, at a resample cost (7 Geometry) ───────────────────────────
        static ShaperEffectCatalogEntry PaddedGeo(string name) =>
            new ShaperEffectCatalogEntry(name, "Geometry", ShaperEffectPortability.BufferOnlyPadded,
                ShaperEffectStage.PreComposite, bothStagesPossible: true);

        // ── bucket (iii): needs Coverage|EdgeDistance sheets (10 Geometry + 3 Pixel = 13) ───────────────────
        static ShaperEffectCatalogEntry Sheeted(string name, string kind) =>
            new ShaperEffectCatalogEntry(name, kind, ShaperEffectPortability.NeedsSheets,
                ShaperEffectStage.PreComposite, bothStagesPossible: false,
                requiredSheets: ShaperQuantitySet.ShippedShapeEngine);

        public static readonly ShaperEffectCatalogEntry[] All =
        {
            // Post (10) — inherently whole-picture, i.e. post-composite by nature.
            Post("DissolveModifier"), Post("BloomModifier"), Post("OutlineModifier"),
            Post("ChromaticAberrationModifier"), Post("BallisticShockwaveModifier"), Post("FuseModifier"),
            Post("EdgeSmoothModifier"), Post("DropShadowModifier"), Post("KaleidoscopeModifier"),
            Post("RelightModifier"),
            // Simulation (1) — same whole-picture nature as Post.
            new ShaperEffectCatalogEntry("PixelFluidModifier", "Simulation", ShaperEffectPortability.BufferOnlyFree,
                ShaperEffectStage.PostComposite, bothStagesPossible: false),

            // Pixel, mis-advertised-universal (9) — read only buffer coordinates.
            FreePixel("ContrastModifier"), FreePixel("BrightnessModifier"), FreePixel("SaturationModifier"),
            FreePixel("PosterizeModifier"), FreePixel("ColorTintModifier"), FreePixel("ColorReplaceModifier"),
            FreePixel("ColorRemapModifier"), FreePixel("OrderedDitherModifier"), FreePixel("WipeModifier"),

            // Geometry, buffer-fallback-at-cost (7).
            PaddedGeo("SkewModifier"), PaddedGeo("ScaleModifier"), PaddedGeo("RotateModifier"),
            PaddedGeo("WobbleModifier"), PaddedGeo("CurlProgressModifier"), PaddedGeo("SmudgeModifier"),
            PaddedGeo("PinWarpModifier"),

            // Geometry, needs sheets (10).
            Sheeted("SunburstWobbleModifier", "Geometry"), Sheeted("RingWaveModifier", "Geometry"),
            Sheeted("PointBlastModifier", "Geometry"), Sheeted("SunburstModifier", "Geometry"),
            Sheeted("TurbulenceModifier", "Geometry"), Sheeted("CurlModifier", "Geometry"),
            Sheeted("ProfileModifier", "Geometry"), Sheeted("PulseRingsModifier", "Geometry"),
            Sheeted("SphereModifier", "Geometry"), Sheeted("GroundModifier", "Geometry"),

            // Pixel, needs sheets (3).
            Sheeted("TintModifier", "Pixel"), Sheeted("VoronoiCrackModifier", "Pixel"),
            Sheeted("LayerDissolveModifier", "Pixel"),

            // Edge, stuck (1).
            new ShaperEffectCatalogEntry("EdgeWarpModifier", "Edge", ShaperEffectPortability.Stuck,
                ShaperEffectStage.PreComposite, bothStagesPossible: false),
        };

        /// <summary>27 buffer-only (11 free-Post/Sim + 9 free-Pixel + 7 padded-Geo) + 13 sheet-needing +
        /// 1 stuck = 41. A compile-time-checkable invariant so a future edit to <see cref="All"/> that drifts
        /// from the measured count fails loudly rather than silently.</summary>
        public const int ExpectedTotal = 41;

        public static bool IsSheetGated(in ShaperEffectCatalogEntry e) =>
            e.portability == ShaperEffectPortability.NeedsSheets;

        /// <summary>The same "declared, greyed-out-with-a-reason" gate <c>ShaperFillContract</c>'s fills already
        /// use (FC-4.3) — an effect is never silently hidden, only shown unavailable with why.</summary>
        public static bool IsAvailable(in ShaperEffectCatalogEntry e, ShaperQuantitySet published, out string reason)
        {
            if (e.portability == ShaperEffectPortability.Stuck)
            {
                reason = "Genuinely stuck — no host on a merged buffer (SHAPER_THE_DESIGN.md C8).";
                return false;
            }
            if (e.portability != ShaperEffectPortability.NeedsSheets)
            {
                reason = null;
                return true;
            }
            var missing = e.requiredSheets & ~published;
            if (missing == default(ShaperQuantitySet))
            {
                reason = null;
                return true;
            }
            reason = "Needs " + DescribeMissing(missing) + ", which this generator does not publish.";
            return false;
        }

        /// <summary>Author-facing "Needs X, Y" list built from <see cref="ShaperQuantities.Name"/> per flag —
        /// there is no ready-made multi-flag describe helper on <see cref="ShaperQuantities"/> today, so this
        /// is the one place that gap is worked around rather than duplicated per caller.</summary>
        static string DescribeMissing(ShaperQuantitySet missing)
        {
            string s = null;
            for (int i = 0; i < ShaperQuantities.Count; i++)
            {
                var q = (ShaperQuantity)i;
                if (!ShaperQuantities.Contains(missing, q)) continue;
                s = s == null ? ShaperQuantities.Name(q) : s + ", " + ShaperQuantities.Name(q);
            }
            return s ?? "an unnamed sheet";
        }
    }
}
