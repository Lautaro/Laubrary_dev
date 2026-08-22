// SkeletonSweepModule — sweeps a 2D cross-section along a hand-authored GRAPH of nodes and edges, not just
// one continuous analytic curve (ProfileSweepModule). This is the generalization the original design
// conversation flagged as needing "click-to-place gizmo authoring" — see LatheWindow.Skeleton.cs for the
// interactive editor; nodes/edges are also plain reflected lists here as the precise/numeric fallback.
//
// CORNERS — the part the original idea was explicitly unsure about — are handled by decomposing the graph
// into maximal chains of valence-2 nodes (walked between "chain endpoints": any node with valence != 2)
// and sweeping each chain like a polyline, same parallel-transport technique ProfileSweepModule uses:
//   - an INTERIOR bend (valence 2, mid-chain) uses the AVERAGED incoming/outgoing tangent — a standard
//     corner-softening heuristic that reduces (does not eliminate) pinching at a sharp turn, without the
//     linear-algebra of a true per-vertex miter stretch;
//   - a BRANCH point (valence >= 3, where a real single miter direction isn't well-defined for more than
//     two tubes meeting at once) gets a joining SPHERE sized to swallow the profile's own extent, hiding
//     every incoming tube's wall inside it — the general, robust answer real pipe/road tools use for
//     junctions;
//   - a true DEAD END (valence 1) gets a flat fan cap, same convention as Profile Sweep's open ends.
// A pure closed loop (a hand-drawn ring with no branches) is swept exactly like Profile Sweep's Circle path.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModuleInfo("Skeleton Sweep", "Sweep", "graph")]
    [Serializable]
    public class SkeletonSweepModule : LatheModule
    {
        // The cross-section — same convention as Profile Sweep: a closed polygon in the swept frame's own
        // local (normal, binormal) axes.
        public List<Vector2> profile = new List<Vector2>
        {
            new Vector2(-0.1f, -0.1f), new Vector2(0.1f, -0.1f), new Vector2(0.1f, 0.1f), new Vector2(-0.1f, 0.1f),
        };

        // The skeleton itself. Authored via LatheWindow's "Edit Skeleton" click-to-place tool, or typed
        // here directly — an edge is a pair of NODE INDICES (x, y both index into `nodes`).
        public List<Vector3> nodes = new List<Vector3>();
        public List<Vector2Int> edges = new List<Vector2Int>();

        [Range(0f, 360f)] public float twistPerUnit = 0f;    // extra roll of the profile per world unit travelled
        public bool capOpenEnds = true;                       // valence-1 dead ends: flat cap vs leave open
        [Range(3, 32)] public int jointSegments = 16;         // branch-joint sphere resolution

        public override string DisplayName => "Skeleton Sweep";
        public override string Description =>
            "Sweeps a 2D cross-section along a hand-authored graph of nodes and edges — draw the skeleton "
            + "with the Edit Skeleton tool in the preview. Bends soften automatically; branch points get a "
            + "joining sphere, dead ends get a flat cap.";

        public override void Generate(LatheMeshData data)
        {
            if (profile == null || profile.Count < 3) return;
            if (nodes == null || nodes.Count < 2 || edges == null || edges.Count == 0) return;

            int n = nodes.Count;
            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++) adjacency[i] = new List<int>();
            foreach (var e in edges)
            {
                if (e.x < 0 || e.x >= n || e.y < 0 || e.y >= n || e.x == e.y) continue;
                if (!adjacency[e.x].Contains(e.y)) adjacency[e.x].Add(e.y);
                if (!adjacency[e.y].Contains(e.x)) adjacency[e.y].Add(e.x);
            }

            bool IsChainEndpoint(int idx) => adjacency[idx].Count != 2;

            var visited = new HashSet<(int, int)>();
            (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);
            var chains = new List<List<int>>();

            // Walk every chain that starts (and ends) at a true endpoint or a branch point.
            for (int start = 0; start < n; start++)
            {
                if (!IsChainEndpoint(start)) continue;
                foreach (int first in adjacency[start])
                {
                    var ek = EdgeKey(start, first);
                    if (visited.Contains(ek)) continue;
                    var chain = new List<int> { start };
                    int prev = start, cur = first;
                    int guard = 0;
                    while (true)
                    {
                        visited.Add(EdgeKey(prev, cur));
                        chain.Add(cur);
                        if (IsChainEndpoint(cur) || ++guard > n * 4) break;
                        int a = adjacency[cur][0], b = adjacency[cur][1];
                        int next = a == prev ? b : a;
                        prev = cur; cur = next;
                    }
                    chains.Add(chain);
                }
            }

            // Whatever edges are still unvisited belong to pure closed loops (every node valence 2, no
            // endpoint anywhere on that component) — walk each until it comes back to its own start.
            foreach (var e in edges)
            {
                if (e.x < 0 || e.x >= n || e.y < 0 || e.y >= n || e.x == e.y) continue;
                var ek0 = EdgeKey(e.x, e.y);
                if (visited.Contains(ek0)) continue;
                visited.Add(ek0);
                var chain = new List<int> { e.x, e.y };
                int prev = e.x, cur = e.y;
                int guard = 0;
                while (cur != e.x && ++guard <= n * 4)
                {
                    int a = adjacency[cur][0], b = adjacency[cur][1];
                    int next = a == prev ? b : a;
                    visited.Add(EdgeKey(cur, next));
                    prev = cur; cur = next;
                    chain.Add(cur);
                }
                chains.Add(chain);
            }

            var sphereDone = new HashSet<int>();   // one branch-joint sphere per node, not one per incident chain
            foreach (var chain in chains)
                SweepChain(data, chain, adjacency, sphereDone);
        }

        void SweepChain(LatheMeshData data, List<int> chainNodeIdx, List<int>[] adjacency, HashSet<int> sphereDone)
        {
            int count = chainNodeIdx.Count;
            if (count < 2) return;
            bool closed = chainNodeIdx[0] == chainNodeIdx[count - 1] && count > 2;
            int ringCount = closed ? count - 1 : count;
            if (ringCount < 2) return;

            var centers = new Vector3[ringCount];
            for (int i = 0; i < ringCount; i++) centers[i] = nodes[chainNodeIdx[i]];

            // Tangents: a chain endpoint uses its one adjacent segment; an interior bend AVERAGES the
            // incoming/outgoing segment directions — softens (does not eliminate) pinching at a sharp turn.
            var tangents = new Vector3[ringCount];
            for (int i = 0; i < ringCount; i++)
            {
                Vector3 t;
                if (closed)
                {
                    Vector3 prevP = centers[(i - 1 + ringCount) % ringCount];
                    Vector3 nextP = centers[(i + 1) % ringCount];
                    t = (centers[i] - prevP).normalized + (nextP - centers[i]).normalized;
                }
                else if (i == 0) t = centers[1] - centers[0];
                else if (i == ringCount - 1) t = centers[ringCount - 1] - centers[ringCount - 2];
                else t = (centers[i] - centers[i - 1]).normalized + (centers[i + 1] - centers[i]).normalized;
                tangents[i] = t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
            }

            // Parallel-transport frame — identical technique to Profile Sweep's own path sweep.
            var normals = new Vector3[ringCount];
            var binormals = new Vector3[ringCount];
            Vector3 seed = Mathf.Abs(Vector3.Dot(tangents[0], Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            normals[0] = Vector3.ProjectOnPlane(seed, tangents[0]).normalized;
            binormals[0] = Vector3.Cross(tangents[0], normals[0]);
            for (int i = 1; i < ringCount; i++)
            {
                Vector3 nrm = Vector3.ProjectOnPlane(normals[i - 1], tangents[i]);
                if (nrm.sqrMagnitude < 1e-8f) nrm = Vector3.ProjectOnPlane(binormals[i - 1], tangents[i]);
                normals[i] = nrm.normalized;
                binormals[i] = Vector3.Cross(tangents[i], normals[i]);
            }
            if (closed)
            {
                float drift = Vector3.SignedAngle(normals[0], normals[ringCount - 1], tangents[0]);
                for (int i = 0; i < ringCount; i++)
                {
                    var q = Quaternion.AngleAxis(-drift * (i / (float)ringCount), tangents[i]);
                    normals[i] = q * normals[i];
                    binormals[i] = q * binormals[i];
                }
            }

            int profCount = profile.Count;
            var ring = new int[ringCount, profCount];
            float traveled = 0f;
            for (int i = 0; i < ringCount; i++)
            {
                if (i > 0) traveled += Vector3.Distance(centers[i], centers[i - 1]);
                Quaternion roll = Quaternion.AngleAxis(twistPerUnit * traveled, tangents[i]);
                Vector3 nrm = roll * normals[i], b = roll * binormals[i];
                for (int p = 0; p < profCount; p++)
                {
                    Vector2 pt = profile[p];
                    Vector3 pos = centers[i] + nrm * pt.x + b * pt.y;
                    Vector3 outward = nrm * pt.x + b * pt.y;
                    ring[i, p] = data.AddVert(pos, outward.sqrMagnitude > 1e-8f ? outward.normalized : nrm);
                }
            }

            int segCount = closed ? ringCount : ringCount - 1;
            for (int i = 0; i < segCount; i++)
            {
                int ni = closed ? (i + 1) % ringCount : i + 1;
                for (int p = 0; p < profCount; p++)
                {
                    int np = (p + 1) % profCount;
                    data.AddQuad(ring[i, p], ring[ni, p], ring[ni, np], ring[i, np]);
                }
            }

            if (!closed)
            {
                CapEnd(data, ring, 0, profCount, chainNodeIdx[0], adjacency, sphereDone);
                CapEnd(data, ring, ringCount - 1, profCount, chainNodeIdx[chainNodeIdx.Count - 1], adjacency, sphereDone);
            }
        }

        void CapEnd(LatheMeshData data, int[,] ring, int i, int profCount, int nodeIdx, List<int>[] adjacency, HashSet<int> sphereDone)
        {
            int valence = adjacency[nodeIdx].Count;
            if (valence >= 3)
            {
                if (!sphereDone.Add(nodeIdx)) return;   // this branch node already got its sphere from another chain
                float maxR = 0.05f;
                foreach (var pt in profile) maxR = Mathf.Max(maxR, pt.magnitude);
                var sphere = LatheMeshBuilders.BuildScratch(PrimitiveKind.Sphere, maxR * 1.25f, 0f, jointSegments);
                data.AppendTransformed(sphere, Matrix4x4.Translate(nodes[nodeIdx]));
            }
            else if (capOpenEnds)
            {
                for (int p = 1; p < profCount - 1; p++)
                    data.AddTri(ring[i, 0], ring[i, p], ring[i, p + 1]);
            }
        }
    }
}
