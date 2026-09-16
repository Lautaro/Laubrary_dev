using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The placement-and-stagger loop for a <see cref="PyreBlast"/> laid out as a Line or a Ring.
    ///
    /// Separate from the blast itself because it is a different concern: the blast decides WHAT comes out and
    /// how it looks, the formation decides WHERE each point is, and this waits out the beats between them. A
    /// coroutine also cannot read an instance field of the capability that started it without capturing it,
    /// which is what makes the split load-bearing rather than cosmetic.
    public static class SpawnFormationRunner
    {
        /// Resolve formation into placements around centre and spawn one of blast's effects at each, staggered.
        public static void Fire(in ChunkModuleContext ctx, PyreBlast blast, SpawnFormation formation, Vector3 centre)
        {
            // Pure placement has nothing of its own to spawn: an unconfigured blast degrades quietly — nothing
            // spawned, no exception — rather than erroring in the middle of a burst.
            if (blast == null || formation == null || !blast.HasSpawner) return;

            var placements = new List<SpawnPlacement>(Mathf.Max(1, formation.count));
            formation.Resolve(centre, placements);
            if (placements.Count == 0) return;

            List<SpawnPlacement> delayed = null;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                if (p.Delay <= 0f) { blast.SpawnOne(ctx, p.Position, p.Index, centre); continue; }
                (delayed ??= new List<SpawnPlacement>()).Add(p);
            }

            // staggerSeconds == 0 resolves every placement's Delay to 0, so `delayed` is never populated and
            // no coroutine is ever started — the "fires at once and costs nothing" promise, made true here
            // rather than re-checked in every caller.
            if (delayed == null) return;

            delayed.Sort((a, b) => a.Delay.CompareTo(b.Delay));
            ctx.Runner.StartCoroutine(FireStaggered(ctx, blast, delayed, centre));
        }

        static IEnumerator FireStaggered(ChunkModuleContext ctx, PyreBlast blast, List<SpawnPlacement> delayed,
                                         Vector3 centre)
        {
            float elapsed = 0f;
            for (int i = 0; i < delayed.Count; i++)
            {
                float wait = delayed[i].Delay - elapsed;
                if (wait > 0f) yield return new WaitForSeconds(wait);
                elapsed = delayed[i].Delay;
                // The container can be gone by the time a later point wakes (a short burst, a scene change) —
                // bail rather than spawn into a destroyed parent.
                if (ctx.Container == null) yield break;
                blast.SpawnOne(ctx, delayed[i].Position, delayed[i].Index, centre);
            }
        }
    }
}
