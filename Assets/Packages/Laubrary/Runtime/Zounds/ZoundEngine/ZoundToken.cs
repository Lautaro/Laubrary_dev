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
        internal List<AudioSource> audioSources => m_handler != null ? m_handler.GetAudioSources() : new List<AudioSource>();
        public float duration => m_handler != null ? m_handler.totalDuration : 0f;
        public float time => m_handler != null ? m_handler.currentTime : 0f;
        public bool isDelayFinished => m_handler != null && m_handler.isDelayFinished;
        public bool isChildZound => m_isChildZound;
        public int playedEntryIndex => m_handler != null ? m_handler.playedEntryIndex : -1;

        /// <summary>True while a DSP voice of this token is still rendering (source or tail).</summary>
        public bool isAudioLive => m_liveVoices > 0;

        internal float parentVolume { set { if (m_handler != null) m_handler.parentVolume = value; } }

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
        internal bool isRealtime => m_handler != null && m_handler.isRealtime;

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
            if (m_empty) return false;   // a name no Zound has: already reported when it was asked for
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
                        "No ZPOC '" + zpocId + "' in '" + zn + "' or anything it plays. The value was ignored.", zpocId);
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
            BeginRun(audioSource, zoundArgs);
        }

        // ─────────────── runs: a token can play again (T-0497) ───────────────

        /// <summary>The play arguments of the latest run, used again when the token is played again.</summary>
        private ZoundArgs m_args;
        private bool m_empty;

        /// <summary>How many runs this token has started. Nought for a token whose play did not happen (chance, cooldown).</summary>
        public int runCount { get; private set; }

        /// <summary>True for the empty token <see cref="ZoundEngine.PlayToken(string)"/> returns for a name no Zound has:
        /// every call on it does nothing (the missing name has already been reported).</summary>
        public bool isEmpty => m_empty;

        /// <summary>Whether this token has ever actually played (false when chance or cooldown said no, or it is empty).</summary>
        public bool wasPlayed => runCount > 0;

        /// <summary>A run is under way (playing, paused mid-run, or fading out).</summary>
        public bool isRunning => m_handler != null && m_state != State.Killed;

        /// <summary>A token that holds a Zound but no run: the play did not happen (chance, cooldown), or <paramref name="empty"/>
        /// for a name no Zound has. Its settings (ZPOC values) can be set, and playing it tries again.</summary>
        internal ZoundToken(Zound zound, ZoundArgs zoundArgs, bool empty) {
            m_zound = zound;
            m_args = zoundArgs;
            m_empty = empty;
            m_state = State.Killed;
            m_isChildZound = zoundArgs.isChild;
        }

        /// <summary>
        /// Starts a new run of this token's Zound on <paramref name="audioSource"/>: a fresh handler (and voice), so every
        /// per-play draw is drawn again, while the token's settings -- its ZPOC values -- carry over (the handler applies them
        /// before its first block). Event subscribers carry over too: they hear every run's end. Called by the constructor and
        /// by <see cref="ZoundEngine"/> when the token is played again.
        /// </summary>
        internal void BeginRun(AudioSource audioSource, ZoundArgs zoundArgs) {
            var zound = m_zound;
            // A top-level play is its own settings root; a track's play follows the root it was handed.
            if (zoundArgs.settingsRoot == null) zoundArgs.settingsRoot = this;
            m_args = zoundArgs;
            m_audioSource = audioSource;
            m_state = State.Paused;
            m_started = false;
            m_pendingKill = false;
            m_liveVoices = 0;
            m_audioEndRaised = false;
            m_pendingAudioEnd = false;
            m_isChildZound = zoundArgs.isChild;
            m_soloOverride = zoundArgs.soloOverride;
            runCount++;

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
            return m_handler == null || m_handler.IsMutedOrExcluded();
        }

        /// <summary>
        /// Plays this token. A token whose run has ended (or whose play did not happen) plays again: a new run of the same
        /// Zound, with fresh per-play draws and the same settings (ZPOC values), chance and cooldown rolled again (T-0497).
        /// An empty token does nothing.
        /// </summary>
        public void Play(float fadeDuration = 0f, System.Action onFadeComplete = null) {
            if (m_empty) return;
            if (m_handler == null || m_state == State.Killed) { ZoundEngine.Replay(this, fadeDuration, onFadeComplete); return; }
            Start(0f, fadeDuration, onFadeComplete);
        }

        /// <summary>Ends the current run at once (with the engine's own click-free stop) and starts the next. Settings carry
        /// over; <paramref name="resetZpoc"/> puts every ZPOC back to its project-wide or resting value first.</summary>
        public void Restart(bool resetZpoc = false) {
            if (m_empty) return;
            if (resetZpoc) ClearAllZpoc();
            if (m_handler != null && m_state != State.Killed) Kill();
            ZoundEngine.Replay(this, 0f, null);
        }

        /// <summary>Plays this token unless a run is already under way: for a looping sound driven every frame.</summary>
        public void EnsurePlaying() {
            if (m_empty || isRunning) return;
            Play();
        }

        /// <summary>The arguments this token's runs are started with.</summary>
        internal ZoundArgs args => m_args;

        // ─────────────── snapshots and glides (T-0498) ───────────────

        /// <summary>The snapshot this token last glided to (a setting: the next run starts on it), or null for Default.</summary>
        internal string currentSnapshot { get; private set; }

        /// <summary>Bumped by every glide, so a glide handle can tell whether it is still the latest.</summary>
        internal int glideSerial { get; private set; }

        /// <summary>
        /// Glides this play -- and everything it plays -- to the snapshot called <paramref name="name"/>, over
        /// <paramref name="milliseconds"/>, starting from wherever it is now (mid-way through another glide included).
        /// "Default" is the settings as authored. Every Zound in the tree with a snapshot of that name glides; names are
        /// matched the way Zound names are. The token keeps it as a setting: its next runs start on that snapshot. Returns
        /// a handle that can glide back to exactly where this glide began. A name no Zound in the tree has is reported once
        /// and does nothing.
        /// </summary>
        public ZoundGlide GlideToSnapshot(string name, float milliseconds) {
            if (m_empty || m_zound == null) return default;
            if (!ZoundSnapshots.AnywhereIn(m_zound, name)) {
                string zn = m_zound.name, key = ZpocKeys.Key(name) ?? "";
                if (!ZoundDiagnostics.Count(ZoundDiagnostics.Kind.MissingSnapshot, zn, key))
                    ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingSnapshot, zn, key,
                        "No snapshot called '" + name + "' in '" + zn + "' or anything it plays. Nothing glided.", name);
                return default;
            }
            float seconds = Mathf.Max(0f, milliseconds) * 0.001f;
            currentSnapshot = ZpocKeys.Key(name) == ZpocKeys.Key(ZoundSnapshots.DefaultName) ? null : name;
            glideSerial++;
            m_handler?.ApplySnapshot(name, seconds);
            return new ZoundGlide(this, glideSerial, runCount, seconds);
        }

        internal void ApplySnapshotInternal(string name, float seconds) {
            currentSnapshot = ZpocKeys.Key(name) == ZpocKeys.Key(ZoundSnapshots.DefaultName) ? null : name;
            m_handler?.ApplySnapshot(name, seconds);
        }

        internal ZoundGlide GlideBack(float seconds) {
            glideSerial++;
            m_handler?.GlideBackTo(seconds);
            return new ZoundGlide(this, glideSerial, runCount, seconds);
        }

        internal void GlideBackInternal(float seconds) { m_handler?.GlideBackTo(seconds); }

        /// <summary>The snapshot this play last glided toward and how far along it is (1 once there). False when it has not glided.</summary>
        public bool TryGetGlideProgress(out string snapshot, out float progress) {
            snapshot = null; progress = 0f;
            return m_handler != null && m_handler.TryGlideProgress(out snapshot, out progress);
        }

        // ─────────────── tracks (T-0497) ───────────────

        /// <summary>One track's settings on a token: kept by the ROOT token, keyed by the authored track, so they last
        /// across runs and apply wherever in the tree that track plays.</summary>
        internal sealed class TrackSettings {
            public float volume = 1f, pitch = 1f, speed = 1f;
            public bool mute, solo, enabled = true;
            public float fadeFrom = 1f, fadeStart, fadeSeconds;
            public float enableFadeFrom = 1f, enableFadeStart = -100f;
            const float EnableFade = 0.03f;

            /// <summary>The volume now, following a fade under way.</summary>
            public float Gain(float now) {
                if (fadeSeconds <= 0f) return volume;
                float t = Mathf.Clamp01((now - fadeStart) / fadeSeconds);
                return Mathf.Lerp(fadeFrom, volume, t);
            }

            /// <summary>1 when enabled, 0 when disabled, with a short fade between so switching never clicks.</summary>
            public float EnableGain(float now) {
                float target = enabled ? 1f : 0f;
                float t = Mathf.Clamp01((now - enableFadeStart) / EnableFade);
                return Mathf.Lerp(enableFadeFrom, target, t);
            }

            public bool IsDefault => volume == 1f && pitch == 1f && speed == 1f && !mute && !solo && enabled && fadeSeconds <= 0f;
        }

        private TrackSettings m_selfTrack;
        private Dictionary<CompositeZound.ZoundEntry, TrackSettings> m_tracks;
        private Dictionary<string, CompositeZound.ZoundEntry[]> m_trackIds;

        /// <summary>The token whose track settings this play follows: itself at the top, the top-level token for a track.</summary>
        internal ZoundToken settingsRoot => m_args.settingsRoot ?? this;

        /// <summary>A track's settings on the root token (<paramref name="entry"/> null: the token's own track, a Klip's 0).</summary>
        internal TrackSettings TrackSettingsFor(CompositeZound.ZoundEntry entry, bool create) {
            var root = settingsRoot;
            if (root != this) return root.TrackSettingsFor(entry, create);
            if (entry == null) {
                if (m_selfTrack == null && create) m_selfTrack = new TrackSettings();
                return m_selfTrack;
            }
            if (m_tracks == null) { if (!create) return null; m_tracks = new Dictionary<CompositeZound.ZoundEntry, TrackSettings>(); }
            if (!m_tracks.TryGetValue(entry, out var t) && create) { t = new TrackSettings(); m_tracks[entry] = t; }
            return t;
        }

        /// <summary>Whether a track has been disabled on this play's root (a random / round-robin / playlist pick skips it).</summary>
        internal bool IsTrackDisabled(CompositeZound.ZoundEntry entry) {
            var t = TrackSettingsFor(entry, create: false);
            return t != null && !t.enabled;
        }

        /// <summary>How many tracks this token's Zound has: 1 for a Klip (itself), the entry count for a Zequence.</summary>
        public int trackCount => m_zound is CompositeZound c ? (c.zoundEntries?.Count ?? 0) : (m_zound != null ? 1 : 0);

        /// <summary>
        /// Track <paramref name="index"/> of this play: for a Klip, 0 is the Klip itself; for a Zequence, its tracks in authored
        /// order. A number that does not exist is reported once and gives a track on which every call does nothing.
        /// </summary>
        public ZoundTrack Track(int index) {
            if (m_empty || m_zound == null) return default;
            if (m_zound is CompositeZound c) {
                if (c.zoundEntries != null && index >= 0 && index < c.zoundEntries.Count)
                    return new ZoundTrack(this, new[] { c.zoundEntries[index] }, false);
            }
            else if (index == 0) return new ZoundTrack(this, null, true);
            string zn = m_zound.name, detail = "#" + index;
            if (!ZoundDiagnostics.Count(ZoundDiagnostics.Kind.MissingTrack, zn, detail))
                ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingTrack, zn, detail,
                    "'" + zn + "' has no track " + index + " (it has " + trackCount + "). The call was ignored.");
            return default;
        }

        /// <summary>
        /// Every track, anywhere in this play's tree, whose ZPOC id matches (ids are matched the way Zound names are). An id no
        /// track has is reported once and gives a track on which every call does nothing.
        /// </summary>
        public ZoundTrack Track(string zpocId) {
            if (m_empty || m_zound == null) return default;
            var key = ZpocKeys.Key(zpocId);
            if (key == null) return default;
            if (m_trackIds == null) m_trackIds = new Dictionary<string, CompositeZound.ZoundEntry[]>();
            if (!m_trackIds.TryGetValue(key, out var found)) {
                var list = new List<CompositeZound.ZoundEntry>();
                CollectTracks(m_zound, key, list, 0);
                found = list.ToArray();
                m_trackIds[key] = found;
            }
            if (found.Length == 0) {
                string zn = m_zound.name;
                if (!ZoundDiagnostics.Count(ZoundDiagnostics.Kind.MissingTrack, zn, key))
                    ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingTrack, zn, key,
                        "No track called '" + zpocId + "' in '" + zn + "' or anything it plays. The call was ignored.", zpocId);
                return default;
            }
            return new ZoundTrack(this, found, false);
        }

        static void CollectTracks(Zound z, string key, List<CompositeZound.ZoundEntry> into, int depth) {
            if (!(z is CompositeZound c) || c.zoundEntries == null || depth > 16) return;
            foreach (var e in c.zoundEntries) {
                if (e == null) continue;
                if (!string.IsNullOrEmpty(e.zpocId) && ZpocKeys.Key(e.zpocId) == key) into.Add(e);
                if (c.TryGetEntryZound(e, out var child)) CollectTracks(child, key, into, depth + 1);
            }
        }

        /// <summary>Pushes changed track settings to what is playing now (volumes and mutes also follow every update).</summary>
        internal void RefreshTracks() { settingsRoot.ApplyOwnTrack(); }

        /// <summary>This play's pitch from game code (1 = as authored): a track's pitch, sent to the playing voice.</summary>
        internal float livePitch {
            get => m_handler != null ? m_handler.livePitch : 1f;
            set { if (m_handler != null) m_handler.livePitch = value; }
        }

        /// <summary>This play's speed from its track settings, on top of <see cref="liveSpeed"/>.</summary>
        internal float trackSpeed {
            get => m_handler != null ? m_handler.trackSpeed : 1f;
            set { if (m_handler != null) m_handler.trackSpeed = value; }
        }

        /// <summary>A Klip's own track settings (its track 0), applied to its own play each update.</summary>
        void ApplyOwnTrack() {
            if (m_handler == null || m_zound is CompositeZound || m_selfTrack == null) return;
            float now = Time.realtimeSinceStartup;
            m_handler.trackVolume = m_selfTrack.Gain(now) * m_selfTrack.EnableGain(now);
            m_handler.livePitch = m_selfTrack.pitch;
            m_handler.trackSpeed = m_selfTrack.speed;
            if (m_audioSource != null) m_audioSource.mute = m_selfTrack.mute;
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
            if (m_handler == null) return;   // no run: nothing to pause (a play that did not happen, or an empty token)
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
            if (m_handler == null) { Play(fadeDuration, onFadeComplete); return; }
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
            if (m_handler == null) return;   // no run to end
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

            if (m_selfTrack != null) ApplyOwnTrack();

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
