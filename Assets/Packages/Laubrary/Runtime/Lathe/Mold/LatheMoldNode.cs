// LatheMoldNode — one primitive + operation in a Molded Shape's CSG stack. Nodes combine in LIST ORDER:
// each node fuses (Union) with, or cuts into (Subtract), the accumulated result of every node ABOVE it.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum MoldPrimitiveKind { Box, Sphere, Cylinder, Torus }
    public enum MoldOp { Union, Subtract }

    [Serializable]
    public class LatheMoldNode
    {
        public string name = "Node";
        public bool enabled = true;
        public MoldPrimitiveKind kind = MoldPrimitiveKind.Sphere;
        public MoldOp op = MoldOp.Union;
        // The "clean cut vs soft" dial: 0 = a hard union/subtraction (Subtract's "antibody" removes exactly
        // its own volume, no more). Higher blends the seam into a smooth fillet/scoop instead of a sharp edge.
        [Range(0f, 1.5f)] public float blend = 0f;

        public Vector3 position = Vector3.zero;
        public Vector3 rotationEuler = Vector3.zero;

        // Which of these apply depends on Kind — same "show every field regardless of Kind" convention
        // PrimitiveSolidModule already uses (its Ring Inner/Segments/Rings stay visible in Box mode too).
        [Range(0.02f, 3f)] public float radius = 0.5f;     // Sphere radius / Cylinder radius / Torus major radius
        [Range(0.02f, 3f)] public float radius2 = 0.15f;   // Cylinder half-height / Torus minor radius
        public Vector3 boxHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);

        /// Signed distance from a WORLD-space point (in the mold's own local space, pre-turntable) to this
        /// node's shape, transformed by its position/rotation.
        public float Evaluate(Vector3 p)
        {
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(rotationEuler)) * (p - position);
            switch (kind)
            {
                case MoldPrimitiveKind.Box: return LatheSdf.Box(local, boxHalfExtents);
                case MoldPrimitiveKind.Sphere: return LatheSdf.Sphere(local, radius);
                case MoldPrimitiveKind.Cylinder: return LatheSdf.Cylinder(local, radius, radius2);
                case MoldPrimitiveKind.Torus: return LatheSdf.Torus(local, radius, radius2);
                default: return float.MaxValue;
            }
        }

        public LatheMoldNode Clone() => new LatheMoldNode
        {
            name = name, enabled = enabled, kind = kind, op = op, blend = blend,
            position = position, rotationEuler = rotationEuler,
            radius = radius, radius2 = radius2, boxHalfExtents = boxHalfExtents,
        };
    }
}
