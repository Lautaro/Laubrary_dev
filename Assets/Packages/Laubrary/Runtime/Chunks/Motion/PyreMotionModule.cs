using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>
    /// Gives a spawned Pyre blast (from the Pyre Spawner or the Spawn Formation) real physical motion — velocity,
    /// gravity, drag, an optional launch arc — instead of the "spawn it and let it play where it landed" default.
    ///
    /// Deliberately NOT an <see cref="IChunkModule"/>: it fires nothing of its own and has no burst-time hook.
    /// It exists purely to DECORATE what another module just spawned, so its whole public surface is
    /// <see cref="Apply"/> — the Pyre Spawner / Spawn Formation call it once per spawned transform, right after
    /// spawning it. It hands the transform to <see cref="ChunkModuleRunner.Move"/> rather than moving it (or
    /// adding any component to it) itself — see that runner's doc comment for why: a spawned Pyre blast comes
    /// out of PyreBlastPool and goes back into it, so a component Chunks added would survive into the pool's
    /// next, unrelated use of that same object.
    /// </summary>
    [System.Serializable]
    public class PyreMotionModule
    {
        [Tooltip("Give each spawned blast real physical motion instead of playing it where it was spawned.")]
        public bool enabled = false;

        // ── launch ────────────────────────────────────────────────────────────────
        [Tooltip("Slowest initial launch speed, world units per second.")]
        public float speedMin = 3f;
        [Tooltip("Fastest initial launch speed, world units per second.")]
        public float speedMax = 7f;

        [Tooltip("Cone the blast is launched into centres on the BURST's own direction (so a directional burst " +
                 "throws its blasts the same way it throws its debris) instead of this module's own Direction below.")]
        public bool inheritBurstDirection = true;

        [Tooltip("Centre direction of the launch cone, in degrees. 0 = right, 90 = up. Ignored while Inherit " +
                 "burst direction is on.")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle around the direction. 0 = a tight jet; 180 = a full circle (radial launch). " +
                 "Same meaning as a Chunk Spec's own Spread °.")]
        public float spreadDeg = 20f;

        [Tooltip("Extra initial +Y velocity added on top of the launch, so even a shallow cone still pops upward.")]
        public float upwardBias = 0f;

        // ── flight ────────────────────────────────────────────────────────────────
        [Tooltip("Downward acceleration, world units/sec². Higher = snappier arcs that fall fast.")]
        public float gravity = 20f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second exponential damping of velocity. 0 = none, ~1 = noticeable, ~3 = soupy.")]
        public float drag = 0.6f;
        [Tooltip("Rotate the blast to point along its travel direction instead of keeping its spawned rotation.")]
        public bool faceVelocity = false;

        // ── how long the motion drives it ────────────────────────────────────────
        [Tooltip("Drive the motion until the blast ends itself (its own lifetime/pool release), rather than a " +
                 "fixed duration. The right default for a blast that already knows when it's done.")]
        public bool untilTargetEnds = true;
        [Min(0.01f)]
        [Tooltip("Fixed-duration mode only: how many seconds the launch motion runs before the runner lets go " +
                 "(the blast itself may still be playing — this only stops the flight).")]
        public float lifeSeconds = 1f;

        // ── randomness ────────────────────────────────────────────────────────────
        [Tooltip("Fixes the random speed and cone angle so every play resolves identically. 0 = reroll every time.")]
        public int seed = 0;

        public bool Enabled => enabled;

        /// <summary>
        /// Give <paramref name="spawned"/> real launch motion, or do nothing when this module is off or nothing
        /// was actually spawned. <paramref name="index"/> is this spawn's position within its burst (0 for a
        /// single Pyre Spawn, 0..N-1 for a Spawn Formation's points) — it advances the seeded random stream so
        /// several blasts in one burst launch with DIFFERENT speeds/angles instead of all flying identically,
        /// while a fixed seed still reproduces the exact same set of launches every play.
        /// </summary>
        public void Apply(in ChunkModuleContext ctx, Transform spawned, int index)
        {
            if (!enabled || spawned == null) return;

            // seed == 0 keeps using Unity's own shared generator (reroll every play, as every other Chunks
            // random field does); a non-zero seed derives a per-index stream so index 0..N-1 fan out instead of
            // all drawing the same first value off one shared sequence.
            System.Random rng = seed != 0 ? new System.Random(unchecked(seed * 397 + index)) : null;
            float Next01() => rng != null ? (float)rng.NextDouble() : Random.value;

            float lo = Mathf.Min(speedMin, speedMax), hi = Mathf.Max(speedMin, speedMax);
            float speed = lo + (hi - lo) * Next01();

            // Matches ChunkEmitter's own convention exactly: centerDeg + Random.Range(-spreadDeg, spreadDeg).
            float centerDeg = inheritBurstDirection ? ctx.DirectionDeg : directionDeg;
            float angleDeg = centerDeg + (Next01() * 2f - 1f) * spreadDeg;
            float rad = angleDeg * Mathf.Deg2Rad;

            Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
            velocity.y += upwardBias;

            float life = untilTargetEnds ? 0f : Mathf.Max(0.01f, lifeSeconds);

            ctx.Runner.Move(spawned, velocity, gravity, drag, life, faceVelocity);
        }
    }
}
