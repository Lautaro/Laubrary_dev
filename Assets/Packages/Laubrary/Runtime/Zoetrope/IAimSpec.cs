using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable AIMING TECHNIQUE for a character's weapons — the shelf entry that answers "which way does a
    /// shot from this character actually go, and where does it start". A <see cref="Zoe"/> declares which one
    /// it uses as DATA (<see cref="Zoe.aiming"/>, <c>[SerializeReference]</c>, same shape as
    /// <see cref="IBrainSpec"/> and <see cref="IPlayerControllerSpec"/>), and
    /// <see cref="ZoeSpawner.EquipWeapon"/> attaches it once per weapon slot at spawn — so every spawner (a
    /// game scene, Mirage's preview) picks the technique up with no call-site wiring, and swapping technique is
    /// swapping one field on the character asset.
    ///
    /// <para><b>This is the ONLY place the choice lives.</b> Nothing downstream branches on it: an
    /// implementation attaches a component providing <see cref="IVectorAimSource"/>, which
    /// <see cref="ProjectileWeapon.ResolvedAimDirection"/> already prefers over the owner's raw aim, so
    /// <see cref="ProjectileWeapon"/>, <see cref="WeaponMuzzleCue"/> and the input drivers stay free of any
    /// if-else over technique. A new technique is a new class implementing this interface and nothing else.</para>
    ///
    /// <para>A technique also OWNS the muzzle transform it is handed — the shot's ORIGIN — because a technique
    /// that decides the direction must decide the matching origin too, or the barrel the player sees and the
    /// line the bullet takes disagree. Exactly one technique is attached per slot for that reason: two would
    /// fight over the same Transform every frame, and a weapon consults only one
    /// <see cref="IVectorAimSource"/>.</para>
    ///
    /// <para>Leaving <see cref="Zoe.aiming"/> unset uses <see cref="PaintedMuzzleAimSpec"/>, which is the
    /// long-standing behaviour unchanged (the muzzle position + direction painted frame by frame on the
    /// animation) — so no existing character is affected by this shelf existing.</para>
    /// </summary>
    public interface IAimSpec
    {
        /// <summary>Attach the aiming components to one freshly-built weapon slot.</summary>
        /// <param name="weaponSlot">The slot GameObject carrying that weapon's <see cref="ProjectileWeapon"/>.</param>
        /// <param name="muzzle">The transform that weapon spawns shots from — the technique owns and may move it.</param>
        /// <param name="owner">The firing character. Its <c>aimDirection</c> is the raw aim a technique refines,
        /// and its GameObject is the character root, from which the body's own view (<see cref="IAnimatedView"/>,
        /// <see cref="IPartLookup"/>) is reachable for a technique that needs to read or steer the drawing.</param>
        /// <param name="muzzleLayerId">The MetaLayer this Zoe's own <see cref="ZoeWeaponSlot"/> names as
        /// carrying the muzzle, or empty. Passed to every technique, not just the painted one: a technique that
        /// rotates the body still wants the authored muzzle metadata to rotate with it.</param>
        void Attach(GameObject weaponSlot, Transform muzzle, Combatant owner, string muzzleLayerId);
    }
}
