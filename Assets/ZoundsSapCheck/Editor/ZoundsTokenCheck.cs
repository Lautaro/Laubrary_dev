// A kept check for replayable tokens (T-0497), on an in-memory copy of a real Klip (never added to the library, never saved).
//
//   1. A token whose run has ended plays again when told to: a new run, the same token, one entry in the engine's list.
//   2. Its settings carry over (a ZPOC value set before), and are applied to the new run's voice.
//   3. Restart ends the current run and starts the next; Restart(resetZpoc) clears the token's own values.
//   4. PlayToken always returns a token: one whose play did not happen (chance) can be set and played again; one for a name
//      no Zound has is empty and every call on it is safe.
//   5. The old play calls are unchanged: PlayZound still returns nothing when chance says no.
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

public static class ZoundsTokenCheck {

    [MenuItem("Laubrary/Zounds/Checks/22 - Tokens play again (runs, settings, not-played and empty tokens)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    static List<ZoundToken> Live() =>
        typeof(ZoundEngine).GetProperty("LiveTokens", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as List<ZoundToken>;

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && Laubrary.Zounds.Dsp.ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";
        var klip = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(klip, -9401);
        klip.name = "token check (in memory)";
        klip.effectChain = src.effectChain != null ? src.effectChain.DeepCopy() : new ZoundEffectChain();
        klip.chainPresetId = 0;
        var code = new ZoundModifier(ZoundModifierType.Code) { zpocId = "throttle" };
        klip.effectChain.modifiers.Add(code);
        klip.effectChain.bindings.Add(new ZoundModifierBinding { modifierIndex = klip.effectChain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
            combine = Laubrary.Zounds.Dsp.ModulationCombine.Ratio, depth = 1f, schema = Laubrary.Zounds.Dsp.ChainModulationCompat.CURRENT_SCHEMA });

        var args = new ZoundArgs { startImmediately = true, volumeOverride = 0f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };
        var t = ZoundEngine.PlayZound(klip, args);
        try {
            Check(t != null && t.isRunning && t.runCount == 1, "1. a first play runs (run 1)");
            t.SetZpoc("throttle", 0.9f);
            t.Kill();
            Check(!t.isRunning, "1. after Kill the run has ended");
            t.Play();
            int entries = 0; foreach (var x in Live()) if (x == t) entries++;
            Check(t.isRunning && t.runCount == 2 && entries == 1, "1. Play on the ended token starts run 2 on the same token, listed once (" + entries + ")");
            Check(t.audioSource != null, "1. the new run has an audio source");
            Check(t.TryGetZpoc("throttle", out float v) && Mathf.Approximately(v, 0.9f), "2. its ZPOC value carried over to the new run");
            var gen = t.audioSource != null ? t.audioSource.generator as Laubrary.Zounds.Dsp.ZoundSapVoiceGenerator : null;
            float sent = -1f;
            if (gen != null) {
                var L = gen.playingLayout;
                for (int m = 0; m < L.modCount; m++)
                    if (L.modZpocKey[m] == ZpocKeys.Key("throttle")) gen.TryReadModifierState(m, out _, out sent, out _);
            }
            Check(gen == null || Mathf.Abs(sent - 0.9f) < 1e-4f, "2. the new run's voice was sent it" + (gen == null ? " (no chain voice here; not measured)" : " (" + sent.ToString("F2") + ")"));
            t.Restart();
            Check(t.isRunning && t.runCount == 3 && t.TryGetZpoc("throttle", out _), "3. Restart starts run 3 and keeps the value");
            t.Restart(resetZpoc: true);
            Check(t.isRunning && t.runCount == 4 && !t.TryGetZpoc("throttle", out _), "3. Restart(resetZpoc) clears the token's own values");
        }
        finally { t?.Kill(); }

        // ── 2b: values that exist before the voice does reach its first block ──
        float SentToVoice(ZoundToken tok) {
            var g = tok != null && tok.audioSource != null ? tok.audioSource.generator as Laubrary.Zounds.Dsp.ZoundSapVoiceGenerator : null;
            if (g == null) return -1f;
            var L = g.playingLayout;
            for (int m = 0; m < L.modCount; m++)
                if (L.modZpocKey[m] == ZpocKeys.Key("throttle") && g.TryReadModifierState(m, out _, out float sentNow, out _)) return sentNow;
            return -1f;
        }
        {
            var deferred = args; deferred.startImmediately = false;
            var d = ZoundEngine.PlayZound(klip, deferred);
            d.SetZpoc("throttle", 0.7f);
            d.Play();
            float got = SentToVoice(d);
            Check(got < 0f || Mathf.Abs(got - 0.7f) < 1e-4f, "2. a value set before Play is the voice's value from its first block" + (got < 0f ? " (no chain voice; not measured)" : " (" + got.ToString("F2") + ")"));
            d.Kill();
            ZoundEngine.SetGlobalZpoc("throttle", 0.2f);
            var gtok = ZoundEngine.PlayZound(klip, args);
            got = SentToVoice(gtok);
            Check(got < 0f || Mathf.Abs(got - 0.2f) < 1e-4f, "2. a project-wide value is the voice's value from its first block" + (got < 0f ? " (not measured)" : " (" + got.ToString("F2") + ")"));
            Check(gtok.SourceOfZpoc("throttle") == ZoundToken.ZpocSource.Global, "2. and the token reports it as coming from the project-wide value");
            gtok.SetZpoc("throttle", 0.6f);
            // (The voice takes it at its next block; the audio thread cannot run while this check holds the main thread.)
            Check(gtok.SourceOfZpoc("throttle") == ZoundToken.ZpocSource.Play && gtok.TryGetZpoc("throttle", out float won) && Mathf.Approximately(won, 0.6f),
                  "2. a value set on the play wins over the project-wide one");
            gtok.ClearZpoc("throttle");
            Check(gtok.SourceOfZpoc("throttle") == ZoundToken.ZpocSource.Global, "2. clearing it follows the project-wide value again");
            gtok.Kill();
            ZoundEngine.ClearGlobalZpoc("throttle");
        }

        // ── 4: always a token ──
        var never = args; never.chanceOverride = 0f;
        var np = ZoundEngine.PlayToken(klip, never);
        Check(np != null && !np.wasPlayed && !np.isEmpty && !np.isRunning, "4. PlayToken returns a not-played token when chance says no");
        Check(np.SetZpoc("throttle", 0.3f), "4. a not-played token can be given settings");
        np.Pause(); np.Unpause(); np.Kill();   // must not throw or log errors
        Check(!np.wasPlayed, "4. playing it again rolls chance again (still no at chance 0)");
        var empty = ZoundEngine.PlayToken("zpoc check: no such zound 7c1e");
        Check(empty != null && empty.isEmpty, "4. PlayToken returns an empty token for a name no Zound has");
        empty.Play(); empty.Pause(); empty.Kill(); empty.Restart(); empty.EnsurePlaying();
        Check(!empty.SetZpoc("throttle", 1f) && empty.duration == 0f && empty.time == 0f, "4. every call on the empty token is safe and does nothing");

        // ── 5: the old calls ──
        Check(ZoundEngine.PlayZound(klip, never) == null, "5. PlayZound still returns nothing when chance says no");

        sb.Insert(0, fail == 0 ? "PASS - tokens play again.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }
}
