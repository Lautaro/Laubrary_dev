using Laubrary.Pooling;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>
    /// One shared pool of <see cref="Chunk"/> pieces for the whole session. A burst can need many pieces at
    /// once (a full burst is genuinely concurrent, unlike a single blast), so growth under load is expected —
    /// the point is that a QUIET period reuses pieces from the last burst instead of every burst re-paying
    /// full Instantiate cost.
    /// </summary>
    public static class ChunkPool
    {
        static ComponentPool<Chunk> _pool;

        static ComponentPool<Chunk> Pool => _pool ??= new ComponentPool<Chunk>(
            "~Pool: Chunk",
            factory: () =>
            {
                var go = new GameObject("Chunk (pooled)");
                go.AddComponent<SpriteRenderer>();
                var chunk = go.AddComponent<Chunk>();
                chunk.pooled = true;
                return chunk;
            },
            defaultCapacity: 32,   // a single burst can want a couple dozen pieces at once
            maxSize: 512);

        public static Chunk Get() => Pool.Get();
        public static void Release(Chunk instance) => Pool.Release(instance);
    }
}
