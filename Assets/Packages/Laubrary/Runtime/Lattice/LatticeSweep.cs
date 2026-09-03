using System;
using UnityEngine;

namespace Laubrary.Lattice
{
    /// <summary>
    /// Marks edge chunks covered by a moving point. Feed it the point's
    /// position at the start and end of a frame; every chunk whose position
    /// lies under that swept path (within <see cref="Radius"/> of it) flips
    /// to covered, and events fire per chunk and per completed edge.
    ///
    /// The check is purely geometric: it has no idea which edge the mover is
    /// "on", so chunks at intersections light up from whichever direction they
    /// are actually crossed and nothing is revealed that the path did not pass.
    /// </summary>
    public sealed class LatticeSweep
    {
        public readonly LatticeGraph Graph;

        /// <summary>
        /// How close the path must pass to a chunk to cover it. Should exceed
        /// the chunk length so a fast frame cannot step over a chunk.
        /// </summary>
        public float Radius = 0.15f;

        /// <summary>The first chunk of an edge has just been covered.</summary>
        public event Action<LatticeEdge> EdgeTouched;

        /// <summary>A chunk flipped to covered: (edge, chunk index, chunk world position).</summary>
        public event Action<LatticeEdge, int, Vector3> ChunkCovered;

        /// <summary>Every chunk of the edge is now covered.</summary>
        public event Action<LatticeEdge> EdgeCovered;

        public LatticeSweep(LatticeGraph graph)
        {
            Graph = graph;
        }

        /// <summary>Covers chunks along the path from prev to curr.</summary>
        public void Sweep(Vector3 prev, Vector3 curr)
        {
            var (cx, cy) = Graph.NearestCell(curr);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!Graph.InBounds(x, y)) continue;
                TestEdge(Graph.EdgeFrom(x, y, LatticeGraph.East), prev, curr);
                TestEdge(Graph.EdgeFrom(x, y, LatticeGraph.North), prev, curr);
            }
        }

        private void TestEdge(LatticeEdge e, Vector3 prev, Vector3 curr)
        {
            if (e == null || e.Covered) return;

            Vector3 a = Graph.CellWorld(e.AX, e.AY);
            Vector3 b = Graph.CellWorld(e.BX, e.BY);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-5f) return;
            Vector3 abn = ab / len;

            float tPrev = Mathf.Clamp01(Vector3.Dot(prev - a, abn) / len);
            float tCurr = Mathf.Clamp01(Vector3.Dot(curr - a, abn) / len);
            float distPrev = Vector3.Distance(prev, a + abn * (tPrev * len));
            float distCurr = Vector3.Distance(curr, a + abn * (tCurr * len));
            if (Mathf.Min(distPrev, distCurr) > Radius) return;

            // Both ends clamp to the same t: the path only touched this edge
            // at a shared vertex without moving along it. Nothing to cover.
            if (tPrev == tCurr) return;

            int count = Graph.ChunkCount;
            bool firstTouch = e.Chunks == null;
            e.EnsureChunks(count);
            if (firstTouch) EdgeTouched?.Invoke(e);

            // Pad by ~1.5 chunks so a large single-frame step cannot skip one.
            float padT = (Graph.ChunkWorldSize * 1.5f) / len;
            float loT = Mathf.Min(tPrev, tCurr) - padT;
            float hiT = Mathf.Max(tPrev, tCurr) + padT;
            int loIdx = Mathf.Max(0, Mathf.FloorToInt(loT * count));
            int hiIdx = Mathf.Min(count - 1, Mathf.CeilToInt(hiT * count));

            bool allCovered = true;
            for (int i = 0; i < count; i++)
            {
                if (i >= loIdx && i <= hiIdx && !e.Chunks[i])
                {
                    e.Chunks[i] = true;
                    ChunkCovered?.Invoke(e, i, a + abn * ((i + 0.5f) / count * len));
                }
                if (!e.Chunks[i]) allCovered = false;
            }

            if (allCovered)
            {
                e.Covered = true;
                EdgeCovered?.Invoke(e);
            }
        }
    }
}
