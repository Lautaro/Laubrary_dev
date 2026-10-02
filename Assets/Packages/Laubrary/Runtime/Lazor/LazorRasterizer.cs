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

            bool drewAny = false;
            foreach (var poly in resolved)
            {
                if (poly.points == null || poly.points.Length < 2) continue;
                float halfW = Mathf.Max(0.6f, poly.thickness * fitScale * 0.5f);
                int count = poly.points.Length;
                int last = poly.closed ? count : count - 1;
                if (last > 0) drewAny = true;
                for (int i = 0; i < last; i++)
                {
                    Vector2 a = ToPixel(poly.points[i], center, fitScale, mid, size);
                    Vector2 c = ToPixel(poly.points[(i + 1) % count], center, fitScale, mid, size);
                    DrawSegment(px, size, a, c, halfW, poly.color, poly.blend);
                }
            }

            // A shape that resolves to nothing (every layer disabled, or no strokes authored yet) would otherwise
            // come back as a flat slab of `background` — indistinguishable, in a browser grid, from a thumbnail
            // that failed to render. Mark it instead, so "empty" reads as deliberate rather than broken.
            if (!drewAny) DrawEmptyMarker(px, size);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// The "intentionally empty" marker: a faint inset frame plus one corner-to-corner diagonal, in a low-alpha
        /// tint of the foreground. Deliberately quiet — it should read as an empty slot, not as content.
        static void DrawEmptyMarker(Color[] px, int size)
        {
            var tint = new Color(1f, 1f, 1f, 0.16f);
            int inset = Mathf.Max(1, size / 12);
            int lo = inset, hi = size - 1 - inset;
            if (hi <= lo) return;

            for (int x = lo; x <= hi; x++) { Tint(px, size, x, lo, tint); Tint(px, size, x, hi, tint); }
            for (int y = lo; y <= hi; y++) { Tint(px, size, lo, y, tint); Tint(px, size, hi, y, tint); }
            for (int i = 0; i <= hi - lo; i++) Tint(px, size, lo + i, lo + i, tint);
        }

        static void Tint(Color[] px, int size, int x, int y, Color tint)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            int idx = y * size + x;
            Color dst = px[idx];
            dst.r = Mathf.Lerp(dst.r, tint.r, tint.a);
            dst.g = Mathf.Lerp(dst.g, tint.g, tint.a);
            dst.b = Mathf.Lerp(dst.b, tint.b, tint.a);
            dst.a = Mathf.Max(dst.a, tint.a);
            px[idx] = dst;
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
