using UnityEngine;

namespace Laubrary.Combat2D
{
    /// <summary>
    /// Optional capability a weapon's shooter MAY provide (discovered via GetComponent, same "optional
    /// capability, quiet no-op" pattern as every other interface in this codebase) — an animation-drawn
    /// direction that should steer firing INSTEAD OF the owner's raw <c>Combatant.aimDirection</c>, for a
    /// weapon whose drawn barrel angle doesn't always match true aim exactly.
    ///
    /// Lives here (Combat2D), not in Zoetrope where its one real implementation
    /// (<c>Laubrary.Zoetrope.MuzzleVectorTracker</c>) does, for the same reason
    /// <c>Laubrary.Zoetrope.IAnimatedView</c> exists at all: Combat2D must not depend on Zoetrope (Zoetrope
    /// already depends on Combat2D — <c>WeaponMuzzleCue</c> references <c>ProjectileWeapon</c> — so the
    /// reverse reference would be circular). <see cref="ProjectileWeapon.ResolvedAimDirection"/> looks this up
    /// lazily and prefers it over the owner's aim whenever <see cref="HasDirection"/> is true, so ordinary
    /// <c>TryFire()</c>/<c>autoFire</c> pick up a live Vector MetaLayer automatically — no call site needs to
    /// know this exists.
    /// </summary>
    public interface IVectorAimSource
    {
        /// Whether a direction was actually resolved right now (current frame or nearest-authored fallback).
        /// False means fall back to the shooter's normal aim source.
        bool HasDirection { get; }
        /// The last resolved WORLD-space fire direction, valid only when <see cref="HasDirection"/> is true.
        Vector2 CurrentDirection { get; }
    }
}
