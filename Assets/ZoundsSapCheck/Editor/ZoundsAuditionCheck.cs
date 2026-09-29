// Kept check for the editors' audition helpers (T-0486): Burst timing in each gap mode, Play on change, and — the hard
// requirement — that every way an editor can go away leaves nothing it started still sounding or still queued.
//
// It plays a sound built in memory for the purpose (never added to the library; its source audio is borrowed read-only
// from the library's first Klip, or any clip in the Zounds sources folder) with the editor's audio muted for the
// duration, so running it neither changes the owner's project nor makes a noise.
//
// It runs over real editor time, because every question it asks is about time: whether plays start when they should,
// and whether a stop leaves anything that starts again later. Step 2 performs a real script reload and reads the answer
// back on the other side of it, because a reload is the one teardown that cannot be simulated in-process.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Uitk;

public static class ZoundsAuditionCheck {

    public static string ReportPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zounds-audition-check.txt");
    const string ReloadFlag = "ZoundsAuditionCheck.awaitingReload";
    const string ReloadSources = "ZoundsAuditionCheck.sourcesBefore";

    [MenuItem("Laubrary/Zounds/Checks/18 - Step 1, audition timing and every stop path")]
    public static void RunFromMenu() { RunLater(); Debug.Log("[ZoundsAuditionCheck] running for ~20 s, report follows in the console and at " + ReportPath); }

    [MenuItem("Laubrary/Zounds/Checks/18 - Step 2, audition across a real script reload")]
    public static void ReloadFromMenu() { StartReloadTest(); }

    // ───────────────────────── plumbing ─────────────────────────

    static double Now => EditorApplication.timeSinceStartup;

