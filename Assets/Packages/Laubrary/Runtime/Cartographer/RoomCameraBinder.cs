using UnityEngine;

namespace Laubrary.Cartographer
{
    /// Points a Camera at whatever the Arena says the view centre is.
    ///
    /// Kept separate from the Arena on purpose: the Arena decides where play is looking, and that is useful
    /// to a minimap, a cutscene or a test with no camera at all. This is the one small piece that says "and a
    /// Camera should follow it", so a project wanting different camera behaviour replaces this file and
    /// nothing else. Named for the Room, not the Arena, because what it actually reads is the *Room's* camera
    /// mode (Follow / Rail / Focus) — the Arena is only where it looks that up.
    [RequireComponent(typeof(Camera))]
    public class RoomCameraBinder : MonoBehaviour
    {
        [Tooltip("The Arena whose view centre this camera follows. Found on the level in the scene if empty.")]
        public Arena arena;

        [Tooltip("How fast the camera closes on the view centre, in units per second per unit of error. " +
                 "0 snaps exactly — right for a hard arcade scroll; a few units softens it.")]
        [Min(0f)] public float follow = 0f;

        [Tooltip("Kept constant so the camera does not drift toward the level plane.")]
        public float depth = -10f;

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (arena == null) arena = FindAnyObjectByType<Arena>();
        }

        void LateUpdate()
        {
            if (arena == null) return;

            // CameraPosition, not ViewCenter: the room's camera mode (Rail / Focus) can deliberately look
            // somewhere other than where play is happening.
            var want = arena.CameraPosition;
            want.z = depth;

            transform.position = follow <= 0f
                ? want
                : Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-follow * Time.deltaTime));
        }
    }
}
