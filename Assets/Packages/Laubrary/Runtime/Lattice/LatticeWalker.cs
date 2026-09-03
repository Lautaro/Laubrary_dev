using UnityEngine;

namespace Laubrary.Lattice
{
    /// <summary>
    /// Headless movement along the edges of a <see cref="LatticeGraph"/>.
    /// Input is a 2D stick vector in lattice-plane coordinates; each tick the
    /// walker picks the cardinal direction closest to the input that has an
    /// edge leaving its current cell, and advances along it. It can never leave
    /// the graph. Plain C#: drive it from a MonoBehaviour, a test, or anything
    /// else, and read <see cref="Position"/> back.
    ///
    /// Position is stored as a cell plus a sub-cell offset in (-CellSize/2,
    /// +CellSize/2]. The owning cell hands over to the neighbour at the
    /// midpoint of an edge, so the offset is rarely near zero while travelling.
    /// </summary>
    public sealed class LatticeWalker
    {
        public readonly LatticeGraph Graph;

        /// <summary>World units per second.</summary>
        public float Speed = 4f;

        /// <summary>Input magnitude below which the walker stands still.</summary>
        public float InputDeadZone = 0.15f;

        /// <summary>Optional: when set, every movement tick sweeps coverage along the path moved.</summary>
        public LatticeSweep Sweep;

        public int CellX { get; private set; }
        public int CellY { get; private set; }

        /// <summary>Sub-cell offset from the owning cell's centre, in lattice-plane units.</summary>
        public float OffsetU { get; private set; }
        public float OffsetV { get; private set; }

        public bool IsMoving { get; private set; }

        /// <summary>Direction index (see <see cref="LatticeGraph.Dirs"/>) moved along last tick, or -1.</summary>
        public int LastMoveDir { get; private set; } = -1;

        public (int x, int y) Cell => (CellX, CellY);

        public Vector3 Position => Graph.CellWorld(CellX, CellY) + Graph.PlaneVector(OffsetU, OffsetV);

        public LatticeWalker(LatticeGraph graph)
        {
            Graph = graph;
        }

        /// <summary>Teleports the walker to a cell centre.</summary>
        public void SetCell(int x, int y)
        {
            CellX = x; CellY = y;
            OffsetU = 0f; OffsetV = 0f;
            IsMoving = false;
            LastMoveDir = -1;
        }

        /// <summary>
        /// Advances by one frame. Returns true if the walker moved. Turning
        /// onto a perpendicular edge mid-segment first walks back to the
        /// vertex at normal speed, so no tick ever moves further than
        /// Speed * dt. Input pointing away from every available edge
        /// (behind the walker) does not move it.
        /// </summary>
        public bool Tick(float inputU, float inputV, float dt)
        {
            float mag = Mathf.Sqrt(inputU * inputU + inputV * inputV);
            if (mag < InputDeadZone) { IsMoving = false; return false; }

            // Closest cardinal direction the walker can go. Perpendicular
            // (dot = 0) still qualifies, so holding a direction with no edge
            // slides along whatever edge exists instead of freezing.
            int bestDir = -1;
            float bestDot = -1f;
            int bestRank = 0;
            for (int d = 0; d < 4; d++)
            {
                var (dx, dy) = LatticeGraph.Dirs[d];
                float dot = (inputU * dx + inputV * dy) / mag;
                if (dot < 0f) continue;
                int rank = MoveRank(d);
                if (rank == 0) continue;
                // On equal angle a real edge beats merely returning to the centre.
                if (dot < bestDot || (dot == bestDot && rank <= bestRank)) continue;
                bestDot = dot;
                bestRank = rank;
                bestDir = d;
            }
            if (bestDir < 0) { IsMoving = false; return false; }

            // A turn requested while still offset on the other axis: head back
            // to the vertex along that axis this tick instead of snapping.
            bool bestOnU = bestDir == LatticeGraph.East || bestDir == LatticeGraph.West;
            int moveDir = bestDir;
            const float vertexEpsilon = 0.0005f;
            if (bestOnU && Mathf.Abs(OffsetV) > vertexEpsilon)
                moveDir = OffsetV > 0f ? LatticeGraph.South : LatticeGraph.North;
            else if (!bestOnU && Mathf.Abs(OffsetU) > vertexEpsilon)
                moveDir = OffsetU > 0f ? LatticeGraph.West : LatticeGraph.East;

            Vector3 before = Position;
            var (mdx, mdy) = LatticeGraph.Dirs[moveDir];
            float step = Speed * dt;
            float u = OffsetU + mdx * step;
            float v = OffsetV + mdy * step;
            if (moveDir != bestDir)
            {
                // Walking back to the vertex: stop exactly on it, never past.
                if (OffsetU > 0f) u = Mathf.Clamp(u, 0f, OffsetU);
                else if (OffsetU < 0f) u = Mathf.Clamp(u, OffsetU, 0f);
                if (OffsetV > 0f) v = Mathf.Clamp(v, 0f, OffsetV);
                else if (OffsetV < 0f) v = Mathf.Clamp(v, OffsetV, 0f);
            }

            // Heading back to the cell centre along an edge that does not
            // continue on the far side: stop exactly at the vertex.
            if (Graph.EdgeFrom(CellX, CellY, moveDir) == null)
            {
                if (mdx > 0) u = Mathf.Min(u, 0f); else if (mdx < 0) u = Mathf.Max(u, 0f);
                if (mdy > 0) v = Mathf.Min(v, 0f); else if (mdy < 0) v = Mathf.Max(v, 0f);
            }

            // Hand the offset over to the neighbour when it crosses the midpoint
            // of an edge, or stop at the midpoint if the edge is missing.
            float half = Graph.CellSize * 0.5f;
            if (u >= half)
            {
                if (HasEdge(CellX, CellY, LatticeGraph.East)) { CellX++; u -= Graph.CellSize; }
                else u = half - 0.001f;
            }
            else if (u <= -half)
            {
                if (HasEdge(CellX, CellY, LatticeGraph.West)) { CellX--; u += Graph.CellSize; }
                else u = -half + 0.001f;
            }
            if (v >= half)
            {
                if (HasEdge(CellX, CellY, LatticeGraph.North)) { CellY++; v -= Graph.CellSize; }
                else v = half - 0.001f;
            }
            else if (v <= -half)
            {
                if (HasEdge(CellX, CellY, LatticeGraph.South)) { CellY--; v += Graph.CellSize; }
                else v = -half + 0.001f;
            }

            OffsetU = u; OffsetV = v;
            IsMoving = Position != before;
            LastMoveDir = IsMoving ? moveDir : -1;
            if (IsMoving) Sweep?.Sweep(before, Position);
            return IsMoving;
        }

        /// <summary>
        /// 2: an edge leaves the current cell that way. 1: no edge, but the
        /// walker is offset on that axis and the direction points back toward
        /// the cell centre (it is standing on that edge already). 0: closed.
        /// </summary>
        private int MoveRank(int dir)
        {
            if (Graph.EdgeFrom(CellX, CellY, dir) != null) return 2;
            switch (dir)
            {
                case LatticeGraph.East:  return OffsetU < 0f ? 1 : 0;
                case LatticeGraph.West:  return OffsetU > 0f ? 1 : 0;
                case LatticeGraph.North: return OffsetV < 0f ? 1 : 0;
                default:                 return OffsetV > 0f ? 1 : 0;
            }
        }

        private bool HasEdge(int x, int y, int dir)
        {
            var (dx, dy) = LatticeGraph.Dirs[dir];
            return Graph.InBounds(x + dx, y + dy) && Graph.EdgeFrom(x, y, dir) != null;
        }
    }
}
