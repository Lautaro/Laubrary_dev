// Kept check: an edit is heard on a sound that is ALREADY PLAYING (the project rule), measured on a Looper, which is where
// breaking it is most obvious: a Looper never plays again, so anything it read once at its start it keeps for ever.
//
// Found 2026-09-29 from the owner's report ("it seems to sample the audio once and then use it for every loop"). Measured
// before the fix: an effect parameter was heard live, but the Klip's volume and pitch were never heard on a playing Looper.
// This check plays a sound built in memory (never added to the library; source audio borrowed read-only from the library's
// first Klip) with the editor's audio muted, changes one thing at a time, and reads the voice's own output after two loop
// passes.
//
// Reported, not judged: what is still read once at the start of a play (the chain's structure, curve shapes, time
// settings). Those need the playing voice to be replaced, which is a separate piece of work.
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsLiveEditCheck {

    public static string ReportPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zounds-live-edit-check.txt");

    [MenuItem("Laubrary/Zounds/Checks/19 - Edits heard on a playing Looper (effect, volume, pitch)")]
    public static void RunFromMenu() { RunLater(); Debug.Log("[ZoundsLiveEditCheck] running for ~7 s, report follows in the console and at " + ReportPath); }

    struct Reading { public double rms; public int crossings; public bool ok; }

    public static string RunLater() {
        System.IO.File.WriteAllText(ReportPath, "running\n");
        var sb = new StringBuilder("=== LIVE EDITS ON A PLAYING LOOPER ===\n");
        var lib = ZoundsProject.Instance != null ? ZoundsProject.Instance.zoundLibrary : null;
        if (lib == null || lib.klips.Count == 0 || lib.klips[0].audioClipRef == null || !(lib.klips[0].audioClipRef.editorAsset is AudioClip)) {
            System.IO.File.WriteAllText(ReportPath, sb.Append("  cannot run: the library has no Klip with source audio to borrow\n").ToString());
            return ReportPath;
        }
        bool wasMuted = EditorUtility.audioMasterMute;
        int wasMonitor = ZoundSapVoiceGenerator.monitorSamples;
        EditorUtility.audioMasterMute = true;
        ZoundSapVoiceGenerator.monitorSamples = 8192;

        var k = new Klip(-48703) { name = "(live edit check)" };
        k.audioClipRef = lib.klips[0].audioClipRef;   // read only; a fresh reference does not resolve the same way here
        k.audioClipPath = lib.klips[0].audioClipPath;
        k.effectChain = new ZoundEffectChain();
        var gain = new ZoundEffectNode(ZoundEffectType.Gain); gain.p[0] = 1f;
        k.effectChain.nodes.Add(gain);
        k.loop = new ZoundLoop { enabled = true };
        k.trimEnabled = true; k.trimStart = 1.0f; k.trimEnd = 1.5f;   // a 0.5 s loop: two passes per reading
        k.minVolume = k.maxVolume = 1f; k.minPitch = k.maxPitch = 1f;
        var tok = ZoundEngine.PlayZound(k, new ZoundArgs { startImmediately = true, volumeOverride = -1f, pitchOverride = -1f,
                                                           chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true });
        var buf = new float[8192];
        Reading Measure() {
            var g = tok != null && tok.audioSource != null ? tok.audioSource.GetComponent<ZoundSapVoiceGenerator>() : null;
            var r = new Reading();
            if (g == null || !g.ReadMonitor(buf)) return r;
            double s = 0; for (int i = 0; i < buf.Length; i++) { s += buf[i] * buf[i]; if (i > 0 && (buf[i - 1] < 0) != (buf[i] < 0)) r.crossings++; }
            r.rms = Math.Sqrt(s / buf.Length); r.ok = true;
            return r;
        }
        int failures = 0;
        void Check(string what, bool ok, string detail) { if (!ok) failures++; sb.Append("  ").Append(ok ? "PASS  " : "FAIL  ").Append(what).Append("  (").Append(detail).Append(")\n"); }

        Reading a = default, b = default, c = default, d = default;
        double t0 = EditorApplication.timeSinceStartup; int step = 0;
        EditorApplication.CallbackFunction upd = null;
        upd = () => {
            EditorApplication.QueuePlayerLoopUpdate();
            double t = EditorApplication.timeSinceStartup - t0;
            try {
                if (step == 0 && t > 1.2) { a = Measure(); gain.p[0] = 0.5f; ZoundDspPlayback.PushLiveParam(k, k.effectChain, 0, 0, 0.5f); step++; }
                else if (step == 1 && t > 2.4) { b = Measure(); k.minVolume = k.maxVolume = 0.5f; step++; }
                else if (step == 2 && t > 3.6) { c = Measure(); k.minPitch = k.maxPitch = 2f; step++; }
                else if (step == 3 && t > 4.8) { d = Measure(); step++; }
                else if (step == 4) {
                    EditorApplication.update -= upd;
                    Check("the Looper was playing and monitored", a.ok && b.ok && c.ok && d.ok, "");
                    Check("an effect parameter (gain 1 -> 0.5) is heard", Math.Abs(b.rms / Math.Max(a.rms, 1e-9) - 0.5) < 0.1, "level x" + (b.rms / Math.Max(a.rms, 1e-9)).ToString("0.00"));
                    Check("the Klip's volume (1 -> 0.5) is heard", Math.Abs(c.rms / Math.Max(b.rms, 1e-9) - 0.5) < 0.1, "level x" + (c.rms / Math.Max(b.rms, 1e-9)).ToString("0.00"));
                    Check("the Klip's pitch (1 -> 2) is heard", Math.Abs((double)d.crossings / Math.Max(c.crossings, 1) - 2.0) < 0.35, "zero-crossing rate x" + ((double)d.crossings / Math.Max(c.crossings, 1)).ToString("0.00"));
                    sb.Append("  REPORT ONLY: still read once when a play starts, so not heard on a playing Looper: the chain's structure (adding,\n")
                      .Append("  removing, reordering, bypassing an effect, swapping a preset), curve shapes, and time settings.\n");
                    sb.Append(failures == 0 ? "\nALL PASS\n" : "\n" + failures + " FAILED\n");
                    Finish();
                }
            }
            catch (Exception e) { EditorApplication.update -= upd; sb.Append("  FAIL  threw: ").Append(e.Message).Append('\n'); failures++; Finish(); }
        };
        void Finish() {
            try { tok?.Kill(); } catch { }
            ZoundSapVoiceGenerator.monitorSamples = wasMonitor;
            EditorUtility.audioMasterMute = wasMuted;
            System.IO.File.WriteAllText(ReportPath, sb.ToString());
            if (failures == 0) Debug.Log("[ZoundsLiveEditCheck]\n" + sb); else Debug.LogWarning("[ZoundsLiveEditCheck]\n" + sb);
        }
        EditorApplication.update += upd;
        return ReportPath;
    }
}
