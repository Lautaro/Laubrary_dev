using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// A playing Klip. Runs one of two fully separate pipelines, decided once at construction from
    /// ZoundsProject.projectSettings.useNativeDsp and never switched mid-flight even if the setting
    /// changes while this instance is playing:
    ///
    /// - Managed (useNative == false): Unity's own AudioSource plays the rendered output clip directly.
    ///   This is the original, long-proven pipeline and the default.
    /// - Native (useNative == true): one DSP voice reads the source clip (trim, time-stretch, pitch)
    ///   through the Klip's effect chain. The pooled carrier AudioSource never plays; it is the
    ///   main-thread control surface (volume, pitch, mute, pause) that the rest of the engine and the
    ///   editor drive, and its values are mirrored into the voice every update (ramped per sample on the
    ///   audio thread).
    ///
    /// Every overridden lifecycle method below dispatches to one pipeline's own private method (or, where
    /// the managed pipeline has no special behaviour at all, guards a native-only addition on top of the
    /// shared base behaviour) - the two are never interleaved.
    /// </summary>
    internal class KlipHandler : ZoundHandler<Klip> {

        private readonly bool useNative;

        // ── Managed pipeline state ──
        private float baseVolume;
        private float basePitch;
        private bool m_isRealtime;

        // ── Native pipeline state ──
        private AudioClip m_clip;
        private Laubrary.Zounds.Dsp.Native.NativeNode m_voice;
        private long m_voiceTokenId;
        private ZoundToken m_token;
        private bool m_voiceReleased;

        public override bool isRealtime => useNative ? true : m_isRealtime;

        public KlipHandler(Klip klip, AudioSource audioSource, ZoundArgs zoundArgs) : base(klip, audioSource, zoundArgs) {
            useNative = ZoundsProject.Instance.projectSettings.useNativeDsp;

            var clipRef = zound.GetAudioClipReference();
            var clip = ZoundDictionary.GetOrLoadClip(clipRef);

#if ZOUNDS_CONSIDER_FOLDERS
            var clipPath = zound.GetAudioClipPath();

            if (!ZoundDictionary.runtimeClipFolders.ContainsKey(clip)) {
                var folderPath = ZoundRoutings.GetFolderFromClipPath(clipPath);
                ZoundDictionary.runtimeClipFolders.Add(clip, folderPath);
            }
#endif

            if (!zoundArgs.overrideMixerGroup) {
                var zoundRoutings = ZoundsProject.Instance.zoundRoutings;
                var mixerGroup = zoundRoutings.GetRouting(zound
#if ZOUNDS_CONSIDER_FOLDERS
                , clip, clipPath
#endif
                    );
                audioSource.outputAudioMixerGroup = mixerGroup;
            }

            if (useNative) {
                m_clip = clip;
                audioSource.clip = null;
            } else {
                // For Klips, we only ever play the rendered output clip (source + edits).
                // The real-time modification logic is preserved but disabled by forcing m_isRealtime to false.
                m_isRealtime = false;
                audioSource.clip = clip;
                basePitch = audioSource.pitch;
                baseVolume = audioSource.volume;
            }
        }

        public override void SetToken(ZoundToken token) { m_token = token; }

        // ── Native pipeline only ──
        private float TrimStart => zound.trimEnabled ? Mathf.Max(0f, zound.trimStart) : 0f;
        private float TrimEnd {
            get {
                float len = m_clip != null ? m_clip.length : 0f;
                if (!zound.trimEnabled || zound.trimEnd <= zound.trimStart + 0.0005f) return len;
                return Mathf.Min(zound.trimEnd, len);
            }
        }

        protected override ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
            return useNative ? OnPlayUpdate_Native(deltaDspTime) : OnPlayUpdate_Managed(deltaDspTime);
        }

        private ZoundUpdateResult OnPlayUpdate_Native(float deltaDspTime) {
            ZoundUpdateResult nextTreatment = base.OnPlayUpdate(deltaDspTime);
            MirrorCarrierToVoice();
            return nextTreatment;
        }

        private ZoundUpdateResult OnPlayUpdate_Managed(float deltaDspTime) {
            if (m_isRealtime) {
                if (zound.pitchEnvelope != null && zound.pitchEnvelope.enabled) {
                    float t = currentTime / totalDuration;
                    var envelopPitch = zound.pitchEnvelope.Evaluate(t);
                    var finalPitch = basePitch * envelopPitch;
                    audioSource.pitch = finalPitch;
                    deltaDspTime *= finalPitch;
                }
                else {
                    audioSource.pitch = basePitch;
                    deltaDspTime *= basePitch;
                }
            }

            ZoundUpdateResult nextTreatment = base.OnPlayUpdate(deltaDspTime);

            if (m_isRealtime) {
                if (zound.volumeEnvelope != null && zound.volumeEnvelope.enabled) {
                    float t = currentTime / totalDuration;
                    var volume = zound.volumeEnvelope.Evaluate(t);
                    audioSource.volume = baseVolume * volume;
                }
                else {
                    audioSource.volume = baseVolume;
                }
            }
            return nextTreatment;
        }

        public override ZoundUpdateResult OnUpdate(float deltaDspTime) {
            var result = base.OnUpdate(deltaDspTime);
            if (useNative && result == ZoundUpdateResult.Kill) ReleaseVoice();
            return result;
        }

        private void MirrorCarrierToVoice() {
            if (!VoiceAlive()) return;
            float gain = audioSource.mute ? 0f : audioSource.volume;
            m_voice.SetOutGainTarget(gain);
            m_voice.SetBasePitchTarget(Mathf.Max(audioSource.pitch, 0.01f));
            if (m_voice.RepeatEnabled) {
                // Grow-only: a retriggered train that ran long extends the resolved duration, so Zound End waits
                // for the last repeat and an envelope over the train never runs backwards.
                float realEnd = (float)m_voice.TrainEndSample / ZoundEngine.Dsp.sampleRate;
                if (realEnd > totalDuration + 0.001f) {
                    ExtendDuration(realEnd);
                    m_voice.SetSourceDuration(totalDuration);
                }
            }
        }

        private bool VoiceAlive() {
            return m_voice != null && m_voice.tokenId == m_voiceTokenId && m_voice.State != VoiceState.Free;
        }

        // Time-stretch: the voice plays a stretched copy of the trimmed region (whole buffer), so trim and
        // duration come from that buffer instead of the clip.
        private PcmClip StretchedPcm() {
            if (m_clip == null || zound.timeStretch == null || !zound.timeStretch.IsEffective(m_clip.length)) return null;
            var src = ZoundPcmCache.Get(m_clip);
            if (src == null || !src.valid) return null;
            return ZoundTimeStretcher.Get(src, zound.timeStretch, TrimStart, TrimEnd);
        }

        protected override float PrepareAndCalculateDuration() {
            return useNative ? PrepareAndCalculateDuration_Native() : PrepareAndCalculateDuration_Managed();
        }

        private float PrepareAndCalculateDuration_Native() {
            float basePitch = Mathf.Max(audioSource.pitch, 0.01f);
            var stretched = StretchedPcm();
            float sourceSeconds = stretched != null ? stretched.LengthSeconds : TrimEnd - TrimStart;
            // A pitch envelope on the chain consumes the source at a varying rate: the play length is its integral.
            float length = ZoundDspPlayback.DurationUnderPitchModulation(zound, sourceSeconds) / basePitch;
            if (args.repeatEntry != null && args.repeatEntry.repeatEnabled) return RepeatTrainDuration(length);
            return length;
        }

        private float PrepareAndCalculateDuration_Managed() {
            if (m_isRealtime) {
                return zound.trimEnd - zound.trimStart;
            }
            else {
                return base.PrepareAndCalculateDuration();
            }
        }

        // Zound End of a repeating track fires when the LAST repeat's source is exhausted. A retriggered train
        // spaced from each repeat's end has no exact length ahead of time: it is estimated from the nominal
        // length here and grown (never shrunk) as the voice arms the real repeats, see MirrorCarrierToVoice.
        private float RepeatTrainDuration(float oneLength) {
            var e = args.repeatEntry;
            float interval = Mathf.Max(e.repeatInterval, 0f);
            if (e.repeatMode == CompositeZound.RepeatMode.FixedDuration) return Mathf.Max(e.repeatTotalDuration, oneLength);
            int count = Mathf.Max(e.repeatCount, 1);
            return e.repeatSpaceFromEnd ? count * oneLength + (count - 1) * interval : (count - 1) * interval + oneLength;
        }

        private RepeatPlan BuildRepeatPlan(float basePitch) {
            var e = args.repeatEntry;
            if (e == null || !e.repeatEnabled) return default;
            int sr = ZoundEngine.Dsp.sampleRate;
            var stretchedForPlan = StretchedPcm();
            float oneLength = (stretchedForPlan != null ? stretchedForPlan.LengthSeconds : TrimEnd - TrimStart) / basePitch;
            var plan = new RepeatPlan {
                enabled = true,
                count = e.repeatMode == CompositeZound.RepeatMode.FixedDuration ? int.MaxValue : Mathf.Max(e.repeatCount, 1),
                intervalSamples = (long)(Mathf.Max(e.repeatInterval, 0f) * sr),
                spaceFromEnd = e.repeatSpaceFromEnd,
                retrigger = e.repeatRetrigger,
                pitchMulMin = 1f, pitchMulMax = 1f, gainMulMin = 1f, gainMulMax = 1f,
                durationLimitSamples = e.repeatMode == CompositeZound.RepeatMode.FixedDuration ? (long)(e.repeatTotalDuration * sr) : 0,
                nominalLengthSamples = (long)(oneLength * sr)
            };
            if (e.repeatRetrigger) {
                if (zound.minPitch != zound.maxPitch) {
                    float f = args.pitchRandomFactor > 0f ? args.pitchRandomFactor : 1f;
                    plan.pitchMulMin = zound.minPitch / f; plan.pitchMulMax = zound.maxPitch / f;
                }
                if (zound.minVolume != zound.maxVolume) {
                    float f = args.volumeRandomFactor > 0f ? args.volumeRandomFactor : 1f;
                    plan.gainMulMin = zound.minVolume / f; plan.gainMulMax = zound.maxVolume / f;
                }
            }
            return plan;
        }

        protected override void OnPlayReady(float timeStartOffset, float childFadeDuration) {
            if (useNative) OnPlayReady_Native(timeStartOffset, childFadeDuration);
            else OnPlayReady_Managed(timeStartOffset, childFadeDuration);
        }

        private void OnPlayReady_Native(float timeStartOffset, float childFadeDuration) {
            currentTime = timeStartOffset;
            if (audioSource == null) return;
            audioSource.transform.parent = ZoundEngine.Instance.transform;
            if (!audioSource.gameObject.activeSelf || !audioSource.enabled) {
                audioSource.gameObject.SetActive(true);
                audioSource.enabled = true;
            }
            float basePitch = Mathf.Max(audioSource.pitch, 0.01f);
            float start = TrimStart + timeStartOffset * basePitch;
            float end = TrimEnd;
            float gain = audioSource.mute ? 0f : audioSource.volume;
            var stretched = StretchedPcm();
            if (stretched != null) { start = timeStartOffset * basePitch; end = 0f; }
            m_voice = ZoundDspPlayback.StartVoice(m_token, zound, m_clip, audioSource.outputAudioMixerGroup,
                start, end, basePitch, gain, totalDuration, false, dspParentGroup, BuildRepeatPlan(basePitch), stretched);
            m_voiceReleased = false;
            if (m_voice != null) {
                m_voiceTokenId = m_voice.tokenId;
                m_token?.NotifyVoiceStarted();
            }
            onPlayStarted?.Invoke();
        }

        private void OnPlayReady_Managed(float timeStartOffset, float childFadeDuration) {
            if (m_isRealtime) {
                base.OnPlayReady(timeStartOffset + zound.trimStart, childFadeDuration);
                currentTime = timeStartOffset;
            }
            else {
                base.OnPlayReady(timeStartOffset, childFadeDuration);
            }
        }

        protected override void OnCompleteDuration() {
            // Native pipeline: no extra action needed here (matches not overriding this method at all).
            if (!useNative && m_isRealtime) {
                audioSource.Stop();
            }
        }

        public override void OnPause() {
            base.OnPause();
            if (useNative && VoiceAlive()) m_voice.SetPaused(true);
        }

        public override void OnResume(float fadeDuration, System.Action onFadeComplete) {
            base.OnResume(fadeDuration, onFadeComplete);
            if (useNative && VoiceAlive()) m_voice.SetPaused(false);
        }

        public override void OnKill() {
            base.OnKill();
            if (useNative && VoiceAlive()) m_voice.RequestKill();
        }

        private void ReleaseVoice() {
            if (m_voiceReleased) return;
            m_voiceReleased = true;
            if (VoiceAlive()) {
                if (audioSource == null) m_voice.RequestKill();
                else m_voice.RequestRelease();
            }
        }
    }

}
