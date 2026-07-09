// LazorRenderer.cs — the ONE vector renderer for Laubrary. Everything that draws a LazorShape as vector line-art
// (the in-game view AND the Lazor editor preview) goes through here, so what you author is pixel-identical to what
// the game shows. Lives in its own Shapes-guarded module: the Lazor CORE stays Shapes-free, only this renderer
// binds to Shapes. Compiles only when SHAPES_INSTALLED is defined (see the asmdef's defineConstraints).
//
// The correct Shapes recipe, learned the hard way:
//   1. RIGID Draw.Matrix (rotation + translation, NO scale) — a scaled matrix also scales the Meters-space stroke
//      width, coupling thickness to size ("fat blocks"). Bake the object's scale into the POINTS and thickness.
//   2. Thickness in Meters, scaled explicitly by the same `scale` — decoupled from shape size, always proportional.
//   3. ONE PolylinePath per stroke. Shapes' immediate mode holds the path BY REFERENCE and defers the draw to the
//      end of Draw.Command, so a single shared path makes every stroke render the LAST stroke's points.

using System.Collections.Generic;
using Shapes;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.VectorRendering
{
    /// <summary>Stateless core: draws already-resolved strokes via Shapes. Call INSIDE an active Draw.Command.</summary>
    public static class LazorDraw
    {
        /// <param name="resolved">Strokes from <see cref="LazorGeometry.Resolve"/> (normalized ~±0.5 points).</param>
        /// <param name="paths">A reusable per-stroke PolylinePath pool (grown as needed — never share one path).</param>
        /// <param name="matrix">A RIGID world matrix (rotation + translation only, no scale).</param>
        /// <param name="scale">Normalized-unit → world-unit factor (also scales thickness to stay proportional).</param>
        public static void Draw(List<ResolvedPolyline> resolved, List<PolylinePath> paths,
                                Matrix4x4 matrix, float scale, float thicknessScale, Color tint)
        {
            if (resolved == null) return;
            while (paths.Count < resolved.Count) paths.Add(new PolylinePath());

            Shapes.Draw.Matrix = matrix;
            Shapes.Draw.ThicknessSpace = ThicknessSpace.Meters;

            for (int s = 0; s < resolved.Count; s++)
            {
                var poly = resolved[s];
                if (poly.points == null || poly.points.Length < 2) continue;

                Shapes.Draw.BlendMode = ToBlend(poly.blend);
                Shapes.Draw.PolylineGeometry = poly.facing == LazorFacing.Billboard ? PolylineGeometry.Billboard : PolylineGeometry.Flat2D;
                Shapes.Draw.PolylineJoins = ToJoins(poly.join);

                var path = paths[s];
                path.ClearAllPoints();
                foreach (var p in poly.points) path.AddPoint(new Vector3(p.x * scale, p.y * scale, 0f));

                Shapes.Draw.Polyline(path, poly.closed, Mathf.Max(0.0001f, poly.thickness * thicknessScale * scale), poly.color * tint);
            }
        }

        public static ShapesBlendMode ToBlend(LazorBlend b)
        {
            switch (b)
            {
                case LazorBlend.Additive: return ShapesBlendMode.Additive;
                case LazorBlend.Screen: return ShapesBlendMode.Screen;
                case LazorBlend.ColorDodge: return ShapesBlendMode.ColorDodge;
                case LazorBlend.Opaque: return ShapesBlendMode.Opaque;
                default: return ShapesBlendMode.Transparent;
            }
        }

        public static PolylineJoins ToJoins(LazorJoin j)
        {
            switch (j)
            {
                case LazorJoin.Miter: return PolylineJoins.Miter;
                case LazorJoin.Bevel: return PolylineJoins.Bevel;
                case LazorJoin.Round: return PolylineJoins.Round;
                default: return PolylineJoins.Simple;
            }
        }
    }

    /// <summary>
    /// A reusable stroke batch that owns its per-stroke PolylinePath pool and wraps the Draw.Command — so callers
    /// (e.g. the Lazor editor preview) can render resolved strokes for a camera WITHOUT referencing Shapes types
    /// themselves. Keep one instance alive per drawing surface and call <see cref="Draw"/> from a render callback.
    /// </summary>
    public sealed class LazorStrokeBatch
    {
        readonly List<PolylinePath> _paths = new List<PolylinePath>();

        /// <summary>Draw resolved strokes for <paramref name="cam"/> (call inside the camera's render, e.g. from
        /// RenderPipelineManager.endCameraRendering). See <see cref="LazorDraw.Draw"/> for the parameters.</summary>
        public void Draw(Camera cam, List<ResolvedPolyline> resolved, Matrix4x4 matrix, float scale, float thicknessScale, Color tint)
        {
            using (Shapes.Draw.Command(cam))
                LazorDraw.Draw(resolved, _paths, matrix, scale, thicknessScale, tint);
        }
    }

    /// <summary>
    /// Drop-in runtime component: draws <see cref="shape"/> at this transform as glowing vector line-art. Size comes
    /// from the object's scale (baked into points + thickness, not the matrix). Re-resolves each frame when
    /// <see cref="liveRebuild"/> so runtime layer edits show live; otherwise call <see cref="SetDirty"/> after edits.
    /// </summary>
    [ExecuteAlways]
    public class LazorRenderer : ImmediateModeShapeDrawer
    {
        [Tooltip("The vector shape to draw.")]
        public LazorShape shape;

        [Tooltip("Tints every stroke (multiplied). White = the shape's own colors.")]
        public Color tint = Color.white;

        [Tooltip("Scales every stroke's width. 1 = as authored.")]
        public float thicknessScale = 1f;

        [Tooltip("Re-resolve every frame so runtime edits to the shape's layers show immediately. Off = static.")]
        public bool liveRebuild = true;

        readonly List<ResolvedPolyline> _resolved = new List<ResolvedPolyline>();
        readonly List<PolylinePath> _paths = new List<PolylinePath>();
        bool _dirty = true;

        /// <summary>Force a re-resolve on the next draw (call after swapping the shape or editing its layers).</summary>
        public void SetDirty() => _dirty = true;

        public override void OnEnable()
        {
            base.OnEnable();   // registers the Shapes camera callback — must run
            _dirty = true;
        }

        public override void DrawShapes(Camera cam)
        {
            if (shape == null) return;

            if (_dirty || liveRebuild)
            {
                _resolved.Clear();
                _resolved.AddRange(LazorGeometry.Resolve(shape));
                _dirty = false;
            }

            using (Shapes.Draw.Command(cam))
                LazorDraw.Draw(_resolved, _paths,
                    Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one),
                    transform.lossyScale.x, thicknessScale, tint);
        }
    }
}
