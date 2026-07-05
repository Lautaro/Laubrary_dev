using UnityEngine;

namespace Laubrary.Colosseum
{
    /// One damage event, passed to <see cref="IDamageable.ApplyDamage"/> and carried through the damage/death
    /// events so listeners (Pyre explosions, Chunks debris, hit-flashes, score) know how much, from whom, and where.
    /// A value type — cheap to pass around and never null.
    public struct DamageInfo
    {
        /// Damage amount (before any hurtbox multiplier). Non-positive amounts are ignored by Health.
        public float amount;
        /// Who dealt it (the weapon/projectile owner's GameObject), or null for an unattributed hazard.
        public GameObject source;
        /// The dealer's faction, or null (unaligned).
        public Faction faction;
        /// World-space point the hit landed at (for spawning effects). Zero if unknown.
        public Vector2 point;
        /// Normalised push direction from attacker to target (for knockback / directional effects). Zero if unknown.
        public Vector2 direction;
        /// Was this a critical hit? (Purely informational — Health does not scale by it.)
        public bool crit;

        public DamageInfo(float amount, GameObject source = null, Faction faction = null,
                          Vector2 point = default, Vector2 direction = default, bool crit = false)
        {
            this.amount = amount;
            this.source = source;
            this.faction = faction;
            this.point = point;
            this.direction = direction;
            this.crit = crit;
        }
    }
}
