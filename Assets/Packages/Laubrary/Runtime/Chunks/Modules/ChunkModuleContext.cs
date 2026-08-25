using UnityEngine;
using Laubrary.Layering;

namespace Laubrary.Chunks
{
    /// Everything a Chunks 2.0 module needs to know to fire itself, handed over as one value so a module never
    /// reaches back into the emitter for anything. Passed by `in` because it is read-only by contract: a module
    /// that wants different numbers changes its own fields, never the burst's.
    public readonly struct ChunkModuleContext
    {
        /// World position the whole burst happens at.
        public readonly Vector3 Origin;
        /// The burst's own container transform — everything a module spawns should parent under this (or under
        /// nothing, if it manages its own lifetime), so one burst tears down as one thing.
        public readonly Transform Container;
        /// The burst's centre direction in degrees (0 = +X, 90 = +Y), already resolved against any per-burst
        /// override. NaN is never passed on: the emitter substitutes the spec's own directionDeg first.
        public readonly float DirectionDeg;
        /// The recipe being fired. A module reads its OWN sub-object off this; it must not read another module's.
        public readonly ChunkSpec Spec;
        /// The composed effect's layer stack (T-0037). Never null — an unconfigured spec still has an empty
        /// LayerSpec, whose resolver degrades every name to baseOrder.
        public readonly LayerSpec Layers;
        /// The emitter's flat sortingOrder, used as the fallback whenever the layer stack has no slot for a
        /// module (which is the standalone case: no layering configured at all).
        public readonly int SortingOrder;
        /// A live MonoBehaviour on the burst container, for modules that need coroutines (stagger, delays,
        /// repeats). Never null.
        public readonly ChunkModuleRunner Runner;
        /// The palette sampled off the exploded object, or null when the caller supplied none.
        public readonly System.Collections.Generic.IList<Color32> Palette;

        public ChunkModuleContext(Vector3 origin, Transform container, float directionDeg, ChunkSpec spec,
                                  LayerSpec layers, int sortingOrder, ChunkModuleRunner runner,
                                  System.Collections.Generic.IList<Color32> palette)
        {
            Origin = origin;
            Container = container;
            DirectionDeg = directionDeg;
            Spec = spec;
            Layers = layers;
            SortingOrder = sortingOrder;
            Runner = runner;
            Palette = palette;
        }

        /// The concrete sortingOrder a module should stamp on what it spawns: the named layer slot when the
        /// stack declares one, else the emitter's flat order. This is the ONE place that decision is made, so
        /// "layering is optional" stays true without every module re-implementing the fallback.
        public int OrderFor(string layerName, int offset = 0)
        {
            if (Layers != null && Layers.Has(layerName)) return Layers.OrderOf(layerName, offset);
            return SortingOrder + offset;
        }

        /// Applies OrderFor plus the stack's sorting LAYER (when it names a real one) to a renderer. No-op for
        /// a null renderer, so a module never has to null-check before calling.
        public void ApplyOrder(Renderer renderer, string layerName, int offset = 0)
        {
            if (renderer == null) return;
            if (Layers != null && !string.IsNullOrEmpty(Layers.sortingLayerName)
                && LayerSpec.IsValidSortingLayer(Layers.sortingLayerName))
                renderer.sortingLayerName = Layers.sortingLayerName;
            renderer.sortingOrder = OrderFor(layerName, offset);
        }
    }

    /// One optional piece of a composed Chunks effect. Every Chunks 2.0 module implements this, and every one of
    /// them must work with nothing else switched on — that standalone-first guarantee is the whole point of the
    /// interface being this small.
    public interface IChunkModule
    {
        /// Whether this module takes part in a burst at all. A disabled module costs nothing and draws no UI.
        bool Enabled { get; }

        /// The name of the layer-stack slot this module draws in. Ignored (and free to be empty) when no layer
        /// stack is configured — see ChunkModuleContext.OrderFor.
        string LayerName { get; }

        /// Do the thing, once, at burst time.
        void Fire(in ChunkModuleContext ctx);
    }
}
