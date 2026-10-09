// A kept check for a Klip's fixed boost into its effects (T-0521): x1 to x10, multiplying Drive.
//
//   1. Old sounds: a Klip saved before the boost existed reads x1, and out-of-range values are held to 1..10.
//   2. The chain analyser hears it: measured on the chain alone, x3 raises the output by 20 log10(3) = 9.54 dB.
//   3. A real play hears it: the level into the chain of a play at x3 is three times that of a play at x1.
//   4. A playing sound follows a live change (x1 -> x2) without being replayed.
// Uses an in-memory Klip (never added to the library, never saved) with one Gain effect, so levels are exact ratios; the
// editor is muted while it runs. Parts 3 and 4 run over a few seconds: the result goes to the console and to LastResult.
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsBoostCheck {

    [MenuItem("Laubrary/Zounds/Checks/29 - Boost into the effects (x1 to x10)")]
    public static void RunFromMenu() { Run(); Debug.Log("[ZoundsBoostCheck] running for a few seconds; the result follows in the console."); }

    public static string LastResult = "(not run yet)";

    public static void Run() {
        LastResult = "(running)";
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        var lib = ZoundsProject.Instance.zoundLibrary;
        Klip src = null;
        foreach (var z in lib.GetAllZounds()) if (z is Klip k && !k.IsLooper && ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) { LastResult = "SKIPPED - no playable Klip in this project"; return; }

        // ── 1: old sounds and clamping ──
        string json = JsonUtility.ToJson(src);
        int at = json.IndexOf("\"boost\":", StringComparison.Ordinal);
        if (at >= 0) { int end = json.IndexOf(',', at); json = json.Remove(at, end - at + 1); }
        var old = JsonUtility.FromJson<Klip>(json);
        // The project loader does not run a Klip's field defaults, so a missing value arrives as 0; what the engine uses is
        // the applied value, which is x1 for 0 (or anything at or below it).
        Check(!json.Contains("\"boost\"") && old.BoostApplied == 1f, "1. a Klip saved without a gain plays at x1 (stored " + old.boost + ", applied " + old.BoostApplied + ")");
        var clampK = new Klip(-9901);
        clampK.boost = 0.4f; float a1 = clampK.BoostApplied; clampK.boost = 14f; float a2 = clampK.BoostApplied; clampK.boost = float.NaN; float a3 = clampK.BoostApplied;
        clampK.boost = 0f; float a4 = clampK.BoostApplied; clampK.boost = 0.75f; float a5 = clampK.BoostApplied;
        Check(a1 == 0.5f && a2 == 8f && a3 == 1f && a4 == 1f && a5 == 0.75f,
              "1. held to 0.5..8, unset reads 1 (0.4 -> " + a1 + ", 14 -> " + a2 + ", NaN -> " + a3 + ", 0 -> " + a4 + ", 0.75 -> " + a5 + ")");

        // An in-memory Klip with one Gain effect at x1, so the level through the chain is exactly the level into it.
        // Built fresh, sharing the library Klip's own audio reference (read only): a copied reference does not resolve
        // the same way in the editor and plays silence (the same finding as check 19).
        var klip = new Klip(-9902) { name = "boost check (in memory)" };
        klip.audioClipRef = src.audioClipRef;
        klip.audioClipPath = src.audioClipPath;
        klip.effectChain = new ZoundEffectChain();
        var gain = new ZoundEffectNode(ZoundEffectType.Gain); gain.p[0] = 1f;
        klip.effectChain.nodes.Add(gain);
        klip.chainPresetId = 0;
        klip.loop = new ZoundLoop { enabled = true };                 // a Looper, so it keeps playing while we listen
        klip.trimEnabled = true; klip.trimStart = src.trimEnabled ? src.trimStart : 0f; klip.trimEnd = klip.trimStart + 0.5f;
        klip.minVolume = klip.maxVolume = 1f; klip.minPitch = klip.maxPitch = 1f;

        // ── 2: the analyser ──
        float Broadband(float b) {
            // The analyser's frequency picture, fed as the analyser feeds it for a sound with this boost.
            var snap = Laubrary.Zounds.EditorTools.ChainSpectrumProbe.MeasureFrozen(klip.effectChain, 72, b);
            if (snap.db == null) return float.NaN;
            float s = 0f; int n = 0; for (int i = 0; i < snap.db.Length; i++) if (snap.measurable[i]) { s += snap.db[i]; n++; }
            return n > 0 ? s / n : float.NaN;
        }
        float d1 = Broadband(1f), d3 = Broadband(3f);
        Check(Mathf.Abs((d3 - d1) - 9.54f) < 0.3f, "2. the analyser shows it: its bands read, on average, x3 " + (d3 - d1).ToString("0.00") + " dB above x1 (20 log10 3 = 9.54)");

        // ── 3 and 4: real plays, read from the voice's own output ──
        bool wasMuted = EditorUtility.audioMasterMute;
        int wasMonitor = ZoundSapVoiceGenerator.monitorSamples;
        EditorUtility.audioMasterMute = true;
        ZoundSapVoiceGenerator.monitorSamples = 8192;
        var buf = new float[8192];
        ZoundToken tok = null;
        double Rms() {
            var g = tok != null && tok.audioSource != null ? tok.audioSource.GetComponent<ZoundSapVoiceGenerator>() : null;
            if (g == null || !g.ReadMonitor(buf)) return double.NaN;
            double s = 0; for (int i = 0; i < buf.Length; i++) s += buf[i] * buf[i];
            return Math.Sqrt(s / buf.Length);
        }
        ZoundToken Play() => ZoundEngine.PlayZound(klip, new ZoundArgs { startImmediately = true, volumeOverride = 1f, pitchOverride = 1f,
                                                                           chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true });
        // Each level is the energy averaged over readings spanning more than one full pass of the 0.5 s loop, so where in
        // the (uneven) recording a single reading happens to fall does not decide the result.
        double r1 = double.NaN, r3 = double.NaN, rLiveBefore = double.NaN, rLiveAfter = double.NaN;
        double acc = 0; int accN = 0;
        void Accumulate() { double r = Rms(); if (!double.IsNaN(r)) { acc += r * r; accN++; } }
        double TakeLevel() { double v = accN > 0 ? Math.Sqrt(acc / accN) : double.NaN; acc = 0; accN = 0; return v; }
        // phases: [settle, measure] per step; step 0 = x1, 1 = x3, 2 = x1 then live -> x2, 3 = after the live change
        double phaseStart = EditorApplication.timeSinceStartup; int step = 0;
        klip.boost = 1f; tok = Play();
        EditorApplication.CallbackFunction upd = null;
        upd = () => {
            EditorApplication.QueuePlayerLoopUpdate();
            double t = EditorApplication.timeSinceStartup - phaseStart;
            try {
                if (t > 0.4 && t <= 1.3) Accumulate();            // settle 0.4 s, then measure across 0.9 s (almost two loop passes)
                if (t <= 1.3) return;
                phaseStart = EditorApplication.timeSinceStartup;
                if (step == 0) { r1 = TakeLevel(); tok.Kill(); klip.boost = 3f; tok = Play(); step++; }
                else if (step == 1) { r3 = TakeLevel(); tok.Kill(); klip.boost = 1f; tok = Play(); step++; }
                else if (step == 2) { rLiveBefore = TakeLevel(); klip.boost = 2f; SapVoiceRegistry.SetBoostLive(klip); step++; }
                else if (step == 3) {
                    rLiveAfter = TakeLevel();
                    EditorApplication.update -= upd;
                    try { tok?.Kill(); } catch { }
                    ZoundSapVoiceGenerator.monitorSamples = wasMonitor;
                    EditorUtility.audioMasterMute = wasMuted;
                    Check(!double.IsNaN(r1) && !double.IsNaN(r3) && Math.Abs(r3 / Math.Max(r1, 1e-9) - 3.0) < 0.1,
                          "3. a play at x3 is x" + (r3 / Math.Max(r1, 1e-9)).ToString("0.00") + " a play at x1 (the level into the chain)");
                    Check(!double.IsNaN(rLiveBefore) && Math.Abs(rLiveAfter / Math.Max(rLiveBefore, 1e-9) - 2.0) < 0.1,
                          "4. a playing sound follows a live change x1 -> x2: level x" + (rLiveAfter / Math.Max(rLiveBefore, 1e-9)).ToString("0.00"));
                    LastResult = (fail == 0 ? "PASS - boost into the effects.\n" : "FAIL - " + fail + " problem(s).\n") + sb;
                    if (fail == 0) Debug.Log("[ZoundsBoostCheck]\n" + LastResult); else Debug.LogWarning("[ZoundsBoostCheck]\n" + LastResult);
                }
            }
            catch (Exception e) {
                EditorApplication.update -= upd;
                ZoundSapVoiceGenerator.monitorSamples = wasMonitor; EditorUtility.audioMasterMute = wasMuted;
                LastResult = "FAIL - threw: " + e.Message + "\n" + sb;
            }
        };
        EditorApplication.update += upd;
    }

}
