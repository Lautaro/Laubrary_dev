using UnityEngine;

namespace Laubrary.Combat2D
{
    /// <summary>
    /// Optional capability a character MAY provide (discovered via GetComponent, same "optional capability,
    /// quiet no-op" pattern as <see cref="IVectorAimSource"/> and the rest of this codebase) — "whichever
    /// weapon I currently have equipped", for a trigger mechanism that shouldn't have to know how many slots
    /// a character has or which one is switched in.
    ///
    /// Lives here (Combat2D), not in Zoetrope where its real implementation
    /// (<c>Laubrary.Zoetrope.WeaponSwitcher</c>) does, for exactly the reason spelled out on
    /// <see cref="IVectorAimSource"/>: Zoetrope already depends on Combat2D, so the reverse reference would be
    /// circular. This lets an input-layer driver fire "the equipped weapon" while depending only on Combat2D.
    ///
    /// A character with no switcher at all needs nothing — a driver falls back to a plain
    /// <see cref="ProjectileWeapon"/> found on the object.
    /// </summary>
    public interface IActiveWeaponSource
    {
        /// The currently equipped weapon, or null when nothing is equipped/active. Re-read every frame rather
        /// than cached: switching slots changes the answer, and a caller that cached it would keep firing the
        /// weapon that was put away.
        ProjectileWeapon ActiveWeapon { get; }
    }
}
