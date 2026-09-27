using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// A playing Klip.
    ///
    /// **Preferred path: the effect chain, in real time, from the sound's original audio.** The author's edits — a
    /// gain, an equaliser, a fade, volume and pitch curves, and anything else in the chain — are applied as the
    /// sound plays. Nothing has to be rendered to a file first, which is why the project no longer needs to carry
    /// one rendered file per sound, and why an edit can be heard while the sound is still playing.
    ///
    /// **Fallback: the old rendered file.** Real-time playback needs to read the source's raw samples, and Unity
    /// only permits that for a clip imported as fully decompressed. A sound whose source is set to stream, or
    /// whose source is missing entirely, therefore cannot play this way — so it falls back to the rendered file
    /// exactly as before. The fallback exists so that switching over cannot silence a project mid-migration; it is
    /// not the intended path, and the reason is reported once so the cause is discoverable rather than mysterious.
    /// </summary>
    internal class KlipHandler : ZoundHandler<Klip> {

        private float baseVolume;
        private float basePitch;

        /// <summary>Legacy flag for the old envelope-driven-AudioSource behaviour. Unused by the chain path.</summary>
        private bool m_isRealtime;

        /// <summary>The chain voice, when this sound is playing through the chain. Null on the fallback path.</summary>
        private Dsp.ZoundSapVoiceGenerator m_voice;

        /// <summary>Play length worked out when the chain voice was set up; the audio source has no clip to measure.</summary>
        private float m_chainDuration;

        /// <summary>True while this sound is playing through the chain rather than from a rendered file.</summary>
        private bool m_chainPath;

        /// <summary>Reported once per session so a project-wide misconfiguration does not spam the console.</summary>
        private static bool s_warnedAboutFallback;

        public KlipHandler(Klip klip, AudioSource audioSource, ZoundArgs zoundArgs) : base(klip, audioSource, zoundArgs) {
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

            m_isRealtime = false;
            basePitch = audioSource.pitch;
            baseVolume = audioSource.volume;

            // Try the chain first, reading the sound's ORIGINAL audio rather than the rendered file.
            var sourceClip = Dsp.ZoundSapPlayback.LoadSourceClip(zound);
            m_voice = Dsp.ZoundSapPlayback.StartVoice(zound, audioSource, sourceClip, basePitch, baseVolume,
                                                     zound.id, out string reason, out m_chainDuration);
            if (m_voice != null) {
                m_chainPath = true;
                return;
            }

            // Fall back to the rendered file so a sound still plays. Reported once, because the usual cause is one
            // import setting applied across a whole folder, and a message per sound per play would bury it.
            m_chainPath = false;
            if (!s_warnedAboutFallback && !string.IsNullOrEmpty(reason)) {
                s_warnedAboutFallback = true;
                Debug.LogWarning("[Zounds] Playing '" + zound.name + "' from its rendered file instead of through " +
                                 "the effect chain, because " + reason + ". Real-time playback needs the source " +
                                 "clip's Load Type set to Decompress On Load. Further occurrences are not repeated.");
            }
            audioSource.clip = clip;
        }

        /// <summary>
        /// The play length. On the chain path the audio source holds no clip to measure, so the length worked out
        /// when the voice was set up is used instead — it already accounts for a pitch curve consuming the source at
        /// a changing rate. Without this the sound would be treated as zero-length and finish instantly.
        /// </summary>
        protected override float PrepareAndCalculateDuration() {
            if (m_chainPath) return m_chainDuration;
            return PrepareAndCalculateDurationLegacy();
        }

        private float PrepareAndCalculateDurationLegacy() {
            if (m_isRealtime) return zound.trimEnd - zound.trimStart;
            return base.PrepareAndCalculateDuration();
        }

        /// <summary>Stops the chain voice as well as the audio source, so a killed sound does not keep rendering.</summary>
        public override void OnKill() {
            if (m_chainPath && m_voice != null) m_voice.StopLive();
            base.OnKill();
        }

        protected override ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
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

        protected override void OnPlayReady(float timeStartOffset, float childFadeDuration) {
            if (m_isRealtime) {
                base.OnPlayReady(timeStartOffset + zound.trimStart, childFadeDuration);
                currentTime = timeStartOffset;
            }
            else {
                base.OnPlayReady(timeStartOffset, childFadeDuration);
            }
        }

        protected override void OnCompleteDuration() {
            if (m_isRealtime) {
                audioSource.Stop();
            }
        }

    }

}
