using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0150 -- plays a baked <see cref="ShaperClip"/> by cycling a <see cref="SpriteRenderer"/> through its
    /// frames. The Shaper counterpart of <c>PyreBlastPlayer</c>, and deliberately the same SHAPE so a
    /// Laubrary user already knows how to drive it:
    /// <code>
    /// var p = ShaperPlayerPool.Get();   // pooled: pooled=true, playOnAwake=false already set
    /// p.clip = myClip; p.fps = 0f; p.loop = false;
    /// p.Finished += onFinished;         // unsubscribe inside, then ShaperPlayerPool.Release(p)
    /// p.Play();
    /// </code>
    ///
    /// <b>Why a clip and not a <see cref="ShaperDocument"/>.</b> Pyre's player holds the spec and asks
    /// <c>PyreRenderer.GetFrames</c> for pixels on every <c>Play()</c>. Shaper cannot: no document renderer
    /// exists, and live evaluation is measured at ~30 ms/frame -- see <see cref="ShaperClip"/>'s class doc for
    /// both findings with their sources. So the bake IS the runtime artifact here.
    ///
    /// <b>Allocates nothing per playback.</b> The frames are <see cref="Sprite"/> references owned by the
    /// clip asset; this component creates no <see cref="Texture2D"/>, no <c>Color32[]</c> and no managed
    /// buffer of any kind, so a pooled instance cycled thousands of times leaks nothing and needs no
    /// disposal hook. (A live-evaluating player would have needed one -- the evaluator allocates a
    /// canvas-sized buffer per node per frame.)
    ///
    /// <b>Deterministic.</b> The only variation in playback is the cherry sequence, and
    /// <see cref="ShaperCherry"/> draws from a hash of (seed, slot, loopIndex) rather than
    /// <c>UnityEngine.Random</c> -- banned in this engine per BC-1.3 -- so the same clip plays the same
    /// sequence every time, per LT-4.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ShaperPlayer : MonoBehaviour
    {
        [Tooltip("The baked clip to play. If left empty, assign one — or framesOverride — before Play() or " +
                 "nothing happens.")]
        public ShaperClip clip;

        [Tooltip("Play these sprites directly instead of the clip's own frames. Lets a caller drive the player " +
                 "straight from sprites sliced out of a baked sheet, with no ShaperClip asset in between. " +
                 "Cherry framing needs a clip, so it is skipped while this is in use.")]
        public Sprite[] framesOverride;

        [Tooltip("Playback speed in frames per second. 0 (the default) uses the clip's own authored rate, " +
                 "so a clip plays at the speed it was authored at unless a caller deliberately overrides it.")]
        public float fps;

        [Tooltip("Loop forever, or play once then (optionally) destroy the GameObject.")]
        public bool loop;

        [Tooltip("When not looping, destroy this GameObject once the animation finishes.")]
        public bool destroyOnFinish = true;

        [Tooltip("Start playing automatically on Awake.")]
        public bool playOnAwake = true;

        [Tooltip("Set by a pool owner. When true, a finished non-looping playback fires Finished instead of " +
                 "destroying the GameObject — the pool decides its fate, not this component.")]
        public bool pooled;

        /// <summary>Fired once when a non-looping playback finishes, ONLY while <see cref="pooled"/> -- the
        /// pool's own signal to reclaim this instance. Never fires when <see cref="destroyOnFinish"/> already
        /// destroyed the object instead. Mirrors <c>PyreBlastPlayer.Finished</c> exactly.</summary>
        public event Action Finished;

        SpriteRenderer _sr;
        float _accumulator;
        int _frame;
        ShaperCherryState _cherry;
        bool _playing;

        /// <summary>True while frames are still advancing.</summary>
        public bool IsPlaying => _playing;

        /// <summary>The frame index currently shown, or <see cref="ShaperCherry.BlankFrame"/> during a cherry
        /// sequence's between-loop gap.</summary>
        public int CurrentFrame => _frame;

        /// <summary>The rate actually used: <see cref="fps"/> when a caller set one, else the clip's own.</summary>
        public float EffectiveFrameRate =>
            fps > 0f ? fps : (clip != null ? clip.frameRate : ShaperClock.DefaultFrameRate);

        /// <summary>The sprites actually played -- <see cref="framesOverride"/> when supplied, else the
        /// clip's own baked frames.</summary>
        public Sprite[] SourceFrames =>
            framesOverride != null && framesOverride.Length > 0 ? framesOverride
                                                                : (clip != null ? clip.frames : null);

        /// <summary>True when there is something to show.</summary>
        public bool HasFrames => SourceFrames != null && SourceFrames.Length > 0;

        /// <summary>Cherry sequencing needs the slot list and seed, which live on the clip -- and its frame
        /// indices are the CLIP's, so it would index the wrong sprites against an override set.</summary>
        bool UseCherry => clip != null && clip.cherryEnabled
                          && (framesOverride == null || framesOverride.Length == 0);

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (playOnAwake) Play();
        }

        /// <summary>Start (or restart) playback from the beginning.</summary>
        public void Play()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            // ALWAYS re-read the clip here rather than caching across calls: a pooled instance is reused for
            // many Play()s, possibly for a DIFFERENT clip each time, or the same clip re-baked between shots.
            // Same reasoning as PyreBlastPlayer's "always re-ask GetFrames" comment.
            if (!HasFrames) { _playing = false; return; }

            _accumulator = 0f;
            _playing = true;

            if (UseCherry)
            {
                _cherry = ShaperCherry.Begin(clip.PlaybackDocument);
                _frame = _cherry.frame;
            }
            else
            {
                _frame = 0;
            }
            ShowCurrent();
        }

        /// <summary>Stop advancing and hold the current frame. Does not fire <see cref="Finished"/> -- a
        /// deliberate stop is the caller's own decision, not a completed playback.</summary>
        public void Stop() => _playing = false;

        void Update()
        {
            if (!_playing || !HasFrames) return;

            int steps = ShaperClock.AdvanceFrames(ref _accumulator, Time.deltaTime, EffectiveFrameRate);
            if (steps <= 0) return;

            if (UseCherry) AdvanceCherry(steps);
            else AdvancePlain(steps);
        }

        void AdvancePlain(int steps)
        {
            int n = SourceFrames.Length;
            _frame += steps;

            if (_frame >= n)
            {
                if (loop)
                {
                    _frame = ShaperClock.WrapFrame(_frame, n);
                }
                else
                {
                    _frame = n - 1;      // rest on the last frame, matching PyreBlastPlayer
                    ShowCurrent();
                    Finish();
                    return;
                }
            }
            ShowCurrent();
        }

        void AdvanceCherry(int steps)
        {
            var doc = clip.PlaybackDocument;
            for (int i = 0; i < steps; i++)
            {
                _cherry = ShaperCherry.AdvanceOneBeat(_cherry, doc);

                // loopIndex only rises when a full pass through the slot list completes, so this is exactly
                // "the sequence played once" -- the cherry equivalent of running off the last plain frame.
                if (!loop && _cherry.loopIndex >= 1)
                {
                    _frame = _cherry.frame;
                    ShowCurrent();
                    Finish();
                    return;
                }
            }
            _frame = _cherry.frame;
            ShowCurrent();
        }

        void ShowCurrent()
        {
            if (_sr == null) return;

            // BlankFrame is a real, authored state (the gap between cherry loops), not an error -- show
            // nothing rather than clamping to a frame the author deliberately left empty.
            if (_frame == ShaperCherry.BlankFrame) { _sr.sprite = null; return; }

            var frames = SourceFrames;
            if (frames == null || frames.Length == 0) { _sr.sprite = null; return; }
            int i = Mathf.Clamp(_frame, 0, frames.Length - 1);
            _sr.sprite = frames[i];
        }

        void Finish()
        {
            _playing = false;
            if (pooled) Finished?.Invoke();
            else if (destroyOnFinish) Destroy(gameObject);
        }
    }
}
