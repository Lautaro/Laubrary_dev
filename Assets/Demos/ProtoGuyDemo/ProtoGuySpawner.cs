using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Spawns the ProtoGuy Zoe LIVE at Play-time, same pattern as GalleryDirector — <c>ZoeSpawner.SpawnCharacter</c>
    /// wires up runtime-only state (MotionPoseAnimator's Bind()) that is NOT scene-serializable, so a composite
    /// Zoe must be spawned by code each time the scene runs, never pre-baked into the saved scene by an editor
    /// script. (Found the hard way: a pre-baked composite character looked right in the Editor but every part's
    /// MotionPoseAnimator came back unbound after a scene reload — nothing to animate, frozen on frame 0.)
    ///
    /// Drives the spawned character with the REAL reusable input stack — <see cref="NewInputSystemSource"/> bound
    /// to the shared <c>ZoeCharacterControls.inputactions</c> asset (gamepad + keyboard/mouse) and
    /// <see cref="TopDownMotionDriver"/> — instead of the throwaway <c>ProtoGuyTestRig</c> used for the first
    /// animation-only verification pass.
    /// </summary>
    public class ProtoGuySpawner : MonoBehaviour
    {
        public Zoe zoeDef;

        [Tooltip("Shared Laubrary.ZoeCharacter input asset (Player action map: Move/Aim/Fire). Reusable across " +
                 "any top-down/twin-stick Zoe, not specific to ProtoGuy.")]
        public InputActionAsset controls;

        [Tooltip("Units per second at full move input magnitude.")]
        public float moveSpeed = 3f;

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("ProtoGuySpawner: zoeDef not assigned."); return; }

            var host = ZoeSpawner.SpawnCharacter(zoeDef, transform.position);
            AddInputDriver(host);

            // Nothing else to do here. Both alignment AND draw order are the Zoe's own data now — anchors and
            // ZoeBodyPart.sortingOrder, resolved by CompositeZonedPlayer for every consumer alike. This used
            // to patch the torso's sortingOrder after spawn, which made THIS scene look correct while the real
            // spawn path (a game, or Mirage) drew the character inside-out, with nothing to reveal the gap.
            // A demo that fixes something up locally is a demo that hides a missing feature.
        }

        void AddInputDriver(GameObject host)
        {
            if (controls == null) { Debug.LogError("ProtoGuySpawner: controls (InputActionAsset) not assigned."); return; }

            var map = controls.FindActionMap("Player");
            if (map == null) { Debug.LogError("ProtoGuySpawner: 'Player' action map not found on controls."); return; }

            var source = host.AddComponent<NewInputSystemSource>();
            // OnEnable (which Enables the actions) fires synchronously on AddComponent, before these fields
            // would otherwise be set — disable first so the assignments land before OnEnable runs for real.
            source.enabled = false;
            source.moveAction = InputActionReference.Create(map.FindAction("Move"));
            source.aimAction = InputActionReference.Create(map.FindAction("Aim"));
            source.fireAction = InputActionReference.Create(map.FindAction("Fire"));
            source.enabled = true;

            host.AddComponent<TopDownMotionDriver>().moveSpeed = moveSpeed;

            // Fire is a separate driver on purpose — TopDownMotionDriver owns a perspective-SPECIFIC
            // locomotion model, while pulling a trigger is identical in every 2D perspective. Keeping them
            // apart is what lets a platformer reuse the fire half verbatim and rewrite only the motion half.
            // It finds ProtoGuy's equipped weapon itself through the WeaponSwitcher that
            // ZoeSpawner.EquipWeaponSlots already put on the host, so there is nothing to wire here.
            host.AddComponent<ZoeWeaponDriver>();
        }
    }
}
