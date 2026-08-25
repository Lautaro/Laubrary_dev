using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// The one place a burst hands control to its optional Chunks 2.0 modules. It exists so that adding a
    /// module never means editing ChunkEmitter again: the emitter makes its container and calls Run, and every
    /// module reads only its own sub-object off the spec.
    ///
    /// Firing goes through the timeline (T-0038) rather than around it: a module with no scheduled delay fires
    /// on the spot, exactly as it would if timelines did not exist, so a one-module standalone recipe never
    /// pays for the timeline it isn't using.
    public static class ChunkModules
    {
        /// The names the timeline schedules modules by. Kept as consts so the timeline UI and the dispatch
        /// cannot drift apart on a typo — the project's "never type a reference string" rule, applied to
        /// Chunks' own internals.
        public const string Splash = "Splash";
        public const string PyreSpawn = "Pyre Spawn";
        public const string Formation = "Spawn Formation";
        public const string Fragments = "Fragments";

        /// Fire every enabled module of spec at worldPos. container must be the burst's own container (Run
        /// puts the ChunkModuleRunner on it). Safe to call for a spec with no modules enabled — it costs one
        /// null check per module and adds no component.
        public static void Run(ChunkSpec spec, Vector3 worldPos, Transform container, int sortingOrder,
                               float directionDeg, IList<Color32> palette = null)
        {
            if (spec == null || container == null) return;
            if (!AnyEnabled(spec)) return;

            var runner = container.GetComponent<ChunkModuleRunner>();
            if (runner == null) runner = container.gameObject.AddComponent<ChunkModuleRunner>();

            var ctx = new ChunkModuleContext(worldPos, container, directionDeg, spec,
                                             spec.layers, sortingOrder, runner, palette);

            // The timeline's own EVENT markers, scheduled first so a marker at t=0 lands before whatever a
            // module does at t=0 — "play the bang, then throw the debris" is the order an author expects when
            // both sit on the same tick.
            if (spec.timeline != null && spec.timeline.Enabled) spec.timeline.Fire(ctx);

            Dispatch(spec.particleSplash, Splash, ctx, runner, spec);
            Dispatch(spec.fragmentSlicer, Fragments, ctx, runner, spec);
            // The formation supersedes the plain spawner when both are on: a formation IS a set of spawns, so
            // running both would double every blast at the origin. Enabling a formation is the user saying
            // "not one, several" — honour that rather than adding a stray extra.
            if (spec.spawnFormation != null && spec.spawnFormation.Enabled)
                Dispatch(spec.spawnFormation, Formation, ctx, runner, spec);
            else
                Dispatch(spec.pyreSpawn, PyreSpawn, ctx, runner, spec);

            // Every FURTHER blast group, each with its own blast, its own layer slot and its own optional
            // formation. They are dispatched after the first so a recipe reads back-to-front in the order it
            // was authored; draw order is the layer stack's job, never this loop's.
            if (spec.blastGroups != null)
                for (int i = 0; i < spec.blastGroups.Count; i++)
                    Dispatch(spec.blastGroups[i], BlastGroupTrack(spec.blastGroups[i], i), ctx, runner, spec);
        }

        /// The timeline lane name for one extra blast group. Keyed on POSITION ONLY, deliberately.
        ///
        /// The obvious alternative — folding the group's DisplayName in, so the stored track reads
        /// "Blast 2: Proper Blast" — is a trap: DisplayName follows the group's label AND its blast asset, so
        /// renaming a group or swapping its blast would silently change the key, orphan the delay already
        /// stored in timeline.tracks, and drop that group back to 0s with nothing on screen to explain it.
        /// A lane's identity is WHICH group it belongs to, and that is its position. The name the user reads
        /// is the UI's business; it is put on the lane's gutter label, never into the stored key.
        ///
        /// `group` is still taken so callers cannot accidentally key a lane to the wrong list, and so this
        /// stays the one place the rule lives if it ever needs to change again.
        public static string BlastGroupTrack(PyreSpawnModule group, int index)
            => "Blast " + (index + 2);

        /// Whether spec has any module switched on at all — the cheap early-out that keeps an old, plain
        /// debris spec byte-identical to how it behaved before modules existed (no extra component, no
        /// coroutine, nothing).
        public static bool AnyEnabled(ChunkSpec spec)
        {
            if (spec == null) return false;
            return (spec.particleSplash != null && spec.particleSplash.Enabled)
                || (spec.fragmentSlicer != null && spec.fragmentSlicer.Enabled)
                || (spec.pyreSpawn != null && spec.pyreSpawn.Enabled)
                || (spec.spawnFormation != null && spec.spawnFormation.Enabled)
                || AnyBlastGroupEnabled(spec)
                // The timeline counts even with every module off: a recipe whose only 2.0 content is a Zound
                // marker ("the debris flies AND a sound plays") is a legitimate, minimal use, and leaving the
                // timeline out of this test would early-out before its markers ever got scheduled.
                || (spec.timeline != null && spec.timeline.Enabled);
        }

        /// Whether any of the extra blast groups is switched on. Split out of AnyEnabled so the early-out
        /// stays one readable boolean expression rather than growing a loop inside it.
        ///
        /// PUBLIC on purpose: the Pyre Movement section greys itself out unless something will actually
        /// spawn, and that decision has to agree with THIS predicate or the UI lies about what the runtime
        /// does. It was briefly copied into the editor instead, which is the shape a silent drift takes —
        /// one definition changes, the other keeps claiming the old answer. One owner, called from both.
        public static bool AnyBlastGroupEnabled(ChunkSpec spec)
        {
            if (spec.blastGroups == null) return false;
            for (int i = 0; i < spec.blastGroups.Count; i++)
                if (spec.blastGroups[i] != null && spec.blastGroups[i].Enabled) return true;
            return false;
        }

        static void Dispatch(IChunkModule module, string moduleName, in ChunkModuleContext ctx,
                             ChunkModuleRunner runner, ChunkSpec spec)
        {
            if (module == null || !module.Enabled) return;

            float delay = (spec.timeline != null && spec.timeline.Enabled) ? spec.timeline.DelayFor(moduleName) : 0f;
            if (delay <= 0f) { module.Fire(ctx); return; }

            runner.StartCoroutine(FireAfter(module, ctx, delay));
        }

        static IEnumerator FireAfter(IChunkModule module, ChunkModuleContext ctx, float delay)
        {
            yield return new WaitForSeconds(delay);
            // The container can be gone by now (a short-lived burst, a scene change); firing into a destroyed
            // parent would spawn orphans that nothing ever cleans up.
            if (ctx.Container == null) yield break;
            module.Fire(ctx);
        }
    }
}
