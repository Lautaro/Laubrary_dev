using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Plays a sound through the real-time effect chain, from its ORIGINAL source audio, with no pre-rendered
    /// file involved.
    ///
    /// **This is the point of the whole exercise.** Until now a sound was played by handing an ordinary audio
    /// source a file that had been rendered in advance, with the author's edits already burnt into it. Every
    /// change meant rendering a new file, so a project accumulated one small file per sound per revision, and
    /// nothing could be altered while a sound was playing. Here the source audio is read as-is and the edits are
    /// applied as it plays, which removes the rendering step, removes the files, and makes a change audible
    /// immediately.
    ///
    /// **How the edits survive that change.** The older editing surface put its effects on the sound itself as
    /// named settings — a gain, an equaliser, compression, normalisation, a fade, volume and pitch curves — and
    /// only the offline render ever applied them. Those are exactly the things the chain system can express, and
    /// a converter for them already exists, so a sound that has never been touched by the new editor still
    /// sounds the way its author intended: its old settings are turned into an equivalent chain on the way to
    /// the engine. Nothing has to be re-authored.
    ///
    /// **What the ordinary audio source is still for.** It no longer holds a file, but it is not redundant: it
    /// is what places the sound in the mixer, and therefore what gives us bus routing, group volumes, distance
    /// attenuation and 3D position for free. The engine renders the sound; the audio source decides where it
    /// goes. This is why no separate mixing graph is needed here — an earlier version of this engine had to
    /// build one because it produced audio outside the mixer entirely and had to do its own summing and routing.
    /// </summary>
    public static class ZoundSapPlayback {

        /// <summary>
        /// Starts <paramref name="zound"/> playing through the chain on the given pooled audio source, and hands
        /// back the object that controls it while it plays. Returns null when the sound cannot be played this
        /// way, with the reason in <paramref name="reason"/> — the caller is then free to fall back.
        ///
        /// Main thread only.
        /// </summary>
        public static ZoundSapVoiceGenerator StartVoice(Zound zound, AudioSource carrier, AudioClip sourceClip,
                                                       float basePitch, float outGain, long tokenId,
                                                       out string reason, out float duration,
                                                       bool sourceAlreadyTrimmed = false, in Excerpt excerpt = default) {
            reason = null;
            duration = 0f;
            if (zound == null || carrier == null) { reason = "no sound or no audio source"; return null; }
            if (sourceClip == null) { reason = "the source audio is not loaded"; return null; }

            // The engine reads raw samples, which means the clip has to be one Unity will let us read. A clip set
            // to stream or to stay compressed cannot be, and the failure would otherwise be silence with no
            // explanation, so it is reported as a reason rather than attempted.
            string clipProblem = ZoundPcmCache.Validate(sourceClip);
            if (!string.IsNullOrEmpty(clipProblem)) { reason = sourceClip.name + ": " + clipProblem; return null; }

            var pcm = ZoundPcmCache.Get(sourceClip);
            if (pcm == null || !pcm.valid) { reason = sourceClip.name + ": its samples could not be read"; return null; }

            int sampleRate = AudioSettings.outputSampleRate;

            // Trim is part of the chain era's source stage rather than something baked into a file: it simply
            // decides where reading starts and stops.
            Region(zound, pcm, sourceAlreadyTrimmed, in excerpt, out double startFrame, out double endFrame);
            if (endFrame <= startFrame) { reason = "the trimmed region is empty"; return null; }

            // What this play is made of (T-0481): the chain (with an old stretch setting converted into a time curve),
            // whether the live stretcher runs and at what speed. A pitch or time curve consumes the source at a changing
            // rate, so the play length is the integral of that rate. The duration reported is the nominal one at the
            // current speed; a stretched voice reports its real end itself, because the speed may change while it plays.
            var plan = Plan(zound, startFrame, endFrame, pcm.frequency, pcm.frames, OwnAxisFor(zound, pcm, sourceAlreadyTrimmed, in excerpt));
            var chain = plan.chain;
            var layout = chain != null && !chain.IsEmpty ? ZoundDspPlayback.GetLayoutFor(chain, zound, sampleRate)
                                                        : ChainLayout.Empty;
            // A curve with random points is measured as THIS play will draw it (T-0483).
            plan.drawn = true; plan.seed = EnvelopeRandom.SeedFor(tokenId);
            duration = PlayLength(in plan, (endFrame - startFrame) / pcm.frequency, basePitch, true);
            var stretch = plan.stretch;
            float authoredSpeed = plan.authoredSpeed;

            var generator = EnsureGenerator(carrier);
            // A Looper (T-0473) loops its region inside the voice, with its crossmix, until it is stopped.
            bool looping = (zound is Klip loopKlip && loopKlip.IsLooper) || (excerpt.on && excerpt.loop);
            generator.SetPlay(pcm, layout, startFrame, endFrame, basePitch, outGain, duration,
                              loop: looping, tokenId: tokenId, heavyTier: layout.heavy, zound: zound,
                              stretch: stretch, authoredSpeed: authoredSpeed);
            if (looping && zound is Klip lk && lk.IsLooper) {
                lk.loop.Effective((float)((endFrame - startFrame) / pcm.frequency), out float xMin, out float xMax);
                generator.SetLoopCrossmix(xMin, xMax);
            }
            else generator.SetLoopCrossmix(0f, 0f);

            // No file. The audio source carries the sound into the mixer and nothing else.
            //
            // Deliberately NOT started here. The existing playback lifecycle starts the audio source itself, once
            // it has applied the delay, the start offset and any fade-in. Starting it here as well would play the
            // sound twice and bypass all of that.
            carrier.clip = null;
            carrier.generator = generator;
            return generator;
        }

        /// <summary>
        /// The sound's ORIGINAL audio — the file the author imported, not the rendered one.
        ///
        /// This is the whole difference between the two eras: the old path deliberately asked for the rendered
        /// file, because that was where the author's edits lived. Real-time playback wants the untouched source and
        /// applies those edits as it goes, which is what makes the rendered file unnecessary.
        /// </summary>
        public static AudioClip LoadSourceClip(Zound zound) {
            return LoadSourceClip(zound, out _);
        }

        /// <summary>
        /// The audio to play, and whether it has already had the sound's trim applied to it.
        ///
        /// **Why there are two candidates and this has to choose.** A sound's original file may live anywhere in the
        /// project, including somewhere that does not ship. To make such a sound playable in a built game, the editor
        /// copies it into a folder that does ship — and when the sound is trimmed, it writes the trimmed region rather
        /// than the whole file, so only the part actually used gets shipped. That shipped copy is not an effects bake
        /// and never was; it is a packaging step, and it is still wanted.
        ///
        /// So: prefer the original, because it is the unmodified truth and lets the trim be applied as the sound plays.
        /// Fall back to the shipped copy when the original cannot be loaded, which is exactly the case a built game
        /// hits when the original was never shippable. **Getting this wrong would not be subtle — the sound would
        /// simply be silent in a build while working perfectly in the editor.**
        ///
        /// <paramref name="alreadyTrimmed"/> comes back true for the shipped copy, because the trim is baked into it.
        /// Trimming it again would cut a second time into an already-cut region and lose the end of the sound.
        /// </summary>
        public static AudioClip LoadSourceClip(Zound zound, out bool alreadyTrimmed) {
            alreadyTrimmed = false;
            if (zound == null) return null;
#if ADDRESSABLES_INSTALLED
            var klip = zound as Klip;
            if (klip == null) return null;

            if (klip.audioClipRef != null) {
                var original = ZoundDictionary.GetOrLoadClip(klip.audioClipRef);
                if (original != null) return original;
            }

            if (klip.outputClipRef != null) {
                var shipped = ZoundDictionary.GetOrLoadClip(klip.outputClipRef);
                if (shipped != null) {
                    // Only a trimmed sound gets a trimmed copy; an untrimmed one is copied whole.
                    alreadyTrimmed = klip.trimEnabled;
                    return shipped;
                }
            }
#endif
            return null;
        }

        /// <summary>
        /// How long one play of <paramref name="zound"/> lasts at its nominal pitch, worked out exactly the way
        /// <see cref="StartVoice"/> works it out — same source, same trim, same allowance for a pitch curve — so that a
        /// display drawn along "the play" lines up with what is heard. False when the source cannot be read.
        /// </summary>
        public static bool TryGetPlayLength(Zound zound, out float seconds) => TryGetPlayLength(zound, default, out seconds);

        /// <summary>As above, for a play of just <paramref name="excerpt"/> of the source (a track's own excerpt, T-0565).</summary>
        public static bool TryGetPlayLength(Zound zound, in Excerpt excerpt, out float seconds) {
            seconds = 0f;
            var clip = LoadSourceClip(zound, out bool alreadyTrimmed);
            if (clip == null || !string.IsNullOrEmpty(ZoundPcmCache.Validate(clip))) return false;
            var pcm = ZoundPcmCache.Get(clip);
            if (pcm == null || !pcm.valid) return false;
            Region(zound, pcm, alreadyTrimmed, in excerpt, out double startFrame, out double endFrame);
            if (endFrame <= startFrame) return false;
            var plan = Plan(zound, startFrame, endFrame, pcm.frequency, pcm.frames, OwnAxisFor(zound, pcm, alreadyTrimmed, in excerpt));
            seconds = PlayLength(in plan, (endFrame - startFrame) / pcm.frequency, 1f, false);
            return seconds > 0f;
        }

        /// <summary>
        /// Where a play of <paramref name="zound"/> (its trim, or <paramref name="excerpt"/>) is in its source at each moment,
        /// for drawing it on a timeline (T-0562): <paramref name="playSeconds"/>[i] is how long the play has run, at pitch one,
        /// when it reaches source second <paramref name="sourceSeconds"/>[i]; the two arrays run evenly over the region read,
        /// from its start to its end. Pitch and time curves and a stretch warp it exactly as they warp the play -- the same
        /// integration as <see cref="TryGetPlayLength(Zound, in Excerpt, out float)"/>, so the last entry is that length.
        /// Also gives the file's length. False when the source cannot be read. Editor use; allocates nothing itself.
        /// </summary>
        public static bool TryMapSourceToPlay(Zound zound, in Excerpt excerpt, float[] sourceSeconds, float[] playSeconds, out float fileLength) {
            fileLength = 0f;
            int n = sourceSeconds != null && playSeconds != null ? Mathf.Min(sourceSeconds.Length, playSeconds.Length) : 0;
            if (n < 2) return false;
            var clip = LoadSourceClip(zound, out bool alreadyTrimmed);
            if (clip == null || !string.IsNullOrEmpty(ZoundPcmCache.Validate(clip))) return false;
            var pcm = ZoundPcmCache.Get(clip);
            if (pcm == null || !pcm.valid) return false;
            Region(zound, pcm, alreadyTrimmed, in excerpt, out double startFrame, out double endFrame);
            if (endFrame <= startFrame) return false;
            var plan = Plan(zound, startFrame, endFrame, pcm.frequency, pcm.frames, OwnAxisFor(zound, pcm, alreadyTrimmed, in excerpt));
            float region = (float)((endFrame - startFrame) / pcm.frequency);
            float shift = alreadyTrimmed && zound is Klip k && k.trimEnabled ? k.trimStart : 0f;
            float a = (float)(startFrame / pcm.frequency) + shift;
            fileLength = alreadyTrimmed ? pcm.LengthSeconds + shift : pcm.LengthSeconds;
            var axis = plan.axis.Valid ? plan.axis : (CurveAnchor.Axis?)null;
            var chain = plan.chain;
            bool plain = chain == null || chain.IsEmpty;
            float speedDiv = plan.stretched ? Mathf.Max(plan.authoredSpeed, SapStretch.MinSpeed) : 1f;
            // The same 400-step rule as the play length, sampled onto n points.
            const int steps = 400;
            double total = 0; int written = 0;
            sourceSeconds[0] = a; playSeconds[0] = 0f; written = 1;
            for (int i = 0; i < steps; i++) {
                float t = (i + 0.5f) / steps;
                float rate = 1f;
                if (!plain) {
                    rate = plan.keepLength && plan.stretched ? 1f : ZoundDspPlayback.PitchAtSource(chain, t, region, plan.drawn, plan.seed, axis);
                    if (plan.stretched) rate *= ZoundDspPlayback.SpeedAtSource(chain, t, region, plan.drawn, plan.seed, axis);
                }
                total += (region / steps) / Mathf.Max(rate, 1e-3f);
                float reached = (float)(i + 1) / steps;
                while (written < n && (float)written / (n - 1) <= reached + 1e-6f) {
                    float want = (float)written / (n - 1);
                    // Linear within this step.
                    double before = total - (region / steps) / Mathf.Max(rate, 1e-3f);
                    float within = Mathf.Clamp01((want - (float)i / steps) * steps);
                    sourceSeconds[written] = a + want * region;
                    playSeconds[written] = (float)((before + within * (total - before)) / speedDiv);
                    written++;
                }
            }
            for (; written < n; written++) { sourceSeconds[written] = a + region; playSeconds[written] = (float)(total / speedDiv); }
            return true;
        }

        /// <summary>
        /// A part of a Klip's source to play instead of its own trim (T-0565), in seconds of the ORIGINAL source file.
        /// Default (off) plays the Klip's own trim.
        /// </summary>
        public struct Excerpt {
            public bool on;
            public float start, end;
            /// <summary>Loop it until stopped (an audition loop).</summary>
            public bool loop;
            public static Excerpt Of(float start, float end, bool loop = false) => new Excerpt { on = true, start = start, end = end, loop = loop };
            public static Excerpt From(in ZoundArgs a) => a.excerpt ? new Excerpt { on = true, start = a.excerptStart, end = a.excerptEnd, loop = a.excerptLoop } : default;
        }

        /// <summary>
        /// The frames of <paramref name="pcm"/> a play reads: the Klip's trim, or the excerpt. When the audio is the shipped
        /// copy, which already holds only the Klip's trim, an excerpt is shifted into it and clamped to what it holds.
        /// </summary>
        static void Region(Zound zound, PcmClip pcm, bool alreadyTrimmed, in Excerpt excerpt, out double startFrame, out double endFrame) {
            double rate = pcm.frequency;
            startFrame = 0d; endFrame = pcm.frames;
            var klip = zound as Klip;
            if (excerpt.on && klip != null) {
                float shift = alreadyTrimmed && klip.trimEnabled ? klip.trimStart : 0f;
                float a = Mathf.Clamp(excerpt.start - shift, 0f, pcm.LengthSeconds);
                float b = Mathf.Clamp(excerpt.end - shift, 0f, pcm.LengthSeconds);
                startFrame = a * rate; endFrame = b * rate;
                return;
            }
            // Skipped when the audio handed to us is already the trimmed copy: trimming it again would cut a second
            // time into an already-cut region and lose the end of the sound.
            if (!alreadyTrimmed && klip != null && klip.trimEnabled) {
                startFrame = Mathf.Clamp(klip.trimStart, 0f, pcm.LengthSeconds) * rate;
                if (klip.trimEnd > klip.trimStart) endFrame = Mathf.Min(klip.trimEnd, pcm.LengthSeconds) * rate;
            }
        }

        /// <summary>
        /// For a play of an excerpt: the Klip's OWN trim, so its curves still anchored to "a fraction of the trim" can be
        /// read on a converted copy exactly as they are drawn (see <see cref="Plan"/>). Null for an ordinary play.
        /// </summary>
        static CurveAnchor.Axis? OwnAxisFor(Zound zound, PcmClip pcm, bool alreadyTrimmed, in Excerpt excerpt) {
            if (!excerpt.on || alreadyTrimmed || !(zound is Klip k)) return null;
            var axis = CurveAnchor.Axis.Of(k, pcm.LengthSeconds);
            return axis.Valid ? axis : (CurveAnchor.Axis?)null;
        }

        private static long s_playSerial;

        /// <summary>
        /// A number for one play of <paramref name="zound"/>, different for every play (T-0484): the seed of everything a
        /// play draws. Deterministic for a given play -- the draws are hashes of it -- but never the same for two plays,
        /// which is what makes "random" differ from one play to the next. Main thread.
        /// </summary>
        public static long NextPlayId(Zound zound) => ((long)(zound != null ? zound.id : 0) << 32) ^ ++s_playSerial;

        /// <summary>What a play of a sound is made of (T-0481); see <see cref="Plan"/>.</summary>
        public struct PlayPlan {
            /// <summary>The chain to play (the sound's own, or a copy with an old stretch setting added as a time curve).</summary>
            public ZoundEffectChain chain;
            /// <summary>The live stretcher's setup, or off.</summary>
            public SapStretchConfig stretch;
            /// <summary>The sound's own speed (1 unless Live speed or an old Uniform stretch sets one).</summary>
            public float authoredSpeed;
            public bool stretched, keepLength;
            /// <summary>An old Uniform/Region/Curve stretch is being played through the live stretcher.</summary>
            public bool legacyStretch;
            /// <summary>Measure random curve points as the play with <see cref="seed"/> draws them (T-0483); otherwise the
            /// curves as drawn in the editor.</summary>
            public bool drawn;
            public uint seed;
            /// <summary>Where the trim sits in the whole source file, for source-anchored curves (T-0501).</summary>
            public CurveAnchor.Axis axis;
        }

        /// <summary>
        /// Decides a play (T-0481). The live stretcher runs when the sound asks for Live speed, carries an old stretch
        /// setting (converted: Uniform to a speed, Region and Curve to a time curve following the waveform), has a time
        /// curve, or keeps its length under a pitch curve; every other sound reads its source directly, exactly as
        /// before. The one place StartVoice and every length display decide this, so they cannot disagree.
        /// </summary>
        public static PlayPlan Plan(Zound zound, double startFrame, double endFrame, double frequency, double totalFrames = 0d,
                                    CurveAnchor.Axis? ownAxis = null) {
            var plan = new PlayPlan { chain = ResolveChainForPlayback(zound), stretch = SapStretchConfig.Off, authoredSpeed = 1f };
            // An excerpt of a sound whose curves still sit on "a fraction of its trim" (T-0565): they are read from a copy
            // converted to source seconds against the sound's OWN trim, so each point is heard over the audio it is drawn
            // over whatever part of the source this play reads. The saved sound is not touched; it converts on its first edit.
            if (ownAxis.HasValue && plan.chain != null && NeedsAnchorConversion(plan.chain)) {
                var c = plan.chain.DeepCopy();
                foreach (var m in c.modifiers) CurveAnchor.ConvertToSource(m, ownAxis.Value);
                plan.chain = c;
            }
            // Where the region sits in the whole file, for source-anchored curves (T-0501); unknown without the file's length.
            if (frequency > 0 && totalFrames > 0)
                plan.axis = new CurveAnchor.Axis { trimStart = (float)(startFrame / frequency), trimEnd = (float)(endFrame / frequency), sourceLength = (float)(totalFrames / frequency) };
            if (!(zound is Klip k) || k.timeStretch == null || frequency <= 0) return plan;
            var ts = k.timeStretch;
            bool legacy = LegacyStretch.IsActive(k);
            bool timeCurve = ZoundDspPlayback.HasTimeCurve(plan.chain);
            if (legacy) {
                // The old stretch measured its region against the Klip's trimmed range, whatever audio it is handed.
                float from = k.trimEnabled ? k.trimStart : 0f;
                float to = from + (float)((endFrame - startFrame) / frequency);
                var curve = LegacyStretch.ToTimeCurve(k, from, to);
                if (curve != null) {
                    var c = plan.chain != null ? plan.chain.DeepCopy() : new ZoundEffectChain();
                    LegacyStretch.AddTimeCurve(c, curve, "Old stretch");
                    plan.chain = c;
                    timeCurve = true;
                }
            }
            bool keep = ts.pitchKeepsLength && ZoundDspPlayback.HasPitchCurve(plan.chain);
            if (!(ts.liveEnabled || legacy || timeCurve || keep)) return plan;
            plan.stretched = true;
            plan.keepLength = keep;
            plan.legacyStretch = legacy;
            plan.stretch = ts.StretcherConfig();
            plan.stretch.keepLength = keep;
            plan.authoredSpeed = Mathf.Clamp((ts.liveEnabled ? ts.liveSpeed : 1f) * LegacyStretch.UniformSpeed(k), SapStretch.MinSpeed, SapStretch.MaxSpeed);
            return plan;
        }

        static bool NeedsAnchorConversion(ZoundEffectChain chain) {
            foreach (var m in chain.modifiers) if (m.curveAnchor != CurveAnchor.Source && CurveAnchor.FollowsWaveform(m)) return true;
            return false;
        }

        /// <summary>How long a play made to <paramref name="plan"/> lasts, for <paramref name="sourceSeconds"/> of source at
        /// the given pitch; <paramref name="withGameSpeed"/> includes the game's global speed (a live play does).</summary>
        public static float PlayLength(in PlayPlan plan, double sourceSeconds, float basePitch, bool withGameSpeed) {
            float len = ZoundDspPlayback.PlayLengthOverSource(plan.chain, (float)sourceSeconds, plan.stretched, plan.keepLength, plan.drawn, plan.seed,
                                                              plan.axis.Valid ? plan.axis : (CurveAnchor.Axis?)null)
                        / Mathf.Max(basePitch, 0.01f);
            if (plan.stretched) len /= Mathf.Max(plan.authoredSpeed * (withGameSpeed ? ZoundEngine.globalSpeed : 1f), SapStretch.MinSpeed);
            return len;
        }

        /// <summary>
        /// The chain a sound should play with, including the conversion of an older sound's named settings into
        /// an equivalent chain when it has no chain of its own. Returns null when there is genuinely nothing to
        /// apply, in which case the sound plays clean.
        /// </summary>
        public static ZoundEffectChain ResolveChainForPlayback(Zound zound) {
            var chain = ZoundDspPlayback.ResolveChain(zound, out _);
            if (chain != null && !chain.IsEmpty) return chain;

            // Nothing authored. If this is an older sound whose edits still live as named settings, convert them
            // rather than ignoring them -- otherwise moving to real-time playback would silently drop the author's
            // work, which is the one outcome that would make this change worse than what it replaces.
            if (zound is Klip klip && ChainMigration.HasLegacyEdits(klip)) {
                return ChainMigration.SynthesizeFromLegacy(klip);
            }
            return null;
        }

        /// <summary>
        /// Whether this sound can play through the chain, and why not when it cannot. Lets a caller decide
        /// between real-time playback and any older path without having to attempt it first.
        /// </summary>
        public static bool CanPlayRealtime(Zound zound, AudioClip sourceClip, out string reason) {
            reason = null;
            if (zound == null) { reason = "no sound"; return false; }
            if (sourceClip == null) { reason = "the source audio is not loaded"; return false; }
            string problem = ZoundPcmCache.Validate(sourceClip);
            if (!string.IsNullOrEmpty(problem)) { reason = problem; return false; }
            return true;
        }

        /// <summary>
        /// One chain player per pooled audio source, created once and reused. The pool hands the same audio
        /// sources out repeatedly, so attaching a fresh one per play would add a component every time a sound
        /// started and never remove it.
        /// </summary>
        public static ZoundSapVoiceGenerator EnsureGenerator(AudioSource carrier) {
            var generator = carrier.GetComponent<ZoundSapVoiceGenerator>();
            if (generator == null) generator = carrier.gameObject.AddComponent<ZoundSapVoiceGenerator>();
            return generator;
        }
    }
}
