using UnityEngine;
using Laubrary.Layering;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// Everything a capability needs to fire itself, handed over as one value so it never reaches back into
    /// the emitter for anything. Passed by `in` because it is read-only by contract: a capability that wants
    /// different numbers changes its own fields, never the burst's.
    public readonly struct ChunkModuleContext
    {
        /// How much sortingOrder space one CARD (a capability's own place in the recipe stack) reserves for
        /// itself before the next card's band begins. Sized to <see cref="SpawnFormation.MaxCount"/> — the
        /// widest a single Pyre Blast pattern's own point-by-point spread (<see cref="OrderFor"/> called once
        /// per placement, offset 0..count-1) can ever be — so that spread can never reach far enough to land
        /// inside a neighbouring card's own numbers, however the recipe is reordered. <see
        /// cref="ChunkModules.Run"/> (runtime) and the Chunks editor preview's own OrderFor both derive a
        /// card's base sortingOrder from this ONE constant so they never disagree about what draws in front of
        /// what — do not let a second copy of this number exist anywhere else.
        public const int CardOrderSpan = SpawnFormation.MaxCount;

        /// World position the whole burst happens at.
        public readonly Vector3 Origin;
        /// The burst's own container transform — everything a capability spawns should parent under this (or
        /// under nothing, if it manages its own lifetime), so one burst tears down as one thing.
        public readonly Transform Container;
        /// The burst's direction in degrees (0 = +X, 90 = +Y), already resolved against any per-burst override.
        /// NaN is never passed on: the emitter substitutes the recipe's own directionDeg first.
        public readonly float DirectionDeg;
        /// The recipe being fired. A capability reads its OWN fields off itself; the recipe is here so it can
        /// ask for the modifiers pointed at it.
        public readonly ChunkSpec Spec;
        /// The recipe's layer stack. Never null — a recipe with no Layer Plan still gets an empty LayerSpec,
        /// whose resolver degrades every name to the flat order below.
        public readonly LayerSpec Layers;
        /// The recipe's Depth list, or null when it has none. When it exists it decides the draw order of
        /// everything the burst puts on screen, and the recipe's stack order stops affecting depth entirely.
        public readonly LayerPlan Plan;
        /// The BURST's own sortingOrder — the number the emitter was authored with, before any card's place in
        /// the stack is added to it. It is the floor the Depth list builds its rows on, which is what keeps a
        /// planned burst sitting where the emitter put it instead of dropping to 0 (T-0349 D3).
        public readonly int BurstOrder;
        /// The flat sortingOrder this capability falls back to when the recipe has no Depth list. Carries the
        /// capability's own position in the recipe, so an unplanned output draws in stack order.
        public readonly int SortingOrder;
        /// A live MonoBehaviour on the burst container, for anything that needs coroutines or externally
        /// driven motion. Never null.
        public readonly ChunkModuleRunner Runner;
        /// The palette sampled off the exploded object, or null when the caller supplied none.
        public readonly System.Collections.Generic.IList<Color32> Palette;
        /// The sprite to cut sampled debris pieces out of, when the caller has one live (a Zoe's current
        /// lauminary frame) — the Sprite counterpart to <see cref="Palette"/>. Null when the caller supplied
        /// none, in which case a Sampled-visual capability falls back to its own authored source field, so a
        /// standalone burst (no Zoe context) behaves exactly as before this existed.
        public readonly Sprite SampleSourceOverride;
        /// Animated content the CALLER wants played for this burst only, outranking anything the recipe names.
        public readonly IChunkAnimation AnimationOverride;
        /// Who is dealing the damage when the recipe has a Hits capability. Null is a legitimate unowned hit.
        public readonly Combatant Owner;

        public ChunkModuleContext(Vector3 origin, Transform container, float directionDeg, ChunkSpec spec,
                                  LayerSpec layers, int sortingOrder, ChunkModuleRunner runner,
                                  System.Collections.Generic.IList<Color32> palette,
                                  IChunkAnimation animationOverride = null, Combatant owner = null,
                                  Sprite sampleSourceOverride = null,
                                  LayerPlan plan = null, int? burstOrder = null)
        {
            Origin = origin;
            Container = container;
            DirectionDeg = directionDeg;
            Spec = spec;
            Layers = layers;
            Plan = plan;
            BurstOrder = burstOrder ?? sortingOrder;
            SortingOrder = sortingOrder;
            Runner = runner;
            Palette = palette;
            AnimationOverride = animationOverride;
            Owner = owner;
            SampleSourceOverride = sampleSourceOverride;
        }

        /// The same context aimed at a different flat order — how the dispatch gives each capability its own
        /// place in stack order without every capability having to know its index.
        public ChunkModuleContext WithSortingOrder(int sortingOrder)
            => new ChunkModuleContext(Origin, Container, DirectionDeg, Spec, Layers, sortingOrder, Runner,
                                      Palette, AnimationOverride, Owner, SampleSourceOverride, Plan, BurstOrder);

        /// The concrete sortingOrder a capability should stamp on what it spawns: its row in the recipe's
        /// Depth list when there is one, else its own place in the recipe. This is the ONE place that decision
        /// is made, so "a Depth list is optional" stays true without every capability re-implementing it.
        ///
        /// <paramref name="instance"/> addresses ONE piece/point of the capability (0-based), or -1 for "all
        /// of it on one row". <paramref name="sub"/> sub-orders renderers that share a row and is confined to
        /// that row's own gap.
        public int OrderFor(ChunkCapability capability, int instance, int sub = 0)
            => ResolveOrder(Plan, capability != null ? capability.id : null, instance, BurstOrder,
                            SortingOrder, sub);

        /// The same decision as <see cref="OrderFor"/>, asked without a live burst. It exists because the
        /// editor preview has to answer "what is in front of what?" before anything has been spawned, and a
        /// preview that decided depth by its own rule would confidently show an order the burst then
        /// contradicts. <paramref name="flatOrder"/> is what an unplanned output falls back to — the emitter's
        /// own sortingOrder plus the capability's place in the stack.
        public static int ResolveOrder(LayerPlan plan, string capabilityId, int instance, int burstOrder,
                                       int flatOrder, int sub = 0)
        {
            if (plan != null && plan.RowCount > 0)
                return plan.OrderFor(capabilityId, instance, burstOrder, sub);
            return flatOrder + sub;
        }

        /// Applies OrderFor plus the stack's sorting LAYER (when it names a real one) to a renderer. No-op for
        /// a null renderer, so a capability never has to null-check before calling.
        public void ApplyOrder(Renderer renderer, ChunkCapability capability, int instance, int sub = 0)
        {
            if (renderer == null) return;
            if (Layers != null && !string.IsNullOrEmpty(Layers.sortingLayerName)
                && LayerSpec.IsValidSortingLayer(Layers.sortingLayerName))
                renderer.sortingLayerName = Layers.sortingLayerName;
            renderer.sortingOrder = OrderFor(capability, instance, sub);
        }
    }
}
