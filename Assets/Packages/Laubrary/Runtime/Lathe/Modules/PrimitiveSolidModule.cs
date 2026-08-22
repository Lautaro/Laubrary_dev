// PrimitiveSolidModule — the baseline LatheModule: a plain box, sphere, cylinder, cone or (tube) ring.
// Proves "several solids share one 3D space" on its own (a Ring-kind + a Box-kind solid = a ring with a
// cube in the middle) and is the fallback every new LatheSolid starts as.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum PrimitiveKind { Box, Sphere, Cylinder, Cone, Ring }

    [LatheModuleInfo("Primitive", "Primitives", "cube")]
    [Serializable]
    public class PrimitiveSolidModule : LatheModule
    {
        public PrimitiveKind kind = PrimitiveKind.Box;
        [Range(0.05f, 5f)] public float size = 1f;            // box half-extent / sphere & cylinder & cone base radius / ring outer radius
        [Range(0.05f, 5f)] public float height = 1f;           // cylinder / cone / ring thickness (ring reads this as thickness * 0.1)
        [Range(0.01f, 0.95f)] public float ringInner = 0.6f;   // Ring inner radius, as a fraction of `size`
        [Range(3, 64)] public int segments = 24;                // radial segments (sphere/cylinder/cone/ring)
        [Range(2, 32)] public int rings = 12;                   // latitude rings (sphere only)
        public bool capEnds = true;                             // cylinder/cone end caps

        public override string DisplayName => "Primitive";
        public override string Description =>
            "A basic solid — box, sphere, cylinder, cone, or a flat tube ring (a Saturn-style annulus with real thickness).";

        public override void Generate(LatheMeshData data)
        {
            switch (kind)
            {
                case PrimitiveKind.Box: BuildBox(data, size); break;
                case PrimitiveKind.Sphere: BuildSphere(data, size, Mathf.Max(3, segments), Mathf.Max(2, rings)); break;
                case PrimitiveKind.Cylinder: BuildCylinder(data, size, height, Mathf.Max(3, segments), capEnds); break;
                case PrimitiveKind.Cone: BuildCone(data, size, height, Mathf.Max(3, segments), capEnds); break;
                case PrimitiveKind.Ring: BuildRing(data, size, size * Mathf.Clamp01(ringInner), Mathf.Max(0.01f, height * 0.1f), Mathf.Max(3, segments)); break;
            }
        }

        static void BuildBox(LatheMeshData d, float s)
        {
            void Face(Vector3 n, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
            {
                int a = d.AddVert(v0 * s, n);
                int b = d.AddVert(v1 * s, n);
                int c = d.AddVert(v2 * s, n);
                int e = d.AddVert(v3 * s, n);
                d.AddQuad(a, b, c, e);
            }
            Face(Vector3.right, new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, -1));
            Face(Vector3.left, new Vector3(-1, -1, 1), new Vector3(-1, -1, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 1));
            Face(Vector3.up, new Vector3(-1, 1, -1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, -1));
            Face(Vector3.down, new Vector3(-1, -1, 1), new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 1));
            Face(Vector3.forward, new Vector3(1, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1));
            Face(Vector3.back, new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1));
        }

        static void BuildSphere(LatheMeshData d, float r, int segs, int latRings)
        {
            var grid = new int[latRings + 1, segs + 1];
            for (int y = 0; y <= latRings; y++)
            {
                float phi = (float)y / latRings * Mathf.PI;
                float sy = Mathf.Cos(phi), ring = Mathf.Sin(phi);
                for (int x = 0; x <= segs; x++)
                {
                    float theta = (float)x / segs * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(ring * Mathf.Cos(theta), sy, ring * Mathf.Sin(theta));
                    grid[y, x] = d.AddVert(dir * r, dir);
                }
            }
            for (int y = 0; y < latRings; y++)
                for (int x = 0; x < segs; x++)
                    d.AddQuad(grid[y, x], grid[y, x + 1], grid[y + 1, x + 1], grid[y + 1, x]);
        }

        static void BuildCylinder(LatheMeshData d, float r, float h, int segs, bool caps)
        {
            float half = h * 0.5f;
            var dirs = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs * Mathf.PI * 2f;
                dirs[i] = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
            }
            var top = new int[segs + 1]; var bot = new int[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                top[i] = d.AddVert(dirs[i] * r + Vector3.up * half, dirs[i]);
                bot[i] = d.AddVert(dirs[i] * r - Vector3.up * half, dirs[i]);
            }
            for (int i = 0; i < segs; i++)
                d.AddQuad(bot[i], bot[i + 1], top[i + 1], top[i]);

            if (!caps) return;
            int ct = d.AddVert(Vector3.up * half, Vector3.up);
            int cb = d.AddVert(-Vector3.up * half, Vector3.down);
            for (int i = 0; i < segs; i++)
            {
                int a = d.AddVert(dirs[i] * r + Vector3.up * half, Vector3.up);
                int b = d.AddVert(dirs[i + 1] * r + Vector3.up * half, Vector3.up);
                d.AddTri(ct, b, a);
                int a2 = d.AddVert(dirs[i] * r - Vector3.up * half, Vector3.down);
                int b2 = d.AddVert(dirs[i + 1] * r - Vector3.up * half, Vector3.down);
                d.AddTri(cb, a2, b2);
            }
        }

        static void BuildCone(LatheMeshData d, float r, float h, int segs, bool caps)
        {
            float half = h * 0.5f;
            Vector3 apex = Vector3.up * half;
            var baseIdx = new int[segs + 1]; var apexIdx = new int[segs + 1]; var dirs = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs * Mathf.PI * 2f;
                dirs[i] = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                Vector3 basePos = dirs[i] * r - Vector3.up * half;
                Vector3 slopeNormal = new Vector3(dirs[i].x * h, r, dirs[i].z * h).normalized;
                baseIdx[i] = d.AddVert(basePos, slopeNormal);
                apexIdx[i] = d.AddVert(apex, slopeNormal);
            }
            for (int i = 0; i < segs; i++)
                d.AddTri(baseIdx[i], baseIdx[i + 1], apexIdx[i]);

            if (!caps) return;
            int cb = d.AddVert(-Vector3.up * half, Vector3.down);
            for (int i = 0; i < segs; i++)
            {
                int a = d.AddVert(dirs[i] * r - Vector3.up * half, Vector3.down);
                int b = d.AddVert(dirs[i + 1] * r - Vector3.up * half, Vector3.down);
                d.AddTri(cb, b, a);
            }
        }

        static void BuildRing(LatheMeshData d, float outerR, float innerR, float halfT, int segs)
        {
            innerR = Mathf.Min(innerR, outerR * 0.95f);
            var dirs = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs * Mathf.PI * 2f;
                dirs[i] = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
            }

            var outerTop = new int[segs + 1]; var outerBot = new int[segs + 1];
            var innerTop = new int[segs + 1]; var innerBot = new int[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                outerTop[i] = d.AddVert(dirs[i] * outerR + Vector3.up * halfT, Vector3.up);
                outerBot[i] = d.AddVert(dirs[i] * outerR - Vector3.up * halfT, Vector3.down);
                innerTop[i] = d.AddVert(dirs[i] * innerR + Vector3.up * halfT, Vector3.up);
                innerBot[i] = d.AddVert(dirs[i] * innerR - Vector3.up * halfT, Vector3.down);
            }
            for (int i = 0; i < segs; i++)
            {
                d.AddQuad(innerTop[i], outerTop[i], outerTop[i + 1], innerTop[i + 1]);
                d.AddQuad(outerBot[i], innerBot[i], innerBot[i + 1], outerBot[i + 1]);
            }

            // Walls get their own radially-outward/inward vertex normals, so they're duplicated separately
            // from the top/bottom face verts above (which are up/down-facing).
            var outerTopW = new int[segs + 1]; var outerBotW = new int[segs + 1];
            var innerTopW = new int[segs + 1]; var innerBotW = new int[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                outerTopW[i] = d.AddVert(dirs[i] * outerR + Vector3.up * halfT, dirs[i]);
                outerBotW[i] = d.AddVert(dirs[i] * outerR - Vector3.up * halfT, dirs[i]);
                innerTopW[i] = d.AddVert(dirs[i] * innerR + Vector3.up * halfT, -dirs[i]);
                innerBotW[i] = d.AddVert(dirs[i] * innerR - Vector3.up * halfT, -dirs[i]);
            }
            for (int i = 0; i < segs; i++)
            {
                d.AddQuad(outerBotW[i], outerBotW[i + 1], outerTopW[i + 1], outerTopW[i]);
                d.AddQuad(innerTopW[i], innerTopW[i + 1], innerBotW[i + 1], innerBotW[i]);
            }
        }
    }
}
