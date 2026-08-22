// TapestrySdf — 2D signed-distance-field helpers for Tapestry's generators. Same exact distance formula as
// LatheSdf's 3D Box case (Quilez's well-known functions), just the 2D rounded-box variant 3D Lathe doesn't
// need. Positive = outside the shape, negative = inside, zero = on the surface.
using UnityEngine;

namespace Laubrary.Tapestry
{
    public static class TapestrySdf
    {
        public static float RoundBox(Vector2 p, Vector2 halfExtents, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - halfExtents + Vector2.one * radius;
            Vector2 outsideV = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return outsideV.magnitude + inside - radius;
        }

        /// A cheap central-difference gradient of any 2D SDF at `p` — used to derive a "surface normal" for
        /// bevel shading (which side of an edge catches a highlight vs a shadow) without needing an analytic
        /// derivative per shape.
        public static Vector2 Gradient(System.Func<Vector2, float> sdf, Vector2 p, float epsilon = 0.001f)
        {
            float dx = sdf(p + new Vector2(epsilon, 0f)) - sdf(p - new Vector2(epsilon, 0f));
            float dy = sdf(p + new Vector2(0f, epsilon)) - sdf(p - new Vector2(0f, epsilon));
            var g = new Vector2(dx, dy);
            return g.sqrMagnitude > 1e-10f ? g.normalized : Vector2.zero;
        }
    }
}
