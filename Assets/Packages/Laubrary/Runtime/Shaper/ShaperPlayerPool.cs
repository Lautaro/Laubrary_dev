using Laubrary.Pooling;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0150 -- one shared pool of <see cref="ShaperPlayer"/> holders for the whole session. Pooling is about
    /// the component SHAPE (a <see cref="SpriteRenderer"/> plus a <see cref="ShaperPlayer"/>), never about any
    /// particular <see cref="ShaperClip"/>, so every Shaper effect in the game draws from this same pool and
    /// is simply reconfigured with a different clip per use. Mirrors <c>PyreBlastPool</c> exactly, so the two
    /// are interchangeable at the call site.
    ///
    /// Built on the shared <see cref="ComponentPool{T}"/> (<c>Runtime/Pooling/ComponentPool.cs</c>) rather
    /// than a hand-rolled pool -- that primitive already exists precisely so Pyre/Chunks/Combat2D and now
    /// Shaper do not each write their own.
    /// </summary>
    public static class ShaperPlayerPool
    {
        static ComponentPool<ShaperPlayer> _pool;

        static ComponentPool<ShaperPlayer> Pool => _pool ??= new ComponentPool<ShaperPlayer>(
            "~Pool: ShaperPlayer",
            factory: () =>
            {
                var go = new GameObject("ShaperPlayer (pooled)");
                var p = go.AddComponent<ShaperPlayer>();   // RequireComponent adds the SpriteRenderer
                p.pooled = true;
                p.playOnAwake = false;
                return p;
            });

        /// <summary>Get a ready-to-configure, currently-idle <see cref="ShaperPlayer"/>. The caller sets
        /// <c>.clip</c>/<c>.fps</c>/<c>.loop</c> and calls <c>.Play()</c>. Subscribe to
        /// <see cref="ShaperPlayer.Finished"/> once per use and call <see cref="Release"/> from it.</summary>
        public static ShaperPlayer Get() => Pool.Get();

        public static void Release(ShaperPlayer instance) => Pool.Release(instance);
    }
}
