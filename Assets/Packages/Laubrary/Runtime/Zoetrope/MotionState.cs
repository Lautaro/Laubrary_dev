using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// The published truth about how a character is moving right now — one small struct, refreshed per frame.
    /// Everything downstream (locomotion, per-part direction resolution, a future turret/hull split) reads
    /// THIS instead of re-measuring the transform its own way, which is how the same question ("is this thing
    /// moving?") ends up answered differently by a mover, an animator and a weapon and disagreeing.
    /// </summary>
    public struct MotionState
    {
        /// World-space velocity (x, y, depth), inferred from transform delta unless overridden.
        public Vector3 velocity;
        /// Scalar speed on the XY plane — what a moving/idle threshold actually compares against.
        public float speed;
        /// Where the body is travelling. Zero when not moving (never normalized from a zero vector).
        public Vector2 heading;
        /// Where the body is turned. Defaults to heading; a part whose channel is Aim reads
        /// <see cref="aim"/> instead — this field is the Heading-channel answer.
        public Vector2 facing;
        /// From <see cref="Combatant.aimDirection"/> when present, else falls back to heading.
        public Vector2 aim;
        /// The platformer axis. True for anything without a jump/gravity concept (the common 2D-top-down case).
        public bool grounded;
        /// Signed vertical speed — rising, falling, or zero. Only meaningful when not grounded.
        public float verticalVelocity;
        /// Moving under its own power vs being shoved (knockback, a scripted fling). Cannot be inferred from
        /// a transform — a knockback and a walk cycle produce the identical position delta — so this is
        /// published, not measured. This is the fix for knockback reading as a walk cycle.
        public bool selfWilled;

        public static MotionState Idle => new MotionState { grounded = true, selfWilled = true };
    }

    /// <summary>
    /// Infers <see cref="MotionState"/> from the transform each frame, the same input-agnostic measurement
    /// <see cref="LocomotionAnimator"/> used to do internally — so it works identically for a Rigidbody, a
    /// Choreographer dancer, a road-clamped walker, or a scripted cutscene walk, none of which share an
    /// interface. Game code that knows better (a knockback effect) publishes an override instead of fighting
    /// the inference.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class MotionStateSource : MonoBehaviour
    {
        Combatant _combatant;
        bool _lastPosSet;
        Vector3 _lastPos;
        MotionState _state;

        bool _selfWilledOverrideActive;
        bool _selfWilledOverrideValue;
        float _selfWilledOverrideUntil;

        // A FULL state override — distinct from the selfWilled-only one above, and mutually exclusive with
        // it: this replaces the whole per-frame reading (Mirage's "pretend it is moving at 3 u/s heading NE"),
        // where there is no real transform movement to infer anything from in the first place.
        MotionState? _fullOverride;
        float _fullOverrideUntil;

        // Resolved LAZILY, never cached in Awake: ZoeSpawner adds MotionStateSource BEFORE Combatant (it has
        // to, for the same reason ZoeState and WeaponMuzzleCue resolve their siblings lazily too), so an
        // Awake-time GetComponent would find nothing and `aim` would silently fall back to `heading` forever.
        Combatant Combatant => _combatant != null ? _combatant : (_combatant = GetComponent<Combatant>());

        public MotionState Current => _state;

        void Awake()
        {
            _state = MotionState.Idle;
        }

        void LateUpdate()
        {
            if (_fullOverride.HasValue)
            {
                if (Time.time < _fullOverrideUntil) { _state = _fullOverride.Value; return; }
                _fullOverride = null;   // expired — resume real inference below
            }

            var pos = transform.position;
            Vector3 vel = _lastPosSet && Time.deltaTime > 0f ? (pos - _lastPos) / Time.deltaTime : Vector3.zero;
            _lastPos = pos;
            _lastPosSet = true;

            var heading2D = new Vector2(vel.x, vel.y);
            _state.velocity = vel;
            _state.speed = heading2D.magnitude;
            _state.heading = _state.speed > 0.0001f ? heading2D.normalized : Vector2.zero;
            _state.facing = _state.heading;
            _state.aim = Combatant != null ? Combatant.aimDirection : _state.heading;
            _state.grounded = true;
            _state.verticalVelocity = 0f;

            if (_selfWilledOverrideActive && Time.time < _selfWilledOverrideUntil)
            {
                _state.selfWilled = _selfWilledOverrideValue;
            }
            else
            {
                _selfWilledOverrideActive = false;
                _state.selfWilled = true;
            }
        }

        /// <summary>Publish "this body is moving, but not by its own will" for the next <paramref name="forSeconds"/>
        /// — call from a knockback / pushback effect the instant it displaces the transform. Locomotion (or
        /// anything reading <see cref="Current"/>) can then tell a shove from a walk even though both look
        /// identical to a transform-delta measurement.</summary>
        public void MarkNotSelfWilled(float forSeconds)
        {
            _selfWilledOverrideActive = true;
            _selfWilledOverrideValue = false;
            _selfWilledOverrideUntil = Mathf.Max(_selfWilledOverrideUntil, Time.time + Mathf.Max(0f, forSeconds));
        }

        /// <summary>Replace the ENTIRE published state for the next <paramref name="forSeconds"/> — what
        /// Mirage's motion-preview trigger uses to say "pretend this is moving at 3 u/s heading NE, airborne"
        /// with no real transform movement to infer it from. <paramref name="forSeconds"/> &lt;= 0 holds it
        /// until <see cref="ClearOverride"/> or another call replaces it.</summary>
        public void PublishOverride(MotionState state, float forSeconds)
        {
            _fullOverride = state;
            _fullOverrideUntil = forSeconds > 0f ? Time.time + forSeconds : float.PositiveInfinity;
        }

        /// End a <see cref="PublishOverride"/> early and resume real transform-based inference next frame.
        public void ClearOverride() => _fullOverride = null;
    }
}
