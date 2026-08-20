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
        string _lastZone;
        bool _started;
        IZonedView _zoned;

        MotionCondition? _overrideBucket;
        float _overrideAngle;

        /// Freezes this part onto an EXPLICIT (bucket, angle) pose instead of resolving from the character's
        /// real, live MotionState — the Mirage "pick a named pose and hold it" preview
        /// (<see cref="MotionPoseCatalog"/> derives the bucket/angle pairs worth offering). Forces a re-Play
        /// next LateUpdate (<c>_started = false</c>) so the override takes effect immediately even if it
        /// happens to match whatever clip/zone was already showing.
        public void SetPoseOverride(MotionCondition bucket, float angleDeg)
        {
            _overrideBucket = bucket; _overrideAngle = angleDeg; _started = false;
        }

        /// Returns to normal, live-MotionState-driven resolution.
        public void ClearPoseOverride()
        {
            if (!_overrideBucket.HasValue) return;
            _overrideBucket = null; _started = false;
        }

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
            _zoned = animatedView as IZonedView;
        }

        void OnEnable()
        {
            if (Arbiter != null) Arbiter.Reassert += OnArbiterReassert;
        }

        void OnDisable()
        {
            if (_arbiter != null) _arbiter.Reassert -= OnArbiterReassert;
        }

        void OnArbiterReassert() { _started = false; _lastZone = null; }

        void LateUpdate()
        {
            if (_pose == null || _version == null) return;

            LauminationResolution res;
            if (_overrideBucket.HasValue)
            {
                // Frozen preview pose: skip the live-state gate entirely (Mirage's own ZoeState, if present,
                // must never block a pose that was explicitly asked for) and resolve at the exact (bucket,
                // angle) requested instead of reading MotionStateSource/the direction latch.
                res = MotionPoseResolver.ResolveAt(_pose, _version, _overrideBucket.Value, _overrideAngle);
            }
            else
            {
                // Same gate LocomotionAnimator uses: never speak over a hurt/death reaction.
                if (State != null && !State.CanAct) { _started = false; return; }

                var state = Motion != null ? Motion.Current : MotionState.Idle;
                res = MotionPoseResolver.Resolve(_pose, _version, state, _latch);
            }
            if (res.Laumination == null) return;

            if (_flippable != null) _flippable.FlipX = res.FlipX;
            // World rotation, never local: a composite part's transform is parented under its owner (see
            // CompositeZonedPlayer), and local rotation would compound with a rotating parent's own facing.
            transform.rotation = Quaternion.Euler(0f, 0f, -res.RotationDeg);

            string clip = res.Laumination.name;
            string zone = res.ZoneName ?? "";

            // Dedup key is (clip, zone), not just clip — several directions can share ONE "rotation sheet"
            // clip (see LauminationSetMember.zoneName), so clip-name-only comparison would silently swallow
            // every direction change after the first once that's in play. When zone is always "" (today's
            // default, unchanged sets), zoneChanged is always false and this reduces to exactly the old
            // "only re-issue PlayClip on a CHANGE" guard — zero behaviour change for anything not opted in.
            bool clipChanged = !_started || clip != _lastClip;
            bool zoneChanged = zone != (_lastZone ?? "");
            if (!clipChanged && !zoneChanged) return;

            if (clipChanged)
            {
                _lastClip = clip;
                _started = true;
                // A clip switch already restarts at frame 0 / its first zone — only re-issue Play when the
                // CLIP itself changes, so an already-loaded rotation sheet doesn't restart just because the
                // zone target moved.
                if (Arbiter != null) Arbiter.Play(this, Priority, clip, loop: true);
                else _view?.PlayClip(clip, loop: true);
            }

            // Jump to the requested zone whenever it's non-empty and either the clip just (re)started or the
            // zone target itself changed — covers both "just switched onto the rotation sheet" (needs an
            // explicit jump past its default frame-0 zone) and "already on it, just moved to a new direction".
            // _lastZone only advances on a CONFIRMED entry — a failed TryEnterZone (view not zoned yet the
            // same frame Play() was just issued, an unknown zone name) must NOT be recorded as done, or
            // zoneChanged would go permanently false next frame and the jump would silently never retry.
            if (!string.IsNullOrEmpty(zone))
            {
                if (clipChanged || zoneChanged)
                {
                    bool entered = _zoned != null && _zoned.TryEnterZone(zone);
                    if (entered) _lastZone = zone;
                }
            }
            else
            {
                _lastZone = zone;
            }
        }
    }
}
