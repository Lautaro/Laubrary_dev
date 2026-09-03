// DotGenRenderer.cs
// Turns an evaluated document into pixels: background, guide grid, drawers, frame, dot markers — in that
// order, which is what lets a foreground row of boxes cover a background one.
//
// GIZMOS ARE NOT HERE, and that is a design decision rather than an oversight. Process overlays belong to the
// editor, so an export can never accidentally contain one: there is no flag to forget, because the renderer
// has no idea gizmos exist.
//
// Everything rasterises per TARGET, not per frame: each shape walks only its own oriented bounding box, so
// adding a thousand small cells costs a thousand small boxes rather than a thousand full-frame passes. Edges
// take four coverage samples per pixel, which is what keeps a rotated diamond from looking like a staircase.

using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    public static class DotGenRenderer
    {
        /// The reference raster the POC's pixel sizes were authored against. Dot radii and glow scale by
        /// size/900 so a document looks the same at any frame size instead of growing spots as it shrinks.
        public const float ReferenceSize = 900f;

        public static Texture2D Render(DotGen doc, DotGenResult res, int size, bool withDots = true)
        {
            size = Mathf.Clamp(size, 8, 4096);
            var buf = new Color32[size * size];
            Render(doc, res, size, buf, withDots);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(buf);
            tex.Apply(false, false);
            return tex;
        }

        /// Render into a caller-owned buffer, so a live preview can re-render every frame without allocating a
        /// texture's worth of garbage each time.
        public static void Render(DotGen doc, DotGenResult res, int size, Color32[] buf, bool withDots = true)
        {
            if (doc == null || buf == null || buf.Length < size * size) return;
            float s = size / ReferenceSize;

            Clear(buf, size, doc.background);

            if (doc.showGuideGrid) DrawGuideGrid(buf, size, doc.guideGrid, s);

            if (res != null)
            {
                var targets = new List<DotDrawTarget>();
                for (int gi = 0; gi < res.order.Count; gi++)
                {
                    var gd = res.order[gi];
                    var g = gd.gen;
                    if (g == null || !g.enabled || g.drawers == null) continue;

                    for (int di = 0; di < g.drawers.Count; di++)
                    {
                        var d = g.drawers[di];
                        if (d == null || !d.enabled) continue;

                        targets.Clear();
                        d.Targets(g, gd, doc.seed, targets);
                        float opacity = Mathf.Clamp01(d.OpacityFraction);
                        if (opacity <= 0f) continue;

                        for (int ti = 0; ti < targets.Count; ti++)
                        {
                            var t = targets[ti];
                            var fill = d.FillFor(t.fillKey, doc.seed);
                            if (fill == null) continue;
                            PaintShape(buf, size, t.area, t.shape, fill, opacity);
                        }
                    }
                }
            }

            if (doc.showFrameStroke) DrawFrameStroke(buf, size, doc.frameStroke, s);

            if (withDots && res != null) DrawDots(buf, size, res, s);
        }

        /// The export bytes. Same path as the preview, so what is exported is what was judged.
        public static byte[] RenderPng(DotGen doc, int size)
        {
            var res = DotGenEvaluator.Evaluate(doc);
            var tex = Render(doc, res, size, withDots: true);
            byte[] png = tex.EncodeToPNG();
            if (Application.isPlaying) Object.Destroy(tex); else Object.DestroyImmediate(tex);
            return png;
        }

        // ── passes ─────────────────────────────────────────────────────────────────────────

        static void Clear(Color32[] buf, int size, Color color)
        {
            Color32 c = color;
            for (int i = 0; i < size * size; i++) buf[i] = c;
        }

        static void DrawGuideGrid(Color32[] buf, int size, Color color, float s)
        {
            Color32 c = color;
            int w = Mathf.Max(1, Mathf.RoundToInt(1f * s));
            for (int i = 1; i < 10; i++)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(i * size / 10f), 0, size - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(i * size / 10f), 0, size - 1);
                FillRect(buf, size, x, 0, w, size, c);
                FillRect(buf, size, 0, y, size, w, c);
            }
        }

        static void DrawFrameStroke(Color32[] buf, int size, Color color, float s)
        {
            Color32 c = color;
            int w = Mathf.Max(1, Mathf.RoundToInt(2f * s));
            FillRect(buf, size, 0, 0, size, w, c);
            FillRect(buf, size, 0, size - w, size, w, c);
            FillRect(buf, size, 0, 0, w, size, c);
            FillRect(buf, size, size - w, 0, w, size, c);
        }

        static void DrawDots(Color32[] buf, int size, DotGenResult res, float s)
        {
            for (int gi = 0; gi < res.order.Count; gi++)
            {
                var gd = res.order[gi];
                var g = gd.gen;
                if (g == null || !g.enabled || !g.showDots) continue;

                float r = Mathf.Max(0.35f, g.dotSize * s);
                float glow = 4f * s;
                Color col = g.color;

                for (int i = 0; i < gd.finalDots.Count; i++)
                {
                    var p = gd.finalDots[i];
                    // Dots outside the frame are skipped, not clipped: a marker half off the edge would claim
                    // a dot is at the border when it is nowhere near it.
                    if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f) continue;
                    PaintDot(buf, size, p.x * size, p.y * size, r, glow, col);
                }
            }
        }

        // ── rasterisation ──────────────────────────────────────────────────────────────────

        static void FillRect(Color32[] buf, int size, int x, int y, int w, int h, Color32 c)
        {
            int x0 = Mathf.Max(0, x), x1 = Mathf.Min(size, x + w);
            int y0 = Mathf.Max(0, y), y1 = Mathf.Min(size, y + h);
            for (int yy = y0; yy < y1; yy++)
            {
                int row = yy * size;
                for (int xx = x0; xx < x1; xx++) buf[row + xx] = c;
            }
        }

        /// One oriented shape, painted with a per-pixel fill. Walks the shape's own bounding box only.
        static void PaintShape(Color32[] buf, int size, in DotArea a, DotShape shape, ZuiFill fill, float opacity)
        {
            float cx = a.cx * size, cy = a.cy * size;
            float w = Mathf.Abs(a.w) * size, h = Mathf.Abs(a.h) * size;
            if (w <= 0f || h <= 0f) return;

            float ca = Mathf.Cos(a.rot), sa = Mathf.Sin(a.rot);
            // Half-extents of the axis-aligned box that contains the rotated shape.
            float ex = (Mathf.Abs(ca) * w + Mathf.Abs(sa) * h) * 0.5f + 1f;
            float ey = (Mathf.Abs(sa) * w + Mathf.Abs(ca) * h) * 0.5f + 1f;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - ex));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(cx + ex));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ey));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(cy + ey));
            if (x1 < x0 || y1 < y0) return;

            // The fill's own domain: the reference lays a gradient across HALF THE BOUNDING DIAGONAL, with the
            // first colour at the negative end of the axis. Feeding ZuiFill a square half-extent of exactly
            // that length reproduces it, whichever fit the fill is set to.
            float half = Mathf.Sqrt(w * w + h * h) * 0.5f;
            if (half <= 0f) half = 1f;
            var halfV = new Vector2(half, half);
            bool spatial = fill.IsSpatial();
            Color flat = spatial ? Color.clear : fill.Evaluate(0f, 0f, 0f);

            float invW = 1f / w, invH = 1f / h;

            for (int py = y0; py <= y1; py++)
            {
                int row = py * size;
                for (int px = x0; px <= x1; px++)
                {
                    float cov = 0f;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float fx = px + 0.25f + sx * 0.5f - cx;
                            float fy = py + 0.25f + sy * 0.5f - cy;
                            float lx = (fx * ca + fy * sa) * invW;
                            float ly = (-fx * sa + fy * ca) * invH;
                            if (DotGenMath.Inside(lx, ly, shape)) cov += 0.25f;
                        }
                    }
                    if (cov <= 0f) continue;

                    Color c;
                    if (spatial)
                    {
                        float fx = px + 0.5f - cx;
                        float fy = py + 0.5f - cy;
                        float rx = fx * ca + fy * sa;
                        float ry = -fx * sa + fy * ca;
                        Vector2 uv = fill.Normalize(new Vector2(rx, ry), Vector2.zero, halfV);
                        c = fill.Evaluate(0f, uv.x, uv.y);
                    }
                    else c = flat;

                    Blend(buf, row + px, c, c.a * cov * opacity);
                }
            }
        }

        /// A dot marker: an anti-aliased disc with the reference's soft same-colour glow around it. The glow
        /// is what makes a field of dots read as light rather than as confetti, so it scales with the frame.
        static void PaintDot(Color32[] buf, int size, float cx, float cy, float r, float glow, Color col)
        {
            float outer = r + Mathf.Max(0f, glow);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - outer - 1f));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(cx + outer + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - outer - 1f));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(cy + outer + 1f));
            if (x1 < x0 || y1 < y0) return;

            // Canvas' shadowBlur n is a Gaussian of standard deviation n/2.
            float sigma = Mathf.Max(0.35f, glow * 0.5f);
            float twoSigmaSq = 2f * sigma * sigma;

            for (int py = y0; py <= y1; py++)
            {
                int row = py * size;
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // Core and glow are combined by MAX rather than stacked: the glow is the disc blurred, so
                    // it is already half strength at the disc's own edge. Adding it instead would ring every
                    // dot with a brighter halo than the dot.
                    float core = Mathf.Clamp01(r - d + 0.5f);
                    float halo = 0.5f * Mathf.Exp(-((d - r) * (d - r)) / twoSigmaSq);
                    float a = Mathf.Max(core, halo) * col.a;
                    if (a <= 0.002f) continue;
                    Blend(buf, row + px, col, a);
                }
            }
        }

        static void Blend(Color32[] buf, int index, Color src, float alpha)
        {
            if (alpha <= 0f) return;
            if (alpha > 1f) alpha = 1f;
            Color32 dst = buf[index];
            float ia = 1f - alpha;
            buf[index] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(src.r * 255f * alpha + dst.r * ia), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(src.g * 255f * alpha + dst.g * ia), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(src.b * 255f * alpha + dst.b * ia), 0, 255),
                255);
        }
    }
}
