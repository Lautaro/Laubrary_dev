using System;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// The <see cref="IAnimatedView"/> a Zoned Launimator view provides — wraps <see cref="ZonedAnimationPlayer"/>
    /// so core Zoetrope (<see cref="HitReactionPlayer"/>, <see cref="TargetPracticeController"/>) can drive
    /// named-clip playback without referencing Launimator directly, same split as <see cref="CueRelay"/> does
    /// for <c>ICueSink</c>. Added by <see cref="ZonedLauminaryView.Build"/> alongside the player it wraps.
    /// </summary>
    [RequireComponent(typeof(ZonedAnimationPlayer))]
    public class AnimatedViewRelay : MonoBehaviour, IAnimatedView, IFlippableView, IMotionPoseHost
    {
        ZonedAnimationPlayer _player;
        Action _pendingComplete;

        public bool FlipX
        {
            get => _player != null && _player.flipX;
            set { if (_player != null) _player.flipX = value; }
        }

        // IMotionPoseHost: attach + Bind a directional pose animator on this same GameObject (the arbiter is
        // per-GameObject/per-part, matching MotionPoseAnimator's own doc comment). No-op if the pose is
        // unauthored — an authored-but-empty MotionPose degrades to "nothing to animate", not an error, same
        // rule every other optional-capability host in this codebase follows.
        public void BindMotionPose(GameObject go, MotionPose pose)
        {
            if (pose == null || !pose.IsAuthored) return;
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (go.GetComponent<AnimationArbiter>() == null) go.AddComponent<AnimationArbiter>();
            var animator = go.GetComponent<MotionPoseAnimator>() ?? go.AddComponent<MotionPoseAnimator>();
            animator.Bind(pose, _player != null ? _player.version : null, this);
        }

        /// Forwards ZonedAnimationPlayer.OnFrameEvent verbatim — see IAnimatedView's own doc comment.
        public event Action<string, int> OnFrameEvent;

        /// Forwards ZonedAnimationPlayer.OnFrameEntered verbatim — every frame entry, annotated or not.
        public event Action<int> OnFrameEntered;

        /// The lauminary's own frame index, or -1 when there is no player to ask.
        public int CurrentFrame => _player != null ? _player.CurrentFrame : -1;

        void Awake() => _player = GetComponent<ZonedAnimationPlayer>();

        void OnEnable()
        {
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player != null) { _player.OnComplete += HandleComplete; _player.OnFrameEvent += HandleFrameEvent; _player.OnFrameEntered += HandleFrameEntered; }
        }

        void OnDisable()
        {
            if (_player != null) { _player.OnComplete -= HandleComplete; _player.OnFrameEvent -= HandleFrameEvent; _player.OnFrameEntered -= HandleFrameEntered; }
        }

        public bool PlayClip(string clip, bool loop, Action onComplete = null)
        {
            _pendingComplete = loop ? null : onComplete;
            return _player != null && _player.Play(clip, loop);
        }

        /// Forwards ZonedAnimationPlayer.GetClipSeconds — frames / fps for a plain clip, 0 for an unknown name
        /// or a zoned strip (whose end is not fixed). See IAnimatedView's own doc comment.
        public float GetClipSeconds(string clip) => _player != null ? _player.GetClipSeconds(clip) : 0f;

        public void Hide()
        {
            _pendingComplete = null;   // nothing is playing to complete
            _player?.Hide();
        }

        public bool TryGetMetaPoint(string layerId, out Vector2 worldPos)
        {
            worldPos = default;
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player == null || !_player.TryGetMetaPoint(layerId, out var w, out _)) return false;
            worldPos = w;
            return true;
        }

        public bool TryGetMetaPointNearest(string layerId, out Vector2 worldPos)
        {
            worldPos = default;
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player == null || !_player.TryGetMetaPointNearest(layerId, out var w, out _)) return false;
            worldPos = w;
            return true;
        }

        public bool TryGetMetaVector(string layerId, out Vector2 worldOrigin, out Vector2 worldDirection, out float worldLength)
        {
            worldOrigin = default; worldDirection = Vector2.up; worldLength = 0f;
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player == null || !_player.TryGetMetaVector(layerId, out var o, out worldDirection, out worldLength)) return false;
            worldOrigin = o;
            return true;
        }

        public bool TryGetMetaVectorNearest(string layerId, out Vector2 worldOrigin, out Vector2 worldDirection, out float worldLength)
        {
            worldOrigin = default; worldDirection = Vector2.up; worldLength = 0f;
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player == null || !_player.TryGetMetaVectorNearest(layerId, out var o, out worldDirection, out worldLength)) return false;
            worldOrigin = o;
            return true;
        }

        void HandleFrameEvent(string name, int frame) => OnFrameEvent?.Invoke(name, frame);
        void HandleFrameEntered(int frame) => OnFrameEntered?.Invoke(frame);

        void HandleComplete()
        {
            var cb = _pendingComplete;
            _pendingComplete = null;
            cb?.Invoke();
        }
    }
}
