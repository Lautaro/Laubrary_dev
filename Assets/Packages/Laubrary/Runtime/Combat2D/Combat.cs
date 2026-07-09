using UnityEngine;

namespace Laubrary.Combat2D
{
    /// The one place a hit is resolved, shared by Hitbox and Projectile so the funnel — collider overlap →
    /// faction check → optional pixel-perfect filter → apply — is identical everywhere. Keeping it here means
    /// Zoetrope's pixel test (the IHitFilter) is honoured no matter what dealt the hit.
    public static class Combat
    {
        /// The Hurtbox governing a collider (on it or a parent), or null if it isn't a damageable region.
        public static Hurtbox FindHurtbox(Collider2D col) => col == null ? null : col.GetComponentInParent<Hurtbox>();

        /// Run the full funnel against a hurtbox and, if everything passes, apply the damage. Returns true on a hit.
        /// attackerObj/attackerFaction describe the source (a weapon owner or a projectile); either may be null.
        public static bool TryDamage(Hurtbox target, Faction attackerFaction, GameObject attackerObj,
                                     float damage, Vector2 point, out DamageInfo info)
        {
            info = default;
            if (target == null) return false;

            Combatant tc = target.Owner;
            if (tc == null || tc.Health == null || tc.Health.IsDead) return false;
            if (attackerObj != null && tc.gameObject == attackerObj) return false;         // never hit your own owner
            if (!Faction.CanDamage(attackerFaction, tc.faction)) return false;             // teammate / ally → skip

            // Pixel-perfect seam: if the target opts into a filter (a Zoe), it must confirm the sprites really touch.
            if (tc.HitFilter != null && !tc.HitFilter.ConfirmHit(tc, attackerObj, point)) return false;

            Vector2 dir = attackerObj != null
                ? ((Vector2)(target.transform.position - attackerObj.transform.position)).normalized
                : Vector2.zero;
            info = new DamageInfo(damage, attackerObj, attackerFaction, point, dir);
            target.ReceiveHit(info);
            return true;
        }
    }
}
