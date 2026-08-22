// SurfaceStampMeshModifier — scatters a small primitive (studs, rivets, spikes — "parts of other
// primitives") across a solid's surface, one per tiled UV cell. Real appended geometry, not a texture or a
// displacement — the counterpart to SurfaceReliefMeshModifier for details that need actual 3D shape (a
// dome doesn't read as a rivet head; a stamped little cylinder does).
//
// v1 scatters at the base mesh's OWN vertex positions (one stamp per unique tiled-UV cell, first vertex
// found in each), not a true barycentric surface sample — coverage is limited by how many vertices the
// upstream module/modifiers already put there. Good enough for the primitive builders' typical vertex
// density; a low-poly base mesh under a high Density will under-fill. A proper surface-uniform sampler is
// future work.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Surface Stamp", "Surface")]
    [Serializable]
    public class SurfaceStampMeshModifier : LatheMeshModifier
    {
        public PrimitiveKind stampKind = PrimitiveKind.Cone;
        [Range(0.01f, 1f)] public float stampSize = 0.08f;
        [Range(0.5f, 12f)] public float density = 3f;      // tiled-UV grid cells per world unit
        public bool alignToNormal = true;

        public override string DisplayName => "Surface Stamp";
        public override string Description =>
            "Scatters a small primitive (a stud, a rivet, a spike) across the surface, one per tiled grid "
            + "cell — real appended geometry, not a texture.";

        public override void Apply(LatheMeshData data)
        {
            int n = data.verts.Count;
            if (n == 0) return;

            var seen = new HashSet<(int axis, int cx, int cy)>();
            var stamps = new List<(Vector3 pos, Vector3 normal)>();
            for (int i = 0; i < n; i++)
            {
                Vector3 p = data.verts[i];
                Vector3 nrm = i < data.normals.Count ? data.normals[i] : Vector3.up;
                int axis = DominantAxis(nrm);
                Vector2 uv = LatheMeshData.BoxProject(p, nrm) * Mathf.Max(0.01f, density);
                var key = (axis, Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
                if (!seen.Add(key)) continue;
                stamps.Add((p, nrm));
            }

            foreach (var (pos, nrm) in stamps)
            {
                var scratch = LatheMeshBuilders.BuildScratch(stampKind, stampSize, stampSize * 1.5f, 8);
                Vector3 up = nrm.sqrMagnitude > 1e-8f ? nrm.normalized : Vector3.up;
                Quaternion rot = alignToNormal ? Quaternion.FromToRotation(Vector3.up, up) : Quaternion.identity;
                var matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
                data.AppendTransformed(scratch, matrix);
            }
            data.normals.Clear();
        }

        static int DominantAxis(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            return ax >= ay && ax >= az ? 0 : ay >= ax && ay >= az ? 1 : 2;
        }
    }
}
