// TapestrySpec — the top-level asset for Tapestry: a procedural, always-TILEABLE 2D texture, built as a
// stack of generator layers (each layer = one plug-in TapestryGenerator + its own modifier stack + blend
// mode/opacity), finished with a spec-wide global modifier pass over the whole composite — mirrors
// Pyre's own layer/Form/modifier-stack + globalModifiers architecture, applied to a flat pixel buffer
// instead of an animated raster stack.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Tapestry
{
    public class TapestrySpec : ScriptableObject
    {
        [Range(16, 512)] public int resolution = 128;
        public Color previewBackground = new Color(0.12f, 0.12f, 0.14f, 1f);
        public int seed = 1234;

        // Animation loop — only matters to layers with Animate Transform on; a Tapestry with none of those
        // is a single static texture and these are simply unused.
        [Range(1, 120)] public int frameCount = 24;
        [Range(1f, 60f)] public float previewFps = 12f;

        public List<TapestryLayer> layers = new List<TapestryLayer>();
        // Applied once, after every layer is composited — same "wraps every layer's own stack" role as
        // Pyre's spec.globalModifiers (confirmed precedent: Pyre.cs, applied in FrameComposer.Finish
        // as a dedicated post-composite pass, separate from and after any per-layer modifier application).
        [SerializeReference] public List<TapestryLayerModifier> globalModifiers = new List<TapestryLayerModifier>();

        public int previewLayerSel;
    }
}
