using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>A library AudioClip played by name (no Zound authored for it): managed pipeline plays it
    /// directly via the AudioSource; native pipeline runs it as one voice, no chain.</summary>
    internal class ClipZoundHandler : ZoundHandler<ClipZound> {

        private readonly bool useNative;

        // ── Native pipeline only ──
        private Laubrary.Zounds.Dsp.Native.NativeNode m_voice;
        private long m_voiceTokenId;
        private ZoundToken m_token;
        private bool m_voiceReleased;

        public override bool isRealtime => useNative || base.isRealtime;

        public ClipZoundHandler(ClipZound clipZound, AudioSource audioSource, ZoundArgs zoundArgs) : base(clipZound, audioSource, zoundArgs) {
            useNative = ZoundsProject.Instance.projectSettings.useNativeDsp;

#if ZOUNDS_CONSIDER_FOLDERS
            if (!ZoundDictionary.runtimeClipFolders.ContainsKey(zound.audioClip)) {
                var folderPath = ZoundRoutings.GetFolderFromClipPath(zound.audioPath);
                ZoundDictionary.runtimeClipFolders.Add(zound.audioClip, folderPath);
            }
#endif
            audioSource.clip = useNative ? null : zound.audioClip; // native: the carrier never plays; the voice does

            var zoundRoutings = ZoundsProject.Instance.zoundRoutings;
            var mixerGroup = zoundRoutings.GetRouting(zound
#if ZOUNDS_CONSIDER_FOLDERS
                , zound.audioClip, zound.audioPath
#endif
                );
            audioSource.outputAudioMixerGroup = mixerGroup;
        }

        public override void SetToken(ZoundToken token) { m_token = token; }

        private bool VoiceAlive() => m_voice != null && m_voice.tokenId == m_voiceTokenId && m_voice.State != VoiceState.Free;

        protected override float PrepareAndCalculateDuration() {
            if (!useNative) return base.PrepareAndCalculateDuration();
            return zound.audioClip == null ? 0f : zound.audioClip.length / Mathf.Max(audioSource.pitch, 0.01f);
        }

        protected override void OnPlayReady(float timeStartOffset, float childFadeDuration) {
            if (!useNative) {
                base.OnPlayReady(timeStartOffset, childFadeDuration);
                return;
            }
            currentTime = timeStartOffset;
            if (audioSource == null) return;
            audioSource.transform.parent = ZoundEngine.Instance.transform;
            if (!audioSource.gameObject.activeSelf || !audioSource.enabled) {
                audioSource.gameObject.SetActive(true);
                audioSource.enabled = true;
            }
            float gain = audioSource.mute ? 0f : audioSource.volume;
            m_voice = ZoundDspPlayback.StartVoice(m_token, zound, zound.audioClip, audioSource.outputAudioMixerGroup,
                timeStartOffset * Mathf.Max(audioSource.pitch, 0.01f), 0f, Mathf.Max(audioSource.pitch, 0.01f), gain, totalDuration, false, dspParentGroup);
            m_voiceReleased = false;
            if (m_voice != null) {
                m_voiceTokenId = m_voice.tokenId;
                m_token?.NotifyVoiceStarted();
            }
            onPlayStarted?.Invoke();
        }

        protected override ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
            var result = base.OnPlayUpdate(deltaDspTime);
            if (useNative && VoiceAlive()) {
                float gain = audioSource.mute ? 0f : audioSource.volume;
                m_voice.SetOutGainTarget(gain);
                m_voice.SetBasePitchTarget(Mathf.Max(audioSource.pitch, 0.01f));
            }
            return result;
        }

        public override ZoundUpdateResult OnUpdate(float deltaDspTime) {
            var result = base.OnUpdate(deltaDspTime);
            if (useNative && result == ZoundUpdateResult.Kill && !m_voiceReleased) {
                m_voiceReleased = true;
                if (VoiceAlive()) m_voice.RequestRelease();
            }
            return result;
        }

        public override void OnPause() { base.OnPause(); if (useNative && VoiceAlive()) m_voice.SetPaused(true); }
        public override void OnResume(float fadeDuration, System.Action onFadeComplete) { base.OnResume(fadeDuration, onFadeComplete); if (useNative && VoiceAlive()) m_voice.SetPaused(false); }
        public override void OnKill() { base.OnKill(); if (useNative && VoiceAlive()) m_voice.RequestKill(); }

    }

}
