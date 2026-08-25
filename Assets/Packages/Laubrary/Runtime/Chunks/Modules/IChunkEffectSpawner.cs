using UnityEngine;

namespace Laubrary.Chunks
{
    /// How Chunks spawns somebody else's effect — a Pyre blast, most of the time — without ever referencing
    /// that tool. Same reasoning as IChunkAnimation and IChunkTrailSource, and for the same hard reason: Pyre
    /// already references Chunks (PyreChunkAnimation implements IChunkAnimation), so Chunks referencing Pyre
    /// back would be an assembly cycle Unity refuses to compile. The other tool implements this; Chunks only
    /// ever sees the interface.
    ///
    /// It returns the spawned Transform rather than being fire-and-forget, because the Pyre Movement module
    /// has to be able to fly what was just spawned. Returning a plain Transform keeps the contract free of any
    /// type Chunks cannot name.
    public interface IChunkEffectSpawner
    {
        /// Spawn one instance and return its transform, or null if it could not be spawned (an unassigned
        /// spec, say). The implementation owns the instance's whole lifetime — Chunks never destroys or
        /// releases what it gets back, it only reads and moves it while it is alive.
        Transform SpawnEffect(Vector3 worldPos, float rotationDeg, float scale,
                              string sortingLayerName, int sortingOrder);
    }
}
