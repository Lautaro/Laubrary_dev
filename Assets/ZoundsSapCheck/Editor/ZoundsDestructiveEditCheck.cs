// A kept check for destructive editing (2026-10-09): protection, the copy-on-edit swap, the audio verbs, file versions and
// undo, and the ripple that keeps everything pointing at seconds of a file on the same audio.
//
//   1. Frame edits are exact: cut, insert and overwrite leave every untouched frame of a 16- and a 24-bit file bit for bit
//      as it was; a paste between formats follows the stated rule (one channel copied to two, two averaged to one,
//      resampled to the same length in seconds).
//   2. The ripple of moments: trim and excerpt starts and ends before, inside, after and exactly at an edit.
//   3. A curve on the file's own seconds (with extra time after the audio) keeps its exact value over every bit of
//      untouched audio through a cut, an insert and an overwrite that runs past the end.
//   4. A file Zounds made for one local sound is edited in place: the untouched audio is identical, the sound's trim, its
//      track's excerpt and its volume curve follow; Undo puts the old file back byte for byte and Redo the new one.
//      "Repeat this hit four times" (duplicate-insert three times) gives four identical hits.
//   5. A file in the Sources folder is never rewritten: a cut writes a new file the sound plays instead; Undo removes that
//      file and points the sound back, Redo brings the file back under the same id.
//   6. A shared sound edited in its own editor goes to a copy: the object being edited takes a new id and "(copy)", an
//      unchanged twin keeps the old id and place, the Zequence still plays the original; "Edit the original instead" swaps
//      back keeping the edit. Ask-first: Cancel changes nothing, "Edit the original" edits it.
//   7. A shared track edited from a Zequence gets a local copy; "Edit the original instead" puts the shared sound back.
//   8. Only the last five versions of a file are kept.
// Everything it adds to the project is undone at the end (back to the Undo step it started at), its files and versions are
// deleted, and the project file is written back exactly as it was.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Destructive;

public static class ZoundsDestructiveEditCheck {

    [MenuItem("Laubrary/Zounds/Checks/33 - Destructive editing (protection, copy on edit, cut and paste, ripple, undo)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    // ── audio helpers ──

    static AudioPcm Make(int bits, int channels, int rate, double seconds, int seed) {
        var p = new AudioPcm { format = AudioPcm.IntFormat, bits = bits, channels = channels, rate = rate };
        int frames = (int)Math.Round(seconds * rate);
        p.data = new byte[frames * p.BlockAlign];
        uint h = (uint)seed * 2654435761u + 12345u;
        for (int i = 0; i < p.data.Length; i++) { h ^= h << 13; h ^= h >> 17; h ^= h << 5; p.data[i] = (byte)h; }
        return p;
    }

    static bool SameFrames(AudioPcm x, int xFrom, AudioPcm y, int yFrom, int frames) {
        int ba = x.BlockAlign;
        if (ba != y.BlockAlign) return false;
        for (int i = 0; i < frames * ba; i++) if (x.data[xFrom * ba + i] != y.data[yFrom * ba + i]) return false;
        return true;
    }

    static ZoundModifier SourceCurve(float extra) {
        var m = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curveAnchor = CurveAnchor.Source };
        m.p[0] = extra; m.p[1] = 0f;
        var pts = m.curve.GetPointsList(); pts.Clear();
        pts.Add(new ZUIEnvelopePoint(0f, 0.2f, 1f)); pts.Add(new ZUIEnvelopePoint(0.18f, 0.9f, 1f)); pts.Add(new ZUIEnvelopePoint(0.41f, 0.35f, 1f));
        pts.Add(new ZUIEnvelopePoint(0.63f, 0.8f, 1f)); pts.Add(new ZUIEnvelopePoint(0.86f, 0.1f, 1f)); pts.Add(new ZUIEnvelopePoint(1f, 0.6f, 1f));
        return m;
    }

    /// <summary>Largest difference between the old curve at s and the new one at the edit's image of s, over untouched audio.</summary>
    static float CurveDrift(ZoundModifier before, ZoundModifier after, AudioSpan e, float extra, int rate, out int samples) {
        double t0 = e.oldLength + extra, t1 = e.newLength + extra, step = 1d / 400d;
        float worst = 0f; samples = 0;
        for (double s = 0d; s <= t0 + 1e-9; s += step) {
            // The audio an edit replaced: [at, at + removed) for a cut, [at, at + inserted) for an overwrite; an insert replaces none.
            bool touched = !e.overwrite ? (e.removed > 0d && s >= e.at - 1e-9 && s < e.at + e.removed - 1e-9) : (s >= e.at - 1e-9 && s < e.at + e.inserted - 1e-9);
            if (touched) continue;
            double s1 = s > e.oldLength + 1e-9 ? s + (e.newLength - e.oldLength) : AudioRipple.MapStart(s, e);
            if (!e.overwrite && e.removed <= 0d && Math.Abs(s - e.at) < 1e-9) s1 = s;   // the insert point itself
            float v0 = before.curve.Evaluate((float)(s / t0)), v1 = after.curve.Evaluate((float)(s1 / t1));
            worst = Math.Max(worst, Math.Abs(v0 - v1)); samples++;
        }
        return worst;
    }

    // ── project helpers ──

    static string WriteWav(string projectPath, AudioPcm pcm) {
        ZoundsProject.EnsureDirectoryExists(Path.GetDirectoryName(projectPath).Replace('\\', '/'));
        File.WriteAllBytes(ZoundsProtection.Absolute(projectPath), pcm.ToWav());
        AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.AssetPathToGUID(projectPath);
    }

    static Klip NewKlip(string name, string guid, string path, float trimStart, float trimEnd) {
        var k = new Klip(ZoundLibrary.GetUniqueZoundId()) {
            name = name, trimEnabled = true, trimStart = trimStart, trimEnd = trimEnd,
            volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0.1f, 2f),
        };
        ZoundsAudioEdits.Repoint(k, path);
        return k;
    }

