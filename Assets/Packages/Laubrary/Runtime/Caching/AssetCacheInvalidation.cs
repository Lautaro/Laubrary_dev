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
    /// </summary>
    public static class AssetCacheInvalidation
    {
        /// Fired whenever an editor tool detects that `asset` was edited. Subscribers should check the
        /// asset's type/identity themselves (e.g. `if (asset is BlastSpec spec) ClearCache(spec);`) — this is
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
