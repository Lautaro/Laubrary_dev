// MoldedShapeModule — a LatheModule that references a LatheMoldAsset and copies its (cached) baked mesh
// into this solid's geometry. Copies the cached mesh's DATA rather than returning it directly: the cache is
// owned by the asset and reused across many repaints, while LatheSolid.BuildMesh's caller destroys whatever
// Mesh it gets back every frame — returning the cache itself would have it destroying its own asset's cache
// out from under it on the very next repaint.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModuleInfo("Molded Shape", "Mold", "blob")]
    [Serializable]
    public class MoldedShapeModule : LatheModule
    {
        public LatheMoldAsset mold;

        public override string DisplayName => "Molded Shape";
        public override string Description =>
            "References a Mold asset — several primitives fused and/or subtracted (with an optional soft "
            + "blend) and baked to a real mesh. Author new ones via Laubrary/Lathe Mold; pick any from the project here.";

        public override void Generate(LatheMeshData data)
        {
            if (mold == null) return;
            var cached = mold.GetOrBakeMesh();
            if (cached == null) return;

            var verts = cached.vertices;
            var normals = cached.normals;
            var tris = cached.triangles;
            var remap = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                remap[i] = data.AddVert(verts[i], i < normals.Length ? normals[i] : Vector3.up);
            for (int t = 0; t < tris.Length; t += 3)
                data.AddTri(remap[tris[t]], remap[tris[t + 1]], remap[tris[t + 2]]);
        }
    }
}
