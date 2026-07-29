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
    /// for <c>ICueSink</c>. Added by <see cref="ZonedReelView.Build"/> alongside the player it wraps.
    /// </summary>
    [RequireComponent(typeof(ZonedAnimationPlayer))]
    public class AnimatedViewRelay : MonoBehaviour, IAnimatedView
    {
        ZonedAnimationPlayer _player;
        Action _pendingComplete;

        /// Forwards ZonedAnimationPlayer.OnFrameEvent verbatim — see IAnimatedView's own doc comment.
        public event Action<string, int> OnFrameEvent;

        void Awake() => _player = GetComponent<ZonedAnimationPlayer>();

        void OnEnable()
        {
            if (_player == null) _player = GetComponent<ZonedAnimationPlayer>();
            if (_player != null) { _player.OnComplete += HandleComplete; _player.OnFrameEvent += HandleFrameEvent; }
        }

        void OnDisable()
        {
            if (_player != null) { _player.OnComplete -= HandleComplete; _player.OnFrameEvent -= HandleFrameEvent; }
        }

        public bool PlayClip(string clip, bool loop, Action onComplete = null)
        {
            _pendingComplete = loop ? null : onComplete;
            return _player != null && _player.Play(clip, loop);
        }

        public void Hide()
        {
            _pendingComplete = null;   // nothing is playing to complete
            _player?.Hide();
        }

        public bool TryGetMetaPoint(string layerId, out Vector2 worldPos)
        {
            worldPos = default;
            if (_player == null || !_player.TryGetMetaPoint(layerId, out var w, out _)) return false;
            worldPos = w;
            return true;
        }

        void HandleFrameEvent(string name, int frame) => OnFrameEvent?.Invoke(name, frame);

        void HandleComplete()
        {
            var cb = _pendingComplete;
            _pendingComplete = null;
            cb?.Invoke();
        }
    }
}
