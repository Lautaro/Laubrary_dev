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

            // A classic Asteroids-style ship: a triangular hull with a notched tail. Nose points along +X —
            // Laubrary's forward convention (gameplay rotates entities via Atan2(dir.y, dir.x), which spins the
            // shape's local +X to face `dir`; the Lazor window draws a "forward" marker along +X as a reminder).
            var hull = new LazorLayer("Hull")
            {
                color = Color.cyan,
                thickness = 0.03f,
                symmetryEnabled = true,
                symmetryCount = 2,
                symmetryAngle = 90f,     // mirror across the x-axis: draw the top half, get the bottom
                symmetryReflect = true,
            };
            // Draw only the top half; the bottom half is produced by the mirror.
            hull.paths.Add(new LazorPath(false)
            {
                points = new List<Vector2>
                {
                    new Vector2(5f, 0f),      // nose (on axis)
                    new Vector2(-4f, 3f),     // top wing tip
                    new Vector2(-2.5f, 1.2f), // top tail notch
                    new Vector2(-3.2f, 0f),   // tail center (on axis)
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
