using UnityEngine;

namespace Laubrary.Combat2D
{
    /// The identity of a fighter: which <see cref="Faction"/> it's on, plus quick access to its <see cref="Health"/>
    /// and (optionally) a pixel-perfect <see cref="IHitFilter"/>. Hitboxes, Hurtboxes and Projectiles all resolve
    /// "who is this and can I hit them?" through the Combatant. Compose it with a Health and a Hurtbox to make a
    /// damageable actor; a projectile/weapon reads its owner Combatant for the attacking faction.
    [DisallowMultipleComponent]
    public class Combatant : MonoBehaviour
    {
        [Tooltip("Team this fighter belongs to. Null = unaligned (hits and is hit by everything).")]
        public Faction faction;
        [Tooltip("Optional label for logs / debug UI.")]
        public string label;

        Health health;
        bool healthCached;
        IHitFilter hitFilter;
        bool filterCached;

        /// The Health on this object (cached; may be null if this combatant can't be damaged, e.g. a bullet owner).
        public Health Health
        {
            get { if (!healthCached) { health = GetComponent<Health>(); healthCached = true; } return health; }
        }

        /// Optional pixel-perfect hit confirmer (e.g. a future Zoetrope component). Null = collider overlap is enough.
        public IHitFilter HitFilter
        {
            get { if (!filterCached) { hitFilter = GetComponent<IHitFilter>(); filterCached = true; } return hitFilter; }
        }

        public bool IsAlive => Health == null || !Health.IsDead;
    }
}
