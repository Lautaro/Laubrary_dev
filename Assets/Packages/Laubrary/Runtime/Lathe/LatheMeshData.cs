// LatheMeshData — the growable vertex/triangle/normal buffer a LatheModule fills and a LatheMeshModifier
// can mutate, converted to a real Mesh only once. Nothing here is cached: at the low-poly counts these
// generators produce (primitives, swept profiles), rebuilding from scratch every preview repaint is cheap
// — see LatheSolid.BuildMesh, called fresh on every LathePreview.Render.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    public class LatheMeshData
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<int> tris = new List<int>();

        /// World-units-per-UV-tile for the automatic box-projected UVs ToMesh generates — no builder or
        /// modifier has to think about UVs itself. A module MAY tune it (a tighter weave on a small solid,
        /// say) by setting this before Generate returns.
        public float uvScale = 1f;

        public int AddVert(Vector3 pos, Vector3 normal)
        {
            verts.Add(pos);
            normals.Add(normal);
            return verts.Count - 1;
        }

        public void AddTri(int a, int b, int c)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        /// a,b,c,d in order around the quad (any consistent winding — Lathe's preview material renders both
        /// faces, see LathePreview, so a builder never has to prove its winding is correct).
        public void AddQuad(int a, int b, int c, int d)
        {
            AddTri(a, b, c);
            AddTri(a, c, d);
        }

        /// Merges another LatheMeshData into this one under `matrix` — positions fully transformed, normals
        /// rotated (MultiplyVector, so translation doesn't apply and non-uniform scale isn't inverse-
        /// transposed; callers with a non-uniform-scale matrix should clear `normals` afterward to force a
        /// clean recalculation in ToMesh, same escape hatch Taper/Noise/Twist already use). What
        /// SkeletonSweepModule uses to drop a branch-joint sphere at a node, and SurfaceStampMeshModifier to
        /// scatter a stud at each surface sample — build the primitive once at the origin via
        /// LatheMeshBuilders, then place it wherever it's needed.
        public void AppendTransformed(LatheMeshData other, Matrix4x4 matrix)
        {
            int baseIdx = verts.Count;
            bool hasNormals = other.normals.Count == other.verts.Count;
            for (int i = 0; i < other.verts.Count; i++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(other.verts[i]);
                Vector3 n = hasNormals ? matrix.MultiplyVector(other.normals[i]).normalized : Vector3.up;
                AddVert(p, n);
            }
            for (int t = 0; t < other.tris.Count; t += 3)
                AddTri(baseIdx + other.tris[t], baseIdx + other.tris[t + 1], baseIdx + other.tris[t + 2]);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = string.IsNullOrEmpty(name) ? "Lathe Solid" : name, hideFlags = HideFlags.HideAndDontSave };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (normals.Count == verts.Count) mesh.SetNormals(normals);
            else mesh.RecalculateNormals();
            // A generic box/triplanar projection (pick the dominant normal axis, project the other two
            // position components as UV) rather than a per-builder unwrap — every current and future
            // module/modifier gets a reasonable, seamless-enough-for-tiling UV set for free, no builder ever
            // has to touch UVs itself. Uses the FINAL (post-recalculate) normals, so a modifier that
            // reshapes the mesh (Taper, Noise — both clear `normals` to force a recalculation) still gets
            // UVs that match the shape it actually produced.
            mesh.SetUVs(0, BoxProjectedUVs(mesh.vertices, mesh.normals, uvScale));
            mesh.RecalculateBounds();
            return mesh;
        }

        static List<Vector2> BoxProjectedUVs(Vector3[] verts, Vector3[] normals, float scale)
        {
            var uvs = new List<Vector2>(verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = i < normals.Length ? normals[i] : Vector3.up;
                uvs.Add(BoxProject(verts[i], n) * scale);
            }
            return uvs;
        }

        /// The same box/triplanar projection ToMesh uses for UVs, exposed so a modifier that tiles
        /// something across a surface (SurfaceReliefMeshModifier, SurfaceStampMeshModifier) samples in the
        /// exact same 2D space the final texture UVs land in — a pattern and a texture assigned to the same
        /// solid line up.
        public static Vector2 BoxProject(Vector3 pos, Vector3 normal)
        {
            float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
            return ax >= ay && ax >= az ? new Vector2(pos.z, pos.y)
                : ay >= ax && ay >= az ? new Vector2(pos.x, pos.z)
                : new Vector2(pos.x, pos.y);
        }
    }
}
