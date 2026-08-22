// LatheSdf — signed-distance-field primitives and combinators, the basis for Molded Shapes' fuse/subtract
// CSG. A positive distance = outside the shape, negative = inside, zero = on the surface. Every primitive
// is evaluated in the QUERY POINT's own local space (the caller transforms into each node's local frame
// first — see LatheMoldNode.Evaluate). All exact formulas (Inigo Quilez's well-known distance-function set),
// not approximations — Marching-Cubes-style meshing is only as clean as the field it samples.
using UnityEngine;

namespace Laubrary.Lathe
{
    public static class LatheSdf
    {
        public static float Box(Vector3 p, Vector3 halfExtents)
        {
            Vector3 q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - halfExtents;
            Vector3 outsideV = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outsideV.magnitude + inside;
        }

        public static float Sphere(Vector3 p, float radius) => p.magnitude - radius;

        public static float Cylinder(Vector3 p, float radius, float halfHeight)
        {
            float dxz = new Vector2(p.x, p.z).magnitude - radius;
            float dy = Mathf.Abs(p.y) - halfHeight;
            Vector2 outsideV = new Vector2(Mathf.Max(dxz, 0f), Mathf.Max(dy, 0f));
            float inside = Mathf.Min(Mathf.Max(dxz, dy), 0f);
            return outsideV.magnitude + inside;
        }

        public static float Torus(Vector3 p, float majorRadius, float minorRadius)
        {
            float qx = new Vector2(p.x, p.z).magnitude - majorRadius;
            return new Vector2(qx, p.y).magnitude - minorRadius;
        }

        // ── combinators ────────────────────────────────────────────────────────
        public static float Union(float a, float b) => Mathf.Min(a, b);

        /// a minus b — keeps a's volume except where b overlaps it.
        public static float Subtract(float a, float b) => Mathf.Max(a, -b);

        /// Polynomial smooth minimum (Quilez) — k=0 reduces exactly to Union.
        public static float SmoothUnion(float a, float b, float k)
        {
            if (k <= 1e-5f) return Union(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// Smooth subtraction (Quilez) — k=0 reduces exactly to Subtract.
        public static float SmoothSubtract(float a, float b, float k)
        {
            if (k <= 1e-5f) return Subtract(a, b);
            float h = Mathf.Clamp01(0.5f - 0.5f * (b + a) / k);
            return Mathf.Lerp(a, -b, h) + k * h * (1f - h);
        }
    }
}
