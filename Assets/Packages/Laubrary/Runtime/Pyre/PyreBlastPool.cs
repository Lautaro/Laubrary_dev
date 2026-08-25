using Laubrary.Pooling;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// <summary>
    /// One shared pool of <see cref="PyreBlastPlayer"/> holders for the whole session — pooling is about the
    /// component SHAPE (a SpriteRenderer + PyreBlastPlayer), not any particular <see cref="Pyre"/>,
    /// so every effect in the game draws from this same pool and just gets reconfigured with a different spec
    /// per use. Mirrors Pyre1's own PyreBlastPool exactly.
    /// </summary>
    public static class PyreBlastPool
    {
        static ComponentPool<PyreBlastPlayer> _pool;

        static ComponentPool<PyreBlastPlayer> Pool => _pool ??= new ComponentPool<PyreBlastPlayer>(
            "~Pool: PyreBlastPlayer",
            factory: () =>
            {
                var go = new GameObject("PyreBlastPlayer (pooled)");
                var bp = go.AddComponent<PyreBlastPlayer>();   // RequireComponent adds the SpriteRenderer
                bp.pooled = true;
                bp.playOnAwake = false;
                return bp;
            });

        /// Get a ready-to-configure, currently-idle PyreBlastPlayer. Caller sets `.spec`/`.fps`/`.loop` and
        /// calls `.Play()`. Subscribe to `Finished` once per use and call `Release` from it to return it.
        public static PyreBlastPlayer Get() => Pool.Get();

        public static void Release(PyreBlastPlayer instance) => Pool.Release(instance);
    }
}