    static AudioPcm ReadBack(string path) => AudioPcm.ReadWav(ZoundsProtection.Absolute(path), out _);
    static string PathOf(Klip k) => ZoundsProtection.FileOf(k).path;
    static string GuidOf(Klip k) => ZoundsProtection.FileOf(k).guid;

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        bool Near(double a, double b, double tol = 1e-5) => Math.Abs(a - b) <= tol;

        // ── 1: frame edits are exact; a paste between formats follows the rule ──
        foreach (int bits in new[] { 16, 24 }) {
            var src = Make(bits, 2, 48000, 1.0, bits);
            int a = 12000, b = 30000, n = src.Frames;
            var cut = src.Spliced(a, b - a, null);
            bool cutOk = cut.Frames == n - (b - a) && SameFrames(src, 0, cut, 0, a) && SameFrames(src, b, cut, a, n - b);
            var piece = src.Slice(a, b);
            var ins = src.Spliced(40000, 0, piece);
            bool insOk = ins.Frames == n + piece.Frames && SameFrames(src, 0, ins, 0, 40000) && SameFrames(piece, 0, ins, 40000, piece.Frames) && SameFrames(src, 40000, ins, 40000 + piece.Frames, n - 40000);
            var over = src.Overwritten(n - 5000, piece);
            bool overOk = over.Frames == n - 5000 + piece.Frames && SameFrames(src, 0, over, 0, n - 5000) && SameFrames(piece, 0, over, n - 5000, piece.Frames);
            var wavBack = AudioPcmFromBytes(src.ToWav());
            Check(cutOk && insOk && overOk && wavBack != null && SameFrames(src, 0, wavBack, 0, n), "1. " + bits + "-bit stereo: cut, insert, overwrite past the end and a write-and-read keep every untouched frame bit for bit");
        }
        {
            var mono = Make(16, 1, 48000, 0.5, 7);
            var stereoTarget = new AudioPcm { format = AudioPcm.IntFormat, bits = 24, channels = 2, rate = 44100 };
            var up = mono.ConvertedTo(stereoTarget);
            bool lenOk = up.Frames == (int)Math.Round(mono.Frames * 44100d / 48000d);
            bool dup = true; for (int f = 0; f < up.Frames; f += 97) dup &= up.Sample(f, 0) == up.Sample(f, 1);
            var st = Make(16, 2, 48000, 0.25, 9);
            var down = st.ConvertedTo(new AudioPcm { format = AudioPcm.IntFormat, bits = 16, channels = 1, rate = 48000 });
            float worst = 0f; for (int f = 0; f < down.Frames; f += 31) worst = Math.Max(worst, Math.Abs(down.Sample(f, 0) - 0.5f * (st.Sample(f, 0) + st.Sample(f, 1))));
            bool same = ReferenceEquals(st.ConvertedTo(st.EmptyLike()), st);
            Check(lenOk && dup && worst <= 1f / 32768f + 1e-6f && same, "1. paste between formats: mono into 24-bit stereo at 44.1 kHz copies the channel (" + up.Frames + " frames), stereo into mono averages (largest error " + worst.ToString("0.0e0") + "), same format untouched");
        }

