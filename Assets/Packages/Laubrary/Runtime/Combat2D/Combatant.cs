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

        [Tooltip("Which way this fighter is currently aiming/shooting — a generic, view-agnostic signal any " +
                 "ProjectileWeapon it owns fires along by default. Deliberately NOT read from any specific " +
                 "visual system (sprite flip, animation clip, Lazor render) — whichever view this Combatant " +
                 "has (if any) may optionally feed this as a convenience (e.g. a flipping 2D character keeping " +
                 "it in sync with facing), but player input, AI, or a Mirage preview override are equally " +
                 "valid sources. A character with no notion of \"facing\" at all (a turret, an omnidirectional " +
                 "burst) just leaves this at whatever it was last set to.")]
        public Vector2 aimDirection = Vector2.up;

        Health health;
        bool healthCached;
        IHitFilter[] hitFilters;
        bool filterCached;

        /// The Health on this object (cached; may be null if this combatant can't be damaged, e.g. a bullet owner).
        public Health Health
        {
            get { if (!healthCached) { health = GetComponent<Health>(); healthCached = true; } return health; }
        }

        /// Every hit confirmer on this object. EVERY one must agree before a hit lands — a filter is a veto, so
        /// they compose rather than compete. That matters because a filter answers a different question each
        /// time and more than one can be true at once: ReelHitFilter asks "did the sprites really touch", a
        /// depth-band filter asks "are these two even at the same distance" (Combat2D collides on XY, so two
        /// things at different depths share a collider position). Before this was a list, whichever component
        /// happened to be found first silently won and the other never ran.
        /// Empty = collider overlap is enough.
        public IHitFilter[] HitFilters
        {
            get { if (!filterCached) { hitFilters = GetComponents<IHitFilter>(); filterCached = true; } return hitFilters; }
        }

        /// The first hit confirmer, or null. Kept for callers written before filters could compose; prefer
        /// <see cref="HitFilters"/>, which is what Combat.TryDamage honours.
        public IHitFilter HitFilter
        {
            get { var f = HitFilters; return f != null && f.Length > 0 ? f[0] : null; }
        }

        public bool IsAlive => Health == null || !Health.IsDead;
    }
}
