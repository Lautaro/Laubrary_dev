using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A Zoe-event effect that applies a physical KNOCKBACK impulse to the target Zoe along the event's resolved
    /// direction param (default <see cref="DirectionParam.HitDirection"/> = attacker→target, so the Zoe is shoved
    /// away from the hit), with a magnitude that is a fixed base plus an optional per-scalar term (a bigger hit
    /// shoves harder). Part of the Zoe-event effect palette (ZOE_EVENTS_DESIGN.md step 3); lives in Zoetrope CORE
    /// because it needs only a <see cref="Rigidbody2D"/> on the Zoe — no external presentation module.
    ///
    /// Per the design caveat: it NO-OPS (with a <see cref="Debug.LogWarning"/>) when the Zoe has no Rigidbody2D —
    /// there's nothing to push. It also no-ops silently for an omni-directional event (the direction param resolved
    /// to NaN), since there is then no axis to push along.
    /// </summary>
    [System.Serializable]
    public class PushbackEffect : IEffect, IEventParamUser
    {
        /// Reads a DIRECTION (which way to shove) and a SCALAR (how hard) — never a position (it pushes the Zoe
        /// itself, wherever it is). So the Zoe-event editor shows this effect a Direction + Scalar picker only.
        public EventParam UsedParams => EventParam.Direction | EventParam.Scalar;

        [Tooltip("Base impulse magnitude (world units/sec added to the rigidbody's velocity) applied along the " +
                 "resolved direction, regardless of the scalar param.")]
        public float force = 5f;
        [Tooltip("Extra impulse magnitude added per unit of the event's resolved SCALAR param (e.g. the damage " +
                 "amount). 0 = a fixed-strength shove. Total magnitude = force + forcePerAmount * scalar.")]
        public float forcePerAmount = 0f;
        [Tooltip("Flip the push to the OPPOSITE of the resolved direction (e.g. to pull toward the attacker " +
                 "instead of away). Default off = shove along the event's direction param.")]
        public bool invert = false;

        public bool IsEmpty => force == 0f && forcePerAmount == 0f;

        public void Apply(EventContext ctx)
        {
            if (IsEmpty) return;
            if (float.IsNaN(ctx.DirectionDeg)) return;   // omni-directional event → no push axis

            Transform t = ctx.Transform;
            Rigidbody2D rb = t != null ? t.GetComponent<Rigidbody2D>() : null;
            if (rb == null && t != null) rb = t.GetComponentInChildren<Rigidbody2D>();
            if (rb == null)
            {
                Debug.LogWarning("[PushbackEffect] The Zoe has no Rigidbody2D — knockback is a no-op. Add a " +
                                 "Rigidbody2D (or a movement component) to the Zoe to receive pushback.",
                                 t != null ? t.gameObject : null);
                return;
            }

            float rad = ctx.DirectionDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (invert) dir = -dir;
            float mag = force + forcePerAmount * ctx.Scalar;
            rb.linearVelocity += dir * mag;
        }
    }
}
