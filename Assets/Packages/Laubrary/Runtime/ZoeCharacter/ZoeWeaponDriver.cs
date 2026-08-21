using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// The missing link between <see cref="IZoeInputSource"/>'s fire buttons and
    /// <see cref="ProjectileWeapon"/>: reads the trigger and calls <c>TryFire()</c>. That is the whole job.
    ///
    /// Deliberately knows NOTHING about perspective, locomotion, or aim. It is the sibling of
    /// <see cref="TopDownMotionDriver"/>, not a part of it, and the split is the point: locomotion is
    /// perspective-specific (a platformer needs gravity and a jump; a top-down character doesn't), but
    /// everything downstream of <c>Combatant.aimDirection</c> is not. A platformer, a side-scroller, a
    /// twin-stick and a light-gun game all pull the trigger identically, so this driver is reused verbatim by
    /// all of them while each brings its own motion driver.
    ///
    /// It resolves neither the fire DIRECTION nor the spawn POSITION — <see cref="ProjectileWeapon.TryFire()"/>
    /// already does both, preferring a sibling <see cref="IVectorAimSource"/>'s animation-drawn barrel angle
    /// (a painted Vector MetaLayer, via Zoetrope's MuzzleVectorTracker) over the owner's raw aim, and spawning
    /// from the muzzle Transform that the same tracker keeps live-positioned. Adding direction handling here
    /// would duplicate that and quietly override the art.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZoeWeaponDriver : MonoBehaviour
    {
        public enum TriggerMode
        {
            /// Ask the weapon: <see cref="ProjectileWeapon.automatic"/> true = fire while held, false = fire on
            /// press. The weapon's own doc calls `automatic` "a property, not a behaviour" precisely because
            /// something like this driver is meant to read it — this is that reader.
            FollowWeapon = 0,
            /// Always edge-triggered, whatever the weapon says (semi-auto).
            Press = 1,
            /// Always level-triggered, whatever the weapon says (full-auto). The weapon's own fireRate
            /// cooldown still paces the shots — holding the button does not fire every frame.
            Hold = 2,
            /// Fire on RELEASE — a bow, a charged shot. Uses <see cref="IZoeInputSource.FireReleased"/>.
            Release = 3,
        }

        [Tooltip("How the trigger reads. Follow Weapon (the default) defers to the equipped weapon's own " +
                 "'automatic' flag, so switching from a pistol to a machine gun changes the feel with no " +
                 "code or driver change. The other modes force one behaviour regardless of the weapon.")]
        public TriggerMode trigger = TriggerMode.FollowWeapon;

        [Tooltip("Explicit weapon to fire. Leave empty for the normal case — the driver then fires whichever " +
                 "weapon is currently equipped (via IActiveWeaponSource, e.g. Zoetrope's WeaponSwitcher), " +
                 "falling back to a ProjectileWeapon found on this object or its children.")]
        public ProjectileWeapon weaponOverride;

        IZoeInputSource _input;
        IActiveWeaponSource _weaponSource;
        bool _weaponSourceChecked;
        ProjectileWeapon _fallbackWeapon;
        bool _fallbackChecked;

        // Lazily resolved and re-fetched whenever null, never cached-once in Awake — a spawner routinely adds
        // this driver before (or after) the input source and the weapon slots exist. Same reasoning as
        // TopDownMotionDriver.CombatantRef and ProjectileWeapon's own _vectorAim lookup.
        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());

        /// The weapon this driver would fire right now, or null if it can't find one. Re-resolved every call
        /// rather than cached, because switching slots changes the answer — a cached reference would keep
        /// firing the weapon that was just put away.
        public ProjectileWeapon CurrentWeapon
        {
            get
            {
                if (weaponOverride != null) return weaponOverride;

                if (!_weaponSourceChecked)
                {
                    _weaponSource = GetComponentInParent<IActiveWeaponSource>();
                    _weaponSourceChecked = true;
                }
                if (_weaponSource != null)
                {
                    var active = _weaponSource.ActiveWeapon;
                    if (active != null) return active;
                }

                // No switcher (or nothing switched in yet): a plain single-weapon character. Search children
                // too — ZoeSpawner builds each weapon slot as a child GameObject, not a component on the root.
                if (!_fallbackChecked || _fallbackWeapon == null)
                {
                    _fallbackWeapon = GetComponentInChildren<ProjectileWeapon>();
                    _fallbackChecked = true;
                }
                return _fallbackWeapon;
            }
        }

        void Update()
        {
            var src = InputSource;
            if (src == null) return;

            var weapon = CurrentWeapon;
            if (weapon == null) return;

            if (WantsToFire(src, weapon)) weapon.TryFire();
        }

        bool WantsToFire(IZoeInputSource src, ProjectileWeapon weapon)
        {
            switch (trigger)
            {
                case TriggerMode.Press:   return src.FirePressed();
                case TriggerMode.Hold:    return src.FireHeld();
                case TriggerMode.Release: return src.FireReleased();
                default:                  return weapon.automatic ? src.FireHeld() : src.FirePressed();
            }
        }
    }
}
