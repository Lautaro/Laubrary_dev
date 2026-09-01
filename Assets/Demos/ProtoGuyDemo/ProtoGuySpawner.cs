using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Spawns the ProtoGuy Zoe LIVE at Play-time, same pattern as GalleryDirector — <c>ZoeSpawner.SpawnCharacter</c>
    /// wires up runtime-only state (MotionPoseAnimator's Bind()) that is NOT scene-serializable, so a composite
    /// Zoe must be spawned by code each time the scene runs, never pre-baked into the saved scene by an editor
    /// script. (Found the hard way: a pre-baked composite character looked right in the Editor but every part's
    /// MotionPoseAnimator came back unbound after a scene reload — nothing to animate, frozen on frame 0.)
    ///
    /// The real reusable input stack (gamepad + keyboard/mouse) is no longer wired up HERE — it's attached
    /// automatically by <c>ZoeSpawner.SpawnCharacter</c> itself, driven by ProtoGuy.asset's own
    /// <c>playerController</c> field (a <c>TopDownPlayerControllerSpec</c>). That is what makes the SAME
    /// gamepad control work in Mirage's "Preview in Mirage" too, with no Mirage-side wiring: input rigging is
    /// character DATA now, not something a demo scene bolts on for itself.
    /// </summary>
    public class ProtoGuySpawner : MonoBehaviour
    {
        public Zoe zoeDef;

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("ProtoGuySpawner: zoeDef not assigned."); return; }

            ZoeSpawner.SpawnCharacter(zoeDef, transform.position);

            // Nothing else to do here. Both alignment AND draw order are the Zoe's own data now — anchors and
            // ZoeBodyPart.sortingOrder, resolved by CompositeZonedPlayer for every consumer alike. This used
            // to patch the torso's sortingOrder after spawn, which made THIS scene look correct while the real
            // spawn path (a game, or Mirage) drew the character inside-out, with nothing to reveal the gap.
            // A demo that fixes something up locally is a demo that hides a missing feature.
        }
    }
}
