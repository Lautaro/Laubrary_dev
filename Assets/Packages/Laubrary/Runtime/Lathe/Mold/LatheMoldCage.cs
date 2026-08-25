// LatheMoldCage — turns a simple "corners + edges" control cage into a LatheMoldNode list, the way a
// 9-slice defines fixed corner pieces and a stretchy strip between them. Every corner becomes a primitive
// node at its position; every edge becomes a Cylinder node stretched and rotated to span the two corners it
// connects. All nodes Union with a shared blend, so corners and edges read as one fused surface rather than
// parts glued together. Pure placement math over LatheMoldNode's EXISTING primitive kinds and combinators —
// no new SDF primitive, no new mesher, nothing beyond what hand-placed Mold nodes already do.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [System.Serializable]
    public struct LatheCageEdge
    {
        public int a, b;
        public float radius;
        public LatheCageEdge(int a, int b, float radius) { this.a = a; this.b = b; this.radius = radius; }
    }

    public static class LatheMoldCage
    {
        /// Builds a node per corner (as `cornerKind`, sized `cornerRadius`) and a Cylinder node per edge,
        /// all Union'd with `blend`. `edges` index into `corners` the same way a mesh's edge list would.
        public static List<LatheMoldNode> BuildNodes(IList<Vector3> corners, IList<LatheCageEdge> edges,
            float cornerRadius, float blend, MoldPrimitiveKind cornerKind = MoldPrimitiveKind.Sphere)
        {
            var nodes = new List<LatheMoldNode>();
            if (corners == null) return nodes;

            for (int i = 0; i < corners.Count; i++)
            {
                nodes.Add(new LatheMoldNode
                {
                    name = "Corner " + i,
                    kind = cornerKind,
                    op = MoldOp.Union,
                    blend = blend,
                    position = corners[i],
                    radius = cornerRadius,
                    boxHalfExtents = Vector3.one * cornerRadius,
                });
            }

            if (edges != null)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    if (e.a < 0 || e.a >= corners.Count || e.b < 0 || e.b >= corners.Count) continue;
                    Vector3 a = corners[e.a], b = corners[e.b];
                    Vector3 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 1e-5f) continue;
                    dir /= len;
                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

                    nodes.Add(new LatheMoldNode
                    {
                        name = "Edge " + e.a + "-" + e.b,
                        kind = MoldPrimitiveKind.Cylinder,
                        op = MoldOp.Union,
                        blend = blend,
                        position = (a + b) * 0.5f,
                        rotationEuler = rot.eulerAngles,
                        radius = e.radius,
                        radius2 = len * 0.5f,
                    });
                }
            }

            return nodes;
        }
    }
}
