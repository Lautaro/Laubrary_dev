using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// The PROCGEN pass: props marked `procgen` mutate the level data from a seed, before the level builds.
    /// Cartographer owns only the lifecycle — clear generated, walk the placements, hand each procgen prop's
    /// behaviours OnProcgen with one shared rng — while the mutation logic itself is game code, the same
    /// line PropBehaviour and the tag system draw. Deterministic: same level, same seed, same result.
    public static class LevelProcgen
    {
        /// Run the pass on `level`: ClearGenerated() first (a re-run replaces the previous run, never
        /// stacks on it), then every placement whose prop is marked procgen, in placement order. For each,
        /// OnProcgen is called on the prop's prefab components DIRECTLY — no scene object is spawned, which
        /// is why implementations must never touch their transform. Returns how many procgen placements
        /// actually reached a behaviour.
        public static int Run(LevelAsset level, int seed)
        {
            if (level == null) return 0;

            level.ClearGenerated();

            var rng = new System.Random(seed);
            int ran = 0;

            // Snapshot: an OnProcgen may Place new generated content, and mid-pass additions must not
            // join (or invalidate) this run's walk.
            var pass = new List<PropPlacement>(level.placements ?? new List<PropPlacement>());
            foreach (var placement in pass)
            {
                if (placement?.prop == null || !placement.prop.procgen) continue;

                bool reached = false;
                var choices = placement.prop.prefabChoices;
                if (choices == null) continue;
                foreach (var choice in choices)
                {
                    if (choice?.prefab == null) continue;
                    foreach (var b in choice.prefab.GetComponentsInChildren<PropBehaviour>(true))
                    {
                        b.OnProcgen(level, placement, rng, seed);
                        reached = true;
                    }
                }
                if (reached) ran++;
            }
            return ran;
        }

        /// A disposable copy for a runtime room: DontSave, name suffixed so it can never be mistaken for
        /// the original. Runtime rooms scramble the CLONE, never the shared asset — Run on the asset itself
        /// is the EDITOR's move, where the mutation is undoable and deliberate.
        public static LevelAsset CloneForRun(LevelAsset source)
        {
            if (source == null) return null;
            var clone = Object.Instantiate(source);
            clone.hideFlags = HideFlags.DontSave;
            clone.name = source.name + " (run)";
            return clone;
        }

        /// The procgen sanity assert: can `to` be walked to from `from`, 4-way, inside the level's bounds,
        /// with a cell blocked whenever `solidLayerName` resolves ANY tile there? Run it after a pass that
        /// closes doors — a maze that sealed the exit is a bug this catches before a player does.
        public static bool Reachable(LevelAsset level, string solidLayerName, Vector2Int from, Vector2Int to)
        {
            if (level == null) return false;
            var bounds = level.bounds;
            if (!bounds.Contains(from) || !bounds.Contains(to)) return false;

            // Resolved once — flood fill asks per cell, and ResolveCell per ask would rescan the level.
            var layer = level.GetLayer(solidLayerName);
            var solid = layer != null ? level.ResolveLayer(layer) : null;
            bool Blocked(Vector2Int c) => solid != null && solid.ContainsKey(c);

            if (Blocked(from) || Blocked(to)) return false;
            if (from == to) return true;

            var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            var seen = new HashSet<Vector2Int> { from };
            var frontier = new Queue<Vector2Int>();
            frontier.Enqueue(from);
            while (frontier.Count > 0)
            {
                var c = frontier.Dequeue();
                foreach (var s in steps)
                {
                    var n = c + s;
                    if (n == to) return true;
                    if (!bounds.Contains(n) || seen.Contains(n) || Blocked(n)) continue;
                    seen.Add(n);
                    frontier.Enqueue(n);
                }
            }
            return false;
        }
    }
}
