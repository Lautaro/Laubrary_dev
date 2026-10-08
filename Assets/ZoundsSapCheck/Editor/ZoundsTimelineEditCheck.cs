// A kept check for non-destructive editing in the Zequence window (T-0558): the window's own edit verbs and timeline
// placements, run on an IN-MEMORY Zequence holding a local copy of a real Klip (never added to the library).
//
//   1. A track is drawn as long as a play really lasts (the live plan), and the entry length the window shows agrees.
//   2. Split at 0.3-0.6 s makes three tracks, each with a local sound of its OWN trimmed to its piece (a local sound is
//      never shared between tracks, owner's rule 2026-10-08), contiguous, with every piece's audio exactly where it was.
//   3. Deleting the middle piece removes its own sound with it; with Ripple on, the piece after the gap moves up to close it.
//   4. Trim to a selection keeps the kept audio where it was (the piece's start moves); Untrim brings the whole source
//      back, again in place. Both edit the track's own sound's trim.
//   5. Copy and paste into the same Zequence gives the pasted piece a sound of its own trimmed to the copied part, placed
//      at the paste moment.
//   6. The pasted piece's sound is its own (not the source track's).
//   7. Bake: a one-piece bake equals that piece rendered on its own; a random-run bake repeats exactly from the same seed.
//   8. Nothing touches the source audio: the source clip's samples are unchanged by every step above.
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;

public static class ZoundsTimelineEditCheck {

