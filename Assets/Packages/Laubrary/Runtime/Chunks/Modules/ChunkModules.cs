using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// The one place a burst hands control to its recipe. It exists so that adding a kind of capability never
    /// means editing the emitter again: the emitter makes its container and calls Run, and the stack answers
    /// for everything that happens after that.
    public static class ChunkModules
    {
        /// Fire every enabled capability of spec at worldPos. container must be the burst's own container (Run
        /// puts the runner on it). Safe for an empty recipe — it costs one null check and adds no component.
        public static void Run(ChunkSpec spec, Vector3 worldPos, Transform container, int sortingOrder,
                               float directionDeg, IList<Color32> palette = null,
                               IChunkAnimation animationOverride = null, Combatant owner = null,
                               Sprite sampleSourceOverride = null)
        {
            if (spec == null || container == null) return;
            spec.UpgradeIfNeeded();

            var stack = spec.capabilities;
            if (stack == null || stack.Count == 0) return;

            var runner = container.GetComponent<ChunkModuleRunner>();
            if (runner == null) runner = container.gameObject.AddComponent<ChunkModuleRunner>();

            // The Layer Plan is resolved BEFORE anything fires, not dispatched: it is context every producer
            // reads, so it cannot be something that happens at a moment.
            var ctx = new ChunkModuleContext(worldPos, container, directionDeg, spec, spec.ResolveLayers(),
                                             sortingOrder, runner, palette, animationOverride, owner,
                                             sampleSourceOverride);

            for (int i = 0; i < stack.Count; i++)
            {
                var capability = stack[i];
                if (capability == null || !capability.enabled) continue;
                // A modifier is applied BY the producer it targets, so dispatching it here would fire it
                // against nothing; a coordinator with no moment has nothing to fire either.
                if (capability is ChunkModifier || capability is LayerPlan) continue;

                // Its place in the recipe IS its flat draw order, so an unslotted output stacks in the order
                // it was authored rather than every one of them landing on the same number. Spaced by a whole
                // ChunkModuleContext.CardOrderSpan rather than a flat 1: a capability with its own pattern
                // (e.g. a PyreBlast's Ring formation) hands out its OWN sequential offsets to every point it
                // spawns (see PyreBlast.SpawnOne -> ctx.OrderFor), up to SpawnFormation.MaxCount wide, and a
                // spacing of 1 let that spread reach straight into a neighbouring card's own numbers — so
                // reordering two cards in the stack looked like it did nothing, because the wide card's points
                // still bracketed the narrow card's single new value either way.
                var own = ctx.WithSortingOrder(sortingOrder + i * ChunkModuleContext.CardOrderSpan);

                float delay = Mathf.Max(0f, capability.delay);
                if (delay <= 0f) { capability.Fire(own); continue; }
                runner.StartCoroutine(FireAfter(capability, own, delay));
            }
        }

        static IEnumerator FireAfter(ChunkCapability capability, ChunkModuleContext ctx, float delay)
        {
            yield return new WaitForSeconds(delay);
            // The container can be gone by now (a short-lived burst, a scene change); firing into a destroyed
            // parent would spawn orphans that nothing ever cleans up.
            if (ctx.Container == null) yield break;
            capability.Fire(ctx);
        }
    }
}
