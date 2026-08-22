// TwistMeshModifier — rotates a mesh progressively along an axis, like a barber pole or a drill bit. Each
// vertex is rotated around the axis (through Pivot) by an angle proportional to its own position along
// that same axis, which naturally leaves the on-axis coordinate untouched and only turns the cross-section.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Twist", "Shape")]
    [Serializable]
    public class TwistMeshModifier : LatheMeshModifier
    {
        public LatheAxis axis = LatheAxis.Y;
        [Range(-720f, 720f)] public float degreesPerUnit = 90f;
        [Range(-5f, 5f)] public float pivot = 0f;

        public override string DisplayName => "Twist";
        public override string Description =>
            "Rotates the mesh progressively along an axis, like a barber pole or a drill bit — the twist "
            + "angle at each vertex is proportional to its own position along that axis.";

        public override void Apply(LatheMeshData data, float animT)
        {
            int axisIdx = axis == LatheAxis.X ? 0 : axis == LatheAxis.Y ? 1 : 2;
            Vector3 axisVec = axis == LatheAxis.X ? Vector3.right : axis == LatheAxis.Y ? Vector3.up : Vector3.forward;

            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 v = data.verts[i];
                float axisVal = Component(v, axisIdx) - pivot;
                float deg = axisVal * degreesPerUnit;
                // Rotating the WHOLE point around an axis through the origin preserves the coordinate along
                // that axis by construction and only turns the perpendicular components — exactly a twist,
                // with no need to manually split "on-axis" from "off-axis".
                data.verts[i] = Quaternion.AngleAxis(deg, axisVec) * v;
            }
            data.normals.Clear();
        }

        static float Component(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;
    }
}
