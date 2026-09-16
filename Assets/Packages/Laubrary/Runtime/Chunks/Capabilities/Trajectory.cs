using UnityEngine;

namespace Laubrary.Chunks
{
    /// How a Trajectory aims its launch cone. Kept as a real enum (not a bool) because it has three answers,
    /// not two — <see cref="Trajectory.MigrateLegacyCurves"/> (reused as this capability's one-time-upgrade
    /// hook, same guarded pattern every other capability uses for its own migration) folds the old two-way
    /// <see cref="Trajectory.inheritBurstDirection"/> bool into this on first load, so an existing asset keeps
    /// exactly the choice it always had.
    public enum TrajectoryDirectionMode
    {
        /// The recipe's own aim (the "Burst direction" dial under the preview) — every point in the pattern
        /// shares one direction, the way a Pyre Blast itself aims.
        Burst = 0,
        /// The angle authored on this card.
        Fixed = 1,
        /// Away from where THIS spawn's own pattern started, so a ring flies out like an explosion instead of
        /// everywhere the same way.
        OutwardFromCentre = 2,
    }

    /// Real physical flight for the blasts a Pyre Blast spawns — velocity, gravity, drag, an optional launch
    /// arc — instead of "spawn it and let it play where it landed". User-facing name: **Fling**.
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
        // The class stays "Trajectory" (existing SerializeReference assets are typed by this name), but what
        // the user reads is "Fling" — the word the owner's own use-case walk reached for and could not find
        // anywhere in the window (T-0357 / WALK.md row G1).
        public override string KindName => "Fling";

        public override bool CanTarget(ChunkCapability producer) => producer is PyreBlast;

        // ── launch ────────────────────────────────────────────────────────────────
        [Tooltip("Slowest launch speed, world units/sec.")]
        public float speedMin = 3f;
        [Tooltip("Fastest launch speed, world units/sec.")]
        public float speedMax = 7f;

        // Legacy migration source ONLY — MigrateLegacyCurves() folds this into directionMode on first load and
        // it is never read directly again. Kept (rather than removed) purely so an asset saved before this
        // field existed still carries the choice it authored.
        [HideInInspector] public bool inheritBurstDirection = true;
        [SerializeField, HideInInspector] bool directionModeMigrated;

        [Tooltip("Burst follows the recipe's own aim (the 'Burst direction' dial under the preview). Fixed " +
                 "uses the angle set below. Outward from centre points each spawn away from where its own " +
                 "pattern started, so a ring flies out like an explosion instead of one shared direction.")]
        public TrajectoryDirectionMode directionMode = TrajectoryDirectionMode.Burst;

        [Tooltip("Centre of the launch cone in degrees, used in Fixed mode. 0 = right, 90 = up.")]
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

        // Defaults to 0-0 so an existing asset (which never had spin) keeps flying exactly as before: no
        // rotation is ever written unless this band is actually opened up.
        [Tooltip("Slowest spin, degrees/sec. Positive turns counter-clockwise. 0-0 leaves the blast's own " +
                 "spawned rotation alone (or Face velocity's, if that is on).")]
        public float spinDegMin = 0f;
        [Tooltip("Fastest spin, degrees/sec.")]
        public float spinDegMax = 0f;

        // ── how long the flight lasts ─────────────────────────────────────────────
        [Tooltip("Fly until the blast ends itself, rather than for a fixed time. The right answer for anything " +
                 "that already knows when it is done.")]
        public bool untilTargetEnds = true;
        [Min(0.01f)]
        [Tooltip("Fixed-duration mode: seconds of flight. The blast may still be playing — this only stops it moving.")]
        public float lifeSeconds = 1f;

        [Tooltip("Fixes the random speed, angle and spin so every play is identical. 0 = reroll every time.")]
        public int seed = 0;

        /// The launch cone's centre in degrees for ONE spawn — the single place this decision is made, so the
        /// runtime (<see cref="Apply"/>) and the editor preview (Editor/Chunks/ChunkPreviewSim.cs, LaunchOf)
        /// can never disagree about which way a mode points. <paramref name="patternCentre"/> and
        /// <paramref name="point"/> are both world positions; Outward mode is the angle from the first to the
        /// second.
        public static float ResolveCentreDeg(TrajectoryDirectionMode mode, float fixedDeg, float burstDeg,
                                             Vector3 patternCentre, Vector3 point)
        {
            switch (mode)
            {
                case TrajectoryDirectionMode.Fixed:
                    return fixedDeg;
                case TrajectoryDirectionMode.OutwardFromCentre:
                {
                    Vector2 delta = (Vector2)(point - patternCentre);
                    // A spawn sitting exactly on its own pattern's centre (a Single blast, or a formation of
                    // one) has no outward direction to give — the recipe's own aim is the only honest fallback.
                    return delta.sqrMagnitude > 1e-6f ? Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg : burstDeg;
                }
                default:
                    return burstDeg;
            }
        }

        /// Give <paramref name="spawned"/> its launch, or do nothing when nothing was actually spawned.
        /// <paramref name="index"/> is the spawn's position within its blast (0 for a single, 0..N-1 across a
        /// pattern): it advances the seeded stream so several blasts in one pattern fan out instead of all
        /// flying identically, while a fixed seed still reproduces the exact same set of launches.
        /// <paramref name="patternCentre"/> is where the producer's OWN pattern is centred (its offset from
        /// the recipe origin) — what Outward mode flies away from; a producer with no pattern passes its one
        /// spawn point, which degrades Outward to the burst-direction fallback above.
        public void Apply(in ChunkModuleContext ctx, Transform spawned, int index, Vector3 patternCentre)
        {
            if (!enabled || spawned == null || ctx.Runner == null) return;

            var rng = seed != 0 ? new ChunkRng(seed, index)
                                : new ChunkRng(Random.Range(1, int.MaxValue), index);

            float lo = Mathf.Min(speedMin, speedMax), hi = Mathf.Max(speedMin, speedMax);
            float speed = rng.Range(lo, hi);

            float centreDeg = ResolveCentreDeg(directionMode, directionDeg, ctx.DirectionDeg,
                                               patternCentre, spawned.position);
            float rad = (centreDeg + rng.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;

            // Drawn last (after speed and direction) so the two existing draws keep landing on the same
            // numbers they always did for an asset that never touches the spin band.
            float spinLo = Mathf.Min(spinDegMin, spinDegMax), spinHi = Mathf.Max(spinDegMin, spinDegMax);
            float spin = rng.Range(spinLo, spinHi);

            Vector3 velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * speed;
            velocity.y += upwardBias;

            // 0 tells the runner "until the target dies", which is what a self-ending blast wants.
            float life = untilTargetEnds ? 0f : Mathf.Max(0.01f, lifeSeconds);

            ctx.Runner.Move(spawned, velocity, gravity, drag, life, faceVelocity, spin);
        }

        public override bool MigrateLegacyCurves()
        {
            if (directionModeMigrated) return false;
            directionMode = inheritBurstDirection ? TrajectoryDirectionMode.Burst : TrajectoryDirectionMode.Fixed;
            directionModeMigrated = true;
            return true;
        }
    }
}
