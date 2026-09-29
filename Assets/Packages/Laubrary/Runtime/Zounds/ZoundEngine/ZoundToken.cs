using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    public class ZoundToken {

        public event System.Action onFrameUpdate;

        /// <summary>
        /// Zound End: the source material is exhausted (duration reached; the last repeat, if repeating).
        /// Fires where onComplete always fired, on the same condition, from the same call site.
        /// </summary>
        public event System.Action onZoundEnd;

        /// <summary>Kept for one version as a forwarder; subscribe to onZoundEnd.</summary>
        [System.Obsolete("Renamed to onZoundEnd (same event, same timing). Audio End is the separate onAudioEnd.")]
        public event System.Action onComplete { add { onZoundEnd += value; } remove { onZoundEnd -= value; } }

        /// <summary>
        /// Audio End (native pipeline only): the rendered output of this token's voice, including any
        /// effect tail (reverb, delay), has decayed below the silence threshold or hit the chain's tail
        /// budget. Never fires before Zound End. For tokens without a DSP voice it fires together with
        /// Zound End (m_liveVoices stays 0 for the managed pipeline, so this is immediate and harmless).
        /// </summary>
        public event System.Action onAudioEnd;

        private bool m_audioEndRaised;
        private int m_liveVoices;

        internal void NotifyVoiceStarted() { m_liveVoices++; }

        /// <summary>A parent (Zequence) token's Audio End waits for every child token's Audio End.</summary>
        internal void AttachChildAudioEnd(ZoundToken child) {
            if (child == null) return;
            m_liveVoices++;
            child.onAudioEnd += RaiseAudioEnd;
        }

        /// <summary>Which DSP group node (if any) this token's audio sums into; -1 when it goes to the bus.</summary>
        internal int dspParentGroup { get => m_handler != null ? m_handler.dspParentGroup : -1; set { if (m_handler != null) m_handler.dspParentGroup = value; } }

        /// <summary>Called by the engine's event drain when a voice owned by this token reports Audio End.</summary>
        internal void RaiseAudioEnd() {
            if (m_liveVoices > 0) m_liveVoices--;
            if (m_liveVoices > 0 || m_audioEndRaised) return;
            if (m_state != State.Killed) { m_pendingAudioEnd = true; return; } // Zound End first, always
            m_audioEndRaised = true;
            onAudioEnd?.Invoke();
        }

        private bool m_pendingAudioEnd;

        private void RaiseZoundEnd() {
            onZoundEnd?.Invoke();
            if (m_liveVoices == 0 || m_pendingAudioEnd) {
                if (!m_audioEndRaised) { m_audioEndRaised = true; onAudioEnd?.Invoke(); }
            }
        }

        public enum State {
            Playing, Paused, Killed, FadeToKill
        }

        private Zound m_zound;
        private IZoundHandler m_handler;
        private AudioSource m_audioSource;
        private bool m_started;
        private State m_state = State.Paused;
        private double m_lastDspTime;
        private bool m_isChildZound;
        private bool m_pendingKill;

        private CompositeZound.ZoundEntry m_soloOverride;

        public Zound zound => m_zound;
        public State state => m_state;
        public AudioSource audioSource => m_audioSource;
        internal List<AudioSource> audioSources => m_handler.GetAudioSources();
        public float duration => m_handler.totalDuration;
        public float time => m_handler.currentTime;
        public bool isDelayFinished => m_handler.isDelayFinished;
        public bool isChildZound => m_isChildZound;
        public int playedEntryIndex => m_handler.playedEntryIndex;

        /// <summary>True while a DSP voice of this token is still rendering (source or tail).</summary>
        public bool isAudioLive => m_liveVoices > 0;

        internal float parentVolume { set => m_handler.parentVolume = value; }

        /// A live volume multiplier any holder of the token may drive every frame (a looping engine note faded
        /// by speed and throttle). The handler rewrites AudioSource.volume every tick from a value fixed at start,
        /// so writing the source directly was stomped; this is one extra factor in that same write. Defaults to 1
        /// (every existing sound unchanged). Managed pipeline only for now. From OutBurner, 2026-09-26.
        public float liveVolume {
            get => m_handler != null ? m_handler.liveVolume : 1f;
            set { if (m_handler != null) m_handler.liveVolume = value; }
        }
        /// <summary>
        /// This play's speed, changeable while it plays, without changing its pitch (T-0409): 0.5 plays it at half speed.
        /// Multiplied with the sound's own speed and <see cref="ZoundEngine.globalSpeed"/>. Only heard on a sound with live
        /// speed switched on (a Klip playing through its chain); any other sound ignores it.
        /// </summary>
        public float liveSpeed {
            get => m_handler != null ? m_handler.liveSpeed : 1f;
            set { if (m_handler != null) m_handler.liveSpeed = value; }
        }
        internal bool isRealtime => m_handler.isRealtime;

        // ─────────────── ZPOC: programmatic control (T-0496) ───────────────

        /// <summary>The Zequence token this one plays a track of, whose ZPOC values it follows; null for a top-level play.</summary>
        internal ZoundToken parentToken;

        /// <summary>This token's own ZPOC values by matching key. Created on the first set; a token nobody drives has none.</summary>
        private Dictionary<string, float> m_zpoc;

        /// <summary>
        /// Sets a ZPOC value (0..1, clamped) on this play and everything it plays: every modifier or track, anywhere in this
        /// Zound's tree, whose ZPOC id matches (ids are matched the way Zound names are, and only have to be unique within
        /// the Zound that declares them — so sibling Klips sharing an id are all reached, on purpose). The sound eases to
        /// the value rather than jumping. A part not sounding right now (a track still waiting, or one a random Zequence
        /// did not pick) keeps the value for when it starts.
        ///
        /// It is just a value on this token: it never changes the saved Zound, and it is safe to set whatever the play is
        /// doing. An id that nothing in this Zound's tree declares is reported once and ignored, never thrown; returns false
        /// then. Setting the same id every frame allocates nothing.
        /// </summary>
        public bool SetZpoc(string zpocId, float value) {
            var key = ZpocKeys.Key(zpocId);
            if (key == null) {
                ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingZpoc, m_zound != null ? m_zound.name : "", "",
                    "A ZPOC value was sent with an empty id" + (m_zound != null ? " to '" + m_zound.name + "'" : "") + ". Nothing was changed.");
                return false;
            }
            value = value < 0f ? 0f : value > 1f ? 1f : value;
            if (m_zpoc != null && m_zpoc.TryGetValue(key, out float old)) {
                if (old == value) return true;
                m_zpoc[key] = value;
                RefreshZpoc(key);
                return true;
            }
            if (!ZpocIndex.Declares(m_zound, key)) {
                string zn = m_zound != null ? m_zound.name : "";
                if (!ZoundDiagnostics.Count(ZoundDiagnostics.Kind.MissingZpoc, zn, key))
                    ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingZpoc, zn, key,
                        "No ZPOC '" + zpocId + "' in '" + zn + "' or anything it plays. The value was ignored.");
                return false;
            }
            if (m_zpoc == null) m_zpoc = new Dictionary<string, float>();
            m_zpoc[key] = value;
            RefreshZpoc(key);
            return true;
        }

        /// <summary>Removes this token's own value for an id, so it follows a project-wide value again, or its resting value.</summary>
        public void ClearZpoc(string zpocId) {
            var key = ZpocKeys.Key(zpocId);
            if (key != null && m_zpoc != null && m_zpoc.Remove(key)) RefreshZpoc(key);
        }

        /// <summary>Removes every value this token has set: the whole play goes back to project-wide or resting values.</summary>
        public void ClearAllZpoc() {
            if (m_zpoc == null || m_zpoc.Count == 0) return;
            m_zpoc.Clear();
            RefreshAllZpoc();
        }

        /// <summary>The value an id resolves to for this play — its own, a parent Zequence's, or the project-wide one — or
        /// false when nothing has set it (the ZPOC is at rest). This is the value sent, not the eased one being heard.</summary>
        public bool TryGetZpoc(string zpocId, out float value) => TryResolveZpoc(ZpocKeys.Key(zpocId), out value);

        internal bool TryResolveZpoc(string key, out float value) {
            if (key != null) {
                if (m_zpoc != null && m_zpoc.TryGetValue(key, out value)) return true;
                if (parentToken != null && parentToken.TryResolveZpoc(key, out value)) return true;
                if (ZpocGlobals.TryGet(key, out value)) return true;
            }
            value = 0f;
            return false;
        }

        /// <summary>Where a ZPOC value comes from, in priority order (highest first). Shown in the editor so a value code
        /// has moved is never a mystery.</summary>
        public enum ZpocSource { Play = 0, Parent = 1, Global = 2, Rest = 3 }

        /// <summary>Which source wins for an id on this play right now (see <see cref="ZpocSource"/>).</summary>
        public ZpocSource SourceOfZpoc(string zpocId) {
            var key = ZpocKeys.Key(zpocId);
            if (key != null) {
                if (m_zpoc != null && m_zpoc.ContainsKey(key)) return ZpocSource.Play;
                if (parentToken != null && parentToken.TryResolveZpoc(key, out _)) return ZpocSource.Parent;
                if (ZpocGlobals.TryGet(key, out _)) return ZpocSource.Global;
            }
            return ZpocSource.Rest;
        }

        internal void RefreshZpoc(string key) { m_handler?.ApplyZpoc(key); }
        internal void RefreshAllZpoc() { m_handler?.ApplyAllZpoc(); }

        internal CompositeZound.ZoundEntry soloOverride => m_soloOverride;

        internal bool TryGetEntryToken(CompositeZound.ZoundEntry entry, out ZoundToken token) {
            if (m_handler is ZequenceHandler zeqHandler) {
                token = zeqHandler.GetEntryToken(entry);
            }
            else {
                token = null;
            }
            return token != null;
        }

        internal bool IsEntryMuted(CompositeZound.ZoundEntry entry) {
            if (m_handler is ZequenceHandler zeqHandler) {
                return zeqHandler.IsEntryMuted(entry);
            }
            return true;
        }

        public ZoundToken(Zound zound, AudioSource audioSource, ZoundArgs zoundArgs) {
            m_zound = zound;
            m_audioSource = audioSource;
            m_state = State.Paused;
            m_isChildZound = zoundArgs.isChild;
            m_soloOverride = zoundArgs.soloOverride;

            if (zound is Klip klip) {
                m_handler = new KlipHandler(klip, audioSource, zoundArgs);
            }
            else if (zound is Zequence zequence) {
                m_handler = new ZequenceHandler(zequence, audioSource, zoundArgs);
            }
            else if (zound is ClipZound clipZound) {
                m_handler = new ClipZoundHandler(clipZound, audioSource, zoundArgs);
            }
            else {
                Debug.LogError("Invalid Zound type: " + zound.GetType()); // actually impossible, but need to fill "else"
                m_handler = null;
            }

            if (m_handler != null) {
                m_handler.SetToken(this);
                m_handler.Init();
                if (!m_isChildZound) {
                    m_handler.onPlayStarted = () => ZoundEngine.NotifyZoundStartedPlaying(m_zound, this);
                }
                if (!isChildZound) {
                    ApplyMixerGroupToChildren(audioSource.outputAudioMixerGroup);
                }
            }
        }

        internal void ApplyMixerGroupToChildren(AudioMixerGroup mixerGroup) {
            m_handler?.ApplyMixerGroupToChildren(mixerGroup);
        }

        internal void Start(float timeOffset = 0f, float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (m_state == State.Killed || m_state == State.FadeToKill) {
                Debug.LogError("Invalid token to start: The token has been killed.");
                return;
            }
            m_started = true;
            m_lastDspTime = AudioSettings.dspTime;
            m_state = State.Playing;
            m_handler.OnStart(timeOffset, fadeDuration, onFadeComplete);

            m_pendingKill = false;
            // Begin at once when nothing is left to wait for, instead of on the engine's next update.
            // A zero update runs the same path: delay, cooldown, OnPlayReady (which starts Zequence children), volume.
            var result = m_handler.OnUpdate(0f);
            if (result == ZoundUpdateResult.Kill) {
                // Reported on the next engine update, as before, so a caller subscribing to onComplete after PlayZound still hears it.
                m_pendingKill = true;
            }
            else if (result == ZoundUpdateResult.Pause) {
                m_state = State.Paused;
            }
        }

        public bool IsMutedOrExcluded() {
            return m_handler.IsMutedOrExcluded();
        }

        public void Play(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            Start(0f, fadeDuration, onFadeComplete);
        }

        public Task PlayAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Play();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Play(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        public void Pause(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (fadeDuration > Mathf.Epsilon) {
                if (m_state == State.Killed || m_state == State.FadeToKill) {
                    Debug.LogError("Invalid token to pause: The token has been killed.");
                    return;
                }
                if (m_state == State.Paused) return;
                m_state = State.Playing;
                m_handler.OnFadeAndPause(fadeDuration, onFadeComplete);
            }
            else {
                if (m_state == State.Killed || m_state == State.FadeToKill) {
                    Debug.LogError("Invalid token to pause: The token has been killed.");
                    return;
                }
                if (m_state == State.Paused) return;
                m_state = State.Paused;
                m_handler.OnPause();
            }
        }

        public Task PauseAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Pause();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Pause(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        public void Unpause(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (!m_started) {
                Play(fadeDuration, onFadeComplete);
                return;
            }
            if (m_state == State.Killed || m_state == State.FadeToKill) {
                Debug.LogError("Invalid token to resume: The token has been killed.");
                return;
            }
            if (m_state == State.Playing) return;
            m_lastDspTime = AudioSettings.dspTime;
            m_state = State.Playing;
            m_handler.OnResume(fadeDuration, onFadeComplete);
        }

        public Task UnpauseAsync(float fadeDuration) {
            if (m_started) {
                if (fadeDuration <= Mathf.Epsilon) {
                    Unpause();
                    return Task.CompletedTask;
                }
                var tcs = new TaskCompletionSource<bool>();
                Unpause(fadeDuration, () => {
                    tcs.SetResult(true);
                });
                return tcs.Task;
            }
            else {
                return PlayAsync(fadeDuration);
            }
        }

        public void Kill(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (fadeDuration > Mathf.Epsilon) {
                if (m_state == State.Killed) return;
                m_state = State.FadeToKill;
                m_handler.OnFadeAndKill(fadeDuration, onFadeComplete);
            }
            else {
                if (m_state == State.Killed) return;
                m_state = State.Killed;
                m_handler.OnKill();
                // Nothing left rendering: Audio End is final now (voices still ringing report it via the drain).
                if (m_liveVoices == 0 && !m_audioEndRaised) { m_audioEndRaised = true; onAudioEnd?.Invoke(); }
            }
        }

        public Task KillAsync(float fadeDuration) {
            if (fadeDuration <= Mathf.Epsilon) {
                Kill();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            Kill(fadeDuration, () => {
                tcs.SetResult(true);
            });
            return tcs.Task;
        }

        internal void OnUpdate() {
            double currentDspTime = AudioSettings.dspTime;
            float deltaDspTime = (float)(currentDspTime - m_lastDspTime);
            m_lastDspTime = currentDspTime;
            // A source destroyed underneath a live token (scene teardown, manual delete) ends the token
            // instead of throwing inside the engine's update loop, in every mode, not only the editor.
            if (audioSource == null) {
                m_state = State.Killed;
                if (m_liveVoices == 0 && !m_audioEndRaised) { m_audioEndRaised = true; onAudioEnd?.Invoke(); }
                return;
            }

            if (m_pendingKill) {
                m_pendingKill = false;
                if (m_state != State.Killed) {
                    m_state = State.Killed;
                    RaiseZoundEnd();
                }
                return;
            }

            if (m_state == State.Playing || m_state == State.FadeToKill) {
                ZoundUpdateResult nextTreatment = m_handler.OnUpdate(deltaDspTime);
                if (nextTreatment == ZoundUpdateResult.Kill) {
                    m_state = State.Killed;
                    RaiseZoundEnd();
                }
                else if (nextTreatment == ZoundUpdateResult.Pause) {
                    m_state = State.Paused;
                    onFrameUpdate?.Invoke();
                }
                else {
                    onFrameUpdate?.Invoke();
                }
            }
            else {
                onFrameUpdate?.Invoke();
            }

        }

    }

}
