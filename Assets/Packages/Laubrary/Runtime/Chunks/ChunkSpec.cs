using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// A ChunkSpec is the reusable recipe for one debris burst — the shrapnel a game throws when something explodes.
    /// It says how many bits fly out, how fast and in which cone, how they arc under gravity, drag and spin, how big
    /// and how long they live, how they tint and fade, and how they behave when they hit a floor. It ships ZERO
    /// assets: if <see cref="sprites"/> is empty, chunks use a procedural pixel-square built at runtime, tinted by
    /// the SpriteRenderer's colour. Hand it to a <see cref="ChunkEmitter"/> or the static <c>Chunks.Burst</c> API.
    [CreateAssetMenu(menuName = "Laubrary/Chunks/Chunk Spec", fileName = "Chunks")]
    public class ChunkSpec : ScriptableObject
    {
        // ── Emission ──────────────────────────────────────────────────────────────
        [Header("Emission")]
        [Tooltip("Fewest chunks a burst spawns.")]
        [Min(0)] public int countMin = 8;
        [Tooltip("Most chunks a burst spawns (inclusive). Each burst picks a random count in [min, max].")]
        [Min(0)] public int countMax = 16;

        [Tooltip("Slowest initial launch speed, world units/sec.")]
        public float speedMin = 3f;
        [Tooltip("Fastest initial launch speed, world units/sec.")]
        public float speedMax = 7f;

        [Tooltip("Centre direction of the cone in degrees. 0 = +X (right), 90 = +Y (up).")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle (radial burst).")]
        public float spreadDeg = 180f;
        [Tooltip("Extra initial +Y velocity added to every chunk, so even a radial burst still pops upward.")]
        public float upwardBias = 1.5f;

        // ── Physics ───────────────────────────────────────────────────────────────
        [Header("Physics")]
        [Tooltip("Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.")]
        public float gravity = 20f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.6f;

        [Tooltip("Slowest spin, degrees/sec.")]
        public float angularSpeedMin = 90f;
        [Tooltip("Fastest spin, degrees/sec (sign is randomised per chunk).")]
        public float angularSpeedMax = 540f;
        [Tooltip("Rotate each chunk to point along its travel direction instead of spinning freely.")]
        public bool faceVelocity = false;

        // ── Life / look ───────────────────────────────────────────────────────────
        [Header("Life / look")]
        [Tooltip("Shortest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMin = 0.6f;
        [Tooltip("Longest lifetime, seconds.")]
        [Min(0.01f)] public float lifeMax = 1.1f;

        [Tooltip("Smallest chunk size, world units.")]
        [Min(0.001f)] public float sizeMin = 0.08f;
        [Tooltip("Largest chunk size, world units.")]
        [Min(0.001f)] public float sizeMax = 0.18f;

        [Tooltip("Size multiplier over normalised life (0→1). Default holds at 1. Set a falloff to shrink as they die.")]
        public AnimationCurve sizeOverLife = DefaultSizeCurve();
        [Tooltip("Alpha over normalised life (0→1). Default holds opaque, then fades to 0 near the end.")]
        public AnimationCurve alphaOverLife = DefaultAlphaCurve();
        [Tooltip("Tint over normalised life (0→1). Multiplied onto the chunk's base colour. Default white→white.")]
        public Gradient colorOverLife = DefaultColorGradient();

        [Tooltip("Chunk sprites to pick from at random. Leave EMPTY to use a procedural pixel-square (tinted).")]
        public List<Sprite> sprites = new();
        [Tooltip("Pixels-per-unit for the procedural fallback sprite (only used when the sprite list is empty).")]
        [Min(1f)] public float pixelsPerUnit = 32f;

        // ── Floor / collision (cheap, no Physics2D) ─────────────────────────────────
        [Header("Floor / collision")]
        [Tooltip("Bounce chunks off a horizontal floor at floorY. No Physics2D colliders involved.")]
        public bool useFloor = true;
        [Tooltip("World Y of the floor the chunks land on.")]
        public float floorY = 0f;
        [Range(0f, 1f)]
        [Tooltip("Restitution on a floor hit: 0 = dead stop, 1 = full bounce.")]
        public float bounciness = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("Horizontal speed lost on each floor hit: 0 = frictionless slide, 1 = instantly stops sliding.")]
        public float floorFriction = 0.5f;
        [Tooltip("When a chunk goes slow on the floor, settle it there (stop + rest until it fades) instead of despawning.")]
        public bool restOnFloor = true;

        // ── Sampled pseudo-3D debris (optional) ──────────────────────────────────
        [Header("Sampled pseudo-3D debris (optional)")]
        [Tooltip("Instead of a flat procedural shape or a hand-authored sprite, cut small chunks directly " +
                 "out of the exploding object's own sprite and tumble them with a squash+shade trick that " +
                 "reads as a lit 3D fragment with no real 3D geometry. Leave null to use plain debris. " +
                 "Loses to animationSource if that's also set. Source texture must have Read/Write Enabled.")]
        public Sprite sampleSource;
        [Tooltip("Smallest sampled chunk size, in source-texture pixels.")]
        [Min(1)] public int samplePxMin = 5;
        [Tooltip("Largest sampled chunk size, in source-texture pixels.")]
        [Min(1)] public int samplePxMax = 20;
        [Tooltip("Tumble sampled chunks (squash + shade) instead of a flat 2D spin — the pseudo-3D trick. " +
                 "Only applies to chunks sourced from sampleSource.")]
        public bool tumble = true;
        [Tooltip("Slowest simulated tumble rate, degrees/sec.")]
        public float tumbleSpeedMin = 180f;
        [Tooltip("Fastest simulated tumble rate, degrees/sec.")]
        public float tumbleSpeedMax = 720f;
        [Range(0f, 1f)]
        [Tooltip("How strong the light/dark swing is as a chunk turns. 0 = squash only, 1 = full swing.")]
        public float tumbleShadeStrength = 0.6f;

        /// True when a chunk should be sourced by sampling sampleSource rather than sprites/procedural.
        public bool UsesSampledDebris => sampleSource != null;

        // ── Animated content (optional) ──────────────────────────────────────────
        [Header("Animated content (optional)")]
        [Tooltip("Optional animated content every chunk plays instead of a static/procedural sprite — an asset " +
                 "implementing IChunkAnimation (e.g. a Pyre Blast Chunk Animation or a Zoe Chunk Animation). " +
                 "Leave empty for plain debris.")]
        public Object animationSource;

        /// animationSource cast to the interface Chunks actually needs, or null if unset/incompatible.
        public IChunkAnimation AnimationSource => animationSource as IChunkAnimation;

        void OnValidate()
        {
            countMin = Mathf.Max(0, countMin);
            countMax = Mathf.Max(countMin, countMax);
            speedMin = Mathf.Max(0f, speedMin);
            speedMax = Mathf.Max(speedMin, speedMax);
            spreadDeg = Mathf.Clamp(spreadDeg, 0f, 180f);
            gravity = Mathf.Max(0f, gravity);
            drag = Mathf.Clamp(drag, 0f, 20f);
            angularSpeedMin = Mathf.Max(0f, angularSpeedMin);
            angularSpeedMax = Mathf.Max(angularSpeedMin, angularSpeedMax);
            lifeMin = Mathf.Max(0.01f, lifeMin);
            lifeMax = Mathf.Max(lifeMin, lifeMax);
            sizeMin = Mathf.Max(0.001f, sizeMin);
            sizeMax = Mathf.Max(sizeMin, sizeMax);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            bounciness = Mathf.Clamp01(bounciness);
            floorFriction = Mathf.Clamp01(floorFriction);
            samplePxMin = Mathf.Max(1, samplePxMin);
            samplePxMax = Mathf.Max(samplePxMin, samplePxMax);
            tumbleSpeedMin = Mathf.Max(0f, tumbleSpeedMin);
            tumbleSpeedMax = Mathf.Max(tumbleSpeedMin, tumbleSpeedMax);
            tumbleShadeStrength = Mathf.Clamp01(tumbleShadeStrength);
            sizeOverLife ??= DefaultSizeCurve();
            alphaOverLife ??= DefaultAlphaCurve();
            colorOverLife ??= DefaultColorGradient();
        }

        // ── default curves/gradient (also used as field initialisers so CreateInstance assets look good too) ──
        static AnimationCurve DefaultSizeCurve() => AnimationCurve.Constant(0f, 1f, 1f);

        static AnimationCurve DefaultAlphaCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f));

        static Gradient DefaultColorGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
