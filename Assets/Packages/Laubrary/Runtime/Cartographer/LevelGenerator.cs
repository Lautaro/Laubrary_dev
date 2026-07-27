using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// Builds a level from a recipe and a seed. Deterministic: same recipe + same seed = the same level, every
    /// time, on any machine — the property that lets a world map hand out seeds and get reproducible places
    /// without storing any of them.
    ///
    /// It is a COMPOSER, not a painter: every tile it places goes through `CartographerLevel.PlaceClump`, the
    /// same call the Stamper uses. So a generated level carries the same placement records, publishes the same
    /// named spots, and rebuilds the same way as a hand-built one — there is one runtime representation, which
    /// was the point of the whole design.
    public static class LevelGenerator
    {
        /// Fill `level` from `recipe`, discarding whatever was there. Returns how many clumps were placed.
        public static int Generate(LevelRecipe recipe, int seed, CartographerLevel level)
        {
            if (recipe == null || level == null) return 0;

            foreach (var l in level.layers) l?.tilemap?.ClearAllTiles();
            level.placements.Clear();
            level.ClearSpots();

            var rng = new System.Random(seed);
            int placed = 0;

            // Rooms come straight off the recipe — the generator decides terrain, not pacing.
            level.rooms = new List<CartographerRoom>(recipe.rooms);

            // ── ground: a wandering surface, laid one ground-clump width at a time ──
            var surface = new int[Mathf.Max(1, recipe.width)];
            int height = recipe.groundLevel;
            int step = Mathf.Max(1, FootprintWidth(recipe.groundClump));

            for (int x = 0; x < recipe.width; x += step)
            {
                if (recipe.groundVariation > 0 && rng.NextDouble() < recipe.groundRoughness)
                {
                    height += rng.Next(0, 2) == 0 ? -1 : 1;
                    height = Mathf.Clamp(height,
                        recipe.groundLevel - recipe.groundVariation,
                        recipe.groundLevel + recipe.groundVariation);
                }

                for (int i = x; i < Mathf.Min(x + step, recipe.width); i++) surface[i] = height;

                if (recipe.groundClump != null)
                {
                    level.PlaceClump(recipe.groundClump, new Vector2Int(x, height), Pick(rng));
                    placed++;
                }
            }

            // ── scatter: clumps sprinkled across the level within their height bands ──
            var taken = new List<RectInt>();
            foreach (var entry in recipe.scatter)
            {
                if (entry?.clump == null) continue;

                // The biome is the gate: a clump it does not allow is never placed, whatever the dials say.
                if (recipe.biome != null && !recipe.biome.Allows(entry.clump)) continue;

                int count = Mathf.RoundToInt(recipe.width / 100f * entry.per100Cells);
                for (int i = 0; i < count; i++)
                {
                    int x = rng.Next(0, recipe.width);
                    int band = Mathf.Max(0, entry.maxHeight - entry.minHeight);
                    int y = surface[Mathf.Clamp(x, 0, surface.Length - 1)] + 1
                            + entry.minHeight + (band > 0 ? rng.Next(0, band + 1) : 0);

                    var foot = FootprintAt(entry.clump, new Vector2Int(x, y));
                    var padded = new RectInt(foot.xMin - recipe.scatterSpacing, foot.yMin - recipe.scatterSpacing,
                        foot.width + recipe.scatterSpacing * 2, foot.height + recipe.scatterSpacing * 2);

                    bool clash = false;
                    foreach (var t in taken) if (Overlaps(padded, t)) { clash = true; break; }
                    if (clash) continue;   // a crowded roll is dropped, not retried — retrying skews density

                    level.PlaceClump(entry.clump, new Vector2Int(x, y), Pick(rng));
                    taken.Add(foot);
                    placed++;
                }
            }

            return placed;
        }

        /// A weighted picker bound to this run's RNG, so a clump's prefab alternatives are chosen from the
        /// level seed rather than at random — regenerating the same seed picks the same variants.
        static System.Func<IList<float>, int> Pick(System.Random rng) => weights =>
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += Mathf.Max(0f, weights[i]);
            if (total <= 0f) return -1;

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Mathf.Max(0f, weights[i]);
                if (roll <= 0d) return i;
            }
            return weights.Count - 1;
        };

        static int FootprintWidth(Clump c) => c == null ? 1 : c.CellBounds.width;

        static RectInt FootprintAt(Clump c, Vector2Int origin)
        {
            var b = c.CellBounds;
            return new RectInt(origin.x + b.xMin, origin.y + b.yMin, b.width, b.height);
        }

        static bool Overlaps(RectInt a, RectInt b) =>
            a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;
    }
}
