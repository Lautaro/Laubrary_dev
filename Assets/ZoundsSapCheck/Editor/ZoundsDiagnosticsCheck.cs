// A kept check for the running list of game-code requests that found nothing (T-0490), shown in the Zounds window's
// Problems tab. Uses an in-memory copy of a real Klip (never added to the library, never saved), and removes only the rows
// it added itself, so a real session's list is left as it was.
//
//   1. Each kind of miss makes a row: an unknown sound name, a ZPOC id the play does not declare, a track past the last,
//      a snapshot nobody has, a project-wide value nobody listens to.
//   2. The same problem made many times is ONE row with a count, and differently spelled requests that mean the same id
//      ("Throtle", "throtle", "thro tle") are the same row. The row keeps the caller's first spelling for display.
//   3. A repeated miss allocates nothing (a request made every frame costs no garbage).
//   4. Nothing throws, and a miss changes nothing: the play keeps running.
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;

public static class ZoundsDiagnosticsCheck {

    [MenuItem("Laubrary/Zounds/Checks/25 - Problems list (one row per miss, spelling kept, no garbage)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && Laubrary.Zounds.Dsp.ZoundSapPlayback.LoadSourceClip(k, out bool _) != null) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";
        var klip = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(klip, -9501);
        klip.name = "diagnostics check (in memory)";
        klip.effectChain = src.effectChain != null ? src.effectChain.DeepCopy() : new ZoundEffectChain();
        klip.chainPresetId = 0;

        var before = new HashSet<ZoundDiagnostics.Entry>(ZoundDiagnostics.Entries);
        ZoundDiagnostics.Entry Row(ZoundDiagnostics.Kind kind, string zound) {
            foreach (var e in ZoundDiagnostics.Entries) if (!before.Contains(e) && e.kind == kind && e.zound == zound) return e;
            return null;
        }
        int Added(ZoundDiagnostics.Kind kind) { int n = 0; foreach (var e in ZoundDiagnostics.Entries) if (!before.Contains(e) && e.kind == kind) n++; return n; }

        var args = new ZoundArgs { startImmediately = true, volumeOverride = 0f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };
        var t = ZoundEngine.PlayZound(klip, args);
        string bogusSound = "diagnostics check no such sound " + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        string bogusGlobal = "Diag Check Nobody " + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        try {
            bool threw = false;
            try {
                ZoundEngine.PlayZound(bogusSound);
                ZoundEngine.SetGlobalZpoc(bogusGlobal, 0.3f);
                t.SetZpoc("Throtle", 0.5f); t.SetZpoc("throtle", 0.5f); t.SetZpoc("thro tle", 0.5f);
                t.Track(9).volume = 0.5f;
                t.GlideToSnapshot("Diag Stormy", 100); t.GlideToSnapshot("diagstormy", 100);
            } catch (System.Exception e) { threw = true; sb.Append("  threw: ").Append(e.Message).Append('\n'); }
            Check(!threw, "4. no miss throws");
            Check(t.isRunning, "4. the play is still running after every miss");

            var sound = Row(ZoundDiagnostics.Kind.MissingZound, "");
            Check(sound != null && sound.shown == bogusSound, "1. an unknown sound name makes a row, spelled as asked");
            var global = Row(ZoundDiagnostics.Kind.UndeclaredGlobalZpoc, "");
            Check(global != null && global.shown == bogusGlobal, "1. a project-wide value nobody declares makes a row, spelled as asked ('" + global?.shown + "')");
            var zpoc = Row(ZoundDiagnostics.Kind.MissingZpoc, klip.name);
            Check(zpoc != null, "1. an undeclared ZPOC id makes a row");
            Check(Added(ZoundDiagnostics.Kind.MissingZpoc) == 1 && zpoc != null && zpoc.count == 3,
                "2. 'Throtle', 'throtle' and 'thro tle' are one row counted 3 times (rows " + Added(ZoundDiagnostics.Kind.MissingZpoc) + ", count " + zpoc?.count + ")");
            Check(zpoc != null && zpoc.shown == "Throtle", "2. the row keeps the first spelling ('" + zpoc?.shown + "')");
            var track = Row(ZoundDiagnostics.Kind.MissingTrack, klip.name);
            Check(track != null, "1. a track past the last makes a row");
            var snap = Row(ZoundDiagnostics.Kind.MissingSnapshot, klip.name);
            Check(snap != null && snap.count == 2 && Added(ZoundDiagnostics.Kind.MissingSnapshot) == 1, "1+2. a missing snapshot makes one row for two spellings (count " + snap?.count + ")");

            // 3. Repeats allocate nothing. Warm once, then measure many.
            t.SetZpoc("Throtle", 0.5f); t.GlideToSnapshot("Diag Stormy", 100); t.Track(9).volume = 0.5f;
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { t.SetZpoc("Throtle", 0.5f); t.GlideToSnapshot("Diag Stormy", 100); t.Track(9).volume = 0.5f; }
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - a0;
            Check(bytes == 0, "3. 3000 repeated misses allocate nothing (" + bytes + " bytes)");
            Check(zpoc != null && zpoc.count == 1004, "3. and every one was counted (" + zpoc?.count + ")");
        }
        finally {
            t?.Kill();
            var mine = new List<ZoundDiagnostics.Entry>();
            foreach (var e in ZoundDiagnostics.Entries) if (!before.Contains(e)) mine.Add(e);
            foreach (var e in mine) ZoundDiagnostics.Remove(e);
            ZoundEngine.ClearGlobalZpoc(bogusGlobal);
        }
        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - Zounds problems list\n" + sb;
    }
}
