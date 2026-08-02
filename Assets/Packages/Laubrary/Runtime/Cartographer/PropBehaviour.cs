using UnityEngine;

namespace Laubrary.Cartographer
{
    /// A script that acts on a PART of the level: put one on a prop's prefab, stamp the prop, and when
    /// the level builds in Play mode the spawned component is handed its context — which instance built it
    /// and which placement it came from. "Spawn the player here", "emit enemies from here", "open when the
    /// room clears" are all this shape.
    ///
    /// Cartographer owns only the hand-off; the behaviours themselves are game code, and the tool never
    /// learns what they do — the same line the tag system draws. Unlike a named spot, a behaviour scales to
    /// many copies of the same prop: each instance knows its own placement, so nothing is name-keyed.
    ///
    /// Settings live on the behaviour's prefab — different flavours of one behaviour are prefab variants.
    /// (Per-placement overrides would need a serialized payload per placement; that machinery waits for a
    /// second real use case.)
    public abstract class PropBehaviour : MonoBehaviour
    {
        /// The instance that built the level this behaviour is part of.
        public LevelInstance Level { get; private set; }

        /// The placement that put this behaviour here — its prop, cell, rotation and mirror.
        public PropPlacement Placement { get; private set; }

        public Prop Prop => Placement?.prop;
        public Vector2Int Cell => Placement != null ? Placement.cell : default;

        /// Called by LevelInstance right after the behaviour's prefab is spawned at its placement.
        public void Attach(LevelInstance level, PropPlacement placement)
        {
            Level = level;
            Placement = placement;
            OnPlaced();
        }

        /// The behaviour's entry point: the level is built, the context is set, act. Runtime only —
        /// an editing scene shows the level, not the gameplay.
        protected virtual void OnPlaced() { }

        /// The procgen pass's entry point, called by LevelProcgen.Run for every placement whose prop is
        /// marked `procgen`. This runs on a LEVEL ASSET — possibly a runtime clone, never necessarily a
        /// built instance — and it is invoked on the PREFAB's component directly, without a spawned scene
        /// object. So implementations mutate `level` only, via Paint(..., Origin.Generated) / ErasePaint /
        /// Place(..., Origin.Generated), and must never touch their transform, Level or Placement — none of
        /// those exist yet. `rng` is one stream shared across the whole run (same seed, same level); its
        /// state depends on pass order, so a behaviour that must agree with its OTHER placements derives a
        /// private System.Random from `seed` instead of drawing from the stream.
        public virtual void OnProcgen(LevelAsset level, PropPlacement placement, System.Random rng, int seed) { }
    }
}
