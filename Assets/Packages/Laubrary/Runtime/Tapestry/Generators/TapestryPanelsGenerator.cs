// TapestryPanelsGenerator — sci-fi metal panelling: a wrapped grid of jittered, mostly-square panels (some
// with rounded corners), each with its own slight colour/brightness variance ("different areas of metal
// looking shapes") and a directional bevel ring at its own edge (a highlight on the lit side, a shadow on
// the dark side). ALWAYS tileable by construction — no toggle — because every per-cell random value is a
// deterministic hash of the cell's own WRAPPED index, so a cell checked from either side of a seam (its
// "home" copy, or a neighbouring copy reached by wrapping across the edge) always agrees with itself.
using UnityEngine;

namespace Laubrary.Tapestry
{
    [TapestryGeneratorInfo("Panels", "Shape")]
    [System.Serializable]
    public class TapestryPanelsGenerator : TapestryGenerator
    {
        [Range(2, 20)] public int gridSize = 6;
        [Range(0f, 0.4f)] public float gap = 0.08f;
        [Range(0f, 0.5f)] public float jitter = 0.15f;
        [Range(0f, 1f)] public float cornerRadius = 0.3f;
        [Range(0f, 1f)] public float roundedChance = 0.4f;
        [Range(0f, 0.3f)] public float bevelWidth = 0.06f;
        public Color baseColor = new Color(0.55f, 0.57f, 0.6f, 1f);
        [Range(0f, 0.5f)] public float tintVariance = 0.12f;
        public Color highlightColor = new Color(0.92f, 0.94f, 0.97f, 1f);
        public Color shadowColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        [Range(0f, 360f)] public float lightAngle = 135f;
        public Color seamColor = new Color(0.05f, 0.05f, 0.06f, 1f);
        public int seed = 0;

        public override string DisplayName => "Panels";
        public override string Description =>
            "A wrapped grid of jittered metal panels — mostly square, sometimes rounded, each with its own "
            + "colour variance and a directional edge bevel. Always seamless, no matter the settings.";

        public override void Generate(in TapestryGenCtx ctx, Color32[] target)
        {
            int W = ctx.width, H = ctx.height;
            int grid = Mathf.Max(2, gridSize);
            Vector2 lightDir = new Vector2(Mathf.Cos(lightAngle * Mathf.Deg2Rad), Mathf.Sin(lightAngle * Mathf.Deg2Rad));

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float gx = (x + 0.5f) / W * grid;
                    float gy = (y + 0.5f) / H * grid;
                    int baseCx = Mathf.FloorToInt(gx), baseCy = Mathf.FloorToInt(gy);

                    float bestDist = float.MaxValue;
                    Vector2 bestLocal = Vector2.zero, bestHalf = Vector2.one;
                    float bestRadius = 0f, bestTint = 1f;

                    // Check this pixel's own cell AND its 8 neighbours (each WRAPPED mod grid) — a panel's
                    // jitter/bevel can bleed slightly into an adjacent cell, and near a canvas edge the
                    // "neighbour" IS the wrapped cell on the opposite side. This is what makes the seam
                    // between the last and first cell read identically to every other seam.
                    for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = baseCx + ox, cy = baseCy + oy;
                        int wcx = Wrap(cx, grid), wcy = Wrap(cy, grid);
                        float h1 = HashCell(wcx, wcy, seed + ctx.seed, 1);
                        float h2 = HashCell(wcx, wcy, seed + ctx.seed, 2);
                        float h3 = HashCell(wcx, wcy, seed + ctx.seed, 3);
                        float h4 = HashCell(wcx, wcy, seed + ctx.seed, 4);

                        float halfBase = (1f - gap) * 0.5f;
                        Vector2 half = Vector2.one * Mathf.Max(0.05f, halfBase * (1f - jitter * h1 * 0.6f));
                        Vector2 offset = new Vector2((h2 - 0.5f) * jitter, (h3 - 0.5f) * jitter);
                        float radius = h4 < roundedChance ? cornerRadius * Mathf.Min(half.x, half.y) : 0f;

                        Vector2 cellCenter = new Vector2(cx + 0.5f, cy + 0.5f) + offset;
                        Vector2 pLocal = new Vector2(gx, gy) - cellCenter;
                        float d = TapestrySdf.RoundBox(pLocal, half, radius);

                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestLocal = pLocal;
                            bestHalf = half;
                            bestRadius = radius;
                            bestTint = 1f + (h1 - 0.5f) * 2f * tintVariance;
                        }
                    }

                    float distUV = bestDist / grid;
                    Color result;
                    if (distUV >= 0f)
                    {
                        result = seamColor;
                    }
                    else
                    {
                        Color panelColor = new Color(baseColor.r * bestTint, baseColor.g * bestTint,
                            baseColor.b * bestTint, baseColor.a);
                        float bevelT = Mathf.Clamp01(-distUV / Mathf.Max(0.0001f, bevelWidth));
                        if (bevelT < 1f)
                        {
                            Vector2 half = bestHalf; float radius = bestRadius;
                            Vector2 grad = TapestrySdf.Gradient(p => TapestrySdf.RoundBox(p, half, radius), bestLocal);
                            float lit = Vector2.Dot(grad, lightDir) * (1f - bevelT);
                            result = lit >= 0f
                                ? Color.Lerp(panelColor, highlightColor, lit)
                                : Color.Lerp(panelColor, shadowColor, -lit);
                        }
                        else result = panelColor;
                    }
                    target[y * W + x] = result;
                }
            }
        }

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        // Deterministic per-cell pseudo-random value keyed by (cx, cy, seed, salt) — NOT a sequentially-
        // advancing RNG, which would give a wrapped neighbour a DIFFERENT value than its "home" cell and
        // break tiling. Any reference to cell (cx,cy) reproduces the exact same hash, from any direction.
        static float HashCell(int cx, int cy, int seed, int salt)
        {
            unchecked
            {
                int h = cx * 374761393 + cy * 668265263 + seed * 1103515245 + salt * 2032854233;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }
}
