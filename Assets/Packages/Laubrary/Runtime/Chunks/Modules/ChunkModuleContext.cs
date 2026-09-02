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
        /// The flat sortingOrder this capability falls back to when the stack has no slot for it. Carries the
        /// capability's own position in the recipe, so an unslotted output draws in stack order.
        public readonly int SortingOrder;
        /// A live MonoBehaviour on the burst container, for anything that needs coroutines or externally
        /// driven motion. Never null.
        public readonly ChunkModuleRunner Runner;
        /// The palette sampled off the exploded object, or null when the caller supplied none.
        public readonly System.Collections.Generic.IList<Color32> Palette;
        /// Animated content the CALLER wants played for this burst only, outranking anything the recipe names.
        public readonly IChunkAnimation AnimationOverride;
        /// Who is dealing the damage when the recipe has a Hits capability. Null is a legitimate unowned hit.
        public readonly Combatant Owner;

        public ChunkModuleContext(Vector3 origin, Transform container, float directionDeg, ChunkSpec spec,
                                  LayerSpec layers, int sortingOrder, ChunkModuleRunner runner,
                                  System.Collections.Generic.IList<Color32> palette,
                                  IChunkAnimation animationOverride = null, Combatant owner = null)
        {
            Origin = origin;
            Container = container;
            DirectionDeg = directionDeg;
            Spec = spec;
            Layers = layers;
            SortingOrder = sortingOrder;
            Runner = runner;
            Palette = palette;
            AnimationOverride = animationOverride;
            Owner = owner;
        }

        /// The same context aimed at a different flat order — how the dispatch gives each capability its own
        /// place in stack order without every capability having to know its index.
        public ChunkModuleContext WithSortingOrder(int sortingOrder)
            => new ChunkModuleContext(Origin, Container, DirectionDeg, Spec, Layers, sortingOrder, Runner,
                                      Palette, AnimationOverride, Owner);

        /// The concrete sortingOrder a capability should stamp on what it spawns: the named layer slot when
        /// the stack declares one, else its own place in the recipe. This is the ONE place that decision is
        /// made, so "a Layer Plan is optional" stays true without every capability re-implementing it.
        public int OrderFor(string layerName, int offset = 0) => ResolveOrder(Layers, layerName, SortingOrder, offset);

        /// The same decision as <see cref="OrderFor"/>, asked without a live burst. It exists because the
        /// editor preview has to answer "what is in front of what?" before anything has been spawned, and a
        /// preview that decided depth by its own rule would confidently show an order the burst then
        /// contradicts. <paramref name="flatOrder"/> is what an unslotted output falls back to — the emitter's
        /// own sortingOrder plus the capability's place in the stack.
        public static int ResolveOrder(LayerSpec layers, string layerName, int flatOrder, int offset = 0)
        {
            if (layers != null && layers.Has(layerName)) return layers.OrderOf(layerName, offset);
            return flatOrder + offset;
        }

        /// Applies OrderFor plus the stack's sorting LAYER (when it names a real one) to a renderer. No-op for
        /// a null renderer, so a capability never has to null-check before calling.
        public void ApplyOrder(Renderer renderer, string layerName, int offset = 0)
        {
            if (renderer == null) return;
            if (Layers != null && !string.IsNullOrEmpty(Layers.sortingLayerName)
                && LayerSpec.IsValidSortingLayer(Layers.sortingLayerName))
                renderer.sortingLayerName = Layers.sortingLayerName;
            renderer.sortingOrder = OrderFor(layerName, offset);
        }
    }
}
