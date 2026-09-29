// A kept check: on the chain path a play's volume is applied ONCE (found 2026-09-29).
//
// The chain path's voice is carried into the mixer by an audio source, and that audio source already applies the play's
// volume (with the master volume, a parent Zequence's, live and track volume and fades) on every update. The voice used
// to be given the same volume as its own output gain as well, so the level was applied twice: a sound at 0.5 came out at
// 0.25. The same kind of double application was found and fixed for pitch earlier (T-0443).
//
// Plays an in-memory copy of a real Klip (never added to the library, never saved) at volume 0.5 with the editor muted,
// and reads what each of the two places was given.
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

public static class ZoundsVolumeOnceCheck {

    [MenuItem("Laubrary/Zounds/Checks/26 - Volume applied once on the chain path")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && Laubrary.Zounds.Dsp.ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";
        var klip = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(klip, -9601);
        klip.name = "volume once check (in memory)";
        klip.effectChain = src.effectChain != null ? src.effectChain.DeepCopy() : new ZoundEffectChain();
        klip.chainPresetId = 0;

        bool wasMuted = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = true;
        var t = ZoundEngine.PlayZound(klip, new ZoundArgs { startImmediately = true, volumeOverride = 0.5f, pitchOverride = 1f,
                                                            chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true });
        try {
            Check(t != null && t.audioSource != null, "the play started");
            if (t == null || t.audioSource == null) return "FAIL - Zounds volume applied once\n" + sb;
            var gen = t.audioSource.GetComponent<Laubrary.Zounds.Dsp.ZoundSapVoiceGenerator>();
            Check(gen != null, "it plays through the chain (a voice carries it)");
            if (gen != null) {
                float outGain = (float)typeof(Laubrary.Zounds.Dsp.ZoundSapVoiceGenerator)
                    .GetField("outGain", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gen);
                Check(Mathf.Approximately(outGain, 1f), "the voice runs at gain one (" + outGain.ToString("0.###") + ")");
            }
            float expected = 0.5f * ZoundEngine.GetMasterVolume();
            float vol = t.audioSource.volume;
            Check(Mathf.Abs(vol - expected) < 1e-4f, "the audio source carries the play's volume, 0.5 x master (" + vol.ToString("0.###") + " vs " + expected.ToString("0.###") + ")");
        }
        finally {
            try { t?.Kill(); } catch { }
            EditorUtility.audioMasterMute = wasMuted;
        }
        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - Zounds volume applied once\n" + sb;
    }
}
