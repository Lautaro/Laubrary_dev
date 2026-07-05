using UnityEngine;

namespace Laubrary.Chunks
{
    /// The procedural fallback used when a <see cref="ChunkSpec"/> ships no sprites: a handful of tiny white pixel
    /// shapes (1–4 px squares and little shards) built once and cached, tinted per-chunk via SpriteRenderer.color —
    /// exactly the "explosion from a single white pixel" trick in Larder's WareDebris and the demo sprites. Because
    /// the emitter scales each chunk to a world size, pixels-per-unit only affects the sprite's native footprint;
    /// the shapes are cached once at the first requested ppu.
    public static class ChunkSprites
    {
        static Sprite[] cache;
        static float cachedPpu;

        /// A random shard from the cached set (built at the given ppu on first use).
        public static Sprite Random(float ppu)
        {
            Ensure(ppu);
            return cache[UnityEngine.Random.Range(0, cache.Length)];
        }

        /// The shard at index i (wrapped), for callers that want a stable pick.
        public static Sprite Get(int i, float ppu)
        {
            Ensure(ppu);
            return cache[((i % cache.Length) + cache.Length) % cache.Length];
        }

        public static int Count { get { Ensure(cachedPpu <= 0f ? 32f : cachedPpu); return cache.Length; } }

        static void Ensure(float ppu)
        {
            if (cache != null && cache.Length > 0 && cache[0] != null) return;
            ppu = Mathf.Max(1f, ppu);
            cachedPpu = ppu;
            cache = new[]
            {
                MakeSquare(1, ppu),          // single pixel
                MakeSquare(2, ppu),          // 2×2 bit
                MakeSquare(3, ppu),          // 3×3 bit
                MakeRect(2, 3, ppu),         // tall shard
                MakeRect(3, 2, ppu),         // wide shard
                MakeDiagonalShard(3, ppu),   // little triangle-ish sliver
            };
        }

        static Sprite MakeSquare(int size, float ppu) => MakeRect(size, size, ppu);

        static Sprite MakeRect(int w, int h, float ppu)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            return Build(w, h, px, ppu, $"ChunkPx_{w}x{h}");
        }

        // A lower-left triangle sliver so not every shard is a perfect block.
        static Sprite MakeDiagonalShard(int size, float ppu)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = (x + y) < size ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            return Build(size, size, px, ppu, $"ChunkShard_{size}");
        }

        static Sprite Build(int w, int h, Color32[] px, float ppu, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
            sprite.name = name;
            return sprite;
        }
    }
}
