// A kept check for tracks through the token (T-0497), on an IN-MEMORY Zequence of in-memory copies of a real Klip
// (never added to the library, never saved).
//
//   1. Disabled tracks are never picked: Randomizer, Round robin (and its shared memory is not used up by a disabled track)
//      and Playlist all skip them; with every track disabled nothing plays.
//   2. A track can be reached by its ZPOC id, anywhere in the tree.
//   3. A track number that does not exist is reported once (diagnostics list) and the call does nothing.
//   4. A track's volume and mute reach its play at the next engine update, and survive the token being played again.
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

public static class ZoundsTracksCheck {

    [MenuItem("Laubrary/Zounds/Checks/23 - Tracks through the token (enabled, ids, volume, mute)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    static void EngineUpdate() {
        var inst = typeof(ZoundEngine).GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                   ?? typeof(ZoundEngine).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        typeof(ZoundEngine).GetMethod("OnUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(inst, null);
    }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && Laubrary.Zounds.Dsp.ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";

        Zequence Make(CompositeZound.Mode mode) {
            var zeq = new Zequence(-9500) { name = "tracks check (in memory)" };
            zeq.mode = mode;
            for (int i = 0; i < 3; i++) {
                var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
                typeof(Zound).GetField("id").SetValue(c, -9510 - i);
                c.name = "track " + i;
                c.effectChain = src.effectChain != null ? src.effectChain.DeepCopy() : new ZoundEffectChain();
                c.chainPresetId = 0;
                zeq.localKlips.Add(c);
                zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = c.id, local = true, zpocId = i == 2 ? "Rotor" : "" });
            }
            return zeq;
        }
        var args = new ZoundArgs { startImmediately = true, volumeOverride = 0f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };

        // ── 1: picking skips disabled tracks ──
        {
            var zeq = Make(CompositeZound.Mode.Randomizer);
            var t = ZoundEngine.PlayToken(zeq, args);
            t.Track(0).enabled = false; t.Track(2).enabled = false;
            bool onlyOne = true;
            for (int i = 0; i < 25; i++) { t.Restart(); if (t.playedEntryIndex != 1) onlyOne = false; }
            t.Kill();
            Check(onlyOne, "1. Randomizer: with tracks 0 and 2 disabled, 25 runs all picked track 1");
        }
        {
            var zeq = Make(CompositeZound.Mode.RoundRobin);
            var t = ZoundEngine.PlayToken(zeq, args);
            t.Track(1).enabled = false;
            var picks = new List<int>();
            for (int i = 0; i < 8; i++) { t.Restart(); picks.Add(t.playedEntryIndex); }
            t.Kill();
            bool noOne = !picks.Contains(1) && !zeq.playedEntries.Contains(1);
            bool alternates = true; for (int i = 1; i < picks.Count; i++) if (picks[i] == picks[i - 1]) alternates = false;
            sb.Append("round robin picks: ").Append(string.Join(",", picks)).Append('\n');
            Check(noOne, "1. Round robin: the disabled track is never picked, nor counted as played in the shared memory");
            Check(alternates, "1. Round robin: the remaining two alternate, never the same twice running");
        }
        {
            var zeq = Make(CompositeZound.Mode.Playlist);
            var t = ZoundEngine.PlayToken(zeq, args);
            t.Track(0).enabled = false;
            var picks = new List<int>();
            for (int i = 0; i < 5; i++) { t.Restart(); picks.Add(t.playedEntryIndex); }
            t.Track(1).enabled = false; t.Track(2).enabled = false;
            t.Restart();
            int none = t.playedEntryIndex;
            t.Kill();
            sb.Append("playlist picks: ").Append(string.Join(",", picks)).Append('\n');
            Check(!picks.Contains(0), "1. Playlist: steps past the disabled first track");
            Check(none == -1, "1. Every track disabled: the run plays nothing (" + none + ")");
        }

        // ── 2 + 3: ids and missing tracks ──
        {
            var zeq = Make(CompositeZound.Mode.Parallel);
            var quiet = args; quiet.volumeOverride = 0.01f;   // audible volumes must be non-zero for the ratios below
            var t = ZoundEngine.PlayToken(zeq, quiet);
            var byId = t.Track("rotor");
            byId.volume = 0.25f;
            Check(byId.isValid && Mathf.Approximately(t.Track(2).volume, 0.25f), "2. Track(\"rotor\") reaches track 2 (id matched the way Zound names are)");
            int before = 0; foreach (var e in ZoundDiagnostics.Entries) if (e.kind == ZoundDiagnostics.Kind.MissingTrack && e.detail == "#7") before = e.count;
            var missing = t.Track(7); missing.volume = 0.1f; t.Track(7).mute = true;
            int after = 0; foreach (var e in ZoundDiagnostics.Entries) if (e.kind == ZoundDiagnostics.Kind.MissingTrack && e.detail == "#7") after = e.count;
            Check(!missing.isValid && after == before + 2, "3. Track 7 of a 3-track Zequence is reported (counted, one row) and does nothing");
            Check(!t.Track("nothing-called-this").isValid, "3. an id no track has gives a track that does nothing");

            // ── 4: volume and mute reach the play, and survive a replay ──
            t.Track(0).volume = 0.5f;
            t.Track(1).mute = true;
            EngineUpdate();
            var children = new List<ZoundToken>();
            foreach (var x in (List<ZoundToken>)typeof(ZoundEngine).GetProperty("LiveTokens", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
                if (x.isChildZound && x.zound != null && x.zound.name.StartsWith("track ")) children.Add(x);
            ZoundToken c0 = null, c1 = null, c2 = null;
            foreach (var c in children) { if (c.zound.name == "track 0") c0 = c; if (c.zound.name == "track 1") c1 = c; if (c.zound.name == "track 2") c2 = c; }
            // Each play was started at volume 0 (volumeOverride) -> compare the three against each other through their
            // parent volume instead: read it back from the handler.
            float Parent(ZoundToken c) {
                var h = typeof(ZoundToken).GetField("m_handler", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(c);
                return (float)h.GetType().GetProperty("parentVolume").GetValue(h);
            }
            if (c0 != null && c1 != null && c2 != null) {
                sb.Append("track parent volumes after one update: ").Append(Parent(c0).ToString("F2")).Append(", ").Append(Parent(c1).ToString("F2")).Append(", ").Append(Parent(c2).ToString("F2"))
                  .Append("; track 1 muted: ").Append(c1.audioSource.mute).Append('\n');
                Check(Mathf.Abs(Parent(c0) / Mathf.Max(Parent(c1), 1e-6f) - 0.5f) < 1e-3f, "4. track 0 plays at half the volume of track 1");
                Check(Mathf.Abs(Parent(c2) / Mathf.Max(Parent(c1), 1e-6f) - 0.25f) < 1e-3f, "4. track 2 (set by id) plays at a quarter");
                Check(c1.audioSource.mute && !c0.audioSource.mute, "4. track 1 is muted, track 0 is not");
            }
            else Check(false, "4. the three track plays were found (" + children.Count + ")");
            t.Restart();
            Check(Mathf.Approximately(t.Track(0).volume, 0.5f) && t.Track(1).mute, "4. the track settings survive the token playing again");
            t.Kill();
        }

        sb.Insert(0, fail == 0 ? "PASS - tracks through the token.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }
}
