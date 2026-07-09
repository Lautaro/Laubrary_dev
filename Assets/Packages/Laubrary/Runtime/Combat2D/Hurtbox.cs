using UnityEngine;

namespace Laubrary.Combat2D
{
    /// A region that can RECEIVE damage on behalf of a <see cref="Combatant"/>. Put it on the collider(s) that should
    /// be hittable; a body can have several (a head hurtbox at 2× for headshots, a body at 1×, an armoured part at
    /// 0.5×). Projectiles and Hitboxes find it via the collider and route damage through <see cref="ReceiveHit"/>.
    [RequireComponent(typeof(Collider2D))]
    public class Hurtbox : MonoBehaviour
    {
        [Tooltip("Combatant this hurtbox belongs to. Auto-found on this object or a parent if left null.")]
        public Combatant owner;
        [Min(0f)]
        [Tooltip("Damage multiplier for hits landing here (2 = headshot / weak point, 0.5 = armoured).")]
        public float damageMultiplier = 1f;

        void Reset() { owner = GetComponentInParent<Combatant>(); }
        void Awake() { if (owner == null) owner = GetComponentInParent<Combatant>(); }

        public Combatant Owner => owner != null ? owner : (owner = GetComponentInParent<Combatant>());
        public Health Health => Owner != null ? Owner.Health : null;

        /// Apply a hit that already cleared the faction + filter checks. Scales by the hurtbox multiplier first.
        public void ReceiveHit(DamageInfo info)
        {
            var h = Health;
            if (h == null) return;
            info.amount *= Mathf.Max(0f, damageMultiplier);
            h.ApplyDamage(info);
        }
    }
}
