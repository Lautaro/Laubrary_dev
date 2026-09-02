using UnityEngine;
using Laubrary.Layering;

namespace Laubrary.Chunks
{
    /// The ordered named draw-order slots a composed recipe's output sits in — "the smoke behind the flash,
    /// the flash behind the debris" as an authored order rather than as three numbers somebody typed.
    ///
    /// A COORDINATOR: it puts nothing on screen and has no moment, so it never fires and never takes a timing
    /// lane. It contributes the one thing every producer reads — the layer stack the burst's context carries.
    /// With no Layer Plan in the recipe every producer draws in stack order behind everything slotted, which
    /// is exactly the pre-layering behaviour and the reason the slot picker is absent until this exists.
    [System.Serializable]
    public class LayerPlan : ChunkCapability
    {
        public override string KindName => "Layer Plan";

        /// Nothing is scheduled by a plan; it just is.
        public override bool OccupiesTime => false;

        [Tooltip("The ordered slots. The first draws furthest back; each following one draws in front of it.")]
        public LayerSpec layers = new LayerSpec();
    }
}