        // ── 2: the ripple of moments ──
        {
            var cut = new AudioSpan { at = 1.0, removed = 0.5, inserted = 0, oldLength = 3.0, newLength = 2.5 };
            var ins = new AudioSpan { at = 1.0, removed = 0, inserted = 0.25, oldLength = 3.0, newLength = 3.25 };
            var ovr = new AudioSpan { at = 2.8, removed = 0.2, inserted = 0.5, oldLength = 3.0, newLength = 3.3, overwrite = true };
            bool ok = Near(AudioRipple.MapStart(0.5, cut), 0.5) && Near(AudioRipple.MapStart(2.0, cut), 1.5) && Near(AudioRipple.MapStart(1.2, cut), 1.0)
                   && Near(AudioRipple.MapEnd(3.0, cut), 2.5) && Near(AudioRipple.MapEnd(1.0, cut), 1.0)
                   && Near(AudioRipple.MapStart(1.0, ins), 1.0) && Near(AudioRipple.MapEnd(1.0, ins), 1.25) && Near(AudioRipple.MapStart(0.99, ins), 0.99) && Near(AudioRipple.MapEnd(2.0, ins), 2.25)
                   && Near(AudioRipple.MapStart(1.5, ovr), 1.5) && Near(AudioRipple.MapEnd(3.0, ovr), 3.3) && Near(AudioRipple.MapEnd(2.9, ovr), 2.9);
            Check(ok, "2. moments: before an edit stay, after it move by the change, inside a cut close onto it; a start at an insert stays and an end there moves; an end at the file's end stays at its end; an overwrite moves nothing");
        }

        // ── 3: a curve on the file's own seconds keeps its value over untouched audio ──
        {
            const int rate = 48000; const float extra = 0.3f;
            var cases = new[] {
                ("cut 0.70-1.20 s", new AudioSpan { at = 0.7, removed = 0.5, inserted = 0, oldLength = 2.0, newLength = 1.5 }),
                ("insert 0.40 s at 1.10 s", new AudioSpan { at = 1.1, removed = 0, inserted = 0.4, oldLength = 2.0, newLength = 2.4 }),
                ("overwrite 0.60 s at 1.70 s (past the end)", new AudioSpan { at = 1.7, removed = 0.3, inserted = 0.6, oldLength = 2.0, newLength = 2.3, overwrite = true }),
            };
            foreach (var (label, e) in cases) {
                var before = SourceCurve(extra);
                var after = SourceCurve(extra);
                bool did = AudioRipple.Curve(after, e, rate);
                float drift = CurveDrift(before, after, e, extra, rate, out int n);
                Check(did && drift < 1e-4f && n > 100, "3. curve through a " + label + ": largest change over untouched audio " + drift.ToString("0.0e0") + " (" + n + " places)");
            }
        }

