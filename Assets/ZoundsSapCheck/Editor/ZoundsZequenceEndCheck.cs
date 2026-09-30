// A kept check: a Zequence is over when its tracks are over (T-0505, found 2026-09-30).
//
// A track whose length cannot be known in advance (a Klip with live speed) declares a generous upper bound, about twenty
// times its real length. A Zequence took its length from the longest track's declared length, so with such a track it
// kept "playing" long after it went quiet: the owner's Clop, whose one track stops after about a second, stayed alive for
// 529 s and its row in the Zounds browser kept flashing.
//
// Plays in-memory Zequences (never added to the library, never saved) with the editor muted, and watches them in real time:
//   1. one live-speed track: the Zequence ends within a moment of its track ending, not at the declared upper bound;
//   2. a plain track plus the same track delayed by 0.6 s: the Zequence does NOT end when the first one does, only after
//      the delayed one (so a track still waiting for its delay keeps the Zequence alive).
// Runs over a few seconds; the result goes to the console and to LastResult.
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

public static class ZoundsZequenceEndCheck {

    [MenuItem("Laubrary/Zounds/Checks/27 - A Zequence ends when its tracks end")]
    public static void RunFromMenu() { Run(); Debug.Log("[ZoundsZequenceEndCheck] running for a few seconds; the result follows in the console."); }

    public static string LastResult = "(not run yet)";

    public static void Run() {
        LastResult = "(running)";
        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds())
            if (z is Klip k && !k.IsLooper && Laubrary.Zounds.Dsp.ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) { LastResult = "SKIPPED - no playable Klip in this project"; return; }

        Klip Track(int id, bool live) {
            var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
            typeof(Zound).GetField("id").SetValue(c, id);
            c.name = "end check track " + id;
            c.effectChain = src.effectChain != null ? src.effectChain.DeepCopy() : new ZoundEffectChain();
            c.chainPresetId = 0;
            c.loop = new ZoundLoop { enabled = false };
            c.trimEnabled = true; c.trimStart = 0f; c.trimEnd = 0.4f;   // a short, known region
            if (c.timeStretch == null) c.timeStretch = new ZoundTimeStretch();
            c.timeStretch.liveEnabled = live; c.timeStretch.liveSpeed = 1f;
            return c;
        }
        Zequence Make(int id, params (Klip klip, float delay)[] tracks) {
            var zeq = new Zequence(id) { name = "end check (in memory)", mode = CompositeZound.Mode.Parallel };
            foreach (var t in tracks) {
                zeq.localKlips.Add(t.klip);
                zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = t.klip.id, local = true, delay = t.delay });
            }
            return zeq;
        }

        bool wasMuted = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = true;
        var args = new ZoundArgs { startImmediately = true, volumeOverride = 1f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };
        var liveZeq = Make(-9700, (Track(-9701, true), 0f));
        var delayZeq = Make(-9710, (Track(-9711, false), 0f), (Track(-9712, false), 0.6f));
        var a = ZoundEngine.PlayToken(liveZeq, args);
        var b = ZoundEngine.PlayToken(delayZeq, args);
        float declaredA = a.duration;
        double t0 = EditorApplication.timeSinceStartup, endA = -1, endB = -1;
        EditorApplication.CallbackFunction upd = null;
        upd = () => {
            EditorApplication.QueuePlayerLoopUpdate();
            double t = EditorApplication.timeSinceStartup - t0;
            if (endA < 0 && !a.isRunning) endA = t;
            if (endB < 0 && !b.isRunning) endB = t;
            if ((endA >= 0 && endB >= 0) || t > 6.0) {
                EditorApplication.update -= upd;
                try { a.Kill(); b.Kill(); } catch { }
                EditorUtility.audioMasterMute = wasMuted;
                var sb = new StringBuilder();
                int fail = 0;
                void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
                Check(declaredA > 3f, "1. the live-speed Zequence declares a long upper bound (" + declaredA.ToString("0.0") + " s), which is the case this is about");
                Check(endA >= 0 && endA < 1.5, "1. and it ends when its 0.4 s track ends (" + (endA < 0 ? "still running after 6 s" : endA.ToString("0.00") + " s") + ")");
                Check(endB >= 0 && endB > 0.85 && endB < 2.0, "2. a plain track plus one delayed 0.6 s ends after the delayed one (" + (endB < 0 ? "still running" : endB.ToString("0.00") + " s") + ", expected about 1.0 s)");
                LastResult = (fail == 0 ? "PASS - a Zequence ends when its tracks end.\n" : "FAIL - " + fail + " problem(s).\n") + sb;
                if (fail == 0) Debug.Log("[ZoundsZequenceEndCheck]\n" + LastResult); else Debug.LogWarning("[ZoundsZequenceEndCheck]\n" + LastResult);
            }
        };
        EditorApplication.update += upd;
    }
}
