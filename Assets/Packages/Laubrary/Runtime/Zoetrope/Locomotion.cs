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
        // Steady-state claim priority. Points at the ladder on AnimationArbiter rather than restating a
        // number, so this and MotionPoseAnimator (the directional replacement for this class, which claims at
        // the same level so a reaction preempts either equally) cannot silently drift apart.
        public const float Priority = AnimationArbiter.PriorityLocomotion;

        Locomotion clips;
        IAnimatedView view;
        ZoeState _state;
        AnimationArbiter _arbiter;
        Vector3 lastPos;

        // Resolved LAZILY, never cached at Bind time. ZoeSpawner attaches this animator while building the
        // view and ZoeState much later (it has to come after ReactionFxPlayer, which it listens to), so a
        // Bind-time GetComponent found NOTHING and the gate silently never applied — corpses walked, and the
        // death clip was stomped by the walk cycle. The symptom was beautifully specific: death only survived
        // if the character was ALREADY stunned, because a stopped character produces no moving/idle change,
        // so locomotion had no reason to re-issue its clip over the top.
        ZoeState State => _state != null ? _state : (_state = GetComponent<ZoeState>());
        /// Same lazy rule, same reason: the arbiter is attached by the spawner and this animator can be added
        /// before or after it depending on the path.
        AnimationArbiter Arbiter => _arbiter != null ? _arbiter : (_arbiter = GetComponent<AnimationArbiter>());
        bool moving, started;

        public void Bind(Locomotion locomotion, IAnimatedView animatedView)
        {
            clips = locomotion;
            view = animatedView;
            lastPos = transform.position;
        }

        void OnEnable()
        {
            if (Arbiter != null) Arbiter.Reassert += OnArbiterReassert;
        }

        void OnDisable()
        {
            if (_arbiter != null) _arbiter.Reassert -= OnArbiterReassert;
        }

        // The body became free again (a reaction finished, a claim expired). Forget what we last issued so the
        // next LateUpdate re-plays the current clip instead of concluding nothing changed — otherwise a
        // character stands frozen on the last frame of its hurt animation until it happens to start or stop
        // moving. Exactly MotionPoseAnimator's OnArbiterReassert.
        void OnArbiterReassert() { started = false; }

        void LateUpdate()
        {
            if (clips == null || view == null || !clips.IsAuthored) return;

            var pos = transform.position;
            float speed = Time.deltaTime > 0f ? (pos - lastPos).magnitude / Time.deltaTime : 0f;
            lastPos = pos;

            // Never speak over a hurt or death reaction. Locomotion re-issuing its clip on the next
            // moving/idle change is exactly why the hurt animation "never showed": ReactionFxPlayer started
            // it and the walk cycle overwrote it a frame later. A stunned or dead character animates by
            // whatever hit it, not by where it happens to be drifting.
            if (State != null && !State.CanAct) { started = false; return; }

            bool nowMoving = speed > clips.moveThreshold;
            // Only on a CHANGE — re-issuing PlayClip every frame would restart the clip on frame 0 forever,
            // which looks like a character twitching in place rather than walking.
            if (started && nowMoving == moving) return;

            moving = nowMoving;
            started = true;

            string clip = moving ? clips.moveClip : clips.idleClip;
            if (string.IsNullOrEmpty(clip)) return;
            // Submit a claim rather than playing directly, so a reaction already holding the body refuses this
            // instead of being overwritten a frame later. A refusal is not an error — the reaction is showing,
            // and Reassert brings us back when it lets go.
            if (Arbiter != null) { if (!Arbiter.Play(this, Priority, clip, loop: true)) started = false; }
            else view.PlayClip(clip, loop: true);
        }
    }
}