        // ── 4..8 run on real files and sounds; everything is undone at the end ──
        var settings = ZoundsProject.Instance.projectSettings;
        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        var lib = ZoundsProject.Instance.zoundLibrary;
        var modeBefore = settings.protectedEditPrompt;
        string workDir = settings.workFolderPath + "/_check33", srcDir = settings.sourcesFolderPath + "/_check33";
        var createdKeys = new List<string>();
        var keptBefore = ZoundsAudioEdits.Clipboard;
        var tempIds = new List<int>();
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
        try {
            settings.protectedEditPrompt = ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice;

            // ── 4: in place, on a Zounds-made file one local sound plays ──
            var orig = Make(16, 2, 48000, 1.0, 44);
            string wPath = workDir + "/check33 local.wav";
            string wGuid = WriteWav(wPath, orig);
            createdKeys.Add(wGuid);
            var zeq = new Zequence(ZoundLibrary.GetUniqueZoundId()) { name = "check33 zequence" };
            var local = NewKlip("check33 local", wGuid, wPath, 0.25f, 0.9f);
            local.parentId = zeq.id;
            zeq.localKlips.Add(local);
            zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = local.id, local = true, ownTrim = true, trimStart = 0.6f, trimEnd = 0.95f });
            tempIds.Add(zeq.id); tempIds.Add(local.id);
            ZoundsWindow.ModifyZoundsProject("check33 add", () => lib.zequences.Add(zeq));
            var volEnv = KlipChainEnvelopes.VolumeCurve(local, true);
            var volMod = KlipChainEnvelopes.ModifierOf(local, volEnv);
            if (volMod != null) {
                volMod.curveAnchor = CurveAnchor.Source; volMod.p[0] = 0.2f;
                var pts = volEnv.GetPointsList(); pts.Clear();
                pts.Add(new ZUIEnvelopePoint(0f, 0.3f, 1f)); pts.Add(new ZUIEnvelopePoint(0.3f, 0.9f, 1f)); pts.Add(new ZUIEnvelopePoint(0.75f, 0.2f, 1f)); pts.Add(new ZUIEnvelopePoint(1f, 0.5f, 1f));
            }
            var volBefore = volMod != null ? new ZoundModifier(ZoundModifierType.Envelope) { curveAnchor = CurveAnchor.Source } : null;
            if (volBefore != null) { volBefore.p[0] = 0.2f; var bp = volBefore.curve.GetPointsList(); bp.Clear(); foreach (var p in volEnv.GetPointsList()) bp.Add(new ZUIEnvelopePoint(p.time, p.value, p.exponent)); }
            byte[] origBytes = File.ReadAllBytes(ZoundsProtection.Absolute(wPath));

            var res = ZoundsAudioEdits.Apply(local, new ZoundsAudioEdits.Request { verb = ZoundsAudioEdits.Verb.Cut, selA = 0.1, selB = 0.2, cursor = 0.1 });
            var edited = ReadBack(wPath);
            int a = orig.FrameAt(0.1), b = orig.FrameAt(0.2);
            bool same = edited != null && edited.Frames == orig.Frames - (b - a) && SameFrames(orig, 0, edited, 0, a) && SameFrames(orig, b, edited, a, orig.Frames - b);
            var entry = zeq.zoundEntries[0];
            Check(res.done && same && PathOf(local) == wPath, "4. cut 0.1-0.2 s in place: the file is the old one minus that tenth, every other frame identical (" + (res.message ?? "") + ")");
            Check(Near(local.trimStart, 0.15f, 1e-4) && Near(local.trimEnd, 0.8f, 1e-4) && Near(entry.trimStart, 0.5f, 1e-4) && Near(entry.trimEnd, 0.85f, 1e-4),
                  "4. the trim (0.25-0.90 → " + local.trimStart.ToString("0.000") + "-" + local.trimEnd.ToString("0.000") + ") and the track's excerpt (0.60-0.95 → " + entry.trimStart.ToString("0.000") + "-" + entry.trimEnd.ToString("0.000") + ") stay on the same audio");
            if (volMod != null) {
                var span = new AudioSpan { at = (double)a / 48000, removed = (double)(b - a) / 48000, inserted = 0, oldLength = orig.Seconds, newLength = edited != null ? edited.Seconds : 0 };
                float drift = CurveDrift(volBefore, volMod, span, 0.2f, 48000, out int places);
                Check(drift < 1e-4f, "4. the volume curve keeps its value over the untouched audio (largest change " + drift.ToString("0.0e0") + ", " + places + " places)");
            }
            else Check(false, "4. could not make a volume curve on the test sound");
            Undo.PerformUndo();
            bool undone = File.ReadAllBytes(ZoundsProtection.Absolute(wPath)).AsSpan().SequenceEqual(origBytes);
            var localU = KlipById(local.id);
            Check(undone && localU != null && Near(localU.trimStart, 0.25f, 1e-5), "4. Undo puts the old file back byte for byte, and the trim with it");
            Undo.PerformRedo();
            var redone = ReadBack(wPath);
            Check(redone != null && redone.Frames == orig.Frames - (b - a), "4. Redo puts the edited file back");

            // "Repeat this hit four times": select a hit, duplicate-insert three times.
            local = KlipById(local.id);
            var beforeRepeat = ReadBack(wPath);
            double hA = 0.30, hB = 0.36, cur = hA, sA = hA, sB = hB;
            for (int i = 0; i < 3; i++) {
                var r = ZoundsAudioEdits.Apply(local, new ZoundsAudioEdits.Request { verb = ZoundsAudioEdits.Verb.DuplicateInsert, selA = sA, selB = sB, cursor = cur });
                if (!r.done) break;
                cur = r.cursor; sA = r.selA; sB = r.selB;
                local = KlipById(local.id);
            }
            var rep = ReadBack(wPath);
            int ha = beforeRepeat.FrameAt(hA), hb = beforeRepeat.FrameAt(hB), hl = hb - ha;
            bool four = rep != null && rep.Frames == beforeRepeat.Frames + 3 * hl;
            for (int i = 1; four && i <= 3; i++) four &= SameFrames(beforeRepeat, ha, rep, ha + i * hl, hl);
            four &= rep != null && SameFrames(beforeRepeat, hb, rep, hb + 3 * hl, beforeRepeat.Frames - hb);
            Check(four && Near(cur, (double)(hb + 3 * hl) / 48000, 1e-6), "4. repeat a hit four times: three duplicate-inserts give four identical hits, the rest of the file follows unchanged, the cursor ends after the last");

            // ── 8: only the last five versions are kept (two more edits: six in all, seven versions with the original) ──
            for (int i = 0; i < 2; i++) { local = KlipById(local.id); ZoundsAudioEdits.Apply(local, new ZoundsAudioEdits.Request { verb = ZoundsAudioEdits.Verb.Paste, cursor = 0.01 * (i + 1) }); }
            string vdir = ZoundsFileHistory.VersionsFolderFor(wGuid);
            int versions = Directory.Exists(vdir) ? Directory.GetFiles(vdir, "*.wav").Length : -1;
            Check(versions == ZoundsFileHistory.Keep, "8. after six edits of one file its versions folder holds only the last " + ZoundsFileHistory.Keep + " (" + versions + ")");

            // ── 5: a file in the Sources folder is never rewritten ──
            var srcPcm = Make(24, 1, 44100, 0.8, 55);
            string sPath = srcDir + "/check33 source.wav";
            string sGuid = WriteWav(sPath, srcPcm);
            createdKeys.Add(sGuid);
            byte[] sBytes = File.ReadAllBytes(ZoundsProtection.Absolute(sPath));
            var top = NewKlip("check33 source sound", sGuid, sPath, 0f, 0.8f);
            tempIds.Add(top.id);
            ZoundsWindow.ModifyZoundsProject("check33 add", () => lib.klips.Add(top));
            var info = ZoundsProtection.FileOf(top);
            var r5 = ZoundsAudioEdits.Apply(top, new ZoundsAudioEdits.Request { verb = ZoundsAudioEdits.Verb.Cut, selA = 0.2, selB = 0.3, cursor = 0.2 });
            string newPath = PathOf(top), newGuid = GuidOf(top);
            if (!string.IsNullOrEmpty(newGuid)) createdKeys.Add(newGuid);
            bool srcSame = File.ReadAllBytes(ZoundsProtection.Absolute(sPath)).AsSpan().SequenceEqual(sBytes);
            var np = newPath != null ? ReadBack(newPath) : null;
            Check(info.Protected && info.IsTrueSource && r5.done && srcSame && newPath != sPath && np != null && np.bits == 24 && np.Frames == srcPcm.Frames - srcPcm.FrameAt(0.3) + srcPcm.FrameAt(0.2),
                  "5. a Sources file is protected; the cut went to '" + newPath + "' (24-bit like the original), the source is byte for byte unchanged");
            Undo.PerformUndo();
            var topU = KlipById(top.id);
            bool gone = newPath != null && !File.Exists(ZoundsProtection.Absolute(newPath));
            Check(gone && topU != null && PathOf(topU) == sPath, "5. Undo removes the new file and the sound plays the source again");
            Undo.PerformRedo();
            topU = KlipById(top.id);
            Check(newPath != null && File.Exists(ZoundsProtection.Absolute(newPath)) && topU != null && GuidOf(topU) == newGuid && AssetDatabase.AssetPathToGUID(newPath) == newGuid,
                  "5. Redo brings the new file back under the same id, and the sound plays it again");

            // ── 6: a shared sound edited in its own editor goes to a copy ──
            string w2 = workDir + "/check33 shared.wav";
            string g2 = WriteWav(w2, Make(16, 1, 48000, 0.5, 66));
            createdKeys.Add(g2);
            var shared = NewKlip("check33 shared", g2, w2, 0.05f, 0.45f);
            var user = new Zequence(ZoundLibrary.GetUniqueZoundId()) { name = "check33 user" };
            user.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = shared.id, local = false });
            tempIds.Add(shared.id); tempIds.Add(user.id);
            ZoundsWindow.ModifyZoundsProject("check33 add", () => { lib.klips.Add(shared); lib.zequences.Add(user); });
            int origId = shared.id, idx = lib.klips.IndexOf(shared);
            bool isShared = ZoundsProtection.IsShared(shared);
            bool go = ZoundsEditGuard.BeforeSoundEdit(shared, out var sw);
            tempIds.Add(shared.id);
            ZoundsWindow.ModifyZoundsProject("check33 edit trim", () => shared.trimStart = 0.2f);
            var twin = lib.klips.Find(k => k.id == origId);
            bool userPlaysOriginal = user.TryGetEntryZound(user.zoundEntries[0], out var heard) && ReferenceEquals(heard, twin);
            Check(isShared && go && sw.swapped && shared.id != origId && shared.name == "check33 shared (copy)" && twin != null && !ReferenceEquals(twin, shared)
                  && lib.klips.IndexOf(twin) == idx && Near(twin.trimStart, 0.05f) && userPlaysOriginal,
                  "6. a shared sound: the edited object became '" + shared.name + "' (new id), an unchanged twin kept the id, the place and the trim, and the Zequence still plays it");
            bool back = ZoundsEditGuard.SwapBack(shared, origId);
            Check(back && shared.id == origId && shared.name == "check33 shared" && lib.klips.Find(k => k.id == origId) == shared && Near(shared.trimStart, 0.2f)
                  && !lib.klips.Exists(k => k.name == "check33 shared (copy)"),
                  "6. Edit the original instead: the edited object takes the original's id and name back, keeping its edit; the twin is gone");
            ZoundsEditGuard.ForgetAllowed(origId);
            settings.protectedEditPrompt = ZoundsProject.ProjectSettings.ProtectedEditPrompt.Ask;
            ZoundsEditGuard.dialogOverride = (t, m, o) => 1;
            bool cancelled = !ZoundsEditGuard.BeforeSoundEdit(shared, out var swc) && !swc.swapped && shared.id == origId;
            ZoundsEditGuard.dialogOverride = (t, m, o) => 2;
            bool original = ZoundsEditGuard.BeforeSoundEdit(shared, out var swo) && !swo.swapped && shared.id == origId && ZoundsEditGuard.EditsOriginal(shared);
            Check(cancelled && original, "6. Ask first: Cancel changes nothing; Edit the original goes ahead on the original and is remembered");
            ZoundsEditGuard.dialogOverride = null;
            ZoundsEditGuard.ForgetAllowed(origId);
            settings.protectedEditPrompt = ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice;

            // ── 7: a shared track edited from a Zequence gets a local copy ──
            var tEntry = user.zoundEntries[0];
            bool tgo = ZoundsEditGuard.BeforeTrackEdit(user, tEntry, out var ts);
            bool localNow = tEntry.local && user.localKlips.Count == 1 && user.localKlips[0].name == "check33 shared (copy)" && user.localKlips[0].id == tEntry.zoundId;
            if (localNow) tempIds.Add(user.localKlips[0].id);
            ts.editOriginal?.Invoke();
            bool revert = !tEntry.local && tEntry.zoundId == origId && user.localKlips.Count == 0 && ZoundsEditGuard.EditsOriginal(shared);
            Check(tgo && ts.swapped && localNow && revert, "7. a shared track: editing its curves gives the track a local copy named '(copy)'; Edit the original instead puts the shared sound back");
            ZoundsEditGuard.ForgetAllowed(origId);
        }
        catch (Exception e) { Check(false, "threw: " + e); }
        finally {
            ZoundsEditGuard.dialogOverride = null;
            settings.protectedEditPrompt = modeBefore;
            ZoundsAudioEdits.Clipboard = keptBefore;
            // Back to where the check started: every project change it made, undone without a redo.
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            lib = ZoundsProject.Instance.zoundLibrary;
            lib.klips.RemoveAll(k => tempIds.Contains(k.id));
            lib.zequences.RemoveAll(z => tempIds.Contains(z.id));
            foreach (var key in createdKeys) {
                string vdir = ZoundsFileHistory.VersionsFolderFor(key);
                ZoundsFileHistory.ForgetForCheck(key);
                try { if (Directory.Exists(vdir)) Directory.Delete(vdir, true); } catch (Exception e) { Debug.LogWarning(e.Message); }
                ZoundsAudioEdits.ForgetAddressable(key);
                string p = AssetDatabase.GUIDToAssetPath(key);
                if (!string.IsNullOrEmpty(p) && (p.StartsWith(settings.workFolderPath + "/Edits/") || p.StartsWith(workDir) || p.StartsWith(srcDir))) AssetDatabase.DeleteAsset(p);
            }
            foreach (var dir in new[] { workDir, srcDir })
                if (AssetDatabase.IsValidFolder(dir)) foreach (string g in AssetDatabase.FindAssets("", new[] { dir })) ZoundsAudioEdits.ForgetAddressable(g);
            AssetDatabase.DeleteAsset(workDir);
            AssetDatabase.DeleteAsset(srcDir);
            // The project's save makes shipped copies (and old-style renders) of the test sounds in the ZoundFiles folder.
            var lookIn = new List<string>();
            foreach (var f in new[] { settings.zoundFilesFolderPath, settings.workFolderPath + "/Edits" }) if (AssetDatabase.IsValidFolder(f)) lookIn.Add(f);
            if (lookIn.Count > 0) foreach (string guid in AssetDatabase.FindAssets("check33", lookIn.ToArray())) {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p).StartsWith("check33")) { ZoundsAudioEdits.ForgetAddressable(guid); AssetDatabase.DeleteAsset(p); }
            }
            // The versions folder, if this check was the only thing that made one.
            string versionsRoot = ZoundsProtection.Absolute(settings.systemFolderPath + "/Versions~");
            try { if (Directory.Exists(versionsRoot) && Directory.GetFileSystemEntries(versionsRoot).Length == 0) Directory.Delete(versionsRoot); } catch (Exception e) { Debug.LogWarning(e.Message); }
            string editsDir = settings.workFolderPath + "/Edits";
            if (AssetDatabase.IsValidFolder(editsDir) && Directory.GetFileSystemEntries(ZoundsProtection.Absolute(editsDir)).Length == 0) AssetDatabase.DeleteAsset(editsDir);
            if (projectBytes != null) {
                ZoundsWindow.isSavingJSON = true;
                try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                finally { ZoundsWindow.isSavingJSON = false; }
            }
            ZoundEngine.InvalidateLookups();
            AssetDatabase.SaveAssets();   // the Addressables groups, with the test files' entries gone
        }
        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - destructive editing\n" + sb;
    }

    static Klip KlipById(int id) {
        Klip found = null;
        ZoundsProject.Instance.zoundLibrary.ForEachZound(z => { if (found == null && z is Klip k && k.id == id) found = k; });
        return found;
    }

    static AudioPcm AudioPcmFromBytes(byte[] wav) {
        string tmp = Path.Combine(Path.GetTempPath(), "zounds_check33_" + Guid.NewGuid().ToString("N") + ".wav");
        try { File.WriteAllBytes(tmp, wav); return AudioPcm.ReadWav(tmp, out _); }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
