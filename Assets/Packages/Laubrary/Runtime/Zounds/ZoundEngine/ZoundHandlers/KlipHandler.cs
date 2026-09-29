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
            var sourceClip = Dsp.ZoundSapPlayback.LoadSourceClip(zound, out bool sourceAlreadyTrimmed);
            // A number of its own for this play (T-0484): every per-play draw (random curve points, a Looper's crossmix
            // lengths, repeat and random-oscillator variation) is seeded from it. The sound's id was passed here before,
            // which gave every play of a sound the same draws -- random points moved, but identically every time.
            m_voice = Dsp.ZoundSapPlayback.StartVoice(zound, audioSource, sourceClip, basePitch, baseVolume,
                                                     Dsp.ZoundSapPlayback.NextPlayId(zound), out string reason, out m_chainDuration,
                                                     sourceAlreadyTrimmed);
            if (m_voice != null) {
                m_chainPath = true;
                // The voice applies the pitch itself, as it reads the source (basePitch above), so the audio source that
                // carries it into the mixer must play at one. Left at the play's pitch, it resampled the voice's output a
                // SECOND time: measured at pitch 0.5, both were 0.5, the sound came out two octaves down instead of one, it
                // was still sounding at 2.5 s against a declared 1.54 s, and every modifier's timing ran at half speed
                // (T-0443). Only pitches other than one were affected, which is why it went unnoticed.
                audioSource.pitch = 1f;
                // A chain can go on making sound after the source has run out -- a delay still repeating, a reverb
                // still decaying. The source's own length says nothing about that, so counting only the source would
                // have this handler declare the sound over and hand its audio source back to the pool while the tail
                // was still ringing, chopping it off. The chain already declares how long it can ring for, so that
                // is added on. The engine stops the voice by itself once the tail has actually died away, so this is
                // an upper bound rather than a fixed wait.
                var laidOut = m_voice.playingLayout;
                if (laidOut != null) m_chainDuration += laidOut.tailSeconds;
                // A sound with live speed cannot know its length in advance: game code or a modifier may slow it at any
                // moment (T-0409). So it declares the longest it could possibly last, and ends as soon as the voice itself
                // reports that it has finished (see OnPlayUpdate) — never cut short, never held on longer than it sounds.
                if (m_voice.HasLiveSpeed) {
                    float sourceSeconds = sourceClip != null ? sourceClip.length : m_chainDuration;
                    m_chainDuration = sourceSeconds / (Dsp.SapStretch.MinSpeed * Mathf.Max(basePitch, 0.01f))
                                      + (laidOut != null ? laidOut.tailSeconds : 0f) + 1f;
                }
                // A Looper (T-0473) plays until it is stopped: it has no length of its own. Stopping it (a kill, a
                // culling, a parent stopping) goes through OnKill, which stops the voice itself.
                if (zound.IsLooper) m_chainDuration = float.PositiveInfinity;
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
            // A Looper that could not play through the chain still loops, from the rendered file: a plain restart at the
            // end, without the crossmix (which needs the engine's own read of the source). Undone when the pool takes the
            // audio source back.
            audioSource.loop = zound.IsLooper;
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
            if (zound.IsLooper) return float.PositiveInfinity;
            if (m_isRealtime) return zound.trimEnd - zound.trimStart;
            return base.PrepareAndCalculateDuration();
        }

        /// <summary>Stops the chain voice as well as the audio source, so a killed sound does not keep rendering.</summary>
        public override void OnKill() {
            if (m_chainPath && m_voice != null) m_voice.StopLive();
            base.OnKill();
        }

        /// <summary>This play's speed from game code (ZoundToken.liveSpeed), sent to the playing voice (T-0409).</summary>
        public override float liveSpeed {
            get => m_liveSpeed;
            set { m_liveSpeed = value; if (m_chainPath && m_voice != null) m_voice.SetTokenSpeedLive(value); }
        }
        private float m_liveSpeed = 1f;

        // ───────────── live edits of the sound's volume and pitch ranges (Looper live-edit fix, 2026-09-29) ─────────────
        //
        // Measured: a playing Looper kept the volume and pitch it started with for ever -- editing the Klip's volume or pitch
        // was never heard, because each play draws them once at its start and nothing re-read them. A one-shot hides that
        // (its next play draws again); a Looper has no next play. So a play remembers WHERE its draw sat inside the sound's
        // range, and when the range is edited it moves to the same relative spot of the new range: a fixed volume edited from
        // 1 to 0.5 goes to 0.5; a play that drew the middle of 0.5-1 goes to the middle of whatever the range becomes.
        //
        // A value given from outside the range (game code, or a Zequence entry's own setting) is not a draw from the range,
        // so an edit of the range leaves it alone.
        //
        // Volume follows for every Klip: it changes nothing about how long the play lasts. Pitch follows only where the
        // play's length is not fixed in advance (a Looper, or live speed, which ends when the voice says so): a one-shot's
        // length was worked out from its starting pitch, and lowering it mid-play would cut the end off.
        private float m_volT = -1f, m_pitchT = -1f;
        private float m_seenMinVol, m_seenMaxVol, m_seenMinPitch, m_seenMaxPitch;
        private bool m_rangesSeen;

        static float RelativeIn(float v, float min, float max) {
            const float eps = 1e-4f;
            if (v < min - eps || v > max + eps) return -1f;      // not a draw from this range
            return max - min > eps ? Mathf.Clamp01((v - min) / (max - min)) : 0f;
        }

        private void RememberDraws() {
            m_seenMinVol = zound.minVolume; m_seenMaxVol = zound.maxVolume;
            m_seenMinPitch = zound.minPitch; m_seenMaxPitch = zound.maxPitch;
            m_volT = RelativeIn(selfVolume, m_seenMinVol, m_seenMaxVol);
            m_pitchT = RelativeIn(basePitch, m_seenMinPitch, m_seenMaxPitch);
            m_rangesSeen = true;
        }

        /// <summary>Delivers an edit of the sound's volume or pitch range to this play, as described above.</summary>
        private void FollowRangeEdits() {
            if (!m_rangesSeen) { RememberDraws(); return; }
            bool volEdited = zound.minVolume != m_seenMinVol || zound.maxVolume != m_seenMaxVol;
            bool pitchEdited = zound.minPitch != m_seenMinPitch || zound.maxPitch != m_seenMaxPitch;
            if (!volEdited && !pitchEdited) return;
            if (volEdited) {
                m_seenMinVol = zound.minVolume; m_seenMaxVol = zound.maxVolume;
                if (m_volT >= 0f) {
                    float v = Mathf.Lerp(m_seenMinVol, m_seenMaxVol, m_volT);
                    SetSelfVolume(v);
                    // The voice was given the play's volume as its output gain when it started (see StartVoice), so it
                    // is moved the same way; the audio source follows from SetSelfVolume on the base update.
                    m_voice.SetGainLive(v * ZoundEngine.GetMasterVolume());
                }
            }
            if (pitchEdited) {
                m_seenMinPitch = zound.minPitch; m_seenMaxPitch = zound.maxPitch;
                bool lengthOpen = zound.IsLooper || m_voice.HasLiveSpeed;
                if (m_pitchT >= 0f && lengthOpen) {
                    basePitch = Mathf.Lerp(m_seenMinPitch, m_seenMaxPitch, m_pitchT);
                    m_voice.SetPitchLive(basePitch);
                }
            }
        }

        protected override ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
            if (m_chainPath && m_voice != null && m_voice.IsPlaying) FollowRangeEdits();
            // A live-speed sound ends when its voice says so, since its length could not be known (T-0409).
            if (m_chainPath && m_voice != null && m_voice.HasLiveSpeed && m_voice.VoiceFinished) {
                OnCompleteDuration();
                return ZoundUpdateResult.Kill;
            }
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
