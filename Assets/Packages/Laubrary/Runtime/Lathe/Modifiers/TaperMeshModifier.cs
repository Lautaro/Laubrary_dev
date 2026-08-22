// TaperMeshModifier — scales a mesh's cross-section along a chosen axis, linearly between two positions.
// Works on ANY generated mesh (a cylinder into a cone, a sweep tube tapering to a point, a box into a
// wedge), not just Profile Sweep's own built-in path shapes — demonstrates the modifier stack is general,
// not sweep-specific.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Taper", "Shape")]
    [Serializable]
    public class TaperMeshModifier : LatheMeshModifier
    {
        public LatheAxis axis = LatheAxis.Y;
        [Range(-5f, 5f)] public float rangeMin = -1f;
        [Range(-5f, 5f)] public float rangeMax = 1f;
        [Range(0f, 3f)] public float scaleAtMin = 1f;
        [Range(0f, 3f)] public float scaleAtMax = 0.2f;

        public override string DisplayName => "Taper";
        public override string Description =>
            "Scales the mesh's cross-section along one axis, linearly between the two range positions — a "
            + "cylinder into a cone, a sweep tube tapering to a point, a box into a wedge.";

        public override void Apply(LatheMeshData data)
        {
            int axisIdx = axis == LatheAxis.X ? 0 : axis == LatheAxis.Y ? 1 : 2;
            float lo = Mathf.Min(rangeMin, rangeMax), hi = Mathf.Max(rangeMin, rangeMax);
            float span = Mathf.Max(1e-5f, hi - lo);

            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 v = data.verts[i];
                float axisVal = Component(v, axisIdx);
                float t = Mathf.Clamp01((axisVal - lo) / span);
                float scale = Mathf.Lerp(scaleAtMin, scaleAtMax, t);
                data.verts[i] = ScaleOffAxis(v, axisIdx, scale);
            }
            // The taper distorts the shape, so the pre-taper normals no longer match it — clearing the list
            // (rather than trying to re-derive them here) makes LatheMeshData.ToMesh fall back to
            // Mesh.RecalculateNormals(), Unity's own solver, on the NEW geometry.
            data.normals.Clear();
        }

        static float Component(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        static Vector3 ScaleOffAxis(Vector3 v, int axis, float scale)
        {
            switch (axis)
            {
                case 0: return new Vector3(v.x, v.y * scale, v.z * scale);
                case 1: return new Vector3(v.x * scale, v.y, v.z * scale);
                default: return new Vector3(v.x * scale, v.y * scale, v.z);
            }
        }
    }
}
