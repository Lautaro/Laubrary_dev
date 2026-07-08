using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lazor
{
    /// <summary>
    /// A single layer of a <see cref="LazorShape"/>: a set of strokes that share one style (color, width,
    /// caps, joins, blend) and one optional mirror/symmetry setting. Layers are kept fully separate and are
    /// never baked together, so any layer can be recolored, hidden, or re-mirrored on its own at runtime.
    /// </summary>
    [System.Serializable]
    public class LazorLayer
    {
        [Tooltip("Layer name, shown in the layer list. Purely for the author's convenience.")]
        public string name = "Layer";

        [Tooltip("When off, the layer is skipped everywhere: preview, thumbnails, and runtime.")]
        public bool enabled = true;

        [Tooltip("Stroke color. With an Additive/Screen blend the alpha acts as glow intensity.")]
        public Color color = Color.cyan;

        [Tooltip("Stroke width, as a fraction of the shape's normalized size (so it scales with the shape).")]
        [Min(0f)] public float thickness = 0.03f;

        [Tooltip("How the ends of open strokes are drawn.")]
        public LazorCap cap = LazorCap.Round;

        [Tooltip("How corners between segments are joined.")]
        public LazorJoin join = LazorJoin.Round;

        [Tooltip("How the color composites over the background. Additive or Screen give the glowing laser look.")]
        public LazorBlend blend = LazorBlend.Additive;

        [Tooltip("Whether the stroke lies flat in the shape plane or always faces the camera.")]
        public LazorFacing facing = LazorFacing.Flat2D;

        [Tooltip("The strokes that make up this layer.")]
        public List<LazorPath> paths = new List<LazorPath>();

        // ---- Per-layer mirror / symmetry (expanded live by LazorGeometry, never baked into points) ----

        [Tooltip("When on, everything drawn on this layer is repeated symmetrically around the shape center.")]
        public bool symmetryEnabled = false;

        [Tooltip("How many symmetric sections to divide the shape into (2 = a single mirror line, up to 8).")]
        [Range(2, 8)] public int symmetryCount = 2;

        [Tooltip("Rotates the whole symmetry pattern. For 2 sections: 0 = left/right split, 90 = top/bottom, 45 = diagonal.")]
        public float symmetryAngle = 0f;

        [Tooltip("On = adjacent sections are mirror images (kaleidoscope). Off = each section is a pure rotation (pinwheel).")]
        public bool symmetryReflect = true;

        public LazorLayer() { }

        public LazorLayer(string name) { this.name = name; }

        public LazorLayer Clone()
        {
            var l = (LazorLayer)MemberwiseClone();
            l.paths = new List<LazorPath>(paths.Count);
            foreach (var p in paths) l.paths.Add(p != null ? p.Clone() : new LazorPath());
            return l;
        }
    }
}
