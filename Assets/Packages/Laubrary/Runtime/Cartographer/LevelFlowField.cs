using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// A cheap room-scale flow field for tile levels: ONE 4-way BFS flood from a target cell over a
    /// LevelInstance's walkable cells, shared by N followers — "from any cell, which neighbouring cell
    /// is one step closer to the target?" Followers ask TryGetNextWaypoint each frame and fall back to
    /// a direct chase when it says no; their own physics sliding covers everything finer than a cell.
    ///
    /// Deliberately dumb — basic navigation, NOT a pathfinding framework: one field, one target, no
    /// per-agent paths, costs or smoothing. Intended consumers are anything that walks a tile level
    /// toward a point of interest — a game's own mover, a Zoe brain, a future Daemon behavior — all
    /// reading the same shared field the same way.
    ///
    /// Walkability = in-bounds cells where NO solid layer resolves a tile — the same resolve pattern as
    /// LevelProcgen.Reachable — resolved ONCE at Bind and cached. Call RefreshWalkability() (or its
    /// alias Rebind()) whenever the level's content changes underneath the field, e.g. after a procgen
    /// pass. The field rebuilds immediately when the target changes cell, otherwise on a short interval
    /// knob; a room is a few hundred cells, so a rebuild is trivial.
    [AddComponentMenu("")]
    public class LevelFlowField : MonoBehaviour
    {
        [Tooltip("Seconds between rebuilds while the target stays in the same cell. A target cell change rebuilds immediately.")]
        [Min(0.05f)] public float rebuildInterval = 0.3f;

        LevelInstance level;
        RectInt bounds;
        readonly HashSet<Vector2Int> blocked = new();
        readonly Dictionary<Vector2Int, Vector2Int> next = new();   // cell -> the neighbour one step closer to the target
        Vector2Int target;
        bool hasTarget;
        Vector2Int builtFrom;
        bool hasField;
        float timer;

        static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// Bind to the level whose cells the field floods. Resolves walkability immediately.
        public void Bind(LevelInstance instance)
        {
            level = instance;
            RefreshWalkability();
        }

        /// Re-resolve which cells are blocked from the bound level's CURRENT data. Solid geometry is
        /// resolved once and cached, so this must be called whenever the level changes under the field
        /// (a procgen pass, a live asset edit + rebuild). Invalidates the built field; the next Update
        /// rebuilds it from the current target.
        public void RefreshWalkability()
        {
            blocked.Clear();
            next.Clear();
            hasField = false;
            if (level == null || level.level == null) return;

            bounds = level.level.bounds;
            foreach (var layer in level.level.layers)
            {
                if (layer == null || !layer.solid) continue;
                foreach (var cell in level.level.ResolveLayer(layer).Keys) blocked.Add(cell);
            }
        }

        /// RefreshWalkability under its other natural name — "the level was rebuilt, take it in again".
        public void Rebind() => RefreshWalkability();

        /// Point the field at a world position — the everyday call; feed it the thing being converged on.
        public void SetTarget(Vector3 world)
        {
            if (level != null) SetTarget(level.WorldToCell(world));
        }

        /// Point the field at a cell. A cell CHANGE rebuilds the field immediately (this frame's
        /// Update); a target staying in its cell only refreshes on the interval.
        public void SetTarget(Vector2Int cell)
        {
            hasTarget = true;
            target = cell;
        }

        /// In-bounds and no solid layer resolves a tile there — exactly the cells the flood walks.
        public bool IsWalkable(Vector2Int cell) => bounds.Contains(cell) && !blocked.Contains(cell);

        /// True when the built field reaches `world` — something standing there can walk to the target
        /// (or already stands on the target's own cell). False with no field yet, off the grid, or in a
        /// sealed pocket.
        public bool HasPath(Vector3 world)
        {
            if (!hasField || level == null) return false;
            var cell = level.WorldToCell(world);
            return cell == builtFrom || next.ContainsKey(cell);
        }

        void Update()
        {
            if (level == null || !hasTarget) return;
            timer -= Time.deltaTime;
            if (hasField && target == builtFrom && timer > 0f) return;
            timer = rebuildInterval;
            Rebuild(target);
        }

        void Rebuild(Vector2Int from)
        {
            next.Clear();
            hasField = false;
            builtFrom = from;
            // A target somehow inside a wall (or off the grid) means no field — followers fall back to
            // their direct chase.
            if (!bounds.Contains(from) || blocked.Contains(from)) return;

            var frontier = new Queue<Vector2Int>();
            frontier.Enqueue(from);
            next[from] = from;
            while (frontier.Count > 0)
            {
                var c = frontier.Dequeue();
                foreach (var s in Steps)
                {
                    var n = c + s;
                    if (!bounds.Contains(n) || blocked.Contains(n) || next.ContainsKey(n)) continue;
                    next[n] = c;   // the cell we flooded OUT of is one step closer to the target
                    frontier.Enqueue(n);
                }
            }
            hasField = true;
        }

        /// Where something at `world` should head next: the centre of the neighbouring cell one step
        /// closer to the target. False when the field cannot help — no field yet, off the grid, an
        /// unreachable pocket, or already standing on the target's own cell (a direct chase reads
        /// better than snapping to a cell centre at arm's length).
        public bool TryGetNextWaypoint(Vector3 world, out Vector3 waypoint)
        {
            waypoint = default;
            if (!hasField || level == null) return false;
            var cell = level.WorldToCell(world);
            if (cell == builtFrom) return false;
            if (!next.TryGetValue(cell, out var step)) return false;
            waypoint = level.CellToWorld(step);
            return true;
        }
    }
}
