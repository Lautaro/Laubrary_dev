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
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
