using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// Which clips a character plays just for MOVING. Character data, not weapon data: a zombie shambles the
    /// same whatever it is holding, so this belongs on the Zoe and a weapon never overrides it.
    ///
    /// Deliberately only two states. A full state machine is a different tool (Daemon / Story); this is the
    /// floor — "is it going somewhere or not" — which is what every crowd, patrol and walk cycle actually
    /// needs, and what a swarm of enemies sliding across the floor without a walk cycle is missing.
    [System.Serializable]
    public class Locomotion
    {
        [Tooltip("Clip while standing still. Empty = leave whatever the view is already showing.")]
        public string idleClip = "";

        [Tooltip("Clip while moving. Empty disables locomotion entirely — the character keeps its resting pose.")]
        public string moveClip = "";

        [Tooltip("Speed, in world units per second, above which the character counts as moving. Small enough " +
                 "that a slow shamble still animates; large enough that being nudged does not.")]
        [Min(0f)] public float moveThreshold = 0.15f;

        public bool IsAuthored => !string.IsNullOrEmpty(moveClip);
    }

    /// Plays a character's <see cref="Locomotion"/> clips based on how fast it is actually travelling.
    ///
    /// Measures the transform rather than taking a velocity from whoever moves it, so it works identically for
    /// a Rigidbody, a Choreographer dancer, a SwarmMover's own steering, or a scripted cutscene walk — none of
    /// which share an interface. That is the same input-agnostic rule the rest of the project follows: the
    /// component takes its "what moves me" from the world, not from a specific mover.
    [RequireComponent(typeof(Transform))]
    public class LocomotionAnimator : MonoBehaviour
    {
        Locomotion clips;
        IAnimatedView view;
        Vector3 lastPos;
        bool moving, started;
        float suppressedUntil;

        public void Bind(Locomotion locomotion, IAnimatedView animatedView)
        {
            clips = locomotion;
            view = animatedView;
            lastPos = transform.position;
        }

        /// Hand control away for a moment — for a one-shot that must not be stomped on the next state change
        /// (a hit reaction, a fire animation). Locomotion resumes on its own afterwards, so a caller never has
        /// to remember to give control back, which is the failure mode of a plain enable/disable flag.
        public void SuppressFor(float seconds)
        {
            suppressedUntil = Mathf.Max(suppressedUntil, Time.time + Mathf.Max(0f, seconds));
            started = false;   // force a re-play when locomotion resumes, whatever the reaction left showing
        }

        void LateUpdate()
        {
            if (clips == null || view == null || !clips.IsAuthored) return;

            var pos = transform.position;
            float speed = Time.deltaTime > 0f ? (pos - lastPos).magnitude / Time.deltaTime : 0f;
            lastPos = pos;

            if (Time.time < suppressedUntil) return;

            bool nowMoving = speed > clips.moveThreshold;
            // Only on a CHANGE — re-issuing PlayClip every frame would restart the clip on frame 0 forever,
            // which looks like a character twitching in place rather than walking.
            if (started && nowMoving == moving) return;

            moving = nowMoving;
            started = true;

            string clip = moving ? clips.moveClip : clips.idleClip;
            if (!string.IsNullOrEmpty(clip)) view.PlayClip(clip, loop: true);
        }
    }
}
