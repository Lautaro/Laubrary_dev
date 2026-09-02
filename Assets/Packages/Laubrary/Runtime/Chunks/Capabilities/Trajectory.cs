using UnityEngine;

namespace Laubrary.Chunks
{
    /// Real physical flight for the blasts a Pyre Blast spawns — velocity, gravity, drag, an optional launch
    /// arc — instead of "spawn it and let it play where it landed".
    ///
    /// It is a MODIFIER: it produces nothing and has no moment of its own, so it is never fired. The Pyre
    /// Blast asks the recipe for the Trajectory pointed at it and applies it to each transform as it spawns —
    /// which is also the only point in Chunks that ever holds a freshly spawned blast.
    ///
    /// The transform is handed to <see cref="ChunkModuleRunner.Move"/> rather than being moved (or given a
    /// component) here: a spawned blast comes out of a pool and goes back into it, so anything Chunks bolted
    /// onto it would survive into the pool's next, unrelated use of that same object.
    [System.Serializable]
    public class Trajectory : ChunkModifier
    {
        public override string KindName => "Trajectory";

        public override bool CanTarget(ChunkCapability producer) => producer is PyreBlast;

        // ── launch ────────────────────────────────────────────────────────────────
        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 3f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 7f;

        [Tooltip("Launch along the recipe's own direction (so an aimed burst throws its blasts the way it " +
                 "throws everything else) instead of the angle set here.")]
        public bool inheritBurstDirection = true;

        [Tooltip("Centre of the launch cone in degrees. 0 = right, 90 = up.")]
        public float directionDeg = 90f;
        [Range(0f, 180f)]
        [Tooltip("Cone half-angle. 0 = a tight jet; 180 = a full circle.")]
        public float spreadDeg = 20f;

        [Tooltip("Extra upward velocity on top of the launch, so even a shallow cone pops.")]
        public float upwardBias = 0f;

        // ── flight ────────────────────────────────────────────────────────────────
        [Tooltip("Downward acceleration, world units/sec².")]
        public float gravity = 20f;
        [Range(0f, 5f)]
        [Tooltip("Air resistance: per-second damping of velocity.")]
        public float drag = 0.6f;
        [Tooltip("Point the blast along its travel direction instead of keeping its spawned rotation.")]
        public bool faceVelocity = false;

        // ── how long the flight lasts ─────────────────────────────────────────────
        [Tooltip("Fly until the blast ends itself, rather than for a fixed time. The right answer for anything " +
                 "that already knows when it is done.")]
        public bool untilTargetEnds = true;
        [Min(0.01f)]
        [Tooltip("Fixed-duration mode: seconds of flight. The blast may still be playing — this only stops it moving.")]
        public float lifeSeconds = 1f;

        [Tooltip("Fixes the random speed and angle so every play is identical. 0 = reroll every time.")]
        public int seed = 0;

        /// Give <paramref name="spawned"/> its launch, or do nothing when nothing was actually spawned.
        /// <paramref name="index"/> is the spawn's position within its blast (0 for a single, 0..N-1 across a
        /// pattern): it advances the seeded stream so several blasts in one pattern fan out instead of all
        /// flying identically, while a fixed seed still reproduces the exact same set of launches.
        public void Apply(in ChunkModuleContext ctx, Transform spawned, int index)
        {
            if (!enabled || spawned == null || ctx.Runner == null) return;

            var rng = seed != 0 ? new ChunkRng(seed, index)
                                : new ChunkRng(Random.Range(1, int.MaxValue), index);

            float lo = Mathf.Min(speedMin, speedMax), hi = Mathf.Max(speedMin, speedMax);
            float speed = rng.Range(lo, hi);

            float centreDeg = inheritBurstDirection ? ctx.DirectionDeg : directionDeg;
            float rad = (centreDeg + rng.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;

            Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
            velocity.y += upwardBias;

            // 0 tells the runner "until the target dies", which is what a self-ending blast wants.
            float life = untilTargetEnds ? 0f : Mathf.Max(0.01f, lifeSeconds);

            ctx.Runner.Move(spawned, velocity, gravity, drag, life, faceVelocity);
        }
    }
}
