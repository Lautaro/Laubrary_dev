using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>
    /// Lays out several spawn points in a shape and staggers when each one fires — WHERE and WHEN, never WHAT.
    /// The picking (which blast, or which one of a pool) is entirely the Pyre Spawner's own job
    /// (<see cref="PyreSpawnModule"/>); this module resolves its own <see cref="SpawnFormation"/> into
    /// <see cref="SpawnPlacement"/>s and calls <see cref="PyreSpawnModule.SpawnOne"/> once per placement, so
    /// picking/rotation/scaling logic exists in exactly one place. Per the design doc, an enabled formation
    /// SUPERSEDES the plain Pyre Spawner — <see cref="ChunkModules.Run"/> is what enforces that, not this class.
    /// </summary>
    [System.Serializable]
    public class SpawnFormationModule : IChunkModule
    {
        [Tooltip("Place several spawns in a shape and stagger when each one fires, instead of one at the origin.")]
        public bool enabled = false;

        [Tooltip("Which layer-stack slot the formation's spawns draw in. Ignored when no layer stack is configured.")]
        public string layerName = "Blast";

        [Tooltip("Where each spawn point sits and how long after burst-start it fires.")]
        public SpawnFormation formation = new SpawnFormation();

        public bool Enabled => enabled;
        public string LayerName => layerName;

        public void Fire(in ChunkModuleContext ctx)
        {
            // LayerName is the FORMATION's own slot: a formation is its own visual band, so its spawns must
            // not silently inherit the Pyre Spawner's slot instead. The loop itself is shared with a blast
            // group's own formation — see SpawnFormationRunner.
            SpawnFormationRunner.Fire(ctx, ctx.Spec != null ? ctx.Spec.pyreSpawn : null,
                                      formation, LayerName, ctx.Origin);
        }
    }
}
