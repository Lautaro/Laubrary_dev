using Laubrary.Shaper;

namespace Laubrary.PyreShaper
{
    /// <summary>
    /// T-0115 -- the cache-key scheme for the pre/post-composite effect stages T-0114 built
    /// (<see cref="ShaperEffectStage"/>, <see cref="ShaperEffectStageRunner"/>). An effect's output is a pure
    /// function of (a) the picture it runs on and (b) its own authored settings, so its cache key is exactly
    /// those two things folded together, plus WHICH STAGE it ran at -- <see cref="ShaperEffectContract"/>'s own
    /// doc already establishes that Pre and Post give genuinely different output the moment instances overlap,
    /// so the stage is part of the key, never assumed from the effect alone.
    ///
    /// <b>Honest scope limit, matching T-0114's own posture (<c>ShaperEffectStageRunner</c>'s class doc:
    /// "Deliberately standalone... wiring this stage split into the production compile pass is real, separate
    /// surgery this task does not take on").</b> This file provides the KEY SCHEME only -- it does not modify
    /// <see cref="ShaperEffectStageRunner"/> to actually cache anything, because that runner is a proxy proof,
    /// not the production pipeline, and there is no production effect-stage evaluator yet to wire caching into.
    /// A future task that DOES build one calls <see cref="Compute"/> with the real input key and a real effect
    /// state hash; this file is the seam, not the wiring.
    ///
    /// <b>Why the effect's own state hash is a caller-supplied <see cref="ShaperCacheKey"/>, not derived here.</b>
    /// <c>PixelModifier</c> (Laubrary.SpriteFx) has no generic, reflection-free way to enumerate "every field
    /// that changes its output" the way <see cref="ShaperNodeIdentity"/> does for a <see cref="ShaperNode"/> --
    /// its concrete subclasses (posterise, contrast, colour replace, ...) each carry different fields with no
    /// shared contract for hashing them. Requiring each effect to publish its own <see cref="ShaperCacheKey"/>
    /// (the same "declared capability" pattern as <see cref="IShaperCacheableSource"/>) is the honest answer;
    /// reflecting over arbitrary fields here would be fragile and silently miss a field a future effect adds.
    /// </summary>
    public static class ShaperEffectCacheKey
    {
        /// <summary>Combine an input picture's own key, the stage this effect ran at, the effect's type name
        /// (so two different effect types with coincidentally-equal state hashes can never collide), and the
        /// effect's own state hash into one key for the effect's OUTPUT picture.</summary>
        public static ShaperCacheKey Compute(ShaperCacheKey inputKey, ShaperEffectStage stage, string effectTypeName, ShaperCacheKey effectStateHash)
        {
            var m = ShaperCacheMixer.Begin("shaper.effect.stage.v1");
            m.MixKey(inputKey);
            m.MixInt((int)stage);
            m.MixString(effectTypeName);
            m.MixKey(effectStateHash);
            return m.Key;
        }
    }
}
