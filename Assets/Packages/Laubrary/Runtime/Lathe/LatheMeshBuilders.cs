// LatheMeshBuilders — the shared primitive-geometry builders PrimitiveSolidModule uses, factored out so
// OTHER code can stamp the same primitives elsewhere: SkeletonSweepModule's branch-joint spheres and
// SurfaceStampMeshModifier's scattered studs both build a primitive into a scratch LatheMeshData, then
// LatheMeshData.AppendTransformed merges it into the real mesh at whatever position/orientation they need.
using UnityEngine;

namespace Laubrary.Lathe
{
    public static class LatheMeshBuilders
    {
        public static void BuildBox(LatheMeshData d, float s)
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

        public static void BuildSphere(LatheMeshData d, float r, int segs, int latRings)
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

        public static void BuildCylinder(LatheMeshData d, float r, float h, int segs, bool caps)
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

        public static void BuildCone(LatheMeshData d, float r, float h, int segs, bool caps)
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

        public static void BuildRing(LatheMeshData d, float outerR, float innerR, float halfT, int segs)
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

        /// Builds `kind` at unit-ish scale into a fresh scratch LatheMeshData — for callers that need to
        /// place a primitive at an arbitrary transform via LatheMeshData.AppendTransformed (a branch-joint
        /// sphere, a scattered surface stud) rather than growing the primitive in world space directly.
        public static LatheMeshData BuildScratch(PrimitiveKind kind, float size, float height, int segments)
        {
            var d = new LatheMeshData();
            int segs = Mathf.Max(3, segments);
            switch (kind)
            {
                case PrimitiveKind.Box: BuildBox(d, size); break;
                case PrimitiveKind.Sphere: BuildSphere(d, size, segs, Mathf.Max(2, segs / 2)); break;
                case PrimitiveKind.Cylinder: BuildCylinder(d, size, height, segs, true); break;
                case PrimitiveKind.Cone: BuildCone(d, size, height, segs, true); break;
                case PrimitiveKind.Ring: BuildRing(d, size, size * 0.6f, Mathf.Max(0.01f, height * 0.1f), segs); break;
            }
            return d;
        }
    }
}
