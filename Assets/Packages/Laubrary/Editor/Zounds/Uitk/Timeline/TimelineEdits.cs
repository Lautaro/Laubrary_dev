using System;
using System.Collections.Generic;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The editing verbs of the Zequence timeline (T-0564, T-0565): trim to a selection, untrim, split, delete, move, slip,
    /// copy, paste, make independent, ripple, and auditioning a selection. None of them reads, writes or copies audio: each
    /// is a change to a few numbers on tracks and Klips, one undo step per action.
    ///
    /// <b>Which trim an edit changes.</b> A trim made in a Zequence never changes the sound anywhere else. A local Klip
    /// played by this track alone has its own trim edited (the same numbers its Klip editor shows). A library Klip, or a
    /// local one shared by pieces split from it, gives the track its own excerpt instead, so the other uses are untouched.
    ///
    /// <b>Where a trimmed piece lands.</b> The kept audio stays exactly where it was on the timeline; the piece's start moves.
    /// Before the first trim change of a sound its curves move onto its source's own seconds, so they stay on their audio.
    /// </summary>
    internal static class TimelineEdits {

        const float MinPiece = 0.002f;

        /// <summary>One undo step: through the window when there is one (it refreshes itself), else straight to the project.</summary>
        static void Mod(ZequenceEditorWindowTK win, string undo, Action a) {
            if (win != null) win.Modify(undo, a); else ZoundsWindow.ModifyZoundsProject(undo, a);
        }

        // ─────────────────────────── the one place a trim changes ───────────────────────────

        public static bool EditsKlipTrim(TrackPlacement p)
            => p.entry.local && !p.entry.ownTrim && !CompositeZoundEditing.SharedLocally(p.parent, p.entry);

        /// <summary>Sets the part of the source <paramref name="p"/> plays. Call inside a modify step.</summary>
        public static void SetExcerpt(TrackPlacement p, float a, float b) {
            if (p?.klip == null) return;
            KlipChainEnvelopes.EnsureSourceAnchored(p.klip);
            float len = p.fileLen > 0f ? p.fileLen : Mathf.Max(a, b);
            a = Mathf.Clamp(a, 0f, len); b = Mathf.Clamp(b, 0f, len);
            if (b < a + MinPiece) b = Mathf.Min(len, a + MinPiece);
            if (EditsKlipTrim(p)) {
                p.klip.trimEnabled = true; p.klip.trimStart = a; p.klip.trimEnd = b;
                p.entry.ownTrim = false;
            }
            else { p.entry.ownTrim = true; p.entry.trimStart = a; p.entry.trimEnd = b; }
        }

        /// <summary>Moves <paramref name="p"/> so it starts sounding at Zequence time <paramref name="time"/>. False if that
        /// would be before its parent's start (then it starts there instead).</summary>
        public static bool StartAt(TrackPlacement p, float time) {
            float d = (time - p.parentStart) * p.parentPitch;
            bool ok = d >= -1e-5f;
            p.entry.delay = Mathf.Max(0f, d);
            return ok;
        }

        // ─────────────────────────── verbs ───────────────────────────

        /// <summary>Trim each selected track to the selection, keeping the kept audio where it was.</summary>
        public static string TrimToSelection(ZequenceEditorWindowTK win, ZequenceTimeline tl) {
            if (!tl.hasSel) return "Select a part of a track first.";
            int n = 0; bool clipped = false;
            Mod(win, "trim to selection", () => {
                foreach (var p in Selected(tl)) {
                    if (p.klip == null) continue;
                    float a = Mathf.Max(0f, p.TimeToSource(tl.selA)), b = Mathf.Min(p.fileLen, p.TimeToSource(tl.selB));
                    if (b <= a + MinPiece) continue;
                    float at = p.SourceToTime(a);
                    SetExcerpt(p, a, b);
                    clipped |= !StartAt(p, at);
                    n++;
                }
            });
            return n == 0 ? "The selection holds no audio of a selected track." : (clipped ? "Trimmed; a piece would have started before the Zequence, so it starts at 0." : "Trimmed " + n + (n == 1 ? " track." : " tracks."));
        }

        /// <summary>Plays each selected track's whole source again, its audio staying where it was.</summary>
        public static string Untrim(ZequenceEditorWindowTK win, ZequenceTimeline tl) {
            int n = 0; bool clipped = false;
            Mod(win, "untrim", () => {
                foreach (var p in Selected(tl)) {
                    if (p.klip == null || p.fileLen <= 0f) continue;
                    float at = p.SourceToTime(0f);
                    SetExcerpt(p, 0f, p.fileLen);
                    clipped |= !StartAt(p, at);
                    n++;
                }
            });
            return n == 0 ? "Select a track first." : clipped ? "Untrimmed; the start of the audio falls before the Zequence, so the piece moved to 0." : "Untrimmed " + n + (n == 1 ? " track." : " tracks.");
        }

        /// <summary>Splits each selected track at the selection's start and end (or at the cursor time when the selection is
        /// a single point), each piece sharing the sound and playing its own excerpt; the middle piece is left selected.</summary>
        public static string Split(ZequenceEditorWindowTK win, ZequenceTimeline tl) {
            if (!tl.hasSel) return "Select a moment or a part of a track first.";
            int made = 0; bool renumbered = false;
            CompositeZound.ZoundEntry middle = null;
            Mod(win, "split", () => {
                foreach (var p in Selected(tl)) {
                    if (p.klip == null) continue;
                    var cuts = new List<float>();
                    foreach (var t in new[] { tl.selA, tl.selB }) {
                        if (t <= p.start + 1e-4f || t >= p.End - 1e-4f) continue;
                        float s = p.TimeToSource(t);
                        if (s > p.exA + MinPiece && s < p.exB - MinPiece && (cuts.Count == 0 || s > cuts[cuts.Count - 1] + MinPiece)) cuts.Add(s);
                    }
                    if (cuts.Count == 0) continue;
                    var bounds = new List<float> { p.exA }; bounds.AddRange(cuts); bounds.Add(p.exB);
                    int at = p.parent.zoundEntries.IndexOf(p.entry);
                    renumbered |= RenumbersUnnamed(p.parent, at + 1);
                    var times = new List<float>();
                    foreach (var b in bounds) times.Add(p.SourceToTime(b));
                    // The original keeps the first piece; the rest go straight after it.
                    SetExcerptShared(p, bounds[0], bounds[1]);
                    for (int i = 1; i < bounds.Count - 1; i++) {
                        var e = CloneEntry(p.entry);
                        p.parent.zoundEntries.Insert(at + i, e);
                        var q = PlacementLike(p, e);
                        e.ownTrim = true; e.trimStart = bounds[i]; e.trimEnd = bounds[i + 1];
                        StartAt(q, times[i]);
                        if (bounds.Count == 4 && i == 1) middle = e;
                        if (bounds.Count == 3 && tl.selB > tl.selA + 1e-4f && i == 1 && Mathf.Abs(times[1] - tl.selA) < 1e-3f) middle = e;
                        made++;
                    }
                }
            });
            if (made == 0) return "The selection does not cross any selected track.";
            if (middle != null) { tl.selTracks.Clear(); tl.selTracks.Add(middle); }
            return "Split into " + (made + 1) + " pieces sharing one sound." + (renumbered ? " Tracks after it were renumbered; give a track an id (its bolt) if game code reaches it by number." : "");
        }

        /// <summary>Deletes the selected part of each selected track (the whole track when the selection covers it), and,
        /// with Ripple on, closes the gap in every track starting after it.</summary>
        public static string Delete(ZequenceEditorWindowTK win, ZequenceTimeline tl) {
            var sel = Selected(tl);
            if (sel.Count == 0) return "Select a track first.";
            int removed = 0, cut = 0; bool renumbered = false;
            float gapA = tl.hasSel ? tl.selA : float.MaxValue, gapB = tl.hasSel ? tl.selB : float.MinValue;
            if (!tl.hasSel) foreach (var p in sel) { gapA = Mathf.Min(gapA, p.start); gapB = Mathf.Max(gapB, p.End); }
            var straddle = new List<TrackPlacement>();
            Mod(win, "delete", () => {
                // Highest index first, so the indices of the rest stay valid.
                sel.Sort((x, y) => y.parent.zoundEntries.IndexOf(y.entry).CompareTo(x.parent.zoundEntries.IndexOf(x.entry)));
                foreach (var p in sel) {
                    bool whole = !tl.hasSel || p.klip == null || (tl.selA <= p.start + 1e-4f && tl.selB >= p.End - 1e-4f);
                    int idx = p.parent.zoundEntries.IndexOf(p.entry);
                    if (idx < 0) continue;
                    if (whole) {
                        renumbered |= RenumbersUnnamed(p.parent, idx + 1);
                        RemoveEntryAt(p.parent, idx);
                        removed++;
                        continue;
                    }
                    float a = p.TimeToSource(Mathf.Max(tl.selA, p.start)), b = p.TimeToSource(Mathf.Min(tl.selB, p.End));
                    bool left = a > p.exA + MinPiece, right = b < p.exB - MinPiece;
                    float rightAt = p.SourceToTime(b) - (tl.ripple ? (tl.selB - tl.selA) : 0f);
                    float exA = p.exA, exB = p.exB;
                    if (left && right) {
                        SetExcerptShared(p, exA, a);
                        var e = CloneEntry(p.entry);
                        p.parent.zoundEntries.Insert(idx + 1, e);
                        renumbered |= RenumbersUnnamed(p.parent, idx + 2);
                        e.ownTrim = true; e.trimStart = b; e.trimEnd = exB;
                        StartAt(PlacementLike(p, e), rightAt);
                    }
                    else if (left) SetExcerpt(p, exA, a);
                    else if (right) { SetExcerpt(p, b, exB); StartAt(p, rightAt); }
                    cut++;
                }
                if (tl.ripple && tl.zeq.mode == CompositeZound.Mode.Parallel) Ripple(tl, gapB, -(gapB - gapA), sel, straddle);
            });
            MarkStraddling(tl, straddle);
            tl.ClearSelection();
            string s = removed > 0 && cut > 0 ? "Removed " + removed + " and cut " + cut + "." : removed > 0 ? "Removed " + removed + (removed == 1 ? " track." : " tracks.") : "Cut the selection out of " + cut + (cut == 1 ? " track." : " tracks.");
            if (straddle.Count > 0) s += " " + straddle.Count + " track(s) sounding across the gap stayed where they were (marked).";
            if (renumbered) s += " Later tracks were renumbered; give a track an id if game code reaches it by number.";
            return s;
        }

        /// <summary>Moves a piece in time by <paramref name="seconds"/> (a drag on its top strip). Inside a modify step.</summary>
        public static void Move(TrackPlacement p, float originalStart, float seconds) => StartAt(p, Mathf.Max(originalStart + seconds, p.parentStart));

        /// <summary>Slides which part of the source a piece plays, keeping its place and length. Inside a modify step.</summary>
        public static void Slip(TrackPlacement p, float originalA, float originalB, float sourceSeconds) {
            float len = originalB - originalA;
            float a = Mathf.Clamp(originalA + sourceSeconds, 0f, Mathf.Max(0f, p.fileLen - len));
            SetExcerpt(p, a, a + len);
        }

        // ─────────────────────────── copy / paste ───────────────────────────

        [Serializable] class ClipItem { public string entryJson, klipJson; public int parentId; public bool local; public float offset, a, b; }
        static readonly List<ClipItem> clipboard = new List<ClipItem>();
        static float clipboardSpan;

        public static bool CanPaste => clipboard.Count > 0;

        public static string Copy(ZequenceTimeline tl) {
            var sel = Selected(tl);
            if (sel.Count == 0) return "Select a track first.";
            clipboard.Clear();
            float baseT = float.MaxValue;
            var items = new List<(TrackPlacement p, float s, float e)>();
            foreach (var p in sel) {
                if (p.klip == null) continue;
                float s = tl.hasSel ? Mathf.Max(tl.selA, p.start) : p.start, e = tl.hasSel ? Mathf.Min(tl.selB, p.End) : p.End;
                if (e <= s + 1e-4f) continue;
                items.Add((p, s, e)); baseT = Mathf.Min(baseT, s);
            }
            clipboardSpan = 0f;
            foreach (var (p, s, e) in items) {
                var it = new ClipItem {
                    entryJson = JsonUtility.ToJson(p.entry), parentId = p.parent.id, local = p.entry.local,
                    klipJson = p.entry.local ? JsonUtility.ToJson(p.klip) : null,
                    offset = s - baseT, a = p.TimeToSource(s), b = p.TimeToSource(e),
                };
                clipboard.Add(it);
                clipboardSpan = Mathf.Max(clipboardSpan, e - baseT);
            }
            return clipboard.Count == 0 ? "Nothing with audio in the selection." : "Copied " + clipboard.Count + (clipboard.Count == 1 ? " piece." : " pieces.");
        }

        /// <summary>Pastes the copied pieces at <paramref name="at"/> as new tracks; with Ripple on, pushes every track that
        /// starts at or after that moment later by the pasted length.</summary>
        public static string Paste(ZequenceEditorWindowTK win, ZequenceTimeline tl, float at) {
            if (clipboard.Count == 0) return "Copy something first.";
            var zeq = tl.zeq;
            var straddle = new List<TrackPlacement>();
            var pasted = new List<CompositeZound.ZoundEntry>();
            Mod(win, "paste", () => {
                if (tl.ripple && zeq.mode == CompositeZound.Mode.Parallel) Ripple(tl, at, clipboardSpan, null, straddle);
                float zp = ZequenceTimeline.Mid(zeq);
                foreach (var it in clipboard) {
                    var e = JsonUtility.FromJson<CompositeZound.ZoundEntry>(it.entryJson);
                    e.zpocId = "";
                    if (it.local) {
                        // Same Zequence and its Klip still there: the new piece shares it. Anywhere else: a private copy,
                        // because one Zequence's local sound cannot belong to another.
                        bool share = it.parentId == zeq.id && zeq.localKlips.Exists(k => k.id == e.zoundId);
                        if (!share && !string.IsNullOrEmpty(it.klipJson)) {
                            var src = JsonUtility.FromJson<Klip>(it.klipJson);
                            var k = new Klip(ZoundLibrary.GetUniqueZoundId(), src);
                            k.parentId = zeq.id; k.tags.Clear();
                            zeq.localKlips.Add(k);
                            e.zoundId = k.id;
                        }
                    }
                    e.ownTrim = true; e.trimStart = it.a; e.trimEnd = it.b;
                    e.delay = Mathf.Max(0f, at + it.offset) * zp;
                    // Straight after the track it came from when that is here; otherwise at the end.
                    int idx = zeq.zoundEntries.FindIndex(x => x.zoundId == e.zoundId);
                    if (idx >= 0) { while (idx + 1 < zeq.zoundEntries.Count && zeq.zoundEntries[idx + 1].zoundId == e.zoundId) idx++; zeq.zoundEntries.Insert(idx + 1, e); }
                    else zeq.zoundEntries.Add(e);
                    pasted.Add(e);
                }
            });
            MarkStraddling(tl, straddle);
            tl.selTracks.Clear(); foreach (var e in pasted) tl.selTracks.Add(e);
            tl.hasSel = true; tl.selA = at; tl.selB = at + clipboardSpan;
            return "Pasted " + pasted.Count + (pasted.Count == 1 ? " piece" : " pieces") + " at " + ZequenceTimeline.Seconds(at) + "." + (straddle.Count > 0 ? " " + straddle.Count + " track(s) sounding across it stayed (marked)." : "");
        }

        /// <summary>Gives each selected track a private copy of its sound, trimmed to what the track plays: from now on its
        /// processing is its own.</summary>
        public static string MakeIndependent(ZequenceEditorWindowTK win, ZequenceTimeline tl) {
            var sel = Selected(tl);
            int n = 0;
            Mod(win, "make independent", () => {
                foreach (var p in sel) {
                    if (p.klip == null) continue;
                    if (!p.entry.local || CompositeZoundEditing.SharedLocally(p.parent, p.entry) || p.entry.ownTrim) {
                        float a = p.exA, b = p.exB;
                        var copy = new Klip(ZoundLibrary.GetUniqueZoundId(), p.klip);
                        copy.parentId = p.parent.id; copy.tags.Clear();
                        if (!p.entry.local) copy.originalId = p.klip.id;
                        p.parent.localKlips.Add(copy);
                        p.entry.zoundId = copy.id; p.entry.local = true;
                        copy.trimEnabled = true; copy.trimStart = a; copy.trimEnd = b;
                        p.entry.ownTrim = false;
                        n++;
                    }
                }
            });
            return n == 0 ? "The selected tracks already have sounds of their own." : n + (n == 1 ? " track now has" : " tracks now have") + " a sound of its own.";
        }

        // ─────────────────────────── audition ───────────────────────────

        /// <summary>Plays just the selection through each selected track's live chain (or, with no track selected, the whole
        /// Zequence over that range); looping while Loop is on. Never writes anything.</summary>
        public static List<ZoundToken> Audition(ZequenceTimeline tl, bool isLocal) {
            var tokens = new List<ZoundToken>();
            if (!tl.hasSel) return tokens;
            var sel = Selected(tl);
            if (sel.Count == 0) {
                var t = PlayFrom(tl.zeq, tl.selA, isLocal);
                if (t != null) tokens.Add(t);
                return tokens;
            }
            foreach (var p in sel) {
                if (p.klip == null) continue;
                float a = Mathf.Max(p.exA, p.TimeToSource(tl.selA)), b = Mathf.Min(p.exB, p.TimeToSource(tl.selB));
                if (b <= a + MinPiece) continue;
                var args = new ZoundArgs {
                    startImmediately = true, delay = Mathf.Max(0f, p.SourceToTime(a) - tl.selA),
                    volumeOverride = -1f, pitchOverride = p.pitch, chanceOverride = 1f,
                    bypassGlobalSolo = true, ignoreCooldown = true,
                    excerpt = true, excerptStart = a, excerptEnd = b, excerptLoop = tl.loop,
                };
                var t = ZoundEngine.PlayZound(p.klip, args);
                if (t != null) tokens.Add(t);
            }
            return tokens;
        }

        /// <summary>The whole Zequence from <paramref name="time"/> on (Play from here).</summary>
        public static ZoundToken PlayFrom(Zequence zeq, float time, bool isLocal) => ZoundEngine.PlayZound(zeq, new ZoundArgs {
            startImmediately = true, volumeOverride = -1f, pitchOverride = -1f, chanceOverride = -1f,
            useFixedAverageValues = true, bypassGlobalSolo = isLocal, ignoreCooldown = true, startAt = Mathf.Max(0f, time),
        });

        // ─────────────────────────── helpers ───────────────────────────

        public static List<TrackPlacement> Selected(ZequenceTimeline tl) {
            var list = new List<TrackPlacement>();
            foreach (var e in tl.selTracks) if (tl.byEntry.TryGetValue(e, out var p) && p.found) list.Add(p);
            return list;
        }

        /// <summary>After a split, every piece plays its own excerpt of the one shared sound.</summary>
        static void SetExcerptShared(TrackPlacement p, float a, float b) {
            KlipChainEnvelopes.EnsureSourceAnchored(p.klip);
            p.entry.ownTrim = true; p.entry.trimStart = a; p.entry.trimEnd = b;
        }

        static CompositeZound.ZoundEntry CloneEntry(CompositeZound.ZoundEntry e) {
            var c = JsonUtility.FromJson<CompositeZound.ZoundEntry>(JsonUtility.ToJson(e));
            c.zpocId = "";
            c.volumeEnvelope = e.volumeEnvelope != null ? e.volumeEnvelope.DeepCopy() : null;
            return c;
        }

        static TrackPlacement PlacementLike(TrackPlacement p, CompositeZound.ZoundEntry e) => new TrackPlacement {
            parent = p.parent, entry = e, parentPitch = p.parentPitch, parentStart = p.parentStart, start = p.parentStart + e.delay / Mathf.Max(p.parentPitch, 0.01f),
            klip = p.klip, zound = p.zound, found = true, fileLen = p.fileLen, pitch = p.pitch,
        };

        /// <summary>Whether inserting or removing at <paramref name="from"/> renumbers a track game code can only reach by number.</summary>
        static bool RenumbersUnnamed(CompositeZound parent, int from) {
            for (int i = from; i < parent.zoundEntries.Count; i++) if (string.IsNullOrEmpty(parent.zoundEntries[i].zpocId)) return true;
            return false;
        }

        static void RemoveEntryAt(CompositeZound parent, int idx) {
            var e = parent.zoundEntries[idx];
            if (e.local && !CompositeZoundEditing.SharedLocally(parent, e)) {
                parent.localKlips.RemoveAll(k => k.id == e.zoundId);
                parent.localZequences.RemoveAll(l => l.zequence.id == e.zoundId);
            }
            parent.zoundEntries.RemoveAt(idx);
        }

        /// <summary>Moves every top-level track that starts at or after <paramref name="at"/> by <paramref name="delta"/>
        /// seconds; a track already sounding at that moment stays where it is and is reported.</summary>
        static void Ripple(ZequenceTimeline tl, float at, float delta, List<TrackPlacement> except, List<TrackPlacement> straddle) {
            foreach (var p in tl.tracks) {
                if (p.depth != 0 || (except != null && except.Contains(p))) continue;
                if (p.start >= at - 1e-4f) StartAt(p, Mathf.Max(0f, p.start + delta));
                else if (p.End > at + 1e-4f) straddle.Add(p);
            }
        }

        static void MarkStraddling(ZequenceTimeline tl, List<TrackPlacement> straddle) {
            tl.straddling.Clear();
            foreach (var p in straddle) tl.straddling.Add(p.entry);
        }
    }
}
