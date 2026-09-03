using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lattice
{
    /// <summary>
    /// Tuning for <see cref="LatticeGen.Bake"/>. Each density is "operations
    /// per cell", so a larger lattice gets proportionally more carving.
    /// </summary>
    public struct LatticeGenSettings
    {
        /// <summary>Void clusters (2-4 deleted edges each) per cell. Forms blocks of varying size.</summary>
        public float VoidClusterDensity;
        /// <summary>5x5 rings per cell. Each replaces a 5x5 area with a ring plus 3-4 connectors.</summary>
        public float RingDensity;
        /// <summary>Long diagonal spines per cell. Each lays a stepped line and prunes half its side streets.</summary>
        public float SpineDensity;

        public static LatticeGenSettings Default => new LatticeGenSettings
        {
            VoidClusterDensity = 0.06f,
            RingDensity = 0.003f,
            SpineDensity = 0.001f,
        };
    }

    /// <summary>
    /// Seeded procedural carving of a <see cref="LatticeGraph"/>. Every
    /// operation is deterministic for a given seed. The modifiers only delete
    /// or add edges; <see cref="EnsureConnected"/> guarantees the result is one
    /// connected component.
    /// </summary>
    public static class LatticeGen
    {
        /// <summary>Full lattice, then the three modifiers, then a connectivity repair.</summary>
        public static void Bake(LatticeGraph g, int seed, LatticeGenSettings settings)
        {
            var rng = new System.Random(seed);
            FullLattice(g);

            int area = g.Width * g.Height;
            int voids = Mathf.RoundToInt(area * settings.VoidClusterDensity);
            int rings = Mathf.Max(settings.RingDensity > 0f ? 1 : 0, Mathf.RoundToInt(area * settings.RingDensity));
            int spines = Mathf.Max(settings.SpineDensity > 0f ? 1 : 0, Mathf.RoundToInt(area * settings.SpineDensity));

            for (int i = 0; i < voids; i++) CarveVoidCluster(g, rng);
            for (int i = 0; i < rings; i++) CarveRing(g, rng);
            for (int i = 0; i < spines; i++) CarveSpine(g, rng);

            EnsureConnected(g);
        }

        public static void Bake(LatticeGraph g, int seed) => Bake(g, seed, LatticeGenSettings.Default);

        /// <summary>Connects every cell to all its in-bounds neighbours.</summary>
        public static void FullLattice(LatticeGraph g)
        {
            for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                if (x + 1 < g.Width) g.AddEdge(x, y, x + 1, y);
                if (y + 1 < g.Height) g.AddEdge(x, y, x, y + 1);
            }
        }

        /// <summary>
        /// Deletes a small cluster of 2-4 edges around a random cell, sometimes
        /// stepping into the void so clusters form L-shapes.
        /// </summary>
        public static void CarveVoidCluster(LatticeGraph g, System.Random rng)
        {
            int x = rng.Next(g.Width);
            int y = rng.Next(g.Height);
            int clusterSize = 2 + rng.Next(3);
            var candidates = new List<int>(4);

            for (int i = 0; i < clusterSize; i++)
            {
                candidates.Clear();
                for (int d = 0; d < 4; d++)
                    if (g.EdgeFrom(x, y, d) != null) candidates.Add(d);
                if (candidates.Count == 0) break;

                int dir = candidates[rng.Next(candidates.Count)];
                var (dx, dy) = LatticeGraph.Dirs[dir];
                g.RemoveEdge(x, y, x + dx, y + dy);
                if (rng.Next(10) < 3) { x += dx; y += dy; }
            }
        }

        /// <summary>
        /// Clears a random 5x5 area and replaces it with a square ring at radius
        /// 2, then punches 3-4 connectors out through the ring's side midpoints.
        /// </summary>
        public static void CarveRing(LatticeGraph g, System.Random rng)
        {
            if (g.Width < 7 || g.Height < 7) return;
            int cx = rng.Next(2, g.Width - 2);
            int cy = rng.Next(2, g.Height - 2);

            for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
                foreach (var (nx, ny, _) in g.Neighbours(x, y))
                    g.RemoveEdge(x, y, nx, ny);

            const int r = 2;
            (int x, int y)[] corners =
            {
                (cx - r, cy - r), (cx + r, cy - r),
                (cx + r, cy + r), (cx - r, cy + r),
            };
            for (int i = 0; i < 4; i++)
                AddPath(g, corners[i], corners[(i + 1) % 4]);

            (int dx, int dy)[] outward = { (0, -1), (+1, 0), (0, +1), (-1, 0) };
            int[] sides = { 0, 1, 2, 3 };
            for (int i = sides.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (sides[i], sides[j]) = (sides[j], sides[i]);
            }
            int connectors = 3 + rng.Next(2);
            for (int i = 0; i < connectors; i++)
            {
                int side = sides[i];
                var a = corners[side];
                var b = corners[(side + 1) % 4];
                int mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
                int ox = mx + outward[side].dx, oy = my + outward[side].dy;
                if (g.InBounds(ox, oy)) g.AddEdge(mx, my, ox, oy);
            }
        }

        /// <summary>
        /// Lays a stepped (Bresenham) line between two far-apart, non-collinear
        /// cells, then deletes 50% of the non-spine edges leaving spine cells.
        /// </summary>
        public static void CarveSpine(LatticeGraph g, System.Random rng)
        {
            int minDist = Mathf.Max(6, Mathf.RoundToInt(Mathf.Min(g.Width, g.Height) * 0.6f));
            int ax, ay, bx, by, tries = 0;
            do
            {
                ax = rng.Next(g.Width); ay = rng.Next(g.Height);
                bx = rng.Next(g.Width); by = rng.Next(g.Height);
                tries++;
            } while (tries < 30
                && (Mathf.Abs(ax - bx) < 3 || Mathf.Abs(ay - by) < 3
                    || Mathf.Max(Mathf.Abs(ax - bx), Mathf.Abs(ay - by)) < minDist));

            var spine = new List<(int x, int y)>(Bresenham(ax, ay, bx, by));
            for (int i = 1; i < spine.Count; i++)
                AddPath(g, spine[i - 1], spine[i]);

            var spineCells = new HashSet<(int, int)>(spine);
            foreach (var (sx, sy) in spineCells)
                foreach (var (nx, ny, _) in g.Neighbours(sx, sy))
                {
                    if (spineCells.Contains((nx, ny))) continue;
                    if (rng.Next(2) == 0) g.RemoveEdge(sx, sy, nx, ny);
                }
        }

        /// <summary>
        /// Union-find over all cells; while more than one component exists,
        /// adds the first lattice-adjacent edge that joins two components.
        /// </summary>
        public static void EnsureConnected(LatticeGraph g)
        {
            int W = g.Width, H = g.Height;
            var parent = new int[W * H];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Idx(int x, int y) => y * W + x;
            int Find(int a)
            {
                while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
                return a;
            }
            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[a] = b;
            }

            foreach (var e in g.Edges) Union(Idx(e.AX, e.AY), Idx(e.BX, e.BY));

            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int y = 0; y < H && !merged; y++)
                for (int x = 0; x < W && !merged; x++)
                {
                    if (x + 1 < W && Find(Idx(x, y)) != Find(Idx(x + 1, y)))
                    {
                        g.AddEdge(x, y, x + 1, y);
                        Union(Idx(x, y), Idx(x + 1, y));
                        merged = true;
                    }
                    else if (y + 1 < H && Find(Idx(x, y)) != Find(Idx(x, y + 1)))
                    {
                        g.AddEdge(x, y, x, y + 1);
                        Union(Idx(x, y), Idx(x, y + 1));
                        merged = true;
                    }
                }
            }
        }

        /// <summary>Adds every unit edge along the axis-aligned line from a to b (a and b share a row or column).</summary>
        private static void AddPath(LatticeGraph g, (int x, int y) a, (int x, int y) b)
        {
            int sx = System.Math.Sign(b.x - a.x), sy = System.Math.Sign(b.y - a.y);
            int x = a.x, y = a.y;
            while (x != b.x || y != b.y)
            {
                int nx = x + sx, ny = y + sy;
                if (g.InBounds(x, y) && g.InBounds(nx, ny)) g.AddEdge(x, y, nx, ny);
                x = nx; y = ny;
            }
        }

        /// <summary>4-connected Bresenham: consecutive points are always lattice-adjacent.</summary>
        private static IEnumerable<(int x, int y)> Bresenham(int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, x = x0, y = y0;
            while (true)
            {
                yield return (x, y);
                if (x == x1 && y == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; yield return (x, y); if (x == x1 && y == y1) yield break; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
