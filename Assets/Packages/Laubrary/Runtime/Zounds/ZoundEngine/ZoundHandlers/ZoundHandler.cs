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
        int playedEntryIndex { get; }
        bool isRealtime { get; }
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
        public virtual bool isRealtime => false;

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

                if (delayTimer >= args.delay - Mathf.Epsilon) {
                    float timeStartOffset = Mathf.Max(0, delayTimer - args.delay);
                    float childFadeDuration;
                    if (fadeState == FadeState.FadingIn) childFadeDuration = fadeDuration;
                    else childFadeDuration = 0f;
                    OnPlayReady(timeStartOffset, childFadeDuration);
                    m_isDelayFinished = true;

                    if (!args.ignoreCooldown) {
                        if (ZoundEngine.IsCoolingDownAtTime(zound, Time.realtimeSinceStartup)) {
                            OnKill();
                            return ZoundUpdateResult.Kill;
                        }
                        ZoundEngine.RecordLastPlayedTime(zound);
                    }

                }
                else {
                    delayTimer += deltaDspTime;
                    if (delayTimer > args.delay) delayTimer = args.delay;
                    return ZoundUpdateResult.Continue;
                }
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
                m_audioSource.volume = parentVolume * Mathf.Lerp(fadeInitialVolume * ZoundEngine.GetMasterVolume(), 0, t);
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
                m_audioSource.volume = parentVolume * Mathf.Lerp(fadeInitialVolume * masterVolume, m_selfVolume * masterVolume, t);
                if (t >= 1f - Mathf.Epsilon) {
                    fadeState = FadeState.None;
                    CompleteFade();
                }
            }
            else {
                m_audioSource.volume = parentVolume * m_selfVolume * ZoundEngine.GetMasterVolume();
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
            }
            else {
                Debug.LogWarning($"[Zounds] Could not play AudioSource for {zound.name}. GameObject active: {m_audioSource.gameObject.activeInHierarchy}, Component enabled: {m_audioSource.enabled}");
            }
        }
    }

}
