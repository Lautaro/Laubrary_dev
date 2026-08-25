using UnityEngine;

namespace Laubrary.Chunks
{
    /// <summary>
    /// The shared "which way is this thing travelling, and what is the reverse of that" primitive
    /// (CHUNKS_OVERHAUL_DESIGN.md → "New shared primitives this design needs" → *Reversed-relative-motion*).
    /// The design names TWO callers for the same idea — a Follow Emitter tracking an external Transform, and a
    /// fragment spawning a trail relative to its own flight — and says explicitly to build it ONCE, so this is
    /// deliberately a plain reusable class in the Chunks runtime assembly rather than private state inside
    /// <see cref="ChunkFollowEmitter"/>. It knows nothing about emitters, specs or modules: feed it positions,
    /// ask it for a direction.
    ///
    /// WHY IT SMOOTHS AND HOLDS (the whole reason this type exists rather than one line of Atan2):
    /// a raw per-frame delta is a terrible direction source. Two failure modes, both of which look like a bug
    /// in whatever consumes the direction rather than in the measurement:
    ///  1. **Jitter.** A near-stationary object still moves a hair per frame (physics settle, animation root
    ///     motion, float noise). The MAGNITUDE of that motion is meaningless but its ANGLE is not — it swings
    ///     wildly frame to frame, so a spray aimed at it strobes in every direction at once.
    ///  2. **Garbage on a stop.** The instant the delta hits exactly zero, Atan2(0,0) returns 0° — i.e. "right"
    ///     — so a character that stops moving does not keep its last aim, it snaps to pointing due east.
    /// The fix is two-part and both parts are needed: an **exponential moving average** over the measured
    /// velocity (frame-rate independent — the blend factor is derived from dt against a time constant, so the
    /// same smoothing reads identically at 30 and 240fps, which a bare `Lerp(a, b, 0.2f)` does not), and a
    /// **minimum speed gate** below which the last good direction is HELD instead of recomputed. EMA was chosen
    /// over a rolling buffer of the last N deltas because it is one float pair of state, needs no allocation,
    /// and its responsiveness is authorable as a single seconds-valued dial the user can actually reason about
    /// ("how long until the aim catches up with a turn") instead of an opaque sample count.
    /// </summary>
    public class ChunkTravelDirection
    {
        /// <summary>
        /// Time constant of the velocity smoothing, in seconds: roughly how long the smoothed velocity takes
        /// to catch up with a sudden change of course. 0 disables smoothing entirely (raw per-frame delta —
        /// jittery, see the class doc). ~0.1s is a good default: fast enough that a turn reads immediately,
        /// slow enough that per-frame noise averages out.
        /// </summary>
        public float smoothingSeconds = 0.1f;

        /// <summary>
        /// World units per second below which the target counts as standing still, so the last good direction
        /// is HELD rather than recomputed from a meaningless delta. Never let this be zero in practice — that
        /// is precisely the "stopped, so aim snaps to 0°" bug.
        /// </summary>
        public float minSpeed = 0.05f;

        Vector3 _lastPosition;
        Vector2 _smoothedVelocity;
        float _directionDeg;
        bool _hasPosition;
        bool _hasDirection;

        public ChunkTravelDirection() { }

        public ChunkTravelDirection(float smoothingSeconds, float minSpeed)
        {
            this.smoothingSeconds = smoothingSeconds;
            this.minSpeed = minSpeed;
        }

        /// <summary>The smoothed travel velocity, world units/sec. Zero until the first <see cref="Sample"/>.</summary>
        public Vector2 Velocity => _smoothedVelocity;

        /// <summary>Smoothed travel speed, world units/sec — compare against <see cref="minSpeed"/>.</summary>
        public float Speed => _smoothedVelocity.magnitude;

        /// <summary>True once the tracker has seen motion above <see cref="minSpeed"/> at least once. While
        /// false there is no honest direction to give and <see cref="DirectionDeg"/> is a placeholder — a
        /// caller that must not aim wrongly should check this rather than trusting the angle.</summary>
        public bool HasDirection => _hasDirection;

        /// <summary>True while the target is currently moving faster than <see cref="minSpeed"/> (as opposed
        /// to coasting on a HELD direction). This is the "is it actually moving right now" question, which is
        /// a different question from <see cref="HasDirection"/>.</summary>
        public bool IsMoving => _hasDirection && Speed >= minSpeed;

        /// <summary>The direction of travel in degrees, 0 = +X (right), 90 = +Y (up), normalised to [0,360).
        /// HELD at its last good value while the target is below <see cref="minSpeed"/>.</summary>
        public float DirectionDeg => _directionDeg;

        /// <summary>The direction to throw something BEHIND a travelling target: <see cref="DirectionDeg"/>
        /// turned around. This is the value a Follow Emitter hands to
        /// <see cref="ChunkModuleContext.DirectionDeg"/> so a Particle Splash with
        /// <c>inheritBurstDirection</c> on sprays out of the back of a moving character.</summary>
        public float ReverseDirectionDeg => Normalize(_directionDeg + 180f);

        /// <summary>Forget all history and re-anchor on a position. Call this when starting to follow (or when
        /// TELEPORTING a followed target) — without it the first sample after a jump measures the whole jump
        /// as one frame of travel, which is a huge bogus velocity in the direction of the teleport.</summary>
        public void Reset(Vector3 position)
        {
            _lastPosition = position;
            _smoothedVelocity = Vector2.zero;
            _hasPosition = true;
            _hasDirection = false;
            _directionDeg = 0f;
        }

        /// <summary>Feed one frame's world position. Returns whether the target is currently moving above
        /// <see cref="minSpeed"/> (i.e. whether the direction was refreshed rather than held). A non-positive
        /// deltaTime is ignored — a paused frame is not evidence of standing still.</summary>
        public bool Sample(Vector3 position, float deltaTime)
        {
            if (!_hasPosition) { Reset(position); return false; }
            if (deltaTime <= 0f) return IsMoving;

            Vector2 raw = (Vector2)(position - _lastPosition) / deltaTime;
            _lastPosition = position;

            // Frame-rate-independent EMA: the blend factor is derived from dt against the time constant, so
            // halving the frame time halves the per-frame step and the settling time stays put. A literal
            // constant here would make the smoothing (and therefore the aim) depend on the machine.
            if (smoothingSeconds > 0.0001f)
            {
                float alpha = 1f - Mathf.Exp(-deltaTime / smoothingSeconds);
                _smoothedVelocity = Vector2.Lerp(_smoothedVelocity, raw, alpha);
            }
            else _smoothedVelocity = raw;

            float speed = _smoothedVelocity.magnitude;
            // The gate: above it we have a real direction, below it we keep the one we had. Never recompute
            // from a sub-threshold vector — that is where both the jitter and the snap-to-0° come from.
            if (speed >= Mathf.Max(0.0001f, minSpeed))
            {
                _directionDeg = ToDegrees(_smoothedVelocity);
                _hasDirection = true;
                return true;
            }
            return false;
        }

        /// <summary>Angle of a 2D vector in degrees, 0 = +X, 90 = +Y, normalised to [0,360). Returns 0 for a
        /// zero vector — which is exactly why callers must gate on speed rather than call this blind.</summary>
        public static float ToDegrees(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-12f) return 0f;
            return Normalize(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
        }

        /// <summary>Turn a direction around (the "reversed-relative-motion" half of the primitive).</summary>
        public static float Reverse(float degrees) => Normalize(degrees + 180f);

        /// <summary>Fold any angle into [0,360).</summary>
        public static float Normalize(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
