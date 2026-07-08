using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lazor
{
    /// <summary>
    /// Renders resolved Lazor strokes into a <see cref="Texture2D"/> with anti-aliased, glow-capable lines.
    /// Used for the browser/library thumbnails (and available as a sprite fallback for platforms without the
    /// Shapes plugin). It reads the same <see cref="LazorGeometry.Resolve"/> output the runtime draws, so a
    /// thumbnail is a faithful miniature of the real shape. Pure — no editor dependencies.
    /// </summary>
    public static class LazorRasterizer
    {
        /// <summary>Render a whole shape to a square texture, fitting its content with a small margin.</summary>
        public static Texture2D Render(LazorShape shape, int size, Color background)
        {
            return Render(LazorGeometry.Resolve(shape), size, background);
        }

        public static Texture2D Render(List<ResolvedPolyline> resolved, int size, Color background)
        {
            size = Mathf.Max(8, size);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = background;

            // Fit the content bounds into the texture with a 12% margin, so every thumbnail fills the frame.
            Rect b = LazorGeometry.Bounds(resolved);
            float span = Mathf.Max(b.width, b.height);
            if (span < 1e-4f) span = 1f;
            float fitScale = size * 0.76f / span;                 // pixels per normalized unit
            Vector2 center = b.center;
            Vector2 mid = new Vector2(size * 0.5f, size * 0.5f);

            foreach (var poly in resolved)
            {
                if (poly.points == null || poly.points.Length < 2) continue;
                float halfW = Mathf.Max(0.6f, poly.thickness * fitScale * 0.5f);
                int count = poly.points.Length;
                int last = poly.closed ? count : count - 1;
                for (int i = 0; i < last; i++)
                {
                    Vector2 a = ToPixel(poly.points[i], center, fitScale, mid, size);
                    Vector2 c = ToPixel(poly.points[(i + 1) % count], center, fitScale, mid, size);
                    DrawSegment(px, size, a, c, halfW, poly.color, poly.blend);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Vector2 ToPixel(Vector2 norm, Vector2 center, float fitScale, Vector2 mid, int size)
        {
            float x = mid.x + (norm.x - center.x) * fitScale;
            float y = mid.y + (norm.y - center.y) * fitScale;
            return new Vector2(x, size - y);   // texture y grows downward
        }

        /// <summary>Additive/alpha-blend an AA capsule (thick segment) into the pixel buffer.</summary>
        static void DrawSegment(Color[] px, int size, Vector2 a, Vector2 b, float halfW, Color color, LazorBlend blend)
        {
            float pad = halfW + 1.5f;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - pad));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + pad));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - pad));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + pad));

            Vector2 ab = b - a;
            float abLenSq = Mathf.Max(1e-5f, ab.sqrMagnitude);

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSq);
                float dist = Vector2.Distance(p, a + ab * t);
                float cov = Mathf.Clamp01(halfW + 0.5f - dist);   // 1px AA falloff
                if (cov <= 0f) continue;
                Blend(px, y * size + x, color, cov * color.a, blend);
            }
        }

        static void Blend(Color[] px, int idx, Color src, float coverage, LazorBlend blend)
        {
            Color dst = px[idx];
            switch (blend)
            {
                case LazorBlend.Additive:
                case LazorBlend.Screen:
                case LazorBlend.ColorDodge:
                    dst.r += src.r * coverage;
                    dst.g += src.g * coverage;
                    dst.b += src.b * coverage;
                    dst.a = Mathf.Max(dst.a, coverage);
                    break;
                default: // Transparent / Opaque
                    dst.r = Mathf.Lerp(dst.r, src.r, coverage);
                    dst.g = Mathf.Lerp(dst.g, src.g, coverage);
                    dst.b = Mathf.Lerp(dst.b, src.b, coverage);
                    dst.a = Mathf.Max(dst.a, coverage);
                    break;
            }
            px[idx] = dst;
        }
    }
}
