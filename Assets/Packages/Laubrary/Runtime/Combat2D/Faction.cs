using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Combat2D
{
    /// A team. Two combatants can hurt each other only when one's faction is HOSTILE to the other's. By default a
    /// faction is friendly to itself (no friendly fire) and to anyone in its <see cref="allies"/> list, and hostile
    /// to everyone else. A null faction is treated as "unaligned" — a hazard that hits everything and can be hit by
    /// everything — so trap/hazard damage works without authoring a faction.
    [CreateAssetMenu(menuName = "Laubrary/Colosseum/Faction", fileName = "Faction")]
    public class Faction : ScriptableObject
    {
        [Tooltip("Shown in logs / debug UI.")]
        public string displayName;
        [Tooltip("A colour for debug gizmos / demo tinting.")]
        public Color color = Color.white;

        [Tooltip("Factions this one will NOT damage. Everyone not listed (and not self) is hostile.")]
        public List<Faction> allies = new();

        [Tooltip("Take/deal damage within the SAME faction too (friendly fire).")]
        public bool friendlyFire = false;

        [Tooltip("Treat EVERY other faction as hostile, even listed allies (an unaligned hazard).")]
        public bool hostileToAll = false;

        /// Would a member of this faction damage a member of <paramref name="other"/>?
        public bool IsHostileTo(Faction other)
        {
            if (other == null) return true;             // unaligned target = fair game
            if (hostileToAll) return true;
            if (other == this) return friendlyFire;     // same faction
            return allies == null || !allies.Contains(other);
        }

        /// Can <paramref name="attacker"/> damage <paramref name="target"/>? A null attacker (a hazard with no
        /// faction) hits everything; otherwise it's the attacker's hostility that decides.
        public static bool CanDamage(Faction attacker, Faction target)
            => attacker == null || attacker.IsHostileTo(target);
    }
}
