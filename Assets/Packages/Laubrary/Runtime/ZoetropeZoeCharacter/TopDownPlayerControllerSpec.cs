using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;

namespace Laubrary.ZoetropeZoeCharacter
{
    /// <summary>
    /// An <see cref="IPlayerControllerSpec"/> that gives a character the real reusable top-down input stack:
    /// <see cref="NewInputSystemSource"/> bound to a "Player" action map, <see cref="TopDownMotionDriver"/> for
    /// move, <see cref="AimDriver"/> for aim, and <see cref="ZoeWeaponDriver"/> for fire. The OPTIONAL
    /// ZoeCharacter bridge — pulled in only by projects that want a Zoe drivable by a person, same as
    /// <c>DaemonBrainSpec</c> is the optional bridge for AI.
    ///
    /// Move, aim and fire are three separate, independently swappable components on purpose (see
    /// <see cref="TopDownMotionDriver"/>'s own doc) — only locomotion is perspective-specific (a platformer
    /// needs gravity and a jump; top-down doesn't); aim and fire read identically in every 2D perspective. A
    /// future perspective (e.g. a platformer) supplies its OWN <see cref="IPlayerControllerSpec"/> that attaches
    /// a different motion driver while reusing <see cref="AimDriver"/> and <see cref="ZoeWeaponDriver"/> as-is;
    /// it never has to touch this class. Likewise, swapping ONLY the aim style (e.g. lock-on-nearest instead of
    /// stick/cursor) means writing one new component that plays the same role as <see cref="AimDriver"/> —
    /// nothing that reads <c>Combatant.aimDirection</c> downstream (fire, animation) needs to change.
    /// </summary>
    [System.Serializable]
    public class TopDownPlayerControllerSpec : IPlayerControllerSpec
    {
        [Tooltip("Input asset with a 'Player' action map (Move/Aim/Fire). Reusable across any top-down/twin-stick Zoe.")]
        public InputActionAsset controls;

        [Tooltip("Units per second at full move input magnitude.")]
        public float moveSpeed = 3f;

        public void Attach(GameObject host)
        {
            if (controls == null) { Debug.LogError("TopDownPlayerControllerSpec: controls (InputActionAsset) not assigned."); return; }

            var map = controls.FindActionMap("Player");
            if (map == null) { Debug.LogError("TopDownPlayerControllerSpec: 'Player' action map not found on controls."); return; }

            var source = host.GetComponent<NewInputSystemSource>();
            if (source == null) source = host.AddComponent<NewInputSystemSource>();
            // OnEnable (which Enables the actions) fires synchronously on AddComponent, before these fields
            // would otherwise be set — disable first so the assignments land before OnEnable runs for real.
            source.enabled = false;
            source.moveAction = InputActionReference.Create(map.FindAction("Move"));
            source.aimAction = InputActionReference.Create(map.FindAction("Aim"));
            source.fireAction = InputActionReference.Create(map.FindAction("Fire"));
            source.enabled = true;

            var mover = host.GetComponent<TopDownMotionDriver>();
            if (mover == null) mover = host.AddComponent<TopDownMotionDriver>();
            mover.moveSpeed = moveSpeed;

            if (host.GetComponent<AimDriver>() == null) host.AddComponent<AimDriver>();

            // Finds the equipped weapon itself through the WeaponSwitcher ZoeSpawner.EquipWeaponSlots already
            // put on the host — nothing to wire here.
            if (host.GetComponent<ZoeWeaponDriver>() == null) host.AddComponent<ZoeWeaponDriver>();
        }
    }
}
