using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lazor
{
    /// <summary>
    /// A reusable vector line-art asset — the CRUDable unit Lazor authors and the game draws instead of a
    /// sprite. Holds a stack of <see cref="LazorLayer"/>s (drawn back-to-front) plus the design grid it was
    /// authored on. Nothing is baked: layers stay separate so they can be manipulated individually at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Lazor/Shape", fileName = "LazorShape")]
    public class LazorShape : ScriptableObject
    {
        [Tooltip("Resolution of the authoring grid, in cells across. Purely an authoring aid — points are free floats " +
                 "and geometry is normalized by this, so shapes stay resolution-independent.")]
        [Range(4, 64)] public int gridResolution = 16;

        [Tooltip("Layers, drawn back to front. Index 0 is behind the rest.")]
        public List<LazorLayer> layers = new List<LazorLayer>();

        /// <summary>Populate a fresh asset with a simple, recognizable default so the window is never empty.</summary>
        public void AddExampleContent()
        {
            gridResolution = 16;
            layers.Clear();

            // A classic Asteroids-style ship: a triangular hull with a notched tail.
            var hull = new LazorLayer("Hull")
            {
                color = Color.cyan,
                thickness = 0.03f,
                symmetryEnabled = true,
                symmetryCount = 2,
                symmetryAngle = 0f,      // mirror across the vertical axis: draw the right half, get the left
                symmetryReflect = true,
            };
            // Draw only the right half; the left half is produced by the mirror.
            hull.paths.Add(new LazorPath(false)
            {
                points = new List<Vector2>
                {
                    new Vector2(0f, 5f),    // nose
                    new Vector2(3f, -4f),   // right wing tip
                    new Vector2(1.2f, -2.5f), // right tail notch
                    new Vector2(0f, -3.2f), // tail center
                },
            });
            layers.Add(hull);
        }

        public LazorShape Clone()
        {
            var s = CreateInstance<LazorShape>();
            s.gridResolution = gridResolution;
            s.layers = new List<LazorLayer>(layers.Count);
            foreach (var l in layers) s.layers.Add(l != null ? l.Clone() : new LazorLayer());
            return s;
        }
    }
}
