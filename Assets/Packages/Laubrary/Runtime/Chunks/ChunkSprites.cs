using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The procedural fallback used when a <see cref="ChunkSpec"/> ships no sprites: a handful of tiny white pixel
    /// shapes (1–4 px squares and little shards) built once per pixels-per-unit and cached, tinted per-chunk via
    /// SpriteRenderer.color — exactly the "explosion from a single white pixel" trick in Larder's WareDebris and
    /// the demo sprites.
    ///
    /// ⚠ The cache is keyed BY PPU, and that is load-bearing, not a micro-optimisation (T-0391). It used to be one
    /// flat cache built at whatever ppu was asked for FIRST and never rebuilt, which silently pinned every shard in
    /// the domain to that first value. <see cref="Count"/> made that worse: it primed the cache with a hardcoded 32
    /// whenever nothing had built it yet, and since PaletteSplash.Fire (and DebrisScatter) read
    /// `Get(rng.Next(Count), ppu)` — where C# evaluates `Count` BEFORE the call — every cold domain built the shards
    /// at 32 and then ignored the ppu actually passed in. That made a splash's particle size (`sizePx / ppu`, since
    /// Fire scales by `targetPx / nativeShapePx`) come out at 32 ppu no matter what the recipe asked for, which is
    /// precisely the half-size bug T-0391 set out to fix — the capability-side fix alone would have been a runtime
    /// no-op while the editor preview (which divides by ppu directly) showed the corrected size, so preview and game
    /// would have disagreed. Count therefore no longer builds anything, and a new ppu gets its own set.
    public static class ChunkSprites
    {
        // The shard shapes, as descriptors rather than built sprites, so Count can answer without building a set at
        // some invented ppu and the two can never drift apart.
        enum ShapeKind { Rect, DiagonalShard }
        readonly struct ShapeDef
        {
            public readonly ShapeKind kind; public readonly int w, h;
            public ShapeDef(ShapeKind kind, int w, int h) { this.kind = kind; this.w = w; this.h = h; }
        }

        static readonly ShapeDef[] Shapes =
        {
            new ShapeDef(ShapeKind.Rect, 1, 1),           // single pixel
            new ShapeDef(ShapeKind.Rect, 2, 2),           // 2×2 bit
            new ShapeDef(ShapeKind.Rect, 3, 3),           // 3×3 bit
            new ShapeDef(ShapeKind.Rect, 2, 3),           // tall shard
            new ShapeDef(ShapeKind.Rect, 3, 2),           // wide shard
            new ShapeDef(ShapeKind.DiagonalShard, 3, 3),  // little triangle-ish sliver
        };

        static readonly Dictionary<float, Sprite[]> Caches = new Dictionary<float, Sprite[]>();

        /// A random shard from the set for this ppu (built on first use at that ppu).
        public static Sprite Random(float ppu)
        {
            var set = Ensure(ppu);
            return set[UnityEngine.Random.Range(0, set.Length)];
        }

        /// The shard at index i (wrapped), for callers that want a stable pick.
        public static Sprite Get(int i, float ppu)
        {
            var set = Ensure(ppu);
            return set[((i % set.Length) + set.Length) % set.Length];
        }

        /// How many distinct shard shapes exist. Deliberately builds NOTHING — see the type comment: this getter
        /// priming the cache at a hardcoded ppu was half of the T-0391 bug.
        public static int Count => Shapes.Length;

        static Sprite[] Ensure(float ppu)
        {
            ppu = Mathf.Max(1f, ppu);
            // A cached set can come back with destroyed sprites (Sprite.Create output is not marked DontSave, so
            // leaving Play mode can take it with it) — the same guard the single-cache version carried, per set.
            if (Caches.TryGetValue(ppu, out var set) && set != null && set.Length > 0 && set[0] != null) return set;

            set = new Sprite[Shapes.Length];
            for (int i = 0; i < Shapes.Length; i++)
            {
                var d = Shapes[i];
                set[i] = d.kind == ShapeKind.DiagonalShard
                    ? MakeDiagonalShard(d.w, ppu)
                    : MakeRect(d.w, d.h, ppu);
            }
            Caches[ppu] = set;
            return set;
        }

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
