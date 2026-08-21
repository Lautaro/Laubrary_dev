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
        int _lastFrame = -1;   // -1 = no frame held (see LauminationResolution.FrameIndex)
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

        void OnArbiterReassert() { _started = false; _lastZone = null; _lastFrame = -1; }

        /// <summary>Is this part walking BACKWARDS — travelling roughly opposite the way it faces?
        ///
        /// Only meaningful when the matched rule steers by <see cref="DirectionChannel.Aim"/>. That is the
        /// whole point: once the leg cycle's direction is chosen by where the torso AIMS rather than where the
        /// body travels, "aiming west while moving east" is a backpedal, and the same clip reversed is what
        /// makes the feet push the right way instead of moonwalking. A part still steering by Heading always
        /// faces the way it moves, so it is never backpedalling and this returns false — which is why the
        /// answer is gated on the channel and not just on the two vectors.
        ///
        /// Perpendicular movement (strafing) reports false, i.e. plays forward. With only forward-facing walk
        /// art there is no sideways cycle to choose, and forward is the closer of the two available readings;
        /// true strafing needs its own art, not a playback trick.</summary>
        bool ShouldBackpedal()
        {
            if (_overrideBucket.HasValue) return false;   // frozen preview pose: no real motion to compare against
            if (_pose == null || Motion == null) return false;

            var state = Motion.Current;
            if (state.speed <= _pose.moveThreshold) return false;   // standing still has no travel direction

            var rule = _pose.Match(state);
            if (rule == null) return false;
            var channel = rule.overrideChannel ? rule.channel : _pose.channel;
            if (channel != DirectionChannel.Aim) return false;

            Vector2 travel = state.heading, facing = state.aim;
            if (travel.sqrMagnitude < 1e-6f || facing.sqrMagnitude < 1e-6f) return false;
            return Vector2.Dot(travel.normalized, facing.normalized) < 0f;
        }

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
            _view?.SetPlaybackReversed(ShouldBackpedal());
            // World rotation, never local: a composite part's transform is parented under its owner (see
            // CompositeZonedPlayer), and local rotation would compound with a rotating parent's own facing.
            transform.rotation = Quaternion.Euler(0f, 0f, -res.RotationDeg);

            string clip = res.Laumination.name;
            string zone = res.ZoneName ?? "";
            int frame = res.HasFrame ? res.FrameIndex : -1;

            // Dedup key is (clip, zone, frame), not just clip — several directions can share ONE "rotation
            // sheet" clip (by zone name, or by frame index for a DirectionMode.Rotation set), so
            // clip-name-only comparison would silently swallow every direction change after the first once
            // that's in play. When zone is "" and frame is -1 (a plain per-direction-clip set), neither
            // changes, and this reduces to exactly the old "only re-issue PlayClip on a CHANGE" guard.
            bool clipChanged = !_started || clip != _lastClip;
            bool zoneChanged = zone != (_lastZone ?? "");
            bool frameChanged = frame != _lastFrame;
            // A held FRAME is re-asserted every frame, not just when it changes. Unlike a clip or a zone, it is
            // a position the player can be knocked off by anyone else — the arbiter re-playing, a reaction
            // finishing, a domain reload — and Play() restarts at frame 0. Gating re-entry on "did the
            // direction change" then leaves it stuck on frame 0 forever: the pose is wrong, `_lastFrame` still
            // says it was handled, and nothing ever corrects it (observed as a composite torso frozen facing
            // one way while the legs kept resolving). TryEnterFrame is idempotent and cheap when already
            // parked, so asserting it unconditionally is self-healing at no cost.
            if (!clipChanged && !zoneChanged && !frameChanged && frame < 0) return;

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

            // Same contract for a Rotation sheet, one step simpler: the resolved direction IS a frame index,
            // so hold it. _lastFrame only advances on a CONFIRMED entry, for exactly the reason _lastZone
            // does — a failed TryEnterFrame (view not resolved yet the same frame Play() was issued) must not
            // be recorded as done, or frameChanged would go permanently false and the pose would stick.
            if (frame >= 0)
            {
                // Unconditional, for the reason above — this is a position to HOLD, so it must be restated
                // rather than fired once and assumed to stick.
                bool entered = _zoned != null && _zoned.TryEnterFrame(frame);
                if (entered) _lastFrame = frame;
            }
            else
            {
                _lastFrame = -1;
            }
        }
    }
}
