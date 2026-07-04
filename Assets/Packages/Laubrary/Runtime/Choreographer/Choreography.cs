using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Choreographer
{
    /// A Choreography describes the collective motion of N Dancers as ONE reusable, unitless shape.
    /// A single Path (a spline) says how one dancer travels; a Spread says where each dancer starts
    /// (a line that can bend into a full circle); a Facing decides whether each dancer's path is rotated
    /// to follow that curve or all kept parallel. It is deliberately N-agnostic — everything is a function
    /// of the normalised index ni = i/(N-1), so the same asset drives 3 dancers or 300, packing tighter as
    /// N grows. Geometry is normalised (a ~1×1 box around the origin); the consumer scales it into the world.
    /// A Dancer is nothing but a sampled pose — this asset never owns GameObjects.
    [CreateAssetMenu(menuName = "Laubrary/Choreographer/Choreography", fileName = "Choreography")]
    public class Choreography : ScriptableObject
    {
        // ── Path: the shared spline one dancer travels, authored path-local with point[0] at the origin ──
        [Tooltip("Control points of the path a single dancer travels, in normalised space. Point 0 is the emit origin.")]
        public List<Vector2> pathPoints = new() { Vector2.zero, new Vector2(1f, 0f) };
        [Tooltip("Smooth the path through its points (Catmull-Rom) instead of straight segments.")]
        public bool smooth = true;
        [Tooltip("Travel at a constant speed regardless of how the control points bunch up (arc-length paced).")]
        public bool constantSpeed = true;

        // ── Spread: the locus of dancer start anchors — a line that bends into an arc, then a full circle ──
        [Tooltip("Length of the start line/arc in normalised units. Dancers are placed evenly along it by ni.")]
        public float spreadLength = 1f;
        [Range(0f, 1f)]
        [Tooltip("0 = straight line, 1 = a full circle. In between is an arc.")]
        public float spreadBend = 0f;

        // ── Facing: how each dancer's path is oriented on its anchor ──
        [Tooltip("Radial = each path rotates to follow the spread curve (fans out on a circle). Fixed = all parallel.")]
        public FacingMode facing = FacingMode.Fixed;
        [Tooltip("Fixed: the common heading in degrees. Radial: an offset added to each dancer's outward direction.")]
        public float facingAngle = 0f;

        // ── Population & timing ──
        [Min(1)]
        [Tooltip("Dancer count used by the editor preview and as the default when a player doesn't override it.")]
        public int defaultCount = 5;
        [Min(0.01f)]
        [Tooltip("Seconds for one dancer to travel the whole path.")]
        public float duration = 2f;
        [Range(0f, 1f)]
        [Tooltip("Per-dancer start delay as a fraction of duration (ni × stagger). 0 = all move together, a 'queue' otherwise.")]
        public float stagger = 0f;
        [Tooltip("Scatter plays the path forward (origin → formation); Gather plays it in reverse (arrive into formation).")]
        public Direction direction = Direction.Scatter;
        public bool loop = true;

        // ── Optional anchors (Launcher / Target). The player or preview supplies the actual positions ──
        [Tooltip("Pin the start of every dancer's journey to the Launcher, however the dancers spread or stagger.")]
        public bool useLauncher = false;
        [Range(0.01f, 1f)]
        [Tooltip("Fraction of the journey spent easing away from the launcher before the authored shape takes over.")]
        public float launchBlend = 0.25f;
        [Tooltip("Home the end of every dancer's journey onto the Target coordinate (e.g. the player).")]
        public bool useTarget = false;
        [Range(0f, 1f)]
        [Tooltip("Progress at which a dancer commits to the target: it flies the exact path until here, then the " +
                 "tail is re-aimed so the endpoint lands on where the target is at THAT moment (sampled per dancer).")]
        public float retargetAt = 0.6f;

        /// One full cycle in seconds, including the stagger tail so the last dancer also completes.
        public float CycleSeconds => duration * (1f + stagger);

        // Arc-length lookup rebuilt lazily from pathPoints; not serialized. Editor edits and runtime enable
        // mark it dirty so the preview and the game always sample the current shape.
        [System.NonSerialized] PathCache cache;

        public PathCache Cache
        {
            get
            {
                if (cache == null || cache.Dirty) { cache ??= new PathCache(); cache.Rebuild(pathPoints, smooth); }
                return cache;
            }
        }

        public void MarkPathDirty() { if (cache != null) cache.Dirty = true; }

        void OnValidate() => MarkPathDirty();
    }

    public enum FacingMode { Fixed, Radial }
    public enum Direction { Scatter, Gather }
}
