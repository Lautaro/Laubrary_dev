using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The placement-and-stagger loop, extracted so it has exactly one implementation.
    ///
    /// Two callers need it and neither owns it: the standalone <see cref="SpawnFormationModule"/> (which lays
    /// out the spec's single Pyre Spawner) and a blast GROUP (<see cref="PyreSpawnModule"/> with its own
    /// formation switched on). Before this existed the loop lived inside the module, which is why a group
    /// could not have a formation of its own without copying twenty lines of coroutine and sort order.
    public static class SpawnFormationRunner
    {
        /// Resolve formation into placements around ctx.Origin and spawn one of spawner's effects at each,
        /// staggered. layerSlot is the slot the SPAWNS land in — passed explicitly because the caller's own
        /// slot, not the spawner's, is the visual band a formation occupies.
        public static void Fire(in ChunkModuleContext ctx, PyreSpawnModule spawner, SpawnFormation formation,
                                string layerSlot, Vector3 centre)
        {
            // Standalone-first: pure placement has nothing of its own to spawn. A missing or unconfigured
            // spawner degrades quietly — nothing spawned, no exception — rather than erroring.
            if (spawner == null || formation == null || !spawner.HasSpawner) return;

            var placements = new List<SpawnPlacement>(Mathf.Max(1, formation.count));
            formation.Resolve(centre, placements);
            if (placements.Count == 0) return;

            // ONE shared random sequence for every spawn this formation makes this burst — mirrors what a
            // plain Pyre Spawn burst does in its own Fire(): SpawnOne alone never reseeds, that's the
            // caller's job.
            spawner.ResetRandom();

            List<SpawnPlacement> delayed = null;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                if (p.Delay <= 0f) { spawner.SpawnOne(ctx, p.Position, p.Index, layerSlot); continue; }
                (delayed ??= new List<SpawnPlacement>()).Add(p);
            }

            // staggerSeconds == 0 resolves every placement's Delay to 0, so `delayed` is never populated and
            // no coroutine is ever started — the formation's "costs nothing at all" promise, made true here
            // rather than re-checked in every caller.
            if (delayed == null) return;

            delayed.Sort((a, b) => a.Delay.CompareTo(b.Delay));
            ctx.Runner.StartCoroutine(FireStaggered(ctx, spawner, delayed, layerSlot));
        }

        // layerSlot is passed in rather than read off a module: this coroutine must outlive the call that
        // started it without capturing one, and a static coroutine cannot read an instance field anyway
        // (a real compile error this shape has already caused once).
        static IEnumerator FireStaggered(ChunkModuleContext ctx, PyreSpawnModule spawner,
                                         List<SpawnPlacement> delayed, string layerSlot)
        {
            float elapsed = 0f;
            for (int i = 0; i < delayed.Count; i++)
            {
                float wait = delayed[i].Delay - elapsed;
                if (wait > 0f) yield return new WaitForSeconds(wait);
                elapsed = delayed[i].Delay;
                // The container can be gone by the time a later, longer-delayed point wakes (a short-lived
                // burst, a scene change) — bail rather than spawn into a destroyed parent.
                if (ctx.Container == null) yield break;
                spawner.SpawnOne(ctx, delayed[i].Position, delayed[i].Index, layerSlot);
            }
        }
    }
}
