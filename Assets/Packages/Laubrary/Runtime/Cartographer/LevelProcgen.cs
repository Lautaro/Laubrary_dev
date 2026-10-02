using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// One mutator as it sits in a level: which placement declares it, and the mutator itself. What
    /// <see cref="LevelProcgen.Mutators"/> hands back so a tool can LIST what a procgen run will do before
    /// anyone presses anything.
    public readonly struct LevelMutator
    {
        public readonly PropPlacement placement;
        public readonly ILevelMutator mutator;

        public LevelMutator(PropPlacement placement, ILevelMutator mutator)
        {
            this.placement = placement;
            this.mutator = mutator;
        }

        public Prop Prop => placement?.prop;

        /// The prop's name and the mutator's own sentence — the line an author reads.
        public string Name => Prop != null
            ? (string.IsNullOrEmpty(Prop.displayName) ? Prop.name : Prop.displayName)
            : "(no prop)";

        public string Description => mutator != null ? mutator.DescribeMutation() : "";
    }

    /// The PROCGEN pass: props marked `procgen` mutate the level data from a seed, before the level builds.
    /// Cartographer owns only the lifecycle — clear generated, walk the placements, hand each declared
    /// ILevelMutator its turn with one shared rng — while the mutation logic itself is game code, the same
    /// line PropBehaviour and the tag system draw. Deterministic: same level, same seed, same result.
    public static class LevelProcgen
    {
        /// Run the pass on `level`: ClearGenerated() first (a re-run replaces the previous run, never
        /// stacks on it), then every placement whose prop is marked procgen, in placement order. Mutation is
        /// invoked on the prop's prefab components DIRECTLY — no scene object is spawned, which is why
        /// implementations must never touch their transform. Returns how many procgen placements actually
        /// reached a mutator.
        ///
        /// A prop marked procgen that declares NO mutator is reported loudly rather than skipped in silence:
        /// the author asked for a pass and would otherwise get nothing, with no way to tell that from a pass
        /// that ran and decided to do nothing. The mirror case — a prefab carrying a mutator on a prop
        /// nobody marked procgen — is reported for the same reason.
        public static int Run(LevelAsset level, int seed)
        {
            if (level == null) return 0;

            level.ClearGenerated();

            var rng = new System.Random(seed);
            int ran = 0;
            var reported = new HashSet<Prop>();   // one complaint per prop, not per placement

            // Snapshot: a Mutate may Place new generated content, and mid-pass additions must not join (or
            // invalidate) this run's walk.
            var pass = new List<PropPlacement>(level.placements ?? new List<PropPlacement>());
            var mutators = new List<ILevelMutator>();
            foreach (var placement in pass)
            {
                var prop = placement?.prop;
                if (prop == null) continue;

                CollectMutators(prop, mutators);

                if (!prop.procgen)
                {
                    if (mutators.Count > 0 && reported.Add(prop))
                        Debug.LogWarning($"[Procgen] '{PropName(prop)}' carries a level mutator " +
                            $"({mutators[0].GetType().Name}) but is NOT marked Procgen, so the pass skips it " +
                            "and the mutation never happens. Tick Procgen on the prop, or take the mutator " +
                            "off its prefab.", level);
                    continue;
                }

                if (mutators.Count == 0)
                {
                    if (reported.Add(prop))
                        Debug.LogWarning($"[Procgen] '{PropName(prop)}' is marked Procgen but declares no " +
                            "mutator, so running the pass changes nothing. A prefab component has to " +
                            "implement ILevelMutator for a procgen prop to do anything at all.", level);
                    continue;
                }

                foreach (var m in mutators) m.Mutate(level, placement, rng, seed);
                ran++;
            }
            return ran;
        }

        /// Every mutator this level will run, in placement order — what a tool lists beside the seed so an
        /// author can see what a run is about to do to their room.
        public static List<LevelMutator> Mutators(LevelAsset level)
        {
            var result = new List<LevelMutator>();
            if (level?.placements == null) return result;

            var mutators = new List<ILevelMutator>();
            foreach (var placement in level.placements)
            {
                if (placement?.prop == null || !placement.prop.procgen) continue;
                CollectMutators(placement.prop, mutators);
                foreach (var m in mutators) result.Add(new LevelMutator(placement, m));
            }
            return result;
        }

        /// The props in this level marked procgen that declare nothing — the silent no-op, named. The same
        /// fact Run warns about, available to a tool that would rather show it than wait for a log line.
        public static List<Prop> SilentProcgenProps(LevelAsset level)
        {
            var result = new List<Prop>();
            if (level?.placements == null) return result;

            var mutators = new List<ILevelMutator>();
            foreach (var placement in level.placements)
            {
                var prop = placement?.prop;
                if (prop == null || !prop.procgen || result.Contains(prop)) continue;
                CollectMutators(prop, mutators);
                if (mutators.Count == 0) result.Add(prop);
            }
            return result;
        }

        /// The mutators a prop declares, into `into` (cleared first). Every prefab CHOICE is searched — a
        /// prop with an exit prefab and an urn prefab may declare its mutator on either — but a mutator TYPE
        /// is taken once, so a component sitting on two choices of the same prop does not run twice.
        static void CollectMutators(Prop prop, List<ILevelMutator> into)
        {
            into.Clear();
            if (prop?.prefabChoices == null) return;

            foreach (var choice in prop.prefabChoices)
            {
                if (choice?.prefab == null) continue;
                foreach (var m in choice.prefab.GetComponentsInChildren<ILevelMutator>(true))
                {
                    if (m == null) continue;
                    bool seen = false;
                    foreach (var already in into) if (already.GetType() == m.GetType()) { seen = true; break; }
                    if (!seen) into.Add(m);
                }
            }
        }

        static string PropName(Prop prop) =>
            prop == null ? "(none)" : string.IsNullOrEmpty(prop.displayName) ? prop.name : prop.displayName;

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

        /// The first layer marked solid, or null. Procgen that seals or opens routes must find the wall
        /// layer by its PROPERTY, never by a hard-coded name — a behaviour that looks for "Walls" fails
        /// silently and unfindably on any level that calls its wall layer something else, which is exactly
        /// how the first version of the aisle mutator managed to do nothing at all for weeks.
        public static LevelLayer SolidLayerOf(LevelAsset level)
        {
            if (level?.layers == null) return null;
            foreach (var l in level.layers) if (l != null && l.solid) return l;
            return null;
        }

        /// Split `cells` into 4-way connected components.
        ///
        /// DETERMINISTIC by construction, which matters more than it looks: the natural source of a cell set
        /// is a Dictionary's keys, whose iteration order is not stable across runs or domain reloads, so an
        /// unsorted flood would hand back the same components in a different ORDER and any rng drawn per
        /// component would then produce a different level from the same seed. Input is sorted, neighbours
        /// are visited in a fixed order, each component is sorted, and the components themselves are sorted
        /// by their minimum cell. Same input set, same output, always.
        public static List<List<Vector2Int>> Components(ICollection<Vector2Int> cells)
        {
            var result = new List<List<Vector2Int>>();
            if (cells == null || cells.Count == 0) return result;

            var remaining = new HashSet<Vector2Int>(cells);
            var ordered = new List<Vector2Int>(cells);
            ordered.Sort(CompareCells);

            var steps = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            foreach (var start in ordered)
            {
                if (!remaining.Remove(start)) continue;

                var group = new List<Vector2Int>();
                var frontier = new Queue<Vector2Int>();
                frontier.Enqueue(start);
                while (frontier.Count > 0)
                {
                    var c = frontier.Dequeue();
                    group.Add(c);
                    foreach (var s in steps)
                    {
                        var n = c + s;
                        if (remaining.Remove(n)) frontier.Enqueue(n);
                    }
                }
                group.Sort(CompareCells);
                result.Add(group);
            }

            result.Sort((a, b) => CompareCells(a[0], b[0]));
            return result;
        }

        /// Row-major cell order: bottom-to-top, then left-to-right. The one comparison every deterministic
        /// walk in this file uses.
        public static int CompareCells(Vector2Int a, Vector2Int b) =>
            a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);

        /// Every in-bounds cell that `solidLayer` does NOT fill — the walkable space, as procgen sees it.
        public static HashSet<Vector2Int> FreeCells(LevelAsset level, LevelLayer solidLayer)
        {
            var free = new HashSet<Vector2Int>();
            if (level == null) return free;
            var solid = solidLayer != null ? level.ResolveLayer(solidLayer) : null;
            foreach (var c in level.bounds.allPositionsWithin)
                if (solid == null || !solid.ContainsKey(c)) free.Add(c);
            return free;
        }

        /// Every in-bounds cell carrying `tag`, on ANY layer — the read side of "structure is declared, not
        /// inferred". A procgen pass that needs to know where the aisles are (or the doorways, or the
        /// no-spawn zones) asks for the cells the author MARKED, instead of trying to deduce the room's
        /// intent from its geometry.
        ///
        /// Goes through LevelAsset.TagsAt, which is the one place a cell's tags are resolved — from its
        /// tile, from the prop that stamped it, and from the LAYER. That last source is what makes a marker
        /// LAYER work: paint cells on a layer whose `tags` carry the tag, and the painting itself is the
        /// declaration. Costs a resolve per cell per layer, so it is a once-per-pass call, not a per-frame
        /// one.
        public static HashSet<Vector2Int> CellsWith(LevelAsset level, TileTag tag)
        {
            var found = new HashSet<Vector2Int>();
            if (level?.layers == null || tag == null) return found;

            foreach (var cell in level.bounds.allPositionsWithin)
                foreach (var layer in level.layers)
                {
                    if (layer == null) continue;
                    if (!level.TagsAt(cell, layer.name).Contains(tag)) continue;
                    found.Add(cell);
                    break;
                }
            return found;
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
