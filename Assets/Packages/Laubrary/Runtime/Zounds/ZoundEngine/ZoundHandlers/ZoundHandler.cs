using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    internal enum ZoundUpdateResult {
        Continue = 0,
        Kill = 1,
        Pause = 2
    }

    internal interface IZoundHandler {
        float totalDuration { get; }
        float currentTime { get; }
        bool isDelayFinished { get; }
        float parentVolume { get; set; }
        // A live, externally-driven volume multiplier (OutBurner 2026-09-26, its engine sound): defaults to 1 so
        // every existing sound is unchanged. Managed pipeline only - a native-DSP voice does not read it yet.
        float liveVolume { get; set; }
        float liveSpeed { get; set; }
        /// <summary>A track's volume from its token's track settings (T-0497), multiplied into every volume write.</summary>
        float trackVolume { get; set; }
        /// <summary>A track's speed multiplier from its token (T-0497), on top of the token's own live speed.</summary>
        float trackSpeed { get; set; }
        /// <summary>A track's pitch multiplier from its token (T-0497).</summary>
        float livePitch { get; set; }
        int playedEntryIndex { get; }
        bool isRealtime { get; }
        System.Action onPlayStarted { get; set; }
        List<AudioSource> GetAudioSources();
        void Init();
        bool IsMutedOrExcluded();
        void ApplyMixerGroupToChildren(AudioMixerGroup mixerGroup);
        void OnStart(float timeOffset, float fadeDuration, System.Action onFadeComplete);
        void OnPause();
        void OnFadeAndPause(float fadeDuration, System.Action onFadeComplete);
        void OnResume(float fadeDuratio, System.Action onFadeCompleten);
        void OnKill();
        void OnFadeAndKill(float fadeDuration, System.Action onFadeComplete);
        /// <summary>
        /// Update and check what's to be done next.
        /// </summary>
        /// <param name="deltaDspTime"></param>
        /// <returns>Continue, Kill, or Pause.</returns>
        ZoundUpdateResult OnUpdate(float deltaDspTime);
        void SetToken(ZoundToken token);
        int dspParentGroup { get; set; }
        /// <summary>ZPOC: re-applies one id (a key from ZpocKeys) from the token's resolved value, or its resting value.</summary>
        void ApplyZpoc(string key);
        /// <summary>ZPOC: re-applies every id this play declares, e.g. once its token or parent token is known.</summary>
        void ApplyAllZpoc();
        /// <summary>Snapshot glide (T-0498): this play (and, for a Zequence, its tracks) glides to its Zound's snapshot of that name.</summary>
        void ApplySnapshot(string name, float seconds);
        /// <summary>Glides back to exactly where the last glide began.</summary>
        void GlideBackTo(float seconds);
        /// <summary>A glide under way on this play: the snapshot it is heading for and how far along (0..1).</summary>
        bool TryGlideProgress(out string target, out float progress);
    }

    internal class ZoundHandler<TZound> : IZoundHandler where TZound : Zound {

        private TZound m_zound;
        private AudioSource m_audioSource;
        private float m_selfVolume;

        private float latestTime;
        private float m_currentTime;
        private float m_totalDuration;

        public enum FadeState {
            None, FadingOut, FadingIn
        }

        private FadeState fadeState;
        private System.Action onFadeComplete;
        private float fadeStartTime;
        private float fadeInitialVolume;
        private float fadeDuration;
        private bool killOnFadeOut;
        private bool isPaused;

        protected ZoundArgs args;
        private float delayTimer;
        private bool m_isDelayFinished;

        public float parentVolume { get; set; } = 1f;
        // See the interface member and ZoundToken.liveVolume. Multiplied into every managed volume write below.
        public float liveVolume { get; set; } = 1f;
        // See ZoundToken.liveSpeed. Only a sound playing through the chain with live speed on hears it (KlipHandler).
        public virtual float liveSpeed { get; set; } = 1f;
        public float trackVolume { get; set; } = 1f;
        public virtual float trackSpeed { get; set; } = 1f;
        public virtual float livePitch { get; set; } = 1f;
        public virtual bool isRealtime => false;
        public System.Action onPlayStarted { get; set; }

        public virtual List<AudioSource> GetAudioSources() { return new List<AudioSource> { m_audioSource }; }

        public ZoundHandler(TZound zound, AudioSource audioSource, ZoundArgs zoundArgs) {
            m_zound = zound;
            m_audioSource = audioSource;
            args = zoundArgs;

            m_selfVolume = args.volumeOverride >= 0f
                ? args.volumeOverride
                : Random.Range(zound.minVolume, zound.maxVolume);
            m_audioSource.volume = m_selfVolume * ZoundEngine.GetMasterVolume();

            m_audioSource.pitch = args.pitchOverride >= 0f
                ? args.pitchOverride
                : Random.Range(zound.minPitch, zound.maxPitch);

            if (zoundArgs.overrideMixerGroup) {
                audioSource.outputAudioMixerGroup = zoundArgs.mixerGroupOverride;
            }
        }

        protected TZound zound => m_zound;
        protected AudioSource audioSource => m_audioSource;
        protected float selfVolume => m_selfVolume;
        /// <summary>Moves this play's own volume while it plays (a live edit of the sound's volume range); the audio source
        /// follows on the next update, where it is set from this every frame.</summary>
        protected void SetSelfVolume(float v) { m_selfVolume = v; }
        public bool isDelayFinished => m_isDelayFinished;
        public float currentTime { get => m_currentTime; protected set { m_currentTime = value; } }
        public float totalDuration => m_totalDuration;
        public virtual int playedEntryIndex => 0;

        public void Init() {
            m_totalDuration = PrepareAndCalculateDuration();
            if (args.overrideDuration > 0f) {
                m_totalDuration = args.overrideDuration;
            }
            parentVolume = 1f;
        }

        /// <summary>Grows the resolved duration (never shrinks it): a repeat train whose real length turned out longer than estimated.</summary>
        protected void ExtendDuration(float newDuration) {
            if (newDuration > m_totalDuration) m_totalDuration = newDuration;
        }

        public virtual void ApplyMixerGroupToChildren(AudioMixerGroup mixerGroup) {
            audioSource.outputAudioMixerGroup = mixerGroup;
        }

        public virtual void OnStart(float timeOffset, float fadeDuration, System.Action onFadeComplete) {
            float offsetAfterDelay = timeOffset - args.delay;

            if (offsetAfterDelay >= 0) {
                m_currentTime = offsetAfterDelay;
                delayTimer = args.delay;
            }
            else {
                m_currentTime = 0f;
                delayTimer = timeOffset;
            }
            latestTime = m_currentTime;

            if (!ReferenceEquals(m_audioSource.clip, null)) {
                m_audioSource.time = m_currentTime;
            }

            m_isDelayFinished = false;
            isPaused = false;

            if (fadeDuration > Mathf.Epsilon) {
                this.fadeDuration = fadeDuration;
                this.onFadeComplete = onFadeComplete;
                fadeState = FadeState.FadingIn;
                fadeStartTime = currentTime;
                fadeInitialVolume = 0f;
            }
            else {
                fadeState = FadeState.None;
            }
        }

        /// <summary>The token that owns this handler; set before Init so voices can be registered to it.</summary>
        public virtual void SetToken(ZoundToken token) { m_token = token; }

        private ZoundToken m_token;
        /// <summary>The token that owns this handler (null until <see cref="SetToken"/>).</summary>
        protected ZoundToken token => m_token;

        public virtual void ApplyZpoc(string key) { }
        public virtual void ApplySnapshot(string name, float seconds) { }
        public virtual void GlideBackTo(float seconds) { }
        public virtual bool TryGlideProgress(out string target, out float progress) { target = null; progress = 0f; return false; }
        public virtual void ApplyAllZpoc() { }

        /// <summary>The DSP group node this zound's output sums into (-1 = the bus). Set by the parent Zequence before Start.</summary>
        public int dspParentGroup { get; set; } = -1;

        public virtual void OnPause() {
            isPaused = true;
            m_audioSource.Pause();
        }

        public virtual void OnFadeAndPause(float fadeDuration, System.Action onFadeComplete) {
            this.fadeDuration = fadeDuration;
            this.onFadeComplete = onFadeComplete;
            fadeState = FadeState.FadingOut;
            fadeStartTime = currentTime;
            fadeInitialVolume = m_audioSource.volume;
            killOnFadeOut = false;
        }

        public virtual void OnResume(float fadeDuration, System.Action onFadeComplete) {
            m_audioSource.UnPause();
            if (fadeDuration > Mathf.Epsilon) {
                this.fadeDuration = fadeDuration;
                this.onFadeComplete = onFadeComplete;
                fadeState = FadeState.FadingIn;
                fadeStartTime = currentTime;
                fadeInitialVolume = isPaused ? 0f : m_audioSource.volume;
            }
            else {
                fadeState = FadeState.None;
            }
            isPaused = false;
        }

        public virtual void OnKill() {
            m_audioSource.Stop();
        }

        public virtual void OnFadeAndKill(float fadeDuration, System.Action onFadeComplete) {
            if (isPaused) {
                m_audioSource.UnPause();
            }
            isPaused = false;
            this.fadeDuration = fadeDuration;
            this.onFadeComplete = onFadeComplete;
            fadeState = FadeState.FadingOut;
            fadeStartTime = currentTime;
            fadeInitialVolume = m_audioSource.volume;
            killOnFadeOut = true;
        }

        /// <summary>
        /// Update and check what's to be done next.
        /// </summary>
        public virtual ZoundUpdateResult OnUpdate(float deltaDspTime) {
            if (!m_isDelayFinished) {
                if (fadeState == FadeState.FadingOut && killOnFadeOut) return ZoundUpdateResult.Kill;

                if (delayTimer < args.delay - Mathf.Epsilon) {
                    delayTimer += deltaDspTime;
                    if (delayTimer > args.delay) delayTimer = args.delay;
                    if (delayTimer < args.delay - Mathf.Epsilon) return ZoundUpdateResult.Continue;

                    // Delay ran out mid-update: begin playback now instead of waiting for the next update,
                    // and don't count the time already spent on the delay towards playback.
                    deltaDspTime = 0f;
                }

                m_isDelayFinished = true;

                // Refused before anything plays, so a refused Zequence never starts (and never puts in cooldown) its children.
                if (!args.ignoreCooldown) {
                    if (ZoundEngine.IsCoolingDownAtTime(zound, Time.realtimeSinceStartup)) {
                        OnKill();
                        return ZoundUpdateResult.Kill;
                    }
                    ZoundEngine.RecordLastPlayedTime(zound);
                }

                float timeStartOffset = Mathf.Max(0, delayTimer - args.delay);
                float childFadeDuration = fadeState == FadeState.FadingIn ? fadeDuration : 0f;
                OnPlayReady(timeStartOffset, childFadeDuration);
            }

            return OnPlayUpdate(deltaDspTime);
        }

        /// <summary>
        /// Update when the zound is actually playing (delay finished).
        /// </summary>
        protected virtual ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
            if (currentTime > latestTime) latestTime = currentTime;

            if (totalDuration <= Mathf.Epsilon) {
                OnCompleteDuration();
                return ZoundUpdateResult.Kill;
            }

            if (latestTime >= totalDuration - 2 * deltaDspTime) {
                OnCompleteDuration();
                return ZoundUpdateResult.Kill;
            }

            if (fadeState == FadeState.FadingOut) {
                float t = (currentTime - fadeStartTime) / fadeDuration;
                t = Mathf.Clamp01(t);
                m_audioSource.volume = parentVolume * liveVolume * trackVolume * Mathf.Lerp(fadeInitialVolume * ZoundEngine.GetMasterVolume(), 0, t);
                float endTime = fadeStartTime + fadeDuration - Mathf.Epsilon;
                if (killOnFadeOut) {
                    if (currentTime >= endTime) {
                        CompleteFade();
                        return ZoundUpdateResult.Kill;
                    }
                }
                else {
                    if (t >= 1f) {
                        fadeState = FadeState.None;
                        OnPause();
                        CompleteFade();
                    }
                }
            }
            else if (fadeState == FadeState.FadingIn) {
                float t = (currentTime - fadeStartTime) / fadeDuration;
                t = Mathf.Clamp01(t);
                float masterVolume = ZoundEngine.GetMasterVolume();
                m_audioSource.volume = parentVolume * liveVolume * trackVolume * Mathf.Lerp(fadeInitialVolume * masterVolume, m_selfVolume * masterVolume, t);
                if (t >= 1f - Mathf.Epsilon) {
                    fadeState = FadeState.None;
                    CompleteFade();
                }
            }
            else {
                m_audioSource.volume = parentVolume * liveVolume * trackVolume * m_selfVolume * ZoundEngine.GetMasterVolume();
            }

            if (IsMutedOrExcluded()) {
                m_audioSource.volume = 0f;
            }

            if (!isPaused) {
                m_currentTime += deltaDspTime;
                if (m_currentTime > totalDuration) {
                    m_currentTime = totalDuration;
                }
            }
            return isPaused ? ZoundUpdateResult.Pause : ZoundUpdateResult.Continue;
        }

        public bool IsMutedOrExcluded() {
            if (zound.mute) {
                return true;
            }
            else {
                if (!args.bypassGlobalSolo && ZoundEngine.Instance.hasAnySoloZoundThisFrame) {
                    if (!zound.solo) return true;
                }
            }
            return false;
        }

        private void CompleteFade() {
            var action = onFadeComplete;
            onFadeComplete = null;
            action?.Invoke();
        }

        protected virtual void OnCompleteDuration() {
            
        }

        protected virtual float PrepareAndCalculateDuration() {
            return ReferenceEquals(m_audioSource.clip, null) ? 0f : m_audioSource.clip.length / m_audioSource.pitch;
        }

        protected virtual void OnPlayReady(float timeStartOffset, float childFadeDuration) {
            m_currentTime = timeStartOffset;
            if (!ReferenceEquals(m_audioSource.clip, null)) {
                if (m_currentTime > m_audioSource.clip.length) {
                    m_audioSource.time = m_audioSource.clip.length;
                    return;
                }
                else {
                    m_audioSource.time = m_currentTime;
                }
            }

            if (m_audioSource == null) return;
            
            // Re-parent to ZoundEngine just in case it was moved/orphaned in Edit Mode
            m_audioSource.transform.parent = ZoundEngine.Instance.transform;

            if (!m_audioSource.gameObject.activeSelf || !m_audioSource.enabled) {
                m_audioSource.gameObject.SetActive(true);
                m_audioSource.enabled = true;
            }

            if (m_audioSource.gameObject.activeInHierarchy && m_audioSource.enabled) {
                m_audioSource.Play();
                onPlayStarted?.Invoke();
            }
            else {
                Debug.LogWarning($"[Zounds] Could not play AudioSource for {zound.name}. GameObject active: {m_audioSource.gameObject.activeInHierarchy}, Component enabled: {m_audioSource.enabled}");
            }
        }
    }

}
