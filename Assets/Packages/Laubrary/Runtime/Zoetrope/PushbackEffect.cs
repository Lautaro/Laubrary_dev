using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// Shoves the Zoe away from a hit — a fixed DISTANCE over a fixed DURATION, not a physics impulse.
    ///
    /// It used to add velocity to a Rigidbody2D, and that was the wrong tool for this game. An impulse only
    /// lets you pick a SPEED; how far the character ends up is then whatever happens to stop it — which here
    /// was "the stun ran out and the mover took back over". So the two things a designer actually wants to say
    /// (how far it gets knocked, and how snappy that looks) were welded into one number, and turning it down
    /// made the shove shorter AND slower when the complaint was that it was already too slow.
    ///
    /// It also fought the game: movers in this project write transforms authoritatively every frame, so a
    /// physics push was only visible for as long as something else happened to be leaving the transform alone.
    ///
    /// Distance and duration are independent on purpose — 0.4 units over 0.08s reads as a jolt, the same
    /// distance over 0.5s reads as a slide. Ease-out, because a knock starts fast and settles.
    [System.Serializable]
    public class PushbackEffect : IEffect, IEventParamUser
    {
        /// Reads a DIRECTION (which way to shove) and a SCALAR (how far) — never a position (it moves the Zoe
        /// itself, wherever it is). So the Zoe-event editor shows this effect a Direction + Scalar picker only.
        public EventParam UsedParams => EventParam.Direction | EventParam.Scalar;

        [Tooltip("How far the Zoe is knocked, in world units.")]
        [Min(0f)] public float distance = 0.4f;

        [Tooltip("Extra distance per unit of the event's resolved SCALAR param (e.g. the damage amount). " +
                 "0 = every hit knocks the same distance.")]
        public float distancePerAmount = 0f;

        [Tooltip("How long the knock takes. THIS is the snap — the same distance over 0.08s is a jolt and over " +
                 "0.5s is a shove. Kept separate from distance because they are two different decisions.")]
        [Min(0.01f)] public float duration = 0.1f;

        [Tooltip("Push TOWARD the resolved direction instead of away — a pull rather than a knock.")]
        public bool invert = false;

        public bool IsEmpty => distance == 0f && distancePerAmount == 0f;

        public void Apply(EventContext ctx)
        {
            if (IsEmpty) return;
            if (float.IsNaN(ctx.DirectionDeg)) return;   // omni-directional event → no push axis

            var t = ctx.Transform;
            if (t == null) return;

            float rad = ctx.DirectionDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (invert) dir = -dir;

            float dist = distance + distancePerAmount * ctx.Scalar;
            if (Mathf.Approximately(dist, 0f)) return;

            var mover = t.GetComponent<PushbackMotion>();
            if (mover == null) mover = t.gameObject.AddComponent<PushbackMotion>();
            mover.Begin(dir * dist, duration);
        }
    }

    /// Carries out a <see cref="PushbackEffect"/>: slides the transform by an offset over a duration, easing
    /// out, then stops. Added on demand and left in place; re-shoving a character that is still sliding
    /// restarts from wherever it now is, which is what a second hit should feel like.
    ///
    /// LateUpdate, so it moves the character AFTER whatever mover placed it this frame. That ordering is the
    /// whole reason this works where an impulse did not — the push is applied ON TOP of the mover's authored
    /// position rather than competing with it for ownership of the transform.
    public class PushbackMotion : MonoBehaviour
    {
        Vector2 _remaining;
        float _left, _total;

        public void Begin(Vector2 offset, float duration)
        {
            _remaining = offset;
            _total = Mathf.Max(0.01f, duration);
            _left = _total;
        }

        void LateUpdate()
        {
            if (_left <= 0f) return;

            float dt = Mathf.Min(Time.deltaTime, _left);
            // Move the share of what REMAINS that this frame is owed, so the total lands exactly on `offset`
            // however the frame times fall — accumulating eased positions instead would drift.
            float before = Ease(1f - _left / _total);
            float after = Ease(1f - (_left - dt) / _total);
            float share = Mathf.Approximately(1f - before, 0f) ? 1f : (after - before) / (1f - before);

            Vector2 step = _remaining * share;
            transform.position += (Vector3)step;
            _remaining -= step;
            _left -= dt;
        }

        static float Ease(float x) => 1f - (1f - x) * (1f - x);
    }
}
