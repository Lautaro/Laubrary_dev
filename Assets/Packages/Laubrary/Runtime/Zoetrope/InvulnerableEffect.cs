using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// Grants the Zoe a window of invulnerability when the reaction fires.
    ///
    /// Health already has an `invulnerableAfterHit` field, and this deliberately does NOT replace it: that one
    /// is a permanent property of the character ("this thing always gets i-frames"), whereas an effect is
    /// conditional and composable — a hit reaction can grant them, a death reaction can grant a long one so a
    /// corpse cannot be shot apart mid-animation, and a specific attack can grant them for its wind-up. Same
    /// value, different question: what a character IS versus what a moment DOES.
    ///
    /// Core Zoetrope: it needs Combat2D's Health and nothing else.
    [System.Serializable]
    public class InvulnerableEffect : IEffect
    {
        [Tooltip("Seconds of invulnerability granted when this fires. Long enough to stop a single burst " +
                 "deleting the character, short enough that it never feels like the shots are not landing.")]
        [Min(0f)] public float seconds = 0.2f;

        public bool IsEmpty => seconds <= 0f;

        public void Apply(EventContext ctx)
        {
            if (IsEmpty) return;
            var health = ctx.Health;
            if (health == null) return;

            // GrantInvulnerability extends rather than overwrites, so a shorter grant can never cut a longer
            // one short. The character's own invulnerableAfterHit is deliberately left alone — this is a
            // moment granting i-frames, not a permanent change to what the character is.
            health.GrantInvulnerability(seconds);
        }
    }
}
