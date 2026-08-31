namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- an OPTIONAL capability an <see cref="IShaperCompositeSource"/> may additionally implement to
    /// publish a real content hash of its own authored state, so a <see cref="ShaperNodeKind.Composite"/> node
    /// hosting it can be cached precisely instead of falling back to reference-identity (see
    /// <c>ShaperNodeIdentity.SourceContentHash</c>'s doc for exactly what the fallback gets wrong). Same
    /// "declared capability, never assumed from the base interface alone" posture
    /// <see cref="IShaperSwarmNativeSource"/>/<see cref="IShaperSimulationSource"/> already take (T-0113) --
    /// a source that does not implement this is not broken, it is honestly less-precisely-cacheable.
    /// </summary>
    public interface IShaperCacheableSource
    {
        /// <summary>A deterministic hash of every field that changes this source's rendered output. Two calls
        /// with no authored change in between MUST return the same key; two calls after ANY authored change
        /// MUST NOT (collisions aside) -- the same contract <see cref="ShaperNodeIdentity.OwnHash"/> holds
        /// itself to for a plain node.</summary>
        ShaperCacheKey ContentHash();
    }
}
