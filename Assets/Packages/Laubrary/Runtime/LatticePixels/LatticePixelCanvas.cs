using System;
using Laubrary.Lattice;
using UnityEngine;

namespace Laubrary.LatticePixels
{
    /// <summary>
    /// The CPU truth of a pixel-painted lattice: a world-aligned grid of
    /// texels at a fixed pixels-per-unit. <see cref="Road"/> marks which texels
    /// are road (rasterised once from the graph); <see cref="Reveal"/> marks
    /// which texels a brush has painted. An edge counts as covered the moment
    /// every texel on its centreline is painted. Plain C#, no textures: the
    /// view uploads these arrays, tests read them directly.
    /// </summary>
    public sealed class LatticePixelCanvas
    {
        public readonly LatticeGraph Graph;
        public readonly int PixelsPerUnit;
        public readonly int RoadWidthPx;
        public readonly int Width, Height;

        /// <summary>Lattice-plane coordinates of the canvas's lower-left corner.</summary>
        public readonly float MinU, MinV;

        /// <summary>1 where a texel is road. Row 0 is the bottom (Unity texture order).</summary>
        public readonly byte[] Road;

        /// <summary>1 where a texel has been painted.</summary>
        public readonly byte[] Reveal;

        /// <summary>Every texel of the edge's centreline is painted.</summary>
        public event Action<LatticeEdge> EdgeCovered;

        /// <summary>A road texel was painted for the first time: (px, py).</summary>
        public event Action<int, int> RoadPixelRevealed;

        public LatticePixelCanvas(LatticeGraph graph, int pixelsPerUnit, int roadWidthPx, int marginCells = 1)
        {
            Graph = graph;
            PixelsPerUnit = pixelsPerUnit;
            RoadWidthPx = Mathf.Max(1, roadWidthPx);

            var (u0, v0) = graph.PlaneCoords(graph.CellWorld(0, 0));
            float margin = marginCells * graph.CellSize;
            MinU = u0 - margin;
            MinV = v0 - margin;
            Width = Mathf.CeilToInt(((graph.Width - 1) * graph.CellSize + 2f * margin) * pixelsPerUnit);
            Height = Mathf.CeilToInt(((graph.Height - 1) * graph.CellSize + 2f * margin) * pixelsPerUnit);
            Road = new byte[Width * Height];
            Reveal = new byte[Width * Height];
        }

        /// <summary>World-plane size of the canvas.</summary>
        public float SizeU => (float)Width / PixelsPerUnit;
        public float SizeV => (float)Height / PixelsPerUnit;

        // ─── Mapping ───────────────────────────────────────────────────────

        public (int px, int py) ToTexel(Vector3 world)
        {
            var (u, v) = Graph.PlaneCoords(world);
            return (Mathf.FloorToInt((u - MinU) * PixelsPerUnit), Mathf.FloorToInt((v - MinV) * PixelsPerUnit));
        }

        /// <summary>World position of a texel's centre.</summary>
        public Vector3 TexelWorld(int px, int py)
            => Graph.Origin + Graph.PlaneVector(MinU + (px + 0.5f) / PixelsPerUnit, MinV + (py + 0.5f) / PixelsPerUnit);

        public bool InCanvas(int px, int py) => px >= 0 && px < Width && py >= 0 && py < Height;

        public bool IsRoad(int px, int py) => InCanvas(px, py) && Road[py * Width + px] != 0;
        public bool IsRevealed(int px, int py) => InCanvas(px, py) && Reveal[py * Width + px] != 0;

        // ─── Baking ────────────────────────────────────────────────────────

        /// <summary>Rasterises every edge into <see cref="Road"/> as a straight band RoadWidthPx wide.</summary>
        public void Bake()
        {
            Array.Clear(Road, 0, Road.Length);
            int half = RoadWidthPx / 2;
            foreach (var e in Graph.Edges)
            {
                var (ax, ay) = ToTexel(Graph.CellWorld(e.AX, e.AY));
                var (bx, by) = ToTexel(Graph.CellWorld(e.BX, e.BY));
                int x0 = Mathf.Min(ax, bx), x1 = Mathf.Max(ax, bx);
                int y0 = Mathf.Min(ay, by), y1 = Mathf.Max(ay, by);
                if (e.IsHorizontal) { y0 -= half; y1 = y0 + RoadWidthPx - 1; }
                else { x0 -= half; x1 = x0 + RoadWidthPx - 1; }
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (InCanvas(x, y)) Road[y * Width + x] = 1;
            }
        }

