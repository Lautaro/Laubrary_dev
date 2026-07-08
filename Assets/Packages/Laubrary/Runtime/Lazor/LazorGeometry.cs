using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lazor
{
    /// <summary>One stroke ready to draw: normalized points plus the style resolved from its layer.</summary>
    public struct ResolvedPolyline
    {
        public Vector2[] points;   // in normalized shape space (roughly -0.5..0.5), centered on the origin
        public bool closed;
        public Color color;
        public float thickness;    // fraction of normalized size
        public LazorCap cap;
        public LazorJoin join;
        public LazorBlend blend;
        public LazorFacing facing;
    }

    /// <summary>
    /// The single shared, deterministic core of Lazor: turns a <see cref="LazorShape"/> into the flat list of
    /// strokes to draw, applying each layer's mirror/symmetry and normalizing points by the design grid. The
    /// editor preview, the thumbnail rasterizer, and the runtime renderer all go through this, so what you draw
    /// is exactly what gets rasterized and what the game shows. Mirror is expanded here, never baked into the
    /// stored points, so layers remain individually editable at runtime.
    /// </summary>
    public static class LazorGeometry
    {
        // Shared, deterministic geometry core — see LazorRasterizer and the project-side LazorView.

        /// <summary>Resolve the whole shape (all enabled layers, back to front) into draw-ready strokes.</summary>
        public static List<ResolvedPolyline> Resolve(LazorShape shape)
        {
            var result = new List<ResolvedPolyline>();
            if (shape == null || shape.layers == null) return result;
            float inv = 1f / Mathf.Max(1, shape.gridResolution);

            foreach (var layer in shape.layers)
            {
                if (layer == null || !layer.enabled || layer.paths == null) continue;
                ResolveLayer(layer, inv, result);
            }
            return result;
        }

        /// <summary>Resolve a single layer into strokes — useful for runtime code that manipulates one layer.</summary>
        public static void ResolveLayer(LazorLayer layer, float inv, List<ResolvedPolyline> into)
        {
            if (layer == null || !layer.enabled || layer.paths == null) return;

            // Build the set of copy transforms for this layer's symmetry (identity-only when disabled).
            var copies = SymmetryTransforms(layer);

            foreach (var path in layer.paths)
            {
                if (path == null || path.points == null || path.points.Count < 2) continue;

                foreach (var xf in copies)
                {
                    var pts = new Vector2[path.points.Count];
                    for (int i = 0; i < pts.Length; i++)
                        pts[i] = xf.Apply(path.points[i]) * inv;

                    into.Add(new ResolvedPolyline
                    {
                        points = pts,
                        closed = path.closed,
                        color = layer.color,
                        thickness = layer.thickness,
                        cap = layer.cap,
                        join = layer.join,
                        blend = layer.blend,
                        facing = layer.facing,
                    });
                }
            }
        }

        // ---- Symmetry ----

        /// <summary>A 2D similarity used for a symmetry copy: an optional Y-flip, a rotation, all about the origin.</summary>
        public struct SymXform
        {
            public bool flipY;    // reflect across the pattern's base axis before rotating
            public float baseDeg; // pattern-frame alignment (the layer's symmetry angle)
            public float rotDeg;  // this copy's rotation within the pattern

            public Vector2 Apply(Vector2 p)
            {
                // Move into the pattern frame, optionally reflect, rotate to this section, move back out.
                Vector2 u = LazorGeometry.Rotate(p, -baseDeg);
                if (flipY) u.y = -u.y;
                u = LazorGeometry.Rotate(u, rotDeg);
                return LazorGeometry.Rotate(u, baseDeg);
            }
        }

        /// <summary>
        /// The copies that realize a layer's symmetry. Disabled → one identity copy. Enabled → N sections; with
        /// reflect on, odd sections are mirrored (kaleidoscope), else every section is a pure rotation (pinwheel).
        /// </summary>
        public static List<SymXform> SymmetryTransforms(LazorLayer layer)
        {
            var list = new List<SymXform>();
            if (layer == null || !layer.symmetryEnabled)
            {
                list.Add(new SymXform { flipY = false, baseDeg = 0f, rotDeg = 0f });
                return list;
            }

            int n = Mathf.Clamp(layer.symmetryCount, 2, 8);
            float step = 360f / n;
            for (int k = 0; k < n; k++)
            {
                list.Add(new SymXform
                {
                    flipY = layer.symmetryReflect && (k % 2 == 1),
                    baseDeg = layer.symmetryAngle,
                    rotDeg = k * step,
                });
            }
            return list;
        }

        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        /// <summary>The axis-aligned bounds of all resolved strokes, in normalized space (empty → zero rect).</summary>
        public static Rect Bounds(List<ResolvedPolyline> resolved)
        {
            bool any = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var poly in resolved)
            {
                if (poly.points == null) continue;
                foreach (var p in poly.points)
                {
                    if (!any) { minX = maxX = p.x; minY = maxY = p.y; any = true; continue; }
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
            }
            return any ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect();
        }
    }
}
