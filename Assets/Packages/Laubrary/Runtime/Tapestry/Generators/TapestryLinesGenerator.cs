// TapestryLinesGenerator — a "circuit trace" line generator: self-avoiding walkers on a wrapped grid, only
// ever turning in 45° increments, each stroke given an optional bevel (a highlight on one side, a shadow on
// the other, like an engraved or raised line). Two modes: Draw paints the walkers' own colour as a normal
// layer; Etch reads everything composited BELOW this layer (TapestryGenCtx.compositeSoFar) and darkens/
// lightens it along the path instead — a mask that acts on the earlier layers, rather than adding new
// colour of its own. Always tileable: a step that crosses the grid's edge wraps, and every segment (including
// ones that wrap) is stamped at all 9 toroidal offsets so it reads correctly on both sides of the seam.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public enum TapestryLineMode { Draw, Etch }

    [TapestryGeneratorInfo("Lines", "Shape")]
    [System.Serializable]
    public class TapestryLinesGenerator : TapestryGenerator
    {
        [Range(1, 16)] public int walkerCount = 6;
        [Range(4, 80)] public int maxSteps = 24;
        [Range(2, 32)] public int gridSize = 16;
        [Range(0f, 1f)] public float turnChance = 0.35f;
        [Range(0f, 1f)] public float turn90Chance = 0.25f;
        [Range(1, 6)] public int lineWidthPx = 2;
        public TapestryLineMode mode = TapestryLineMode.Draw;
        public Color lineColor = new Color(0.75f, 0.88f, 1f, 1f);
        [Range(0f, 1f)] public float bevelStrength = 0.6f;
        [Range(0f, 1f)] public float etchAmount = 0.4f;
        public int seed = 0;

        public override string DisplayName => "Lines";
        public override string Description =>
            "Self-avoiding walkers that only turn in 45° steps, drawn as bevelled traces — Draw paints them "
            + "as their own colour, Etch darkens/lightens the layers below instead, as a mask.";

        static readonly Vector2Int[] Dirs8 =
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        };

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            int grid = Mathf.Max(2, gridSize);
            var rng = new System.Random(seed + ctx.seed);

            var coverage = new float[W * H];
            var normalBuf = new Vector2[W * H];

            for (int w = 0; w < walkerCount; w++)
            {
                var path = WalkOne(rng, grid, maxSteps);
                for (int i = 0; i < path.Count - 1; i++)
                    StampSegment(path[i], path[i + 1], grid, W, H, lineWidthPx, coverage, normalBuf);
            }

            Vector2 lightDir = new Vector2(0.7f, 0.7f);
            for (int i = 0; i < target.Length; i++)
            {
                float cov = coverage[i];
                if (cov <= 0f) { target[i] = new Color32(0, 0, 0, 0); continue; }
                float lit = Vector2.Dot(normalBuf[i], lightDir) * bevelStrength;

                if (mode == TapestryLineMode.Etch)
                {
                    if (ctx.compositeSoFar == null) { target[i] = new Color32(0, 0, 0, 0); continue; }
                    Color below = ctx.compositeSoFar[i];
                    float amt = etchAmount * cov;
                    Color etched = lit >= 0f ? Color.Lerp(below, Color.white, lit * amt) : Color.Lerp(below, Color.black, -lit * amt);
                    etched.a = below.a;
                    target[i] = etched;
                }
                else
                {
                    Color painted = lit >= 0f ? Color.Lerp(lineColor, Color.white, lit * 0.6f) : Color.Lerp(lineColor, Color.black, -lit * 0.6f);
                    painted.a = lineColor.a * cov;
                    target[i] = painted;
                }
            }
        }

        List<Vector2Int> WalkOne(System.Random rng, int grid, int steps)
        {
            var visited = new HashSet<(int, int)>();
            var path = new List<Vector2Int>();
            int cx = rng.Next(grid), cy = rng.Next(grid);
            int dir = rng.Next(8);
            path.Add(new Vector2Int(cx, cy));
            visited.Add((cx, cy));

            for (int step = 0; step < steps; step++)
            {
                int nextDir = dir;
                if (rng.NextDouble() < turnChance)
                {
                    int turnAmount = rng.NextDouble() < turn90Chance ? 2 : 1;
                    int sign = rng.NextDouble() < 0.5 ? 1 : -1;
                    nextDir = ((dir + sign * turnAmount) % 8 + 8) % 8;
                }

                bool moved = false;
                for (int tryOffset = 0; tryOffset < 8 && !moved; tryOffset++)
                {
                    int d = (nextDir + tryOffset) % 8;
                    var delta = Dirs8[d];
                    int nx = ((cx + delta.x) % grid + grid) % grid;
                    int ny = ((cy + delta.y) % grid + grid) % grid;
                    if (visited.Contains((nx, ny))) continue;
                    cx = nx; cy = ny; dir = d;
                    path.Add(new Vector2Int(cx, cy));
                    visited.Add((cx, cy));
                    moved = true;
                }
                if (!moved) break;   // fully boxed in by its own trail — stop this walker early
            }
            return path;
        }

        static void StampSegment(Vector2Int a, Vector2Int b, int grid, int W, int H, int lineWidthPxV,
            float[] coverage, Vector2[] normalBuf)
        {
            // `b` is always exactly one of `a`'s 8 wrapped neighbours (by construction of WalkOne) — find the
            // SIGNED delta that actually connects them (which may cross a wrap), so the segment is drawn
            // short and local instead of as a long diagonal across the whole canvas whenever a step crossed
            // the grid's edge.
            int dx = b.x - a.x, dy = b.y - a.y;
            if (dx > grid / 2) dx -= grid; else if (dx < -grid / 2) dx += grid;
            if (dy > grid / 2) dy -= grid; else if (dy < -grid / 2) dy += grid;

            Vector2 pa = new Vector2((a.x + 0.5f) / grid, (a.y + 0.5f) / grid);
            Vector2 pb = new Vector2((a.x + dx + 0.5f) / grid, (a.y + dy + 0.5f) / grid);

            // Stamp the segment AND its 8 tile-shifted copies — whichever land within (or near) the canvas
            // is what makes a segment that crosses the seam appear correctly on BOTH sides of it.
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                StampSegmentLocal(pa + new Vector2(ox, oy), pb + new Vector2(ox, oy), W, H, lineWidthPxV, coverage, normalBuf);
        }

        static void StampSegmentLocal(Vector2 pa, Vector2 pb, int W, int H, int lineWidthPxV,
            float[] coverage, Vector2[] normalBuf)
        {
            float widthUV = lineWidthPxV / (float)Mathf.Max(W, H);
            float pad = widthUV * 2.5f;
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pa.x, pb.x) - pad) * W));
            int maxX = Mathf.Min(W - 1, Mathf.CeilToInt((Mathf.Max(pa.x, pb.x) + pad) * W));
            int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(pa.y, pb.y) - pad) * H));
            int maxY = Mathf.Min(H - 1, Mathf.CeilToInt((Mathf.Max(pa.y, pb.y) + pad) * H));
            if (minX > maxX || minY > maxY) return;   // this tile-shifted copy is nowhere near the canvas

            Vector2 ab = pb - pa;
            float abLenSq = Mathf.Max(1e-8f, ab.sqrMagnitude);
            Vector2 perp = new Vector2(-ab.y, ab.x).normalized;

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / W, (y + 0.5f) / H);
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / abLenSq);
                Vector2 closest = pa + ab * t;
                float dist = Vector2.Distance(p, closest);
                float cov = Mathf.Clamp01(1f - (dist - widthUV * 0.5f) / (widthUV * 0.5f + 0.001f));
                if (cov <= 0f) continue;
                int idx = y * W + x;
                if (cov > coverage[idx])
                {
                    coverage[idx] = cov;
                    float side = Vector2.Dot(p - closest, perp);
                    normalBuf[idx] = side >= 0f ? perp : -perp;
                }
            }
        }
    }
}
