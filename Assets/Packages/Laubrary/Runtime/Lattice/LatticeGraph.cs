using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lattice
{
    /// <summary>Which world plane the lattice lies in.</summary>
    public enum LatticePlane
    {
        /// <summary>Cells map to world X/Z (top-down 3D).</summary>
        XZ,
        /// <summary>Cells map to world X/Y (2D).</summary>
        XY,
    }

    /// <summary>
    /// A square lattice of cells with an editable set of edges between
    /// lattice-adjacent cells. Cells are implicit (any (x,y) in bounds);
    /// edges are the data. Also owns the cell-to-world mapping so every
    /// consumer (movement, drawing, visibility) agrees on where things are.
    /// </summary>
    public sealed class LatticeGraph
    {
        public readonly int Width;
        public readonly int Height;

        /// <summary>World distance between adjacent cell centres.</summary>
        public readonly float CellSize;

        /// <summary>World length of one coverage chunk along an edge.</summary>
        public readonly float ChunkWorldSize;

        /// <summary>Chunks per edge (CellSize / ChunkWorldSize, at least 1).</summary>
        public readonly int ChunkCount;

        public readonly LatticePlane Plane;

        /// <summary>World position of the lattice's centre.</summary>
        public Vector3 Origin;

        public const int East = 0, North = 1, West = 2, South = 3;

        /// <summary>Cardinal deltas indexed by direction: East, North, West, South.</summary>
        public static readonly (int dx, int dy)[] Dirs =
        {
            (+1, 0), (0, +1), (-1, 0), (0, -1),
        };

        public static int Opposite(int dir) => (dir + 2) % 4;

        private readonly HashSet<LatticeEdge> _edges = new HashSet<LatticeEdge>();
        private readonly Dictionary<LatticeEdge, LatticeEdge> _canonical = new Dictionary<LatticeEdge, LatticeEdge>();
        private readonly Dictionary<(int x, int y, int dir), LatticeEdge> _byEndpoint
            = new Dictionary<(int, int, int), LatticeEdge>();

        public LatticeGraph(int width, int height, float cellSize,
            float chunkWorldSize = 0.02f, LatticePlane plane = LatticePlane.XZ)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            ChunkWorldSize = chunkWorldSize;
            ChunkCount = Mathf.Max(1, Mathf.RoundToInt(cellSize / chunkWorldSize));
            Plane = plane;
        }

        public IReadOnlyCollection<LatticeEdge> Edges => _edges;

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        // ─── World mapping ─────────────────────────────────────────────────

        /// <summary>A vector lying in the lattice plane with the given in-plane components.</summary>
        public Vector3 PlaneVector(float u, float v)
            => Plane == LatticePlane.XZ ? new Vector3(u, 0f, v) : new Vector3(u, v, 0f);

        /// <summary>The in-plane components of a world position, relative to Origin.</summary>
        public (float u, float v) PlaneCoords(Vector3 world)
        {
            world -= Origin;
            return Plane == LatticePlane.XZ ? (world.x, world.z) : (world.x, world.y);
        }

        /// <summary>World position of a cell centre. The lattice is centred on Origin.</summary>
        public Vector3 CellWorld(int x, int y)
        {
            float u = (x - (Width - 1) * 0.5f) * CellSize;
            float v = (y - (Height - 1) * 0.5f) * CellSize;
            return Origin + PlaneVector(u, v);
        }

        /// <summary>The cell whose centre is closest to a world position, clamped to bounds.</summary>
        public (int x, int y) NearestCell(Vector3 world)
        {
            var (u, v) = PlaneCoords(world);
            int x = Mathf.RoundToInt(u / CellSize + (Width - 1) * 0.5f);
            int y = Mathf.RoundToInt(v / CellSize + (Height - 1) * 0.5f);
            return (Mathf.Clamp(x, 0, Width - 1), Mathf.Clamp(y, 0, Height - 1));
        }

        /// <summary>World position of the centre of chunk i along an edge (index 0 at A).</summary>
        public Vector3 ChunkWorld(LatticeEdge e, int i)
            => Vector3.Lerp(CellWorld(e.AX, e.AY), CellWorld(e.BX, e.BY), (i + 0.5f) / ChunkCount);

        // ─── Edge set ──────────────────────────────────────────────────────

        /// <summary>Adds an edge between two lattice-adjacent cells. False if it already existed.</summary>
        public bool AddEdge(int ax, int ay, int bx, int by)
        {
            var e = new LatticeEdge(ax, ay, bx, by);
            if (!_edges.Add(e)) return false;
            _canonical[e] = e;
            int dir = DirectionIndex(e.AX, e.AY, e.BX, e.BY);
            _byEndpoint[(e.AX, e.AY, dir)] = e;
            _byEndpoint[(e.BX, e.BY, Opposite(dir))] = e;
            return true;
        }

        public bool RemoveEdge(int ax, int ay, int bx, int by)
        {
            var e = new LatticeEdge(ax, ay, bx, by);
            if (!_edges.Remove(e)) return false;
            _canonical.Remove(e);
            int dir = DirectionIndex(e.AX, e.AY, e.BX, e.BY);
            _byEndpoint.Remove((e.AX, e.AY, dir));
            _byEndpoint.Remove((e.BX, e.BY, Opposite(dir)));
            return true;
        }

        public bool RemoveEdge(LatticeEdge e) => RemoveEdge(e.AX, e.AY, e.BX, e.BY);

        /// <summary>The stored edge instance matching these endpoints, if present.</summary>
        public bool TryGetEdge(int ax, int ay, int bx, int by, out LatticeEdge e)
            => _canonical.TryGetValue(new LatticeEdge(ax, ay, bx, by), out e);

        /// <summary>The edge leaving (x,y) in a cardinal direction, or null.</summary>
        public LatticeEdge EdgeFrom(int x, int y, int dir)
        {
            _byEndpoint.TryGetValue((x, y, dir), out var e);
            return e;
        }

        /// <summary>How many edges leave a cell (0..4).</summary>
        public int EdgeCount(int x, int y)
        {
            int n = 0;
            for (int d = 0; d < 4; d++) if (EdgeFrom(x, y, d) != null) n++;
            return n;
        }

        /// <summary>In-bounds lattice neighbours of a cell, whether or not an edge exists.</summary>
        public IEnumerable<(int nx, int ny, int dir)> Neighbours(int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dirs[d].dx, ny = y + Dirs[d].dy;
                if (InBounds(nx, ny)) yield return (nx, ny, d);
            }
        }

        /// <summary>Clears coverage on every edge.</summary>
        public void ResetCoverage()
        {
            foreach (var e in _edges) e.ResetCoverage();
        }

        /// <summary>True if every cell that has at least one edge can reach every other such cell.</summary>
        public bool IsConnected()
        {
            var seen = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            (int, int)? start = null;
            foreach (var e in _edges) { start = (e.AX, e.AY); break; }
            if (start == null) return true;
            stack.Push(start.Value);
            seen.Add(start.Value);
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    if (EdgeFrom(x, y, d) == null) continue;
                    var n = (x + Dirs[d].dx, y + Dirs[d].dy);
                    if (seen.Add(n)) stack.Push(n);
                }
            }
            foreach (var e in _edges)
                if (!seen.Contains((e.AX, e.AY)) || !seen.Contains((e.BX, e.BY))) return false;
            return true;
        }

        private static int DirectionIndex(int ax, int ay, int bx, int by)
        {
            int dx = bx - ax, dy = by - ay;
            if (dx == 1 && dy == 0) return East;
            if (dx == 0 && dy == 1) return North;
            if (dx == -1 && dy == 0) return West;
            if (dx == 0 && dy == -1) return South;
            throw new System.ArgumentException($"Cells ({ax},{ay}) and ({bx},{by}) are not lattice-adjacent.");
        }
    }
}
