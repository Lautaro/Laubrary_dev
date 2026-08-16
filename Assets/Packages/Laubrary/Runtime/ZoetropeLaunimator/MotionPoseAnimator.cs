using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// Plays a character's (or, once composite parts wire their own, one part's) <see cref="MotionPose"/> each
    /// frame: resolves the pose against <see cref="MotionStateSource"/> via <see cref="MotionPoseResolver"/>,
    /// writes the flip through <see cref="IFlippableView"/>, writes rotation as WORLD rotation (ZOE_MOVEMENT_DESIGN.md
    /// section 8.1, break 2 — a rotating parent must not carry its children's aim with it), and submits the
    /// resolved clip to the <see cref="AnimationArbiter"/> at the same steady-state priority
    /// <see cref="LocomotionAnimator"/> uses, so a reaction can preempt it the same way.
    ///
    /// The generalised, directional replacement for <see cref="LocomotionAnimator"/> — <see cref="ZoeSpawner"/>
    /// attaches this INSTEAD of LocomotionAnimator when a Zoe authors <see cref="MotionPose.IsAuthored"/>, and
    /// the old two-clip Locomotion otherwise, so an existing Zoe with no MotionPose authored is unaffected.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class MotionPoseAnimator : MonoBehaviour
    {
        public const float Priority = LocomotionAnimator.Priority;

        MotionPose _pose;
        LauminaryVersion _version;
        IAnimatedView _view;
        IFlippableView _flippable;

        ZoeState _state;
        MotionStateSource _motion;
        AnimationArbiter _arbiter;
        readonly LatchedDirection _latch = new LatchedDirection();

        string _lastClip;
        bool _started;

        // State and Motion are looked up UP THE HIERARCHY, not just this GameObject: for a composite Zoe this
        // component lives on one PART's child GameObject (see CompositeZonedPlayer.Build), but ZoeState (dead/
        // stunned) and MotionStateSource (the body's own measured motion) are character-wide and live on the
        // root. GetComponentInParent also finds them on the object itself for the single-body case, so this is
        // a pure generalisation with no behaviour change there. The arbiter stays per-GameObject: each part
        // owns its own PlayClip, so a reaction targeting one part can preempt it without touching another.
        ZoeState State => _state != null ? _state : (_state = GetComponentInParent<ZoeState>());
        MotionStateSource Motion => _motion != null ? _motion : (_motion = GetComponentInParent<MotionStateSource>());
        AnimationArbiter Arbiter => _arbiter != null ? _arbiter : (_arbiter = GetComponent<AnimationArbiter>());

        public void Bind(MotionPose pose, LauminaryVersion version, IAnimatedView animatedView)
        {
            _pose = pose;
            _version = version;
            _view = animatedView;
            _flippable = animatedView as IFlippableView;
        }

        void OnEnable()
        {
            if (Arbiter != null) Arbiter.Reassert += OnArbiterReassert;
        }

        void OnDisable()
        {
            if (_arbiter != null) _arbiter.Reassert -= OnArbiterReassert;
        }

        void OnArbiterReassert() => _started = false;

        void LateUpdate()
        {
            if (_pose == null || _version == null) return;

            // Same gate LocomotionAnimator uses: never speak over a hurt/death reaction.
            if (State != null && !State.CanAct) { _started = false; return; }

            var state = Motion != null ? Motion.Current : MotionState.Idle;
            var res = MotionPoseResolver.Resolve(_pose, _version, state, _latch);
            if (res.Laumination == null) return;

            if (_flippable != null) _flippable.FlipX = res.FlipX;
            // World rotation, never local: a composite part's transform is parented under its owner (see
            // CompositeZonedPlayer), and local rotation would compound with a rotating parent's own facing.
            transform.rotation = Quaternion.Euler(0f, 0f, -res.RotationDeg);

            string clip = res.Laumination.name;
            if (_started && clip == _lastClip) return;   // only re-issue PlayClip on a CHANGE, same as Locomotion

            _lastClip = clip;
            _started = true;

            if (Arbiter != null) Arbiter.Play(this, Priority, clip, loop: true);
            else _view?.PlayClip(clip, loop: true);
        }
    }
}
