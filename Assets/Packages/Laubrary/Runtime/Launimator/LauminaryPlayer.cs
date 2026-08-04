using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// Lightweight, data-driven sprite-animation player for a <see cref="LauminaryVersion"/>. Plays an
    /// animation by NAME by swapping <see cref="SpriteRenderer.sprite"/> each frame — no Unity Animator /
    /// AnimatorController. This is the blessed runtime way to consume a lauminary when you want precise
    /// per-frame control (event frames, completion callbacks) and raw access to the current frame's sprite
    /// (e.g. to read its texture for procedural effects).
    ///
    /// It deliberately mirrors the frame model the editor bake uses (one sprite per frame, advanced at the
    /// animation's fps); the baked AnimationClips/AnimatorController/prefab remain available for projects
    /// that prefer driving the lauminary through Unity's Animator instead.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class LauminaryPlayer : MonoBehaviour
    {
        [Tooltip("The lauminary version whose animations this plays. May be set in the Inspector or via SetVersion().")]
        public LauminaryVersion version;

        public bool playOnStart = true;

        [Tooltip("Animation name to play on Start (case-insensitive). Falls back to the first animation if blank/missing.")]
        public string startClip = "Idle";

        [Tooltip("Mirror the sprite horizontally (facing). Applied to the SpriteRenderer every frame.")]
        public bool flipX;

        [Tooltip("Multiplies every animation's fps. 1 = play at authored speed; 0 = paused.")]
        public float speedScale = 1f;

        SpriteRenderer _sr;
        readonly Dictionary<string, Laumination> _byName = new Dictionary<string, Laumination>(StringComparer.OrdinalIgnoreCase);

        // All playback timing/looping/event logic lives in the shared AnimationPlayback — the ONE player used
        // by the game AND the editor previews. This component only indexes the version, drives the core from
        // Update, and pushes the resulting sprite to the SpriteRenderer.
        readonly AnimationPlayback _pb = new AnimationPlayback();

        /// <summary>Fires (eventName, frameIndex) when playback ENTERS a frame carrying an authored
        /// <see cref="FrameEvent"/> (once per play-through / loop). Subscribe once, e.g. to fire weapon
        /// damage on the "hit" frame — the metadata is authored in the Laumination Builder, not per Play call.</summary>
        public event Action<string, int> OnFrameEvent;

        public string CurrentClip => _pb.CurrentClip;
        public bool IsPlaying => _pb.IsPlaying;
        public Laumination CurrentAnim => _pb.Anim;
        public int CurrentFrame => _pb.Frame;
        public bool HasFrames => _pb.HasFrames;

        /// <summary>The sprite currently showing — the pristine frame, for consumers that read its texture.</summary>
        public Sprite CurrentFrameSprite => _pb.CurrentSprite;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _pb.OnFrameEvent += (n, f) => OnFrameEvent?.Invoke(n, f);
            Reindex();
        }

        void Start()
        {
            if (playOnStart && version != null)
                Play(string.IsNullOrEmpty(startClip) ? FirstClipName() : startClip);
        }

        public void SetVersion(LauminaryVersion v)
        {
            version = v;
            Reindex();
            _pb.Stop();
        }

        void Reindex()
        {
            _byName.Clear();
            if (version == null || version.animations == null) return;
            foreach (var a in version.animations)
                if (a != null && !string.IsNullOrEmpty(a.name)) _byName[a.name] = a;
        }

        string FirstClipName()
        {
            if (version == null || version.animations == null) return null;
            foreach (var a in version.animations)
                if (a != null && a.frames != null && a.frames.Count > 0) return a.name;
            return null;
        }

        /// <summary>
        /// Play an animation by name. <paramref name="loop"/> defaults to true (Laumination carries no loop
        /// flag yet — pass false for one-shots like deaths/attacks). <paramref name="onHit"/> fires once when
        /// the clip reaches local frame <paramref name="hitFrame"/> (attack→damage sync). Returns false if the
        /// name is unknown. Calling with the already-playing looping clip is a no-op so callers may poll it.
        /// </summary>
        public bool Play(string clip, bool loop = true, Action onComplete = null, Action onHit = null, int hitFrame = -1)
        {
            if (string.IsNullOrEmpty(clip) || !_byName.TryGetValue(clip, out var def)) return false;
            if (!_pb.Play(def, loop, 1f, onComplete, onHit, hitFrame)) return false;
            // Defensive re-resolve, same as ZonedAnimationPlayer.PushSprite() already does — _sr is normally
            // set in Awake(), but a caller building this component synchronously may call Play()
            // synchronously right after AddComponent<LauminaryPlayer>(), before RequireComponent's dependency
            // injection is guaranteed to have run relative to Awake(). Confirmed live: without this, a
            // Build()-time Play() call succeeded (_pb.CurrentSprite non-null) but never reached the
            // SpriteRenderer, leaving it spriteless.
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr != null && _pb.CurrentSprite != null) _sr.sprite = _pb.CurrentSprite;
            return true;
        }

        public void Stop() => _pb.Stop();

        // ── consumer ergonomics: height-fit + duration-fit ──────────────────────────
        // These make a scavenged lauminary (varying per-frame sizes, baked feet pivots) a clean drop-in:
        // the baked pivot keeps the feet planted automatically, FitToHeight sizes any lauminary to a target,
        // and PlayToFit guarantees a one-shot completes within a gameplay window whatever its frame count.

        /// <summary>The resting sprite (start clip's first frame, else the first clip's) — the reference used for
        /// height scaling. Its baked pivot is the feet, so scaling about the transform keeps the feet planted.</summary>
        public Sprite RestingSprite
        {
            get
            {
                if (!string.IsNullOrEmpty(startClip) && _byName.TryGetValue(startClip, out var d) && d.frames != null && d.frames.Count > 0)
                    return d.frames[0];
                var n = FirstClipName();
                if (n != null && _byName.TryGetValue(n, out var f) && f.frames != null && f.frames.Count > 0)
                    return f.frames[0];
                return CurrentFrameSprite;
            }
        }

        /// <summary>World-space height (units) of the resting frame at scale 1 (Sprite.bounds already honours PPU).</summary>
        public float NativeHeight { get { var s = RestingSprite; return s != null ? s.bounds.size.y : 1f; } }

        /// <summary>Scale the whole lauminary so its resting frame is <paramref name="worldUnits"/> tall. Scales
        /// about the transform origin — with feet-registered pivots that keeps the feet planted — and other frames
        /// scale proportionally (a frame where he reaches up genuinely renders taller). Replaces magic-constant scaling.</summary>
        public void FitToHeight(float worldUnits)
        {
            float h = NativeHeight;
            if (h > 1e-4f) transform.localScale = new Vector3(worldUnits / h, worldUnits / h, 1f);
        }

        /// <summary>Authored duration of a clip in seconds (frames ÷ fps) at speed 1, or 0 if unknown.</summary>
        public float ClipLength(string clip)
        {
            if (!string.IsNullOrEmpty(clip) && _byName.TryGetValue(clip, out var d) && d.fps > 0f && d.frames != null)
                return d.frames.Count / d.fps;
            return 0f;
        }

        /// <summary>Play a clip so it COMPLETES within <paramref name="window"/> seconds — the per-clip speed is set
        /// to length/window (clamped). Use for one-shots that must finish inside a gameplay window: a death before
        /// the corpse fades, an attack within its cooldown — regardless of how many frames the scavenged clip has.</summary>
        public bool PlayToFit(string clip, float window, bool loop = false, Action onComplete = null, Action onHit = null, int hitFrame = -1)
        {
            if (!Play(clip, loop, onComplete, onHit, hitFrame)) return false;
            float len = ClipLength(clip);
            _pb.SetSpeed((len > 0f && window > 0f) ? Mathf.Clamp(len / window, 0.25f, 8f) : 1f);
            return true;
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Advance playback by <paramref name="deltaTime"/> seconds. Called automatically from Update; exposed
        /// so playback can be stepped deterministically (tests, manual scrubbing). Timing lives in the shared
        /// <see cref="AnimationPlayback"/>; this only mirrors flip + pushes the current sprite to the renderer.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_sr != null && _sr.flipX != flipX) _sr.flipX = flipX;
            _pb.Tick(deltaTime, speedScale);
            if (_sr != null && _pb.CurrentSprite != null) _sr.sprite = _pb.CurrentSprite;
        }
    }
}
