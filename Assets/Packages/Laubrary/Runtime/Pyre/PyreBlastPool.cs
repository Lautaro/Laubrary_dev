using Laubrary.Pooling;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// <summary>
    /// One shared pool of <see cref="BlastPlayer"/> holders for the whole session — pooling is about the
    /// component SHAPE (a SpriteRenderer + BlastPlayer), not any particular <see cref="BlastSpec"/>, so every
    /// blast effect in the game draws from this same pool and just gets reconfigured with a different spec
    /// per use.
    /// </summary>
    public static class PyreBlastPool
    {
        static ComponentPool<BlastPlayer> _pool;

        static ComponentPool<BlastPlayer> Pool => _pool ??= new ComponentPool<BlastPlayer>(
            "~Pool: BlastPlayer",
            factory: () =>
            {
                var go = new GameObject("BlastPlayer (pooled)");
                var bp = go.AddComponent<BlastPlayer>();   // RequireComponent adds the SpriteRenderer
                bp.pooled = true;
                bp.playOnAwake = false;
                return bp;
            });

        /// Get a ready-to-configure, currently-idle BlastPlayer. Caller sets `.spec`/`.fps`/`.loop` and calls
        /// `.Play()`. Subscribe to `Finished` once per use and call `Release` from it to return it.
        public static BlastPlayer Get() => Pool.Get();

        public static void Release(BlastPlayer instance) => Pool.Release(instance);
    }
}
