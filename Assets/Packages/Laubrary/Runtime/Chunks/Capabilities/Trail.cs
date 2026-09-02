using UnityEngine;

namespace Laubrary.Chunks
{
    /// A puff left behind on a timer by everything a producer throws — the smoke off a flying ember, the wake
    /// behind a tumbling piece of hull.
    ///
    /// The puff comes from an <see cref="IChunkTrailSource"/> asset rather than from a Chunks type, for the
    /// usual reason: Pyre already references Chunks, so Chunks can never reference Pyre back. Whatever spawns
    /// the puff owns its whole lifetime; Chunks only says when and where.
    [System.Serializable]
    public class Trail : ChunkModifier
    {
        public override string KindName => "Trail";

        public override bool CanTarget(ChunkCapability producer)
            => producer is DebrisScatter || producer is FragmentFracture;

        [Tooltip("What each puff is — an asset that can spawn one (a Pyre Blast Trail Source, a fire→smoke blast).")]
        public Object trailSource;

        [Min(0.01f)]
        [Tooltip("Seconds between puffs.")]
        public float interval = 0.08f;

        /// trailSource cast to the contract Chunks actually needs, or null if unset/incompatible.
        public IChunkTrailSource Source => trailSource as IChunkTrailSource;
    }
}
