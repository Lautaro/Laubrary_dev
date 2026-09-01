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
        [Range(0f, 2f)] public float threshold = 0.8f;
        [Range(0f, 3f)] public float intensity = 1f;
        [Range(0f, 16f)] public float radius = 4f;
        public override string DisplayName => "Bloom";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferPadded;
    }

    [System.Serializable]
    public sealed class MockOutlineEffect : ShaperMockEffect
    {
        [Range(0f, 8f)] public float width = 1f;
        [Range(0f, 1f)] public float softness = 0f;
        public override string DisplayName => "Outline";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferFree;
    }

    [System.Serializable]
    public sealed class MockTintEffect : ShaperMockEffect
    {
        [Range(0f, 1f)] public float amount = 0.5f;
        public override string DisplayName => "Edge Tint";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Pre;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.NeedsSheets;
    }

    [System.Serializable]
    public sealed class MockPosterizeEffect : ShaperMockEffect
    {
        [Range(2, 16)] public int levels = 6;
        public override string DisplayName => "Posterize";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Pre;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferFree;
    }

    [System.Serializable]
    public sealed class MockChromaticAberrationEffect : ShaperMockEffect
    {
        [Range(0f, 8f)] public float shift = 2f;
        public override string DisplayName => "Chromatic Aberration";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.BufferPadded;
    }

    [System.Serializable]
    public sealed class MockDropShadowEffect : ShaperMockEffect
    {
        [Range(-8f, 8f)] public float offsetX = 2f;
        [Range(-8f, 8f)] public float offsetY = -2f;
        [Range(0f, 1f)] public float opacity = 0.6f;
        public override string DisplayName => "Drop Shadow";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.NeedsSheets;
        public override string UnmetReason => "needs a heat map, which this generator does not publish";
    }

    [System.Serializable]
    public sealed class MockNoiseEffect : ShaperMockEffect
    {
        [Range(0f, 1f)] public float amount = 0.3f;
        [Range(1f, 50f)] public float scale = 12f;
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
        [Range(0f, 1f)] public float amount = 0.5f;
        public override string DisplayName => "Glitch";
        public override ShaperMockEffectStage Stage => ShaperMockEffectStage.Post;
        public override ShaperMockEffectBucket Bucket => ShaperMockEffectBucket.Stuck;
    }

    public static class ShaperMockEffectCatalog
    {
        /// Every effect offerable from the "+ Add effect" menu — MockGlitchEffect is deliberately excluded
        /// (it's the "stuck" one, §F2: nowhere to host it, so it isn't a legitimate choice to even see).
        public static readonly System.Func<ShaperMockEffect>[] Addable =
        {
            () => new MockBloomEffect(),
            () => new MockOutlineEffect(),
            () => new MockTintEffect(),
            () => new MockPosterizeEffect(),
            () => new MockChromaticAberrationEffect(),
            () => new MockDropShadowEffect(),
            () => new MockNoiseEffect(),
        };
    }
}
