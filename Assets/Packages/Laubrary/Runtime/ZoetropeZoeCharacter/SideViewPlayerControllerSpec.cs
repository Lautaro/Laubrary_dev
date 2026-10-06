using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;

namespace Laubrary.ZoetropeZoeCharacter
{
    /// <summary>
    /// An <see cref="IPlayerControllerSpec"/> for side-view characters (brawlers, fighting games, side-on
    /// platform walkers without a jump): <see cref="NewInputSystemSource"/> bound to a "Player" action map,
    /// <see cref="SideViewMotionDriver"/> for left/right movement along one line with its own facing, and
    /// <see cref="ZoeWeaponDriver"/> for fire, which shoots the way the body faces.
    ///
    /// The sibling of <see cref="TopDownPlayerControllerSpec"/>: same input asset shape, different locomotion.
    /// There is no aim driver: the facing IS the aim. Turning is a call on the motion driver, so the game
    /// decides what turns the character (a button, the end of a turn animation).
    /// </summary>
    [System.Serializable]
    public class SideViewPlayerControllerSpec : IPlayerControllerSpec
    {
        [Tooltip("Input asset with a 'Player' action map (Move/Aim/Fire). Only Move's horizontal part and Fire are used.")]
        public InputActionAsset controls;

        [Tooltip("Units per second at full move input.")]
        public float moveSpeed = 3f;

        [Tooltip("Which way the character faces when it appears.")]
        public bool startFacingRight = true;

        public void Attach(GameObject host)
        {
            if (controls == null) { Debug.LogError("SideViewPlayerControllerSpec: controls (InputActionAsset) not assigned."); return; }

            var map = controls.FindActionMap("Player");
            if (map == null) { Debug.LogError("SideViewPlayerControllerSpec: 'Player' action map not found on controls."); return; }

            var source = host.GetComponent<NewInputSystemSource>();
            if (source == null) source = host.AddComponent<NewInputSystemSource>();
            // Disabled while the fields are set, for the same reason as TopDownPlayerControllerSpec: OnEnable
            // enables the actions synchronously inside AddComponent, before the assignments would land.
            source.enabled = false;
            source.moveAction = InputActionReference.Create(map.FindAction("Move"));
            source.fireAction = InputActionReference.Create(map.FindAction("Fire"));
            source.enabled = true;

            var mover = host.GetComponent<SideViewMotionDriver>();
            if (mover == null) mover = host.AddComponent<SideViewMotionDriver>();
            mover.moveSpeed = moveSpeed;
            mover.startFacingRight = startFacingRight;

            if (host.GetComponent<ZoeWeaponDriver>() == null) host.AddComponent<ZoeWeaponDriver>();
        }
    }
}
