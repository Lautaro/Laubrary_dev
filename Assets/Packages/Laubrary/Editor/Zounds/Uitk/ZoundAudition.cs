using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>How the gap between Burst/Loop plays is counted (T-0486).</summary>
    public enum AuditionGap {
        /// <summary>A fixed rhythm, set once when the run starts: the first play's length plus the gap. It does not adapt
        /// to later plays being longer or shorter.</summary>
        Steady,
        /// <summary>The gap counts from when the previous play STARTED, whatever its length; plays may overlap.</summary>
        FromStart,
        /// <summary>The gap counts from when the previous play actually FINISHED.</summary>
        FromEnd
    }

    /// <summary>
    /// The audition helpers of one Zounds editor window (T-0486): the window's own Play, Play on change, Burst and Loop.
    /// Purely an editor workflow aid: it only ever plays the sound, never edits it, and nothing it does is saved with
    /// the sound or the project.
    ///
    /// **Every play a window starts goes through its session, and dies with it.** The owner's hard requirement is that no
    /// sound an editor started, or queued, outlives that editor. So a session is killed on every way a window can go:
    ///
    /// - the window closing, and the disable Unity sends every window before a script reload (the window's OnDisable);
    /// - a global before-script-reload hook that kills EVERY session, so the guarantee does not depend on the order in
    ///   which Unity disables windows;
    /// - the editor quitting, and entering or leaving Play mode (the engine that played the sound is replaced then);
    /// - a sweep on every tick that kills a session whose window no longer exists, for any path not listed above.
    ///
    /// Killing uses the engine's ordinary stop, which silences the audio source and the voice at once; the voice's own
    /// memory is then released through the engine's existing stopped-for-sure sequence (T-0419) when its source is
    /// reused or destroyed. No second teardown path exists here on purpose.
    ///
    /// Measured 2026-09-29 before this was written: a genuine script reload already silences a playing edit-mode voice by
    /// itself and cleanly. What a reload cannot do is stop the NEXT play of a Loop or a burst, because the schedule lives
    /// in managed memory that is rebuilt — which is why the running state below is never serialized: a reload, a restored
    /// layout or a reopened window always comes back silent.
    /// </summary>
    public sealed class ZoundAudition {

        // ───────────────────────── settings (per sound, editor preferences only) ─────────────────────────

        /// <summary>What the Burst/Loop controls are set to. Remembered per sound in the editor's own preferences: it is
        /// workflow state, not data, so it never touches the project and has no undo entry.</summary>
        [Serializable]
        public class Settings {
            public int count = 4;
            public float gap = 0.5f;
            public AuditionGap mode = AuditionGap.FromEnd;
        }

        public const int MaxCount = 32;
        public const float MaxGap = 5f;
        /// <summary>The shortest start-to-start gap From start allows, so a slip to 0 cannot fire a play every frame.</summary>
        public const float MinStartGap = 0.05f;

        static string PrefsKey(int zoundId) => "Laubrary.Zounds.Audition." + Application.dataPath.GetHashCode().ToString("x") + "." + zoundId;

        public static Settings LoadSettings(int zoundId) {
            var s = new Settings();
            try {
                string json = EditorPrefs.GetString(PrefsKey(zoundId), "");
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, s);
            }
            catch { s = new Settings(); }
            s.count = Mathf.Clamp(s.count, 1, MaxCount);
            s.gap = Mathf.Clamp(s.gap, 0f, MaxGap);
            return s;
        }

        public void SaveSettings() {
            try { EditorPrefs.SetString(PrefsKey(zoundId), JsonUtility.ToJson(settings)); } catch { }
        }

        // ───────────────────────── the live sessions ─────────────────────────

        static readonly List<ZoundAudition> sessions = new List<ZoundAudition>();

        [InitializeOnLoadMethod]
        static void HookGlobalKills() {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting -= KillEverything;
            EditorApplication.quitting += KillEverything;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ZoundsWindow.onEditCommitted -= OnEditCommitted;
            ZoundsWindow.onEditCommitted += OnEditCommitted;
        }

        /// <summary>How many sessions still had something playing or queued when the last script reload began, and so were
        /// stopped by the hook rather than by their window. Kept across the reload for the kept check to read (it proves
        /// the hook ran, which a silent result alone cannot: a reload also silences voices by itself).</summary>
        public const string ReloadKilledKey = "Laubrary.Zounds.Audition.killedAtLastReload";

        static void OnBeforeReload() {
            int withPlays = 0;
            foreach (var s in sessions) if (s.AnyLive) withPlays++;
            KillEverything();
            SessionState.SetInt(ReloadKilledKey, withPlays);
        }

        static void OnPlayModeChanged(PlayModeStateChange s) {
            if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.ExitingPlayMode) KillEverything();
        }

        /// <summary>Stops every play and every queued play of every editor window. The reload/quit/Play-mode path.</summary>
        public static void KillEverything() {
            for (int i = sessions.Count - 1; i >= 0; i--) sessions[i].StopAll();
        }

        /// <summary>How many plays started by any editor window are still sounding, plus how many runs are still queued.
        /// Zero after any of the kill paths above; the kept check reads it.</summary>
        public static int LiveCount {
            get {
                int n = 0;
                foreach (var s in sessions) { s.Prune(); n += s.tokens.Count + (s.running != Run.None ? 1 : 0); }
                return n;
            }
        }

        /// <summary>Every audio source currently carrying a play started through a session (the kept check verifies they
        /// have really stopped, not only that the tokens say so).</summary>
        public static void CollectSources(List<AudioSource> into) {
            foreach (var s in sessions) foreach (var t in s.tokens) CollectSources(t, into);
        }

        static void CollectSources(ZoundToken t, List<AudioSource> into) {
            if (t == null) return;
            try { var list = t.audioSources; if (list != null) foreach (var a in list) if (a != null && !into.Contains(a)) into.Add(a); }
            catch { if (t.audioSource != null && !into.Contains(t.audioSource)) into.Add(t.audioSource); }
        }

        // ───────────────────────── one window's session ─────────────────────────

        enum Run { None, Burst, Loop }

        readonly Func<bool> ownerAlive;
        readonly Func<ZoundToken> play;
        readonly Func<bool> neverEnds;
        readonly int zoundId;
        readonly List<ZoundToken> tokens = new List<ZoundToken>();
        readonly Dictionary<object, ZoundToken> previews = new Dictionary<object, ZoundToken>();
        readonly Dictionary<object, List<ZoundToken>> previewPlays = new Dictionary<object, List<ZoundToken>>();
        readonly Dictionary<object, int> previewVersions = new Dictionary<object, int>();
        int generation;
        public readonly Settings settings;

        /// <summary>Play on change: on while the window says so. The window keeps the flag (it survives a reload, not a close).</summary>
        public bool playOnChange;

        Run running = Run.None;
        int remaining;                 // Burst: plays still to start
        double runStart, period;       // Steady: when the run began and its fixed rhythm
        int started;                   // plays started in this run
        ZoundToken lastPlay;
        double lastStartAt, plannedStartAt, lastEndAt = -1d, nextAt = -1d;

        // Play on change: a commit is waited on until the pointer is released and the commits have settled.
        bool changePending;
        double lastCommitAt, lastInteractionAt = -1e9;
        bool pointerHeld;
        const double Settle = 0.12d;          // commits closer together than this play once
        const double InteractionWindow = 3d;  // a commit counts as this window's if it came this soon after touching it

        /// <summary>Called on every visible state change (a play starting or ending, a run starting or stopping).</summary>
        public Action changed;

        /// <param name="ownerAlive">False once the window is gone; the session then kills itself on its next tick.</param>
        /// <param name="play">Starts one play of the sound, exactly as the window's Play does, and returns its token.</param>
        /// <param name="neverEnds">True while the sound never ends by itself (a Looper): Burst and Loop are then unavailable.</param>
        public ZoundAudition(int zoundId, Func<bool> ownerAlive, Func<ZoundToken> play, Func<bool> neverEnds) {
            this.zoundId = zoundId; this.ownerAlive = ownerAlive; this.play = play; this.neverEnds = neverEnds;
            settings = LoadSettings(zoundId);
            sessions.Add(this);
            EditorApplication.update += Tick;
        }

        /// <summary>Stops everything and unhooks. The window calls this from OnDisable (close and before every reload).</summary>
        public void Dispose() {
            if (IsDisposed) return;
            StopAll();
            EditorApplication.update -= Tick;
            sessions.Remove(this);
        }

        public bool IsDisposed => !sessions.Contains(this);

        /// <summary>Anything this window started still sounding, or a run still going.</summary>
        public bool AnyLive { get { Prune(); return tokens.Count > 0 || running != Run.None; } }
        public bool BurstRunning => running == Run.Burst;
        public bool LoopRunning => running == Run.Loop;
        public int BurstDone => started;
        public bool NeverEnds => neverEnds != null && neverEnds();

        /// <summary>
        /// Tracks the pointer and keyboard in the window, so Play on change can tell a committed edit made HERE from one
        /// made in another window, and waits until a press is released. Re-attach after every rebuild of the window.
        /// </summary>
        public void Attach(VisualElement root) {
            root.RegisterCallback<PointerDownEvent>(_ => { pointerHeld = true; lastInteractionAt = Now; }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(_ => { pointerHeld = false; lastInteractionAt = Now; }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCaptureOutEvent>(_ => pointerHeld = false, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(_ => lastInteractionAt = Now, TrickleDown.TrickleDown);
        }

        static double Now => EditorApplication.timeSinceStartup;

        // ───────────────────────── actions ─────────────────────────

        /// <summary>The window's own Play: one play, added to what is already sounding.</summary>
        public ZoundToken PlayOnce() {
            return PlayPreview(this, StartPlay);
        }

        /// <summary>Stops every play this window started and any run or queued play. The window's Stop.</summary>
        public void StopAll() {
            generation++;
            running = Run.None;
            nextAt = -1d;
            changePending = false;
            for (int i = tokens.Count - 1; i >= 0; i--) {
                var t = tokens[i];
                try { if (t != null && t.state != ZoundToken.State.Killed) t.Kill(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            tokens.Clear();
            previews.Clear();
            previewPlays.Clear();
            previewVersions.Clear();
            lastPlay = null;
            changed?.Invoke();
        }

        public bool PlayControlStops => IsLoopPlaying(this) || running != Run.None;

        public bool IsLoopPlaying(object control) {
            return control != null && previews.TryGetValue(control, out var token) && token != null
                && token.state != ZoundToken.State.Killed && token.state != ZoundToken.State.FadeToKill
                && float.IsPositiveInfinity(token.duration);
        }

        public ZoundToken PlayPreview(object control, Func<ZoundToken> start, bool toggle = true) {
            if (IsDisposed) return null;
            if (toggle && IsLoopPlaying(control)) { StopPreview(control); return null; }
            var token = start?.Invoke();
            TrackPreview(control, token);
            changed?.Invoke();
            return token;
        }

        internal Func<bool> PreviewAlive(object control) {
            int runGeneration = generation;
            previewVersions.TryGetValue(control, out int version);
            return () => !IsDisposed && generation == runGeneration && (ownerAlive == null || ownerAlive())
                && (!previewVersions.TryGetValue(control, out int current) ? version == 0 : current == version);
        }

        internal void TrackPreview(object control, ZoundToken token) {
            if (token == null) return;
            if (!tokens.Contains(token)) tokens.Add(token);
            if (!previewPlays.TryGetValue(control, out var plays)) previewPlays[control] = plays = new List<ZoundToken>();
            if (!plays.Contains(token)) plays.Add(token);
            if (!token.isChildZound) previews[control] = token;
        }

        public void StopPreview(object control) {
            previewVersions.TryGetValue(control, out int version);
            previewVersions[control] = version + 1;
            if (previewPlays.TryGetValue(control, out var plays)) {
                foreach (var token in plays) if (token != null && token.state != ZoundToken.State.Killed) token.Kill();
                previewPlays.Remove(control);
            }
            previews.Remove(control);
            Prune();
            changed?.Invoke();
        }

        public void StartBurst() => StartRun(Run.Burst);
        public void StartLoop() => StartRun(Run.Loop);

        /// <summary>Ends a Burst or Loop: stops what it is playing and cancels what it had queued.</summary>
        public void StopRun() => StopAll();

        void StartRun(Run kind) {
            if (NeverEnds) return;   // a Looper never ends by itself; see the popup's tooltip
            StopAll();
            running = kind;
            remaining = kind == Run.Burst ? Mathf.Clamp(settings.count, 1, MaxCount) : int.MaxValue;
            started = 0;
            period = -1d;
            runStart = Now;
            FireRunPlay();
        }

        ZoundToken StartPlay() {
            ZoundToken t = null;
            try { t = play?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            if (t != null && !tokens.Contains(t)) tokens.Add(t);
            return t;
        }

        void FireRunPlay() {
            // From start counts from when the previous play was PLANNED to start, not when the editor got round to it, so
            // an editor hitch delays one play instead of pushing every later one (measured: 60 ms of drift otherwise).
            plannedStartAt = nextAt >= 0d ? nextAt : Now;
            var t = StartPlay();
            started++;
            if (remaining != int.MaxValue) remaining--;
            lastPlay = t;
            lastStartAt = Now;
            lastEndAt = -1d;
            // Steady's rhythm is set by the first play's REAL length, measured when it is seen to end (in Tick). It used to be
            // the play's declared length, but a sound whose chain stretches time declares a huge upper bound instead (measured
            // 2026-09-30: 35,575 s for a 0.92 s Klip), so the second play was scheduled hours later and Steady seemed to do
            // nothing (T-0506).
            nextAt = -1d;
            if (running == Run.Burst && remaining <= 0) running = Run.None;   // the last play just started; let it finish
            else ScheduleNext();
            changed?.Invoke();
        }

        void ScheduleNext() {
            switch (settings.mode) {
                case AuditionGap.Steady: nextAt = period < 0d ? -1d : runStart + started * period; break;   // unset until the first play ends
                case AuditionGap.FromStart:
                    nextAt = plannedStartAt + Math.Max(MinStartGap, settings.gap);
                    if (nextAt < Now) nextAt = Now;   // after a long stall: one play now, not a pile of catch-ups
                    break;
                case AuditionGap.FromEnd: nextAt = -1d; break;   // decided when the previous play is seen to finish
            }
        }

        static void OnEditCommitted() {
            double now = Now;
            foreach (var s in sessions) {
                if (!s.playOnChange || s.running != Run.None) continue;   // a run already plays every edit live
                if (now - s.lastInteractionAt > InteractionWindow) continue;   // not an edit made in this window
                s.changePending = true;
                s.lastCommitAt = now;
            }
        }

        /// <summary>Drops tokens that have ended.</summary>
        void Prune() {
            for (int i = tokens.Count - 1; i >= 0; i--) {
                var t = tokens[i];
                if (t == null || t.state == ZoundToken.State.Killed) tokens.RemoveAt(i);
            }
        }

        void Tick() {
            if (ownerAlive != null && !ownerAlive()) { Dispose(); return; }
            int before = tokens.Count;
            Prune();
            bool stateChanged = tokens.Count != before;
            double now = Now;

            if (changePending && !pointerHeld && now - lastCommitAt >= Settle) {
                changePending = false;
                if (running == Run.None) {
                    // Restart: the point of Play on change is hearing the sound as it now is, from the top.
                    StopAll();
                    StartPlay();
                    stateChanged = true;
                }
            }

            if (running != Run.None) {
                if (settings.mode == AuditionGap.FromEnd && nextAt < 0d) {
                    bool ended = lastPlay == null || lastPlay.state == ZoundToken.State.Killed;
                    if (ended) { lastEndAt = now; nextAt = now + settings.gap; }
                }
                if (settings.mode == AuditionGap.Steady && period < 0d && nextAt < 0d) {
                    // The first play has just ended: its real length plus the gap is the rhythm from here on.
                    bool ended = lastPlay == null || lastPlay.state == ZoundToken.State.Killed;
                    if (ended) {
                        lastEndAt = now;
                        period = Math.Max(MinStartGap, (now - runStart) + settings.gap);
                        nextAt = runStart + started * period;
                    }
                }
                if (nextAt >= 0d && now >= nextAt) FireRunPlay();
                // Keep the editor ticking while something is queued, so a gap is not stretched by an idle editor.
                EditorApplication.QueuePlayerLoopUpdate();
            }
            if (stateChanged) changed?.Invoke();
        }

        // ───────────────────────── helpers for the windows ─────────────────────────

        /// <summary>True when a sound never ends by itself: a Looper, or a Zequence with a Looper anywhere inside it.</summary>
        public static bool ContainsLooper(Zound z, int depth = 0) => ContainsLooper(z, new HashSet<Zound>());

        static bool ContainsLooper(Zound z, HashSet<Zound> path) {
            if (z == null || !path.Add(z)) return false;
            try {
                if (z is Klip k) return k.IsLooper;
                if (z is CompositeZound c) {
                    foreach (var e in c.zoundEntries)
                        if (c.TryGetEntryZound(e, out var inner) && ContainsLooper(inner, path)) return true;
                }
                return false;
            }
            finally { path.Remove(z); }
        }
    }
}
