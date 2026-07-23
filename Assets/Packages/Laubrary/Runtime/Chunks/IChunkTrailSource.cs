using UnityEngine;

namespace Laubrary.Chunks
{
    /// A short-lived visual puff a Chunk spawns at its own position on a timer while it's alive — a smoke/fire
    /// trail. Same reasoning as IChunkAnimation: Chunks never references Pyre directly (Pyre already depends on
    /// Chunks, for IChunkAnimation, so the reverse reference would cycle) — Pyre instead implements this
    /// interface with a Pyre-backed puff and Chunks only ever sees the interface.
    public interface IChunkTrailSource
    {
        /// Spawn one puff at worldPos, sharing sortingOrder with the chunk that spawned it. Fire-and-forget —
        /// the implementation owns the puff's own lifetime/cleanup.
        void SpawnPuff(Vector3 worldPos, int sortingOrder);
    }
}
