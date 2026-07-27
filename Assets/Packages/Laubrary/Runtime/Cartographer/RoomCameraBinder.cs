using UnityEngine;

namespace Laubrary.Cartographer
{
    /// Points a Camera at whatever the RoomDirector says the view centre is.
    ///
    /// Kept separate from the director on purpose: the director decides where play is looking, and that is
    /// useful to a minimap, a cutscene or a test with no camera at all. This is the one small piece that says
    /// "and a Camera should follow it", so a project wanting different camera behaviour replaces this file and
    /// nothing else.
    [RequireComponent(typeof(Camera))]
    public class RoomCameraBinder : MonoBehaviour
    {
        [Tooltip("The director whose view centre this camera follows. Found on the level in the scene if empty.")]
        public RoomDirector director;

        [Tooltip("How fast the camera closes on the view centre, in units per second per unit of error. " +
                 "0 snaps exactly — right for a hard arcade scroll; a few units softens it.")]
        [Min(0f)] public float follow = 0f;

        [Tooltip("Kept constant so the camera does not drift toward the level plane.")]
        public float depth = -10f;

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (director == null) director = FindAnyObjectByType<RoomDirector>();
        }

        void LateUpdate()
        {
            if (director == null) return;

            var want = director.ViewCenter;
            want.z = depth;

            transform.position = follow <= 0f
                ? want
                : Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-follow * Time.deltaTime));
        }
    }
}
