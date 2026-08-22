// RadialArrayMeshModifier — duplicates a solid's mesh Count times, spun around a chosen axis (gear teeth, a
// flower's petals, spokes on a wheel). Same append-a-transformed-copy trick MirrorMeshModifier uses, just
// N-fold instead of one reflection.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Radial Array", "Symmetry")]
    [Serializable]
    public class RadialArrayMeshModifier : LatheMeshModifier
    {
        [Range(2, 32)] public int count = 6;
        public LatheAxis axis = LatheAxis.Y;
        [Range(1f, 360f)] public float totalDegrees = 360f;
        // Shifts the source shape away from the axis before spinning it, so a shape centred at the origin
        // still fans out into a proper ring. 0 = spin the shape exactly where it is (right for a source
        // that's already off-axis, e.g. a Profile Sweep line pointing outward — a pinwheel/spoke pattern).
        [Range(0f, 5f)] public float radius = 0f;

        public override string DisplayName => "Radial Array";
        public override string Description =>
            "Duplicates this solid's mesh several times, spun evenly around one axis — gear teeth, a "
            + "flower's petals, spokes on a wheel.";

        public override void Apply(LatheMeshData data)
        {
            int n = data.verts.Count;
            if (n == 0 || count < 2) return;

            var origVerts = new List<Vector3>(data.verts);
            var origNormals = data.normals.Count == n ? new List<Vector3>(data.normals) : null;
            var origTris = new List<int>(data.tris);

            Vector3 axisVec = axis == LatheAxis.X ? Vector3.right : axis == LatheAxis.Y ? Vector3.up : Vector3.forward;
            Vector3 radialDir = axis == LatheAxis.Y ? Vector3.right : Vector3.up;
            Vector3 radialOffset = radialDir * radius;

            for (int c = 1; c < count; c++)
            {
                float deg = totalDegrees * c / count;
                Quaternion rot = Quaternion.AngleAxis(deg, axisVec);
                Vector3 shift = rot * radialOffset - radialOffset;
                int baseIdx = data.verts.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 pos = rot * origVerts[i] + shift;
                    Vector3 nrm = origNormals != null ? rot * origNormals[i] : Vector3.up;
                    data.AddVert(pos, nrm);
                }
                for (int t = 0; t < origTris.Count; t += 3)
                    data.AddTri(baseIdx + origTris[t], baseIdx + origTris[t + 1], baseIdx + origTris[t + 2]);
            }
            // Every copy carries a rotated normal already, but a mixed-normals mesh (some verts had none,
            // filled with Vector3.up above) reads as broken lighting — clear so ToMesh recalculates cleanly
            // whenever the upstream state wasn't fully trustworthy. When it WAS (origNormals != null), this
            // just costs one recalculation for a still-correct result.
            data.normals.Clear();
        }
    }
}
