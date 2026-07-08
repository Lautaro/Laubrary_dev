using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lazor
{
    /// <summary>
    /// One continuous stroke: an ordered list of points, optionally closed into a loop. Points are in the
    /// shape's centered design space (grid-cell units); <see cref="LazorGeometry"/> normalizes them.
    /// A stroke carries no style of its own — style lives on the owning <see cref="LazorLayer"/>.
    /// </summary>
    [System.Serializable]
    public class LazorPath
    {
        [Tooltip("The stroke's vertices, in order, in centered grid-cell units.")]
        public List<Vector2> points = new List<Vector2>();

        [Tooltip("When on, the last point connects back to the first, making a closed outline.")]
        public bool closed = false;

        public LazorPath() { }

        public LazorPath(bool closed) { this.closed = closed; }

        public LazorPath Clone()
        {
            var p = new LazorPath(closed);
            p.points = new List<Vector2>(points);
            return p;
        }
    }
}
