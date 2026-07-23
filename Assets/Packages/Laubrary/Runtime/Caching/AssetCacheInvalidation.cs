using System;
using UnityEngine;

namespace Laubrary.Caching
{
    /// <summary>
    /// A general "this authored asset just changed — drop any cached data derived from it" signal. Any
    /// runtime system that caches something DERIVED from a ScriptableObject asset (rendered frames, a baked
    /// mesh, whatever) can subscribe to <see cref="Invalidated"/> and drop its own cache entry for that
    /// specific asset instance. An Editor-side bridge (<c>Laubrary.Caching.Editor</c>, in a separate optional
    /// assembly so this Runtime module stays zero-dependency and buildable standalone) watches for real
    /// asset edits and calls <see cref="Invalidate"/> automatically — nothing in an editor TOOL's own UI code
    /// needs to know this system exists.
    ///
    /// This is a FIRST PREFERENCE, not a hard rule: an asset type that doesn't want live invalidation (e.g.
    /// something intentionally snapshot/frozen once baked, or something too expensive to regenerate on every
    /// edit) simply never subscribes. Nothing here forces any particular caching strategy.
    ///
    /// STANDING PROTOCOL for Mirage and anything Mirage previews: Mirage exists specifically so authoring
    /// changes show up WITHOUT a restart -- that's the entire point of it as a tool, not an optional nicety.
    /// Any runtime system that caches data DERIVED from an authored asset (baked frames, a computed mesh,
    /// whatever) and that data can be seen through Mirage MUST subscribe to <see cref="Invalidated"/> and
    /// drop its cache entry for that asset. When adding or refactoring such a cache, watch specifically for
    /// TWO gotchas that both defeat this even when a subscription exists:
    ///   1. Multiple cache LAYERS, only one of which actually participates. Real case: BlastPlayer had both
    ///      a correctly-invalidated STATIC dictionary (keyed by spec instance ID) AND a separate per-INSTANCE
    ///      `frames` field that `??=`-shortcut past ever asking the static cache again once first set. The
    ///      static layer invalidated correctly; Play() on an already-used instance just never asked it. Rule:
    ///      one cache layer per derived value, or make every layer re-derive from the invalidation-aware one.
    ///   2. POOLED/reused components are the highest-risk case for gotcha #1 — a pooled instance (ComponentPool,
    ///      PyreBlastPool, ProjectilePool, ...) gets reconfigured for a DIFFERENT use (possibly a different
    ///      asset entirely) every time it's handed out, so any per-instance "I already computed this" flag is
    ///      almost always wrong the next time. Re-derive from the shared, invalidation-aware cache at the
    ///      START of every new use, never trust instance state left over from a previous one.
    /// </summary>
    public static class AssetCacheInvalidation
    {
        /// Fired whenever an editor tool detects that `asset` was edited. Subscribers should check the
        /// asset's type/identity themselves (e.g. `if (asset is Pyre spec) ClearCache(spec);`) — this is
        /// a single shared bus, not a per-type event.
        public static event Action<UnityEngine.Object> Invalidated;

        /// Call this (typically only from the Editor-side bridge, but anything may call it) to signal that
        /// `asset` changed and any derived cache for it should be dropped.
        public static void Invalidate(UnityEngine.Object asset)
        {
            if (asset != null) Invalidated?.Invoke(asset);
        }
    }
}
