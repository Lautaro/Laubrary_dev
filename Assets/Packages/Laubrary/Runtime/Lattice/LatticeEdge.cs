using System;

namespace Laubrary.Lattice
{
    /// <summary>
    /// One straight segment between two lattice-adjacent cells. Endpoints are
    /// normalised so (a,b) and (b,a) are the same edge. Carries per-chunk
    /// coverage: the segment is split into <see cref="LatticeGraph.ChunkCount"/>
    /// equal pieces, each an independent covered/uncovered flag, so partial
    /// coverage is representable at whatever resolution the graph was built with.
    /// </summary>
    public sealed class LatticeEdge : IEquatable<LatticeEdge>
    {
        public readonly int AX, AY;
        public readonly int BX, BY;

        public LatticeEdge(int ax, int ay, int bx, int by)
        {
            if (ax < bx || (ax == bx && ay <= by))
            { AX = ax; AY = ay; BX = bx; BY = by; }
            else
            { AX = bx; AY = by; BX = ax; BY = ay; }
        }

        /// <summary>True once every chunk has been covered.</summary>
        public bool Covered;

        /// <summary>
        /// Per-chunk coverage, index 0 at A and the last index at B. Null until
        /// the first chunk is touched, so untouched edges cost nothing.
        /// </summary>
        public bool[] Chunks;

        /// <summary>Touched at least once but not yet fully covered.</summary>
        public bool HasPartialCoverage => Chunks != null && !Covered;

        /// <summary>
        /// Free slot for the consumer to hang its own per-edge state on (a
        /// visibility record, a road type, anything). Lattice never reads it.
        /// </summary>
        public object UserData;

        public bool IsHorizontal => AY == BY;

        public void EnsureChunks(int count)
        {
            if (Chunks == null || Chunks.Length != count) Chunks = new bool[count];
        }

        public void ResetCoverage()
        {
            Covered = false;
            Chunks = null;
        }

        public bool Equals(LatticeEdge e)
            => e != null && e.AX == AX && e.AY == AY && e.BX == BX && e.BY == BY;

        public override bool Equals(object obj) => obj is LatticeEdge e && Equals(e);

        public override int GetHashCode()
            => (AX * 73856093) ^ (AY * 19349663) ^ (BX * 83492791) ^ (BY * 15485863);

        public override string ToString() => $"({AX},{AY})-({BX},{BY})";
    }
}
