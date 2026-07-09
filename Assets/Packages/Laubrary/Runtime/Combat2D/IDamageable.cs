namespace Laubrary.Combat2D
{
    /// Anything that can take damage. <see cref="Health"/> is the standard implementation; games can add their own
    /// (destructible props, shields) and Hitboxes/Projectiles will still drive them through this interface.
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo info);
        bool IsDead { get; }
    }

    /// Optional final gate on a hit AFTER broad-phase + collider overlap + faction checks have passed. Return false
    /// to reject the hit (e.g. a Zoetrope per-pixel / meta-layer test proving the sprites don't actually overlap).
    /// A combatant with no IHitFilter component accepts the collider overlap as the hit. This is the seam Zoetrope
    /// plugs pixel-perfect detection into — Colosseum runs the cheap checks (distance + collider AABB/shape),
    /// Zoetrope confirms the expensive per-pixel one only when both sides opt in.
    /// <para><paramref name="self"/> is the combatant being hit; <paramref name="attacker"/> is the incoming
    /// projectile / hitbox owner GameObject (may be null); <paramref name="worldPoint"/> is the contact point.</para>
    public interface IHitFilter
    {
        bool ConfirmHit(Combatant self, UnityEngine.GameObject attacker, UnityEngine.Vector2 worldPoint);
    }
}