    static Klip MakeSound(out string why) {
        why = null;
        var k = new Klip(-48601) { name = "(audition check)" };
        k.effectChain = new ZoundEffectChain();
        var lib = ZoundsProject.Instance != null ? ZoundsProject.Instance.zoundLibrary : null;
        if (lib != null && lib.klips.Count > 0 && lib.klips[0].audioClipRef != null && lib.klips[0].audioClipRef.RuntimeKeyIsValid()) {
            // The library Klip's own reference object, only read: a fresh reference built from the same asset id does not
            // resolve the same way in the editor (measured: no audio), while sharing it is harmless because playing never
            // writes to it.
            k.audioClipRef = lib.klips[0].audioClipRef;
            k.audioClipPath = lib.klips[0].audioClipPath;
        }
        else {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/ZoundsData/Sources" })) {
                k.audioClipRef = new UnityEngine.AddressableAssets.AssetReference(guid);
                k.audioClipPath = AssetDatabase.GUIDToAssetPath(guid);
                break;
            }
        }
        if (k.audioClipRef == null || !(k.audioClipRef.editorAsset is AudioClip)) { why = "no source audio found to borrow"; return null; }
        // A short play, so the timing questions are answered in a few seconds.
        k.trimEnabled = true; k.trimStart = 0f; k.trimEnd = 0.4f;
        return k;
    }

    static ZoundToken PlayOnce(Klip k, List<(double at, ZoundToken t)> log) {
        // Stamped when the play is REQUESTED: that is what the scheduler controls. The engine's own setup of a play is
        // separate (measured ~45 ms for the first play of a sound, while its audio is decoded) and is not the question here.
        double requested = Now;
        var t = ZoundEngine.PlayZound(k, new ZoundArgs { startImmediately = true, volumeOverride = 1f, pitchOverride = 1f,
                                                         chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true });
        log?.Add((requested, t));
        return t;
    }

    /// <summary>Audio sources the given plays used that are still playing.</summary>
    static int StillSounding(List<(double at, ZoundToken t)> log) {
        int n = 0;
        foreach (var (_, t) in log) if (t != null && t.audioSource != null && t.audioSource.isPlaying) n++;
        return n;
    }

    static void SetPrivate(object o, string field, object value) =>
        o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

    static void RaiseCommit() =>
        typeof(ZoundAudition).GetMethod("OnEditCommitted", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

    // ───────────────────────── step 1: a sequence of timed phases ─────────────────────────

    class Phase {
        public string name;
        public Action start;             // sets the phase up
        public Func<double, bool> done;  // seconds since start -> finished?
        public Action<StringBuilder> judge;
    }

    public static string RunLater() {
        System.IO.File.WriteAllText(ReportPath, "running\n");
        var sb = new StringBuilder("=== AUDITION CHECK (T-0486) ===\n");
        bool wasMuted = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = true;
        var k = MakeSound(out string why);
        if (k == null) { Finish(sb.Append("  cannot run: ").Append(why).Append('\n'), wasMuted, false); return ReportPath; }

        int failures = 0;
        void Check(string what, bool ok, string detail = "") {
            if (!ok) failures++;
            sb.Append("  ").Append(ok ? "PASS  " : "FAIL  ").Append(what).Append(detail.Length > 0 ? "  (" + detail + ")" : "").Append('\n');
        }

        bool alive = true;
        bool looper = false;
        var log = new List<(double at, ZoundToken t)>();
        var ends = new Dictionary<ZoundToken, double>();
        ZoundAudition s = null;
        void NewSession() {
            s?.Dispose();
            alive = true; looper = false;
            log.Clear(); ends.Clear();
            s = new ZoundAudition(-48601, () => alive, () => PlayOnce(k, log), () => looper);
        }

        var phases = new List<Phase> {
            new Phase { name = "Burst, From start, 4 plays, 0.25 s", start = () => { NewSession(); s.settings.mode = AuditionGap.FromStart; s.settings.gap = 0.25f; s.settings.count = 4; s.StartBurst(); },
                done = t => t > 1.6, judge = b => {
                    Check("4 plays started", log.Count == 4, log.Count + " started");
                    // Measured against the planned grid (first start + n x gap): a late tick may delay one play by a frame,
                    // but must not push the ones after it.
                    double worst = 0; var iv = new StringBuilder();
                    for (int i = 1; i < log.Count; i++) {
                        worst = Math.Max(worst, Math.Abs(log[i].at - log[0].at - i * 0.25));
                        iv.Append((log[i].at - log[i - 1].at) * 1000).Append(' ');
                    }
                    // Plays start from the editor's main loop, so one late editor frame can delay ONE play (measured up to
                    // ~50 ms on an unfocused editor). What must not happen is that delay being carried into the next plays.
                    double lastDrift = log.Count == 4 ? Math.Abs(log[3].at - log[0].at - 3 * 0.25) : 1;
                    Check("start-to-start 0.25 s: no accumulated drift, any single play late by under 100 ms",
                          log.Count == 4 && lastDrift < 0.03 && worst < 0.1,
                          "intervals ms " + iv + "; last play off the grid by " + (lastDrift * 1000).ToString("0") + " ms, worst single " + (worst * 1000).ToString("0") + " ms");
                    Check("the burst ended by itself", !s.BurstRunning);
                } },
            new Phase { name = "Burst, From end, 3 plays, 0.2 s", start = () => { NewSession(); s.settings.mode = AuditionGap.FromEnd; s.settings.gap = 0.2f; s.settings.count = 3; s.StartBurst(); },
                done = t => t > 2.6, judge = b => {
                    Check("3 plays started", log.Count == 3, log.Count + " started");
                    double worst = 0; int pairs = 0;
                    for (int i = 1; i < log.Count; i++) if (ends.TryGetValue(log[i - 1].t, out double e)) { pairs++; worst = Math.Max(worst, Math.Abs(log[i].at - e - 0.2)); }
                    Check("each play starts 0.2 s after the previous one finished", pairs == 2 && worst < 0.06, pairs + " pairs, worst error " + (worst * 1000).ToString("0") + " ms");
                } },
            new Phase { name = "Burst, Steady, 3 plays, 0.1 s", start = () => { NewSession(); s.settings.mode = AuditionGap.Steady; s.settings.gap = 0.1f; s.settings.count = 3; s.StartBurst(); },
                done = t => t > 2.0, judge = b => {
                    float len = log.Count > 0 && log[0].t != null ? log[0].t.duration : 0f;
                    double expect = len + 0.1;
                    double worst = 0; for (int i = 1; i < log.Count; i++) worst = Math.Max(worst, Math.Abs(log[i].at - log[0].at - i * expect));
                    Check("3 plays on a fixed rhythm of first length + gap", log.Count == 3 && worst < 0.05,
                          "rhythm " + (expect * 1000).ToString("0") + " ms, worst drift " + (worst * 1000).ToString("0") + " ms");
                } },
            new Phase { name = "Loop, then the window disappears (sweep)", start = () => { NewSession(); s.settings.mode = AuditionGap.FromStart; s.settings.gap = 0.3f; s.StartLoop(); },
                done = t => { if (t > 1.0 && alive) alive = false; return t > 1.6; }, judge = b => {
                    Check("the loop kept going until stopped", log.Count >= 3, log.Count + " plays in 1 s");
                    Check("gone window: session removed itself", s.IsDisposed);
                    Check("gone window: nothing still sounding", StillSounding(log) == 0, StillSounding(log) + " sources playing");
                    Check("gone window: no play started after it went", log.Count > 0 && log[log.Count - 1].at < Now - 0.45, "");
                } },
            new Phase { name = "Loop, then the reload/quit/Play-mode path (KillEverything)", start = () => { NewSession(); s.settings.mode = AuditionGap.FromStart; s.settings.gap = 0.3f; s.StartLoop(); },
                done = t => { if (t > 0.8 && s.LoopRunning) ZoundAudition.KillEverything(); return t > 1.5; }, judge = b => {
                    Check("killed: loop stopped", !s.LoopRunning);
                    Check("killed: nothing still sounding", StillSounding(log) == 0, StillSounding(log) + " sources playing");
                    Check("killed: no play started afterwards", log.Count > 0 && log[log.Count - 1].at < Now - 0.6);
                } },
            new Phase { name = "Burst with plays queued, then the window closes (Dispose)", start = () => { NewSession(); s.settings.mode = AuditionGap.FromStart; s.settings.gap = 0.3f; s.settings.count = 10; s.StartBurst(); },
                done = t => { if (t > 0.7 && !s.IsDisposed) s.Dispose(); return t > 1.6; }, judge = b => {
                    Check("closed: queued plays cancelled", log.Count <= 4, log.Count + " of 10 started");
                    Check("closed: nothing still sounding", StillSounding(log) == 0, StillSounding(log) + " sources playing");
                    Check("closed: no play started afterwards", log.Count > 0 && log[log.Count - 1].at < Now - 0.7);
                } },
            new Phase { name = "A sound that never ends (Looper) refuses Burst and Loop", start = () => { NewSession(); looper = true; s.StartBurst(); s.StartLoop(); },
                done = t => t > 0.3, judge = b => Check("no play started, nothing running", log.Count == 0 && !s.BurstRunning && !s.LoopRunning, log.Count + " started") },
            new Phase { name = "Play on change: two commits close together play once, after the settle", start = () => {
                    NewSession(); s.playOnChange = true; SetPrivate(s, "lastInteractionAt", Now);
                    RaiseCommit();
                    EditorApplication.delayCall += RaiseCommit;
                },
                done = t => t > 0.6, judge = b => Check("exactly one play", log.Count == 1, log.Count + " plays") },
            new Phase { name = "Play on change: nothing while the pointer is held, one play on release", start = () => {
                    NewSession(); s.playOnChange = true; SetPrivate(s, "lastInteractionAt", Now); SetPrivate(s, "pointerHeld", true);
                    RaiseCommit();
                },
                done = t => {
                    if (t > 0.5 && (bool)s.GetType().GetField("pointerHeld", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s)) {
                        if (log.Count > 0) failures++;   // played while held
                        SetPrivate(s, "pointerHeld", false);
                    }
                    return t > 1.0;
                }, judge = b => Check("held: silent; released: one play", log.Count == 1, log.Count + " plays") },
            new Phase { name = "Play on change: an edit in ANOTHER window (no recent interaction here) plays nothing", start = () => {
                    NewSession(); s.playOnChange = true; SetPrivate(s, "lastInteractionAt", Now - 60.0); RaiseCommit();
                },
                done = t => t > 0.5, judge = b => Check("no play", log.Count == 0, log.Count + " plays") },
            new Phase { name = "Play on change: ignored while a Loop runs", start = () => {
                    NewSession(); s.settings.mode = AuditionGap.FromStart; s.settings.gap = 2f; s.playOnChange = true; s.StartLoop();
                    SetPrivate(s, "lastInteractionAt", Now); RaiseCommit();
                },
                done = t => t > 0.5, judge = b => { Check("only the loop's own first play", log.Count == 1, log.Count + " plays"); s.StopAll(); } },
        };

        int idx = -1; double phaseStart = 0;
        void Next() {
            idx++;
            if (idx >= phases.Count) {
                s?.Dispose();
                Check("after everything: no audition play live anywhere", ZoundAudition.LiveCount == 0, ZoundAudition.LiveCount + " live");
                sb.Append(failures == 0 ? "\nALL PASS\n" : "\n" + failures + " FAILED\n");
                EditorApplication.update -= Update;
                Finish(sb, wasMuted, failures == 0);
                return;
            }
            sb.Append('\n').Append(idx + 1).Append(". ").Append(phases[idx].name).Append('\n');
            phaseStart = Now;
            try { phases[idx].start(); } catch (Exception e) { failures++; sb.Append("  FAIL  threw: ").Append(e.Message).Append('\n'); }
        }
        void Update() {
            // Record when each play is SEEN to have finished (what From end measures against).
            foreach (var (_, t) in log) if (t != null && t.state == ZoundToken.State.Killed && !ends.ContainsKey(t)) ends[t] = Now;
            EditorApplication.QueuePlayerLoopUpdate();
            bool finished;
            try { finished = phases[idx].done(Now - phaseStart); }
            catch (Exception e) { failures++; sb.Append("  FAIL  threw: ").Append(e.Message).Append('\n'); finished = true; }
            if (!finished) return;
            try { phases[idx].judge(sb); } catch (Exception e) { failures++; sb.Append("  FAIL  judging threw: ").Append(e.Message).Append('\n'); }
            Next();
        }
        Next();
        EditorApplication.update += Update;
        return ReportPath;
    }

    static void Finish(StringBuilder sb, bool wasMuted, bool pass) {
        EditorUtility.audioMasterMute = wasMuted;
        System.IO.File.WriteAllText(ReportPath, sb.ToString());
        if (pass) Debug.Log("[ZoundsAuditionCheck]\n" + sb); else Debug.LogWarning("[ZoundsAuditionCheck]\n" + sb);
    }

    // ───────────────────────── step 2: across a real script reload ─────────────────────────

    static ZoundAudition reloadSession;

    /// <summary>Starts a Loop, lets it run for a second, then reloads scripts. The other half runs after the reload.</summary>
    public static void StartReloadTest() {
        var k = MakeSound(out string why);
        if (k == null) { Debug.LogWarning("[ZoundsAuditionCheck] step 2 cannot run: " + why); return; }
        SessionState.SetBool("ZoundsAuditionCheck.wasMuted", EditorUtility.audioMasterMute);
        EditorUtility.audioMasterMute = true;
        var log = new List<(double, ZoundToken)>();
        reloadSession = new ZoundAudition(-48602, () => true, () => PlayOnce(k, log), () => false);
        reloadSession.settings.mode = AuditionGap.FromStart; reloadSession.settings.gap = 0.3f;
        reloadSession.StartLoop();
        double t0 = Now;
        void Wait() {
            EditorApplication.QueuePlayerLoopUpdate();
            if (Now - t0 < 1.0) return;
            EditorApplication.update -= Wait;
            int sounding = 0;
            foreach (var (_, t) in log) if (t != null && t.audioSource != null && t.audioSource.isPlaying) sounding++;
            SessionState.SetInt(ReloadSources, sounding);
            SessionState.SetInt("ZoundsAuditionCheck.playsBefore", log.Count);
            SessionState.SetInt(ZoundAudition.ReloadKilledKey, -1);
            SessionState.SetBool(ReloadFlag, true);
            System.IO.File.WriteAllText(ReportPath, "step 2: reloading scripts with a Loop running\n");
            EditorUtility.RequestScriptReload();
        }
        EditorApplication.update += Wait;
    }

    [InitializeOnLoadMethod]
    static void AfterReload() {
        if (!SessionState.GetBool(ReloadFlag, false)) return;
        SessionState.SetBool(ReloadFlag, false);
        double t0 = Now;
        // Watch for a while, so a play that starts again late is caught too.
        void Watch() {
            EditorApplication.QueuePlayerLoopUpdate();
            if (Now - t0 < 1.5) return;
            EditorApplication.update -= Watch;
            int playing = 0;
            foreach (var a in Resources.FindObjectsOfTypeAll<AudioSource>()) if (a != null && a.isPlaying && a.gameObject.name == "ZoundSource") playing++;
            int rendering = 0;
            foreach (var g in Resources.FindObjectsOfTypeAll<Laubrary.Zounds.Dsp.ZoundSapVoiceGenerator>()) if (g != null && g.IsPlaying) rendering++;
            int killedByHook = SessionState.GetInt(ZoundAudition.ReloadKilledKey, -1);
            var sb = new StringBuilder("=== AUDITION CHECK, step 2: across a real script reload ===\n");
            sb.Append("  before the reload: ").Append(SessionState.GetInt("ZoundsAuditionCheck.playsBefore", 0)).Append(" plays started, ")
              .Append(SessionState.GetInt(ReloadSources, 0)).Append(" sounding\n");
            bool hook = killedByHook >= 1;
            bool silent = playing == 0 && rendering == 0;
            sb.Append("  ").Append(hook ? "PASS  " : "FAIL  ").Append("the before-reload hook stopped the audition's plays itself (").Append(killedByHook).Append(" session(s) with plays)\n");
            sb.Append("  ").Append(silent ? "PASS  " : "FAIL  ").Append("1.5 s after the reload nothing is sounding: ").Append(playing).Append(" engine sources playing, ")
              .Append(rendering).Append(" voices rendering\n");
            sb.Append(hook && silent ? "\nALL PASS\n" : "\nFAILED\n");
            // Other Zounds windows the owner has open are not the check's to judge; engine sources playing counts every one.
            EditorUtility.audioMasterMute = SessionState.GetBool("ZoundsAuditionCheck.wasMuted", false);
            System.IO.File.WriteAllText(ReportPath, sb.ToString());
            if (hook && silent) Debug.Log("[ZoundsAuditionCheck]\n" + sb); else Debug.LogWarning("[ZoundsAuditionCheck]\n" + sb);
        }
        EditorApplication.update += Watch;
    }
}
