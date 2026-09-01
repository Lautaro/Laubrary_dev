// ShaperMockEffects — the mock's own tiny effect catalog for the Effect list (design doc §F). NOT
// ShaperEffectCatalog / any real SpriteFx modifier type: a handful of mock effect classes, enough to prove
// the pattern (one ordered list, a stage badge, portability-grouped add menu with greyed-with-reason
// entries, ZuiReflect drawing each effect's own fields with zero per-type code) without needing real
// 41-entry parity (T-0131 scope explicitly doesn't ask for that).
//
// Mirrors PyreModifier's own shape closely enough that ZuiReflect.BuildFields (Pyre's own generic
// reflection drawer, `PyreWindow.Modifiers.cs`) works on these fields unmodified: floats/ints get a
// [Range] for their slider bounds, bool becomes a toggle, Color becomes a colour field. `enabled` is
// skipped by the drawer's own Options.Skip the same way a Pyre modifier's is (the header owns that toggle).
using System.Linq;
using UnityEngine;

namespace ShaperMock.Editor
{
    [System.Serializable]
    public abstract class ShaperMockEffect
    {
        public bool enabled = true;

        public abstract string DisplayName { get; }
        public abstract ShaperMockEffectStage Stage { get; }
        public abstract ShaperMockEffectBucket Bucket { get; }

        /// True when this effect needs a published "sheet" (edge-distance, heat map, etc.) the current
        /// node's source doesn't publish — greyed-with-reason in the add-menu (§F2), never hidden.
        public virtual bool NeedsSheetsUnmet(bool sourcePublishesSheets) => Bucket == ShaperMockEffectBucket.NeedsSheets && !sourcePublishesSheets;
        public virtual string UnmetReason => "needs edge distance, which this generator does not publish";
    }

    [System.Serializable]
    public sealed class MockBloomEffect : ShaperMockEffect
    {
        // T-0141 — verified against the real BloomModifier (Runtime/SpriteFx/SpriteFxModifiers.cs:1811):
        // threshold/intensity are ZUIValue there; radius is explicitly commented "stays a plain int — a
        // perf knob (blur cost scales with it), not a creative dial" — kept plain here too, on purpose.
        [Range(0f, 2f)] public ZUIValue threshold = new ZUIValue(0.8f);
        [Range(0f, 3f)] public ZUIValue intensity = new ZUIValue(1f);
        [Range(0f, 16f)] public float radius = 4f;
        public override string DisplayName => "Bloom";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferPadded;
    }

    [System.Serializable]
    public sealed class MockOutlineEffect : ShaperMockEffect
    {
        // T-0141 — real OutlineModifier (SpriteFxModifiers.cs:1911): size (→ width here) and innerSoftness
        // (→ softness here) are both ZUIValue.
        [Range(0f, 8f)] public ZUIValue width = new ZUIValue(1f);
        [Range(0f, 1f)] public ZUIValue softness = new ZUIValue(0f);
        public override string DisplayName => "Outline";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferFree;
    }

    [System.Serializable]
    public sealed class MockTintEffect : ShaperMockEffect
    {
        // T-0141 — real TintModifier (SpriteFxModifiers.cs:710): crossAmount is ZUIValue.
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(0.5f);
        public override string DisplayName => "Edge Tint";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Pre;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.NeedsSheets;
    }

    [System.Serializable]
    public sealed class MockPosterizeEffect : ShaperMockEffect
    {
        // T-0141 — real PosterizeModifier (SpriteFxModifiers.cs:893): levelsValue is ZUIValue, tagged
        // [ZUIWholeNumber] so the control snaps to whole steps (a discrete band count) while still
        // animating smoothly through them, exactly like the real one.
        [Range(2f, 16f)] [ZUIWholeNumber] public ZUIValue levels = new ZUIValue(6f);
        public override string DisplayName => "Posterize";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Pre;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferFree;
    }

    [System.Serializable]
    public sealed class MockChromaticAberrationEffect : ShaperMockEffect
    {
        // T-0141 — real ChromaticAberrationModifier (SpriteFxModifiers.cs:2126): amount is ZUIValue.
        [Range(0f, 8f)] public ZUIValue shift = new ZUIValue(2f);
        public override string DisplayName => "Chromatic Aberration";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferPadded;
    }

    [System.Serializable]
    public sealed class MockDropShadowEffect : ShaperMockEffect
    {
        // T-0141 — real DropShadowModifier (SpriteFxModifiers.cs:3410): offsetX/offsetY/opacity are all ZUIValue.
        [Range(-8f, 8f)] public ZUIValue offsetX = new ZUIValue(2f);
        [Range(-8f, 8f)] public ZUIValue offsetY = new ZUIValue(-2f);
        [Range(0f, 1f)] public ZUIValue opacity = new ZUIValue(0.6f);
        public override string DisplayName => "Drop Shadow";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.NeedsSheets;
        public override string UnmetReason => "needs a heat map, which this generator does not publish";
    }

    [System.Serializable]
    public sealed class MockNoiseEffect : ShaperMockEffect
    {
        // T-0141 — no real "Noise" SpriteFx modifier exists to check against (judgment call, not a sourced
        // fact): both are continuous creative dials, matching every OTHER real modifier's own posture
        // (Bloom/Outline/Tint/Posterize/ChromaticAberration/DropShadow all make their creative dials
        // ZUIValue), so both go ZUIValue for consistency.
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(0.3f);
        [Range(1f, 50f)] public ZUIValue scale = new ZUIValue(12f);
        public override string DisplayName => "Noise";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Pre;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferFree;
    }

