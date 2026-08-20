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
        public float upperSortingOrder = 1f;

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

            // Alignment itself is now the Rig's job (Zoe.view's composite parts, configured in the Zoe editor's
            // Rig section — see AttachAnchor/ZoeBodyPart), resolved live every frame by CompositeZonedPlayer.
            // Only sorting order is left here — a scene-composition concern (which sprite draws in front), not
            // a rig-alignment one. Composite parts are flat children of the spawned host (never nested under
            // each other — CompositeZonedPlayer.Build parents every part directly to the root and repositions
            // them by anchor each frame instead of by Transform hierarchy), so this is a direct child lookup.
            var upper = host.transform.Find("Upper");
            if (upper != null)
            {
                var sr = upper.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sortingOrder = (int)upperSortingOrder;
            }
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
        }
    }
}