        public void ClearReveal()
        {
            Array.Clear(Reveal, 0, Reveal.Length);
            foreach (var e in Graph.Edges) e.Covered = false;
        }

        // ─── Painting ──────────────────────────────────────────────────────

        /// <summary>
        /// Paints a brush (alpha silhouette, row 0 at the bottom, pivot at its
        /// centre) at a world position. Texels with alpha >= 128 are painted.
        /// Returns the dirty rect in texels (empty if nothing changed).
        /// </summary>
        public RectInt Stamp(Vector3 world, byte[] brushAlpha, int brushWidth, int brushHeight)
        {
            var (cx, cy) = ToTexel(world);
            int x0 = cx - brushWidth / 2, y0 = cy - brushHeight / 2;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int by = 0; by < brushHeight; by++)
            for (int bx = 0; bx < brushWidth; bx++)
            {
                if (brushAlpha[by * brushWidth + bx] < 128) continue;
                int x = x0 + bx, y = y0 + by;
                if (!InCanvas(x, y)) continue;
                int i = y * Width + x;
                if (Reveal[i] != 0) continue;
                Reveal[i] = 1;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (Road[i] != 0) RoadPixelRevealed?.Invoke(x, y);
            }
            return minX == int.MaxValue ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// Stamps the brush repeatedly along the path from one world position
        /// to another so a fast frame cannot leave gaps, then checks nearby
        /// edges for completion. Returns the union dirty rect.
        /// </summary>
        public RectInt StampSweep(Vector3 from, Vector3 to, byte[] brushAlpha, int brushWidth, int brushHeight)
        {
            var (fx, fy) = ToTexel(from);
            var (tx, ty) = ToTexel(to);
            float distPx = Mathf.Max(Mathf.Abs(tx - fx), Mathf.Abs(ty - fy));
            int stepPx = Mathf.Max(1, Mathf.Min(brushWidth, brushHeight) / 2);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distPx / stepPx));

            RectInt dirty = new RectInt(0, 0, 0, 0);
            for (int s = 1; s <= steps; s++)
            {
                var r = Stamp(Vector3.Lerp(from, to, (float)s / steps), brushAlpha, brushWidth, brushHeight);
                dirty = Union(dirty, r);
            }
            CheckEdgesNear(to);
            return dirty;
        }

        /// <summary>Marks any not-yet-covered edge around a world position whose centreline is fully painted.</summary>
        public void CheckEdgesNear(Vector3 world)
        {
            var (cx, cy) = Graph.NearestCell(world);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!Graph.InBounds(x, y)) continue;
                Check(Graph.EdgeFrom(x, y, LatticeGraph.East));
                Check(Graph.EdgeFrom(x, y, LatticeGraph.North));
            }
        }

        private void Check(LatticeEdge e)
        {
            if (e == null || e.Covered || !IsCentrelineRevealed(e)) return;
            e.Covered = true;
            EdgeCovered?.Invoke(e);
        }

        public bool IsCentrelineRevealed(LatticeEdge e)
        {
            var (ax, ay) = ToTexel(Graph.CellWorld(e.AX, e.AY));
            var (bx, by) = ToTexel(Graph.CellWorld(e.BX, e.BY));
            int x0 = Mathf.Min(ax, bx), x1 = Mathf.Max(ax, bx);
            int y0 = Mathf.Min(ay, by), y1 = Mathf.Max(ay, by);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!IsRevealed(x, y)) return false;
            return true;
        }

        public static RectInt Union(RectInt a, RectInt b)
        {
            if (a.width == 0 || a.height == 0) return b;
            if (b.width == 0 || b.height == 0) return a;
            int minX = Mathf.Min(a.xMin, b.xMin), minY = Mathf.Min(a.yMin, b.yMin);
            int maxX = Mathf.Max(a.xMax, b.xMax), maxY = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>A filled disc brush of the given radius in texels (diameter 2r+1).</summary>
        public static byte[] DiscBrush(int radiusPx, out int size)
        {
            size = radiusPx * 2 + 1;
            var a = new byte[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - radiusPx, dy = y - radiusPx;
                if (dx * dx + dy * dy <= radiusPx * radiusPx + 0.25f) a[y * size + x] = 255;
            }
            return a;
        }
    }
}
