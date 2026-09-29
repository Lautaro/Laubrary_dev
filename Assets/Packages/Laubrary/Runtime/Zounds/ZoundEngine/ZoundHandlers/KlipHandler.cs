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

        /// <summary>This play's own id (its per-play draws are seeded from it), and the volume and pitch it started with.</summary>
        private long m_playSeed;
        private float m_startVolume, m_startPitch;

        /// <summary>The chain voice, when this sound is playing through the chain. Null on the fallback path.</summary>
        private Dsp.ZoundSapVoiceGenerator m_voice;

        /// <summary>Play length worked out when the chain voice was set up; the audio source has no clip to measure.</summary>
        private float m_chainDuration;

        /// <summary>True while this sound is playing through the chain rather than from a rendered file.</summary>
        private bool m_chainPath;

        /// <summary>Reported once per session so a project-wide misconfiguration does not spam the console.</summary>
        private static bool s_warnedAboutFallback;

        public override void SetToken(ZoundToken t) {
            base.SetToken(t);
            // A token that has glided to a snapshot plays its next runs on that snapshot, from the first sample (T-0498).
            var snap = t?.settingsRoot?.currentSnapshot;
            if (snap != null) ApplySnapshot(snap, 0f);
            // The voice already exists (it is started in the constructor), so its ZPOCs can take their resolved values
            // now, before its first block: a project-wide value, or anything already set on the token, is heard from the
            // first sample. A track inside a Zequence is applied again once its parent token is linked.
            ApplyAllZpoc();
        }

        public override void ApplyZpoc(string key) {
            if (!m_chainPath || m_voice == null || key == null) return;
            var L = m_voice.playingLayout;
            if (L == null || !L.hasZpoc) return;
            for (int m = 0; m < L.modCount; m++) {
                if (!string.Equals(L.modZpocKey[m], key)) continue;
                SendZpoc(L, m, key);
            }
        }

        public override void ApplyAllZpoc() {
            if (!m_chainPath || m_voice == null) return;
            var L = m_voice.playingLayout;
            if (L == null || !L.hasZpoc) return;
            for (int m = 0; m < L.modCount; m++) {
                var key = L.modZpocKey[m];
                if (key != null) SendZpoc(L, m, key);
            }
        }

        /// <summary>Sends one modifier its control: the token's resolved value for its id, converted by the voice's OWN
        /// layout (the one it started with), or the resting value when nothing is set anywhere.</summary>
        void SendZpoc(Dsp.ChainLayout L, int m, string key) {
            float control = token != null && token.TryResolveZpoc(key, out float v) ? L.ControlFor(m, v) : L.modCtlInit[m];
            m_voice.SetModifierControlLive(m, control);
        }

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
            long playId = Dsp.ZoundSapPlayback.NextPlayId(zound);
            m_playSeed = playId;
            m_startVolume = selfVolume; m_startPitch = basePitch;
            m_voice = Dsp.ZoundSapPlayback.StartVoice(zound, audioSource, sourceClip, basePitch, baseVolume,
                                                     playId, out string reason, out m_chainDuration,
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
            set { m_liveSpeed = value; if (m_chainPath && m_voice != null) m_voice.SetTokenSpeedLive(m_liveSpeed * m_trackSpeed); }
        }
        private float m_liveSpeed = 1f;

        /// <summary>A track's speed from its token's track settings (T-0497), on top of the token's live speed.</summary>
        public override float trackSpeed {
            get => m_trackSpeed;
            set {
                if (value == m_trackSpeed) return;
                m_trackSpeed = value;
                if (m_chainPath && m_voice != null) m_voice.SetTokenSpeedLive(m_liveSpeed * m_trackSpeed);
            }
        }
        private float m_trackSpeed = 1f;

        /// <summary>A track's pitch from its token's track settings (T-0497): the voice reads the source that much faster.</summary>
        public override float livePitch {
            get => m_livePitch;
            set {
                if (value == m_livePitch) return;
                m_livePitch = value;
                if (m_chainPath && m_voice != null) m_voice.SetPitchLive(basePitch * m_livePitch);
                else if (audioSource != null) audioSource.pitch = basePitch * m_livePitch;
            }
        }
        private float m_livePitch = 1f;

        // ───────────── snapshot glides (T-0498) ─────────────
        //
        // A glide moves this play from wherever it is to a snapshot's settings. The voice does the chain part itself, once
        // per control block (see SapVoiceRender.AdvanceGlide); the Zound's own volume and pitch ranges are moved from here,
        // the same way an edit of those ranges already reaches a playing sound. Where the glide started is remembered, worked
        // out the same way the engine moves values, so gliding back returns exactly there.

        ZoundSnapshot m_glideFrom, m_glideTo;
        float m_glideStart, m_glideSeconds;
        /// <summary>This play's volume and pitch drawn from each snapshot's ranges, once, so returning to a snapshot lands
        /// on the same numbers (a new play draws afresh).</summary>
        readonly Dictionary<string, Vector2> m_drawn = new Dictionary<string, Vector2>();
        float m_rangeVolFrom, m_rangeVolTo, m_rangePitchFrom, m_rangePitchTo;
        bool m_rangeGliding;

        public override void ApplySnapshot(string name, float seconds) {
            var target = ZoundSnapshots.Find(zound, name);
            if (target == null) return;
            GlideTo(target, seconds, ZpocKeys.Key(name), null);
        }

        public override void GlideBackTo(float seconds) {
            if (m_glideFrom == null) return;
            // Back to exactly where the glide began: its settings, and the volume and pitch this play had then.
            GlideTo(m_glideFrom, seconds, null, new Vector2(m_rangeVolFrom, m_rangePitchFrom));
        }

        void GlideTo(ZoundSnapshot target, float seconds, string drawKey, Vector2? exact) {
            // Default's "draw" is the one this play started with, so gliding back to Default lands where the play began.
            string defaultKey = ZpocKeys.Key(ZoundSnapshots.DefaultName);
            if (!m_drawn.ContainsKey(defaultKey)) m_drawn[defaultKey] = new Vector2(m_startVolume, m_startPitch);
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            float now = Time.realtimeSinceStartup;
            // Where this play is now: the Default settings, or wherever the last glide has got to.
            var current = m_glideTo == null ? ZoundSnapshots.Find(zound, ZoundSnapshots.DefaultName)
                        : Interpolate(chain, m_glideFrom, m_glideTo, m_glideSeconds <= 0f ? 1f : Mathf.Clamp01((now - m_glideStart) / m_glideSeconds));
            m_glideFrom = current;
            m_glideTo = target;
            m_glideStart = now;
            m_glideSeconds = Mathf.Max(0f, seconds);

            if (m_chainPath && m_voice != null && chain != null) {
                var L = m_voice.playingLayout;
                if (L != null) SendChainGlide(chain, L, target, seconds);
            }

            // The Zound's own ranges: a play draws from the target's ranges once, remembered by snapshot for this play.
            Vector2 drawn;
            if (exact.HasValue) drawn = exact.Value;
            else if (drawKey != null && m_drawn.TryGetValue(drawKey, out drawn)) { }
            else {
                float vol = selfVolume, pit = basePitch;
                uint seed = DrawSeed;
                foreach (var v in target.values) {
                    if (v.kind == SnapshotValueKind.VolumeRange) vol = Mathf.Lerp(v.a, v.b, Hash01(seed, 1));
                    if (v.kind == SnapshotValueKind.PitchRange) pit = Mathf.Lerp(v.a, v.b, Hash01(seed, 2));
                }
                drawn = new Vector2(vol, pit);
                if (drawKey != null) m_drawn[drawKey] = drawn;
            }
            m_rangeVolFrom = selfVolume; m_rangeVolTo = drawn.x;
            m_rangePitchFrom = basePitch; m_rangePitchTo = drawn.y;
            m_rangeGliding = true;
            FollowRangeGlide(now);
        }

        /// <summary>A number for this play's range draws, different per play and per snapshot request (never UnityEngine.Random:
        /// every draw in the engine is a hash of the play, so plays stay independent and repeatable).</summary>
        uint DrawSeed => (uint)(m_drawSalt++ * 2654435761u) ^ (uint)m_playSeed;
        uint m_drawSalt = 1;

        static float Hash01(uint seed, uint salt) {
            uint h = seed ^ (salt * 0x9E3779B1u);
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        void SendChainGlide(ZoundEffectChain chain, Dsp.ChainLayout L, ZoundSnapshot target, float seconds) {
            m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(Dsp.SapVoiceCommandKind.GlideClear, 0, 0f));
            foreach (var v in target.values) {
                switch (v.kind) {
                    case SnapshotValueKind.EffectParam: {
                        int n = ZoundSnapshots.NodeIndex(chain, v.node);
                        if (n < 0 || n >= L.nodeCount || v.param < 0 || v.param >= L.paramCountOf[n]) break;
                        var pd = Dsp.ZoundEffectDescriptors.Get(chain.nodes[n].type).parameters[v.param];
                        var motion = ZoundSnapshots.MotionOf(pd, true);
                        if (motion == ZoundSnapshots.Motion.NotInSnapshots) break;
                        m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(motion == ZoundSnapshots.Motion.Glide ? Dsp.SapVoiceCommandKind.GlideParam : Dsp.SapVoiceCommandKind.GlideParamSwitch,
                                                                    L.FlatIndex(n, v.param), v.a));
                        break;
                    }
                    case SnapshotValueKind.EffectOn: {
                        int n = ZoundSnapshots.NodeIndex(chain, v.node);
                        if (n >= 0 && n < L.nodeCount) m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(Dsp.SapVoiceCommandKind.GlidePresence, n, v.a));
                        break;
                    }
                    case SnapshotValueKind.ModifierParam: {
                        int m = ZoundSnapshots.ModifierIndex(chain, v.mod);
                        if (m < 0 || m >= L.modCount || v.param < 0 || v.param >= L.modParamCountOf[m]) break;
                        var pd = Dsp.ZoundEffectDescriptors.GetModifier(chain.modifiers[m].type).parameters[v.param];
                        var kind = ZoundSnapshots.MotionOf(pd, false) == ZoundSnapshots.Motion.Glide ? Dsp.SapVoiceCommandKind.GlideModParam : Dsp.SapVoiceCommandKind.GlideModParamSwitch;
                        m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(kind, L.modParamOffset[m] + v.param, v.a));
                        break;
                    }
                    case SnapshotValueKind.BindingDepth: {
                        int m = ZoundSnapshots.ModifierIndex(chain, v.mod);
                        int tn = string.IsNullOrEmpty(v.node) ? -1 : ZoundSnapshots.NodeIndex(chain, v.node);
                        if (m < 0 || (!string.IsNullOrEmpty(v.node) && tn < 0)) break;
                        for (int b = 0; b < L.bindCount; b++) {
                            int src = L.bindSource[b];
                            if (src < 0 || src >= chain.bindings.Count) continue;
                            var cb = chain.bindings[src];
                            if (cb.modifierIndex == m && cb.nodeIndex == tn && cb.paramIndex == v.param) {
                                m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(Dsp.SapVoiceCommandKind.GlideDepth, b, Mathf.Clamp(v.a, -1f, 1f)));
                                break;
                            }
                        }
                        break;
                    }
                    case SnapshotValueKind.ZpocRest: {
                        // A value set by code wins over where the snapshot says the ZPOC rests (owner, 2026-09-29).
                        int m = ZoundSnapshots.ModifierIndex(chain, v.mod);
                        if (m < 0 || m >= L.modCount || L.modZpocKey[m] == null) break;
                        if (token != null && token.TryResolveZpoc(L.modZpocKey[m], out _)) break;
                        m_voice.SetModifierControlLive(m, v.a < 0f ? 1f : L.ControlFor(m, v.a));
                        break;
                    }
                }
            }
            int samples = Mathf.RoundToInt(Mathf.Max(0f, seconds) * AudioSettings.outputSampleRate);
            m_voice.SendGlide(Dsp.SapVoiceCommand.Glide(Dsp.SapVoiceCommandKind.GlideBegin, samples, 0f));
        }

        /// <summary>Moves the play's own volume and pitch along a glide (called every update while one is under way).</summary>
        void FollowRangeGlide(float now) {
            if (!m_rangeGliding) return;
            float t = m_glideSeconds <= 0f ? 1f : Mathf.Clamp01((now - m_glideStart) / m_glideSeconds);
            float vol = Mathf.Lerp(m_rangeVolFrom, m_rangeVolTo, t);
            float pit = m_rangePitchFrom * Mathf.Pow(m_rangePitchTo / Mathf.Max(m_rangePitchFrom, 1e-4f), t);
            SetSelfVolume(vol);
            basePitch = pit;
            if (m_chainPath && m_voice != null) {
                m_voice.SetGainLive(vol * ZoundEngine.GetMasterVolume());
                m_voice.SetPitchLive(basePitch * m_livePitch);
            }
            if (t >= 1f) m_rangeGliding = false;
        }

        /// <summary>
        /// Where a glide from <paramref name="a"/> to <paramref name="b"/> is at <paramref name="t"/>, worked out the way the
        /// engine moves each value: along its control for continuous values, at the midpoint for switches, linearly for
        /// modifier settings and strengths. Values only one side has are taken from that side.
        /// </summary>
        static ZoundSnapshot Interpolate(ZoundEffectChain chain, ZoundSnapshot a, ZoundSnapshot b, float t) {
            if (a == null) return b;
            if (t >= 1f) return b;
            var r = new ZoundSnapshot { name = "(mid-glide)" };
            foreach (var vb in b.values) {
                ZoundSnapshotValue va = vb; bool found = false;
                foreach (var x in a.values) if (x.kind == vb.kind && x.node == vb.node && x.mod == vb.mod && x.param == vb.param) { va = x; found = true; break; }
                var v = vb;
                if (found) {
                    switch (vb.kind) {
                        case SnapshotValueKind.EffectParam: {
                            int n = ZoundSnapshots.NodeIndex(chain, vb.node);
                            if (n >= 0) {
                                var pd = Dsp.ZoundEffectDescriptors.Get(chain.nodes[n].type).parameters[vb.param];
                                if (ZoundSnapshots.MotionOf(pd, true) == ZoundSnapshots.Motion.Glide) {
                                    bool ratio = Dsp.ModulationMath.IsRatioSpaced(pd.curve);
                                    float pa = Dsp.ModulationMath.ToPosition(va.a, pd.min, pd.max, ratio), pb = Dsp.ModulationMath.ToPosition(vb.a, pd.min, pd.max, ratio);
                                    v.a = Dsp.ModulationMath.FromPosition(pa + (pb - pa) * t, pd.min, pd.max, ratio);
                                }
                                else v.a = t >= 0.5f ? vb.a : va.a;
                            }
                            break;
                        }
                        case SnapshotValueKind.EffectOn: v.a = t >= 0.5f ? vb.a : va.a; break;
                        default: v.a = va.a + (vb.a - va.a) * t; v.b = va.b + (vb.b - va.b) * t; break;
                    }
                }
                r.values.Add(v);
            }
            return r;
        }

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
                    m_voice.SetPitchLive(basePitch * m_livePitch);
                }
            }
        }

        protected override ZoundUpdateResult OnPlayUpdate(float deltaDspTime) {
            if (m_chainPath && m_voice != null && m_voice.IsPlaying) FollowRangeEdits();
            if (m_rangeGliding) FollowRangeGlide(Time.realtimeSinceStartup);
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
