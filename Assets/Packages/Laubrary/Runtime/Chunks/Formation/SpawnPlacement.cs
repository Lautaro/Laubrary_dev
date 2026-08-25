using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>
    /// One resolved spawn point: a world position and how long after burst-start it fires. Read-only because
    /// a resolved placement is a RESULT — anything that wants different numbers re-resolves the formation
    /// rather than editing a point behind the layout's back.
    /// </summary>
    public readonly struct SpawnPlacement
    {
        /// <summary>Where to spawn, in world space (the formation's origin already folded in).</summary>
        public readonly Vector3 Position;

        /// <summary>Seconds after burst-start that this point fires. 0 = immediately.</summary>
        public readonly float Delay;

        /// <summary>
        /// This point's place in the SHAPE (0 … count-1), not its place in the firing order — the firing
        /// order is already expressed by <see cref="Delay"/>. Kept so a consumer can vary what it spawns
        /// per position (every third point, the two ends of a line) without recomputing the layout.
        /// </summary>
        public readonly int Index;

        public SpawnPlacement(Vector3 position, float delay, int index)
        {
            Position = position;
            Delay = delay;
            Index = index;
        }
    }
}