    [MenuItem("Laubrary/Zounds/Checks/32 - Non-destructive editing (split, delete, trim, paste, bake)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        bool Near(float a, float b, float tol = 2e-3f) => Mathf.Abs(a - b) <= tol;

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";
        var clip = ZoundSapPlayback.LoadSourceClip(src);
        var pcm = ZoundPcmCache.Get(clip);
        float S = clip.length;
        long hashBefore = Hash(pcm.samples);

        var zeq = new Zequence(-9700) { name = "timeline check (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(c, -9701);
        c.name = "piece"; c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0;
        c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f;
        c.trimEnabled = true; c.trimStart = 0f; c.trimEnd = Mathf.Min(S, 1.2f);
        if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
        zeq.localKlips.Add(c);
        zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = c.id, local = true, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 1f });
        float L = c.trimEnd;
        var tl = new ZequenceTimeline { zeq = zeq };
        tl.Rebuild();
        TrackPlacement P(int i) { tl.Rebuild(); return tl.byEntry[zeq.zoundEntries[i]]; }

        // ── 1 ──
        ZoundSapPlayback.TryGetPlayLength(c, out float planned);
        float shown = CompositeZoundEditing.GetEntryDuration(zeq, zeq.zoundEntries[0], 1f);
        Check(Near(P(0).End, planned) && Near(shown, planned), "1. drawn length " + P(0).End.ToString("0.000") + " s, shown " + shown.ToString("0.000") + " s, a play lasts " + planned.ToString("0.000") + " s");

        // ── 2: split ──
        tl.Select(0.3f, 0.6f, zeq.zoundEntries[0], false);
        TimelineEdits.Split(null, tl);
        bool three = zeq.zoundEntries.Count == 3 && zeq.localKlips.Count == 3;
        var ids = three ? new System.Collections.Generic.HashSet<int> { zeq.zoundEntries[0].zoundId, zeq.zoundEntries[1].zoundId, zeq.zoundEntries[2].zoundId } : null;
        bool owned = three && ids.Count == 3 && zeq.zoundEntries.TrueForAll(e => e.local && !e.ownTrim && zeq.localKlips.Exists(k => k.id == e.zoundId));
        bool contiguous = three && Near(P(0).exA, 0f) && Near(P(0).exB, 0.3f) && Near(P(1).exA, 0.3f) && Near(P(1).exB, 0.6f) && Near(P(2).exA, 0.6f) && Near(P(2).exB, L);
        bool inPlace = three && Near(P(1).start, 0.3f) && Near(P(2).start, 0.6f) && Near(P(0).start, 0f);
        Check(three && owned, "2. split makes 3 tracks, each with a local sound of its own (" + zeq.zoundEntries.Count + " tracks, " + zeq.localKlips.Count + " sounds)");
        Check(contiguous && inPlace, "2. pieces 0-0.3, 0.3-0.6, 0.6-" + L.ToString("0.0") + " s, each starting where its audio was (" + (three ? P(1).start.ToString("0.000") + ", " + P(2).start.ToString("0.000") : "-") + ")");

        // ── 3: delete the middle, then the same with ripple ──
        var keepA = zeq.zoundEntries[0]; var keepC = zeq.zoundEntries[2];
        tl.ClearSelection(); tl.selTracks.Add(zeq.zoundEntries[1]);
        TimelineEdits.Delete(null, tl);
        Check(zeq.zoundEntries.Count == 2 && zeq.localKlips.Count == 2 && Near(P(1).start, 0.6f), "3. deleting the middle piece removes its own sound with it, the last piece stays at 0.6 s");
        zeq.zoundEntries.Insert(1, new CompositeZound.ZoundEntry { zoundId = c.id, local = true, ownTrim = true, trimStart = 0.3f, trimEnd = 0.6f, delay = 0.3f, overridePitch = true, pitch = 1f });
        tl.Rebuild();
        tl.ripple = true; tl.ClearSelection(); tl.selTracks.Add(zeq.zoundEntries[1]);
        TimelineEdits.Delete(null, tl);
        tl.ripple = false;
        Check(zeq.zoundEntries.Count == 2 && Near(P(1).start, 0.3f), "3. with Ripple on, the piece after the gap moves up to 0.3 s (" + P(1).start.ToString("0.000") + ")");

        // ── 4: trim to selection, untrim (on the track's own sound) ──
        tl.Select(0.1f, 0.2f, keepA, false);
        TimelineEdits.TrimToSelection(null, tl);
        Check(Near(c.trimStart, 0.1f) && Near(c.trimEnd, 0.2f) && !keepA.ownTrim && Near(P(0).start, 0.1f), "4. trim to 0.1-0.2 s: the piece now starts at " + P(0).start.ToString("0.000") + " s, its audio unmoved");
        tl.selTracks.Clear(); tl.selTracks.Add(keepA);
        TimelineEdits.Untrim(null, tl);
        Check(Near(c.trimStart, 0f) && Near(c.trimEnd, S, 0.01f) && Near(P(0).start, 0f), "4. untrim: the whole source again, starting at " + P(0).start.ToString("0.000") + " s");

        // ── 5: copy / paste ──
        tl.Select(0.4f, 0.5f, keepC, false);
        TimelineEdits.Copy(tl);
        int before = zeq.zoundEntries.Count;
        TimelineEdits.Paste(null, tl, 2.0f);
        var pasted = zeq.zoundEntries.Find(e => e != keepA && e != keepC);
        tl.Rebuild();
        var own = pasted != null ? zeq.localKlips.Find(k => k.id == pasted.zoundId) : null;
        Check(zeq.zoundEntries.Count == before + 1 && own != null && own.id != keepC.zoundId && own.trimEnabled && Near(own.trimStart, 0.7f, 0.003f) && Near(tl.byEntry[pasted].start, 2.0f),
              "5. paste at 2.0 s: a sound of its own, playing " + (own != null ? own.trimStart.ToString("0.000") + "-" + own.trimEnd.ToString("0.000") : "-") + " s of the recording");

        // ── 6: the pasted piece's sound is its own ──
        Check(own != null && !pasted.ownTrim && zeq.localKlips.Count == before + 1, "6. the pasted piece owns its sound (" + zeq.localKlips.Count + " local sounds for " + zeq.zoundEntries.Count + " tracks)");

        // ── 7: bake ──
        var one = new Zequence(-9710) { name = "bake check", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var b = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(c)); typeof(Zound).GetField("id").SetValue(b, -9711);
        b.trimEnabled = true; b.trimStart = 0.2f; b.trimEnd = Mathf.Min(S, 0.9f);
        one.localKlips.Add(b);
        one.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = b.id, local = true, delay = 0.25f, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 0.5f });
        var mix = ZequenceBake.Render(one, new ZequenceBake.Options { name = "x" });
        var alone = ZoundDspOffline.Render(pcm.samples, pcm.channels, pcm.frequency, mix.sampleRate, null, 1f, 1f, b.trimEnd - b.trimStart, b.trimStart, b.trimEnd);
        int off = Mathf.RoundToInt(0.25f * mix.sampleRate);
        float d = 0f, silence = 0f;
        for (int i = 0; i < alone.left.Length && off + i < mix.left.Length; i++) d = Mathf.Max(d, Mathf.Abs(mix.left[off + i] - 0.5f * alone.left[i]));
        for (int i = 0; i < off; i++) silence = Mathf.Max(silence, Mathf.Abs(mix.left[i]));
        Check(d < 1e-4f && silence == 0f, "7. a one-piece bake is the piece itself at its moment and volume (largest difference " + d.ToString("0.0e0") + ", silent before it)");
        var vary = new Zequence(-9720) { name = "seed check", mode = CompositeZound.Mode.Parallel, minPitch = 0.8f, maxPitch = 1.2f };
        var vb = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(b)); typeof(Zound).GetField("id").SetValue(vb, -9721); vb.minPitch = 0.7f; vb.maxPitch = 1.3f;
        vary.localKlips.Add(vb);
        vary.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = vb.id, local = true });
        var r1 = ZequenceBake.Render(vary, new ZequenceBake.Options { random = true, seed = 1234 });
        var r2 = ZequenceBake.Render(vary, new ZequenceBake.Options { random = true, seed = 1234 });
        var r3 = ZequenceBake.Render(vary, new ZequenceBake.Options { random = true, seed = 99 });
        bool same = r1.left.Length == r2.left.Length; for (int i = 0; same && i < r1.left.Length; i++) same = r1.left[i] == r2.left[i];
        Check(same && r3.left.Length != r1.left.Length, "7. a random run repeats exactly from its seed (" + r1.seconds.ToString("0.000") + " s twice), another seed differs (" + r3.seconds.ToString("0.000") + " s)");

        // ── 8 ──
        Check(Hash(pcm.samples) == hashBefore, "8. the source audio is untouched by every edit");

        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - non-destructive editing\n" + sb;
    }

    static long Hash(float[] s) { long h = 17; for (int i = 0; i < s.Length; i += 7) h = h * 31 + s[i].GetHashCode(); return h; }
}