    /// The one "stuck" entry (§F2) — currently hosted nowhere in the project. It deliberately does NOT
    /// appear in ShaperMockEffectCatalog.Addable — the absence rule forbids listing a control that can
    /// never be turned on (nowhere to attach it), rather than greying it.
    [System.Serializable]
    public sealed class MockGlitchEffect : ShaperMockEffect
    {
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(0.5f);
        public override string DisplayName => "Glitch";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.Stuck;
    }

    /// A parametrised stand-in used to top the catalog up to the REAL engine's measured distribution
    /// (T-0114: bufferFree=20, bufferPadded=7, needsSheets=13, stuck=1 — 41 total) without hand-authoring
    /// 34 more one-off classes. T-0135: the mock's catalog was only 7 entries, which never exercised the
    /// add-menu's search/scroll behaviour at anything like real scale — a flat list of 7 always fit on
    /// screen, so the "does this scale" question was untestable until the catalog was actually this big.
    [System.Serializable]
    public sealed class MockGenericEffect : ShaperMockEffect
    {
        // T-0141 — consistent with every hand-authored effect's own creative dial(s) going ZUIValue.
        [Range(0f, 1f)] public ZUIValue amount = new ZUIValue(0.5f);
        readonly string _name;
        readonly ShaperMockEffectStage _stage;
        readonly ShaperMockEffectBucket _bucket;
        readonly string _unmetReason;

        public MockGenericEffect(string name, ShaperMockEffectStage stage, ShaperMockEffectBucket bucket,
            string unmetReason = null)
        {
            _name = name;
            _stage = stage;
            _bucket = bucket;
            _unmetReason = unmetReason ?? "needs a published quantity this generator does not publish";
        }

        public override string DisplayName => _name;
        public override ShaperMockEffectStage Stage => _stage;
        public override ShaperMockEffectBucket Bucket => _bucket;
        public override string UnmetReason => _unmetReason;
    }

    public static class ShaperMockEffectCatalog
    {
        // Plausible names for the top-up entries, grouped by which bucket they land in — real-sounding so a
        // search-box test ("type 'blur', see it filter") behaves like it will against the real 41.
        static readonly string[] MoreFree =
        {
            "Sharpen", "Vignette", "Pixelate", "Invert", "Desaturate", "Levels", "Gamma Curve", "Threshold",
            "Emboss", "Solarize", "Contrast Punch", "Colour Balance", "Hue Shift", "Dither", "Scanlines",
            "Old Film Grain", "Mirror X",
        };
        static readonly string[] MorePadded = { "Motion Blur", "Gaussian Blur", "Radial Blur", "Glow", "Halo Bloom" };
        static readonly string[] MoreNeedsSheets =
        {
            "Rim Light Overlay", "Heat Shimmer", "Fresnel Tint", "Depth Fog", "Normal-Based Shading",
            "Curvature AO", "Height Contour Lines", "Distance Field Glow", "Soot Streaks", "Frost Edge", "Char Mask",
        };

        /// Every effect offerable from the "+ Add effect" menu — MockGlitchEffect is deliberately excluded
        /// (it's the "stuck" one, §F2: nowhere to host it, so it isn't a legitimate choice to even see).
        /// Sized to match T-0114's real measured catalog exactly: 7 hand-authored + 30 generic top-ups =
        /// 20 buffer-free, 7 buffer-padded, 13 needs-sheets (40 addable; +1 stuck = 41 total).
        public static readonly System.Func<ShaperMockEffect>[] Addable = BuildAddable();

        static System.Func<ShaperMockEffect>[] BuildAddable()
        {
            var list = new System.Collections.Generic.List<System.Func<ShaperMockEffect>>
            {
                () => new MockBloomEffect(),               // BufferPadded
                () => new MockOutlineEffect(),              // BufferFree
                () => new MockTintEffect(),                 // NeedsSheets
                () => new MockPosterizeEffect(),            // BufferFree
                () => new MockChromaticAberrationEffect(),  // BufferPadded
                () => new MockDropShadowEffect(),           // NeedsSheets
                () => new MockNoiseEffect(),                // BufferFree
            };
            foreach (var n in MoreFree)
            {
                var name = n;
                list.Add(() => new MockGenericEffect(name, ShaperMockEffectStage.Pre, ShaperMockEffectBucket.BufferFree));
            }
            foreach (var n in MorePadded)
            {
                var name = n;
                list.Add(() => new MockGenericEffect(name, ShaperMockEffectStage.Post, ShaperMockEffectBucket.BufferPadded));
            }
            foreach (var n in MoreNeedsSheets)
            {
                var name = n;
                list.Add(() => new MockGenericEffect(name, ShaperMockEffectStage.Pre, ShaperMockEffectBucket.NeedsSheets,
                    "needs " + name.ToLowerInvariant() + " data, which this generator does not publish"));
            }

            // Grouped by bucket (stable sort — items keep their relative order within a bucket) before
            // returning: the 7 hand-authored effects above interleave buckets in their declaration order
            // (Padded, Free, NeedsSheets, Free, Padded, NeedsSheets, Free), and ShowAddEffectMenu opens a
            // NEW Section header every time the bucket changes as it walks this list in order — left
            // ungrouped, that produced six tiny one-item section headers before the list ever settled into
            // its real per-bucket runs, exactly the "fragmented menu" a real 40-entry catalog would never
            // ship with. Caught live via a screenshot at real scale, T-0136.
            return list.OrderBy(f => (int)f().Bucket).ToArray();
        }
    }
}
