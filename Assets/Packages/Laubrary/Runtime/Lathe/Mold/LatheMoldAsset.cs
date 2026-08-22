// LatheMoldAsset — a saved, reusable "Molded Shape": several primitives fused/subtracted (see
// LatheMoldNode), baked to a real mesh via LatheVoxelMesher. Authored in its own window (Laubrary/Lathe
// Mold), picked from the project like any other asset via MoldedShapeModule's mold field — the "treat it
// like its own primitive, picked from a library" idea.
//
// CACHED, unlike almost everything else in Lathe: voxelizing is genuinely too expensive to redo on every
// preview repaint (LatheSolid.BuildMesh's usual "just rebuild it, nothing here is worth caching" philosophy
// does not hold for a full grid SDF evaluation + Surface Nets pass). The bake is invalidated only when a
// node's content actually changes, not on every repaint.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    public class LatheMoldAsset : ScriptableObject
    {
        public List<LatheMoldNode> nodes = new List<LatheMoldNode>
        {
            new LatheMoldNode { name = "Node 1", kind = MoldPrimitiveKind.Sphere, op = MoldOp.Union },
        };
        [Range(8, 48)] public int resolution = 20;
        [Range(0.5f, 6f)] public float boundsSize = 2.5f;

        [System.NonSerialized] Mesh cachedMesh;
        [System.NonSerialized] int cachedHash;
        [System.NonSerialized] bool hashValid;

        /// Signed distance at a world (mold-local) point — nodes combine in LIST ORDER, each fusing with or
        /// cutting into everything above it.
        public float EvaluateSdf(Vector3 p)
        {
            float d = 1f;   // "outside everywhere" sentinel when no node has run yet
            bool first = true;
            if (nodes == null) return d;
            foreach (var n in nodes)
            {
                if (n == null || !n.enabled) continue;
                float nd = n.Evaluate(p);
                if (first) { d = nd; first = false; continue; }
                d = n.op == MoldOp.Union
                    ? (n.blend > 1e-4f ? LatheSdf.SmoothUnion(d, nd, n.blend) : LatheSdf.Union(d, nd))
                    : (n.blend > 1e-4f ? LatheSdf.SmoothSubtract(d, nd, n.blend) : LatheSdf.Subtract(d, nd));
            }
            return d;
        }

        public Mesh Bake() => LatheVoxelMesher.Mesh(EvaluateSdf, resolution, boundsSize);

        /// The cached bake, re-voxelizing only when a node/resolution/bounds value actually changed since
        /// the last call. Caller does NOT own the returned Mesh (it's this asset's cache, reused across
        /// calls) — MoldedShapeModule copies its data into a LatheMeshData rather than returning it directly.
        public Mesh GetOrBakeMesh()
        {
            int h = ComputeHash();
            if (cachedMesh == null || !hashValid || h != cachedHash)
            {
                if (cachedMesh != null) Object.DestroyImmediate(cachedMesh);
                cachedMesh = Bake();
                cachedHash = h;
                hashValid = true;
            }
            return cachedMesh;
        }

        int ComputeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + resolution;
                h = h * 31 + boundsSize.GetHashCode();
                if (nodes != null)
                    foreach (var n in nodes)
                    {
                        if (n == null) continue;
                        h = h * 31 + n.kind.GetHashCode();
                        h = h * 31 + n.op.GetHashCode();
                        h = h * 31 + n.enabled.GetHashCode();
                        h = h * 31 + n.blend.GetHashCode();
                        h = h * 31 + n.position.GetHashCode();
                        h = h * 31 + n.rotationEuler.GetHashCode();
                        h = h * 31 + n.radius.GetHashCode();
                        h = h * 31 + n.radius2.GetHashCode();
                        h = h * 31 + n.boxHalfExtents.GetHashCode();
                    }
                return h;
            }
        }

        void OnDisable()
        {
            if (cachedMesh != null) { Object.DestroyImmediate(cachedMesh); cachedMesh = null; }
            hashValid = false;
        }
    }
}
