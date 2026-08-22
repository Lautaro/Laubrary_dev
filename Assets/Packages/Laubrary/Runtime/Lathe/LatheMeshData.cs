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
                Vector3 p = verts[i];
                Vector3 n = i < normals.Length ? normals[i] : Vector3.up;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                Vector2 uv = ax >= ay && ax >= az ? new Vector2(p.z, p.y)
                    : ay >= ax && ay >= az ? new Vector2(p.x, p.z)
                    : new Vector2(p.x, p.y);
                uvs.Add(uv * scale);
            }
            return uvs;
        }
    }
}
