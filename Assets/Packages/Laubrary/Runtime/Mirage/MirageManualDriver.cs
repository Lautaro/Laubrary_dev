using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;

namespace Laubrary.Mirage
{
    /// <summary>
    /// Drives a previewed Zoe by hand — hold a facing, walk or don't, fire or don't — so a character can be put
    /// through its paces while its assets are being edited.
    ///
    /// It is a THIRD driver alongside the player's input pair and an AI brain, and deliberately not a special
    /// case of either: a Zoe declares what it can be asked to do (<see cref="ZoetropeLaunimator.ZoeCapabilities"/>)
    /// and anything can ask. That is what makes it possible to put an ENEMY under manual control to test it,
    /// with no "is this the player" branch anywhere.
    ///
    /// Movement is PUBLISHED, not performed: it writes a full <see cref="MotionState"/> override rather than
    /// translating the transform, so the character walks in place and stays framed in the preview instead of
    /// wandering off camera. <see cref="MotionStateSource.PublishOverride"/> exists for exactly this — its own
    /// doc names Mirage's motion preview as the caller.
    ///
    /// Travel direction is separate from aim on purpose. Locking them together would make it impossible to
    /// preview the case the leg system exists to handle — walking one way while facing another, where the walk
    /// cycle has to run backwards.
    /// </summary>
    [DisallowMultipleComponent]
    public class MirageManualDriver : MonoBehaviour
    {
        [Tooltip("Where the character is FACING, in degrees. 0 = up, increasing clockwise — the same convention " +
                 "as the direction sets its art is authored against.")]
        public float aimAngleDeg;

        [Tooltip("Whether the character is walking. Off = idle; the pose system picks its Idle rule.")]
        public bool walking;

        [Tooltip("Which way it TRAVELS while walking, in degrees. Ignored when Lock Move To Aim is on.")]
        public float moveAngleDeg;

        [Tooltip("Travel the way it faces. Turn OFF to preview strafing and backpedalling — walking east while " +
                 "aiming west is what makes the walk cycle run in reverse.")]
        public bool lockMoveToAim = true;

        [Tooltip("Hold the trigger. The weapon's own fire rate paces the shots, so this is safe to leave on.")]
        public bool firing;

        [Tooltip("Speed reported while walking, in units/second. Only has to clear the pose's move threshold " +
                 "for the character to read as moving — nothing actually travels.")]
        [Min(0f)] public float previewSpeed = 3f;

        Combatant _combatant;
        MotionStateSource _motion;
        IActiveWeaponSource _weaponSource;
        ProjectileWeapon _fallbackWeapon;

        // Lazily resolved and re-fetched while null — Mirage builds the character in pieces and this component
        // may land before the rest, the same reasoning every other lazy lookup in this codebase uses.
        Combatant Combatant => _combatant != null ? _combatant : (_combatant = GetComponentInChildren<Combatant>(true));
        MotionStateSource Motion => _motion != null ? _motion : (_motion = GetComponentInChildren<MotionStateSource>(true));

        /// The weapon a Fire press would use — whichever slot is equipped, else any weapon in the hierarchy.
        public ProjectileWeapon CurrentWeapon
        {
            get
            {
                if (_weaponSource == null) _weaponSource = GetComponentInChildren<IActiveWeaponSource>(true);
                var active = _weaponSource != null ? _weaponSource.ActiveWeapon : null;
                if (active != null) return active;
                if (_fallbackWeapon == null) _fallbackWeapon = GetComponentInChildren<ProjectileWeapon>(true);
                return _fallbackWeapon;
            }
        }

        /// 0° = up, clockwise — matches MotionPoseResolver's own angle convention, so an angle taken straight
        /// from ZoeCapabilities.AimAngles reproduces exactly that pose.
        public static Vector2 AngleToVector(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        void OnDisable()
        {
            // Hand the body back to real inference rather than leaving it frozen mid-stride.
            if (Motion != null) Motion.ClearOverride();
        }

        void Update()
        {
            Vector2 aim = AngleToVector(aimAngleDeg);
            Vector2 travel = lockMoveToAim ? aim : AngleToVector(moveAngleDeg);

            var combatant = Combatant;
            if (combatant != null) combatant.aimDirection = aim;

            var motion = Motion;
            if (motion != null)
            {
                // forSeconds = 0 holds the override until it is replaced or cleared, which is what a preview
                // wants: every frame restates it, and OnDisable releases it.
                motion.PublishOverride(new MotionState
                {
                    velocity = walking ? (Vector3)(travel * previewSpeed) : Vector3.zero,
                    speed = walking ? previewSpeed : 0f,
                    heading = walking ? travel : Vector2.zero,
                    facing = aim,
                    aim = aim,
                    grounded = true,
                    selfWilled = true,
                }, 0f);
            }

            if (firing)
            {
                var w = CurrentWeapon;
                if (w != null) w.TryFire();   // its own cooldown paces this
            }
        }
    }
}
