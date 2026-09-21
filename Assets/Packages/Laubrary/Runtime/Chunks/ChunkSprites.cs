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

        /// One shard, chosen by <paramref name="pick"/> from among ONLY those whose native pixel footprint fits
        /// within <paramref name="targetPx"/> (game pixels) — still a random shape, just drawn from the subset
        /// that does not have to be shrunk to reach the target. Scaling one to size therefore never shrinks it
        /// BELOW its own texel grid: a 3×3 shard squashed to a 1px target renders each of its texels at a THIRD
        /// of a game pixel, which is precisely "sub-pixel" in the reader's own sense — fine, smooth-edged noise
        /// instead of the crisp single/double-pixel specks the target size promised (T-0393). Only ever scales
        /// a shard UP or leaves it 1:1.
        /// Falls back to the smallest shard (always 1×1) when targetPx is below even that — the one case some
        /// downscale is unavoidable, and harmless there since a single texel has no internal grid to subdivide.
        /// ⚠ The fit is measured on a shard's LARGER axis, not its width (fixed in T-0397 — it was width-only,
        /// and that was a live bug, caught by measuring real chunks rather than by reading the code). Both
        /// consumers divide the target by a different reference: PaletteSplash scales by `targetPx/rect.width`,
        /// while a DebrisScatter chunk scales by `worldSize / max(bounds.x, bounds.y)`. A width-only fit let the
        /// non-square shards (2×3 and 3×2) through on their short side while their LONG side set the chunk's
        /// scale — so a 2×3 shard on a 2px target rendered at 1/3 scale, each texel a third of a game pixel.
        /// Fitting on the larger axis satisfies both consumers at once (width ≤ max axis ≤ targetPx), which is
        /// why there is one rule here and not a per-caller mode.
        /// ⚠ Knock-on effect, by design: a size range narrows the shape library. The comparison is a strict fit
        /// with no tolerance, so a recipe authored at 1–3 px never reaches any 3-tall or 3-wide shard (the 3×3,
        /// the 3×2, the 2×3 and the diagonal sliver) — they need targetPx to land on exactly 3, which a
        /// continuous roll never does. Crispness was chosen over shape variety here; widen the size range, or
        /// give this a fit epsilon, if a spray ever needs the bigger shapes back.
        /// RNG-agnostic on purpose (a caller supplies its own random int, e.g. ChunkRng.Next(int.MaxValue)) so
        /// this has no opinion about which generator a capability uses.
        public static Sprite GetFitting(float targetPx, float ppu, int pick)
        {
            int n = Shapes.Length;
            int qualifying = 0;
            for (int i = 0; i < n; i++) if (Footprint(Shapes[i]) <= targetPx) qualifying++;

            if (qualifying == 0)
            {
                int smallest = 0, smallestSpan = int.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    int span = Footprint(Shapes[i]);
                    if (span < smallestSpan) { smallestSpan = span; smallest = i; }
                }
                return Get(smallest, ppu);
            }

            int idx = ((pick % qualifying) + qualifying) % qualifying;
            for (int i = 0; i < n; i++)
            {
                if (Footprint(Shapes[i]) > targetPx) continue;
                if (idx == 0) return Get(i, ppu);
                idx--;
            }
            return Get(0, ppu);   // unreachable — qualifying > 0 guarantees the loop above returns
        }

        /// A shard's native footprint in texels: its LARGER axis. This is the single quantity every fit and
        /// every consumer's scale reference has to agree on — see GetFitting's own warning for what happened
        /// when they disagreed.
        static int Footprint(in ShapeDef d) => d.w > d.h ? d.w : d.h;

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
