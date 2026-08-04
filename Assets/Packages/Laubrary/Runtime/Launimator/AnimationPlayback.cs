using System;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// THE animation player for Launimator — the single source of truth for "what frame is showing now".
    /// It owns all playback timing/looping/event logic over an <see cref="Laumination"/>'s baked
    /// <see cref="Laumination.frames"/>, and is deliberately free of any rendering surface: it does NOT
    /// touch a SpriteRenderer or do any drawing. Consumers read <see cref="CurrentSprite"/> and present it
    /// however they must — the runtime <see cref="LauminaryPlayer"/> pushes it to a SpriteRenderer; the
    /// editor previews blit it in IMGUI. Because every surface advances through this one class, the editor
    /// previews and the in-game playback can never drift (which is exactly the bug that having three
    /// hand-rolled play loops produced).
    /// </summary>
    public class AnimationPlayback
    {
        Laumination _anim;
        int _i;
        float _t;
        bool _loop, _playing;
        float _speed = 1f;          // per-clip speed multiplier (PlayToFit sets it; reset to 1 each Play)
        Action _onComplete, _onHit;
        int _hitFrame = -1;
        bool _hitFired;

        /// <summary>Fires (eventName, frameIndex) as playback ENTERS a frame carrying an authored
        /// <see cref="FrameEvent"/> (once per play-through / loop).</summary>
        public event Action<string, int> OnFrameEvent;

        public Laumination Anim => _anim;
        public string CurrentClip => _anim != null ? _anim.name : null;
        public bool IsPlaying => _playing;
        public int Frame => _i;
        public bool HasFrames => _anim != null && _anim.frames != null && _anim.frames.Count > 0;
        public Sprite CurrentSprite =>
            (HasFrames && _i >= 0 && _i < _anim.frames.Count) ? _anim.frames[_i] : null;

        /// <summary>Start (or, for an already-running simple loop, keep) playing <paramref name="def"/>.
        /// Returns false if there is nothing to play. <paramref name="onHit"/> fires once when playback
        /// reaches local frame <paramref name="hitFrame"/>; <paramref name="onComplete"/> fires when a
        /// non-looping clip finishes. <paramref name="speed"/> is the per-clip multiplier (1 = authored).</summary>
        public bool Play(Laumination def, bool loop = true, float speed = 1f,
            Action onComplete = null, Action onHit = null, int hitFrame = -1)
        {
            if (def == null || def.frames == null || def.frames.Count == 0) return false;

            // Don't restart a simple loop that's already running (lets callers poll Play each frame).
            if (def == _anim && _playing && loop == _loop && onComplete == null && onHit == null)
                return true;

            _anim = def;
            _loop = loop;
            _i = 0; _t = 0f; _playing = true;
            _speed = speed <= 0f ? 1f : speed;
            _onComplete = onComplete;
            _onHit = onHit; _hitFrame = hitFrame; _hitFired = false;

            MaybeFireHit();
            FireFrameEvents(); // frame-0 authored events
            return true;
        }

        public void Stop() => _playing = false;

        /// <summary>Set the per-clip speed multiplier after a Play (used by PlayToFit).</summary>
        public void SetSpeed(float speed) => _speed = speed <= 0f ? 0f : speed;

        /// <summary>Advance playback by <paramref name="deltaTime"/> seconds. <paramref name="globalSpeedScale"/>
        /// is the consumer's overall speed knob (1 = authored, 0 = paused).</summary>
        public void Tick(float deltaTime, float globalSpeedScale = 1f)
        {
            if (!_playing || !HasFrames) return;

            float fps = _anim.fps * globalSpeedScale * _speed;
            if (fps <= 0f) return;

            int count = _anim.frames.Count;
            _t += deltaTime * fps;
            while (_t >= 1f)
            {
                _t -= 1f;
                _i++;
                if (_i < count) { MaybeFireHit(); FireFrameEvents(); }
                if (_i >= count)
                {
                    if (_loop) { _i = 0; FireFrameEvents(); }
                    else
                    {
                        _i = count - 1;
                        _playing = false;
                        var cb = _onComplete; _onComplete = null;
                        cb?.Invoke();
                        break;
                    }
                }
            }
        }

        void MaybeFireHit()
        {
            if (_hitFired || _onHit == null || _hitFrame < 0 || _i < _hitFrame) return;
            _hitFired = true;
            var cb = _onHit; _onHit = null;
            cb.Invoke();
        }

        void FireFrameEvents()
        {
            var evs = _anim != null ? _anim.events : null;
            if (OnFrameEvent == null || evs == null) return;
            for (int k = 0; k < evs.Count; k++)
                if (evs[k] != null && evs[k].frame == _i) OnFrameEvent.Invoke(evs[k].name, _i);
        }
    }
}
