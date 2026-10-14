using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>
        /// Every column type × select, copy, cut, paste, Paste Insert, clear and Undo (T-0602), with the real attached
        /// window's keys: note, instrument, volume, pan, delay, sample command id and value, track FX id and value, a
        /// bound FX column in bar view, an envelope column and an event column; then a mixed selection across all of
        /// them, and pasting across column types (accepted where the cells hold the same kind of thing, refused with a
        /// status-line note where they do not).
        /// </summary>
        public static string CheckClipboardMatrix()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var song = CreateInstance<ZTrackerSong>();
            var clip = AudioClip.Create("clipboard-matrix-check", 4800, 1, 48000, false);
            var instrument = TrackerEngineCheck.FixtureInstrument(clip);
            void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why); }
            void Check(string title, Action test) { try { test(); passed++; report.AppendLine("PASS " + title); } catch (Exception e) { failed++; report.AppendLine("FAIL " + title + ": " + e.Message); } }
            void Key(KeyCode code, EventModifiers mods = EventModifiers.None) { window.grid.Focus(); using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, modifiers = mods })) { e.target = window.grid; window.grid.SendEvent(e); } }
            void Rebuild() { window.BuildPane(); LayoutChain(window); }
            string Json() => JsonUtility.ToJson(song.model);
            // What a cell holds, as shown (an envelope cell: its points in the row).
            string Sig(int r, int c)
            {
                var f = window.fields[c];
                if (f.kind == FieldKind.Envelope) return string.Join(",", window.EnvelopeRowPoints(f, r).Select(p => p.line + "=" + Mathf.RoundToInt(p.value * 255)));
                return window.Text(r, f);
            }
            void Fill(int c, int r0, int count)
            {
                var f = window.fields[c];
                window.SongEdit("fill", () =>
                {
                    for (int r = r0; r < r0 + count; r++)
                    {
                        int v = 0x10 + r * 7, rr = r;
                        if (f.kind == FieldKind.Envelope) { ZTrackerEnvelopes.Set(ZTrackerEnvelopes.EnsureLane(window.PatternTrack(f.track, true), song.model.tracks[f.track], window.EnvelopeColumn(f)), r, v / 255f); continue; }
                        window.EditField(r, f, (n, x, e) =>
                        {
                            switch (f.kind)
                            {
                                case FieldKind.Note: n.note = NoteKind.Note; n.pitch = 48 + rr; break;
                                case FieldKind.Instrument: n.instrumentPresent = true; n.instrument = rr; break;
                                case FieldKind.Volume: n.volume = new ColumnValue { kind = ValueKind.Value, value = Math.Min(0x80, v) }; break;
                                case FieldKind.Pan: n.pan = new ColumnValue { kind = ValueKind.Value, value = Math.Min(0x80, v / 2) }; break;
                                case FieldKind.Delay: n.delayPresent = true; n.delay = v; break;
                                case FieldKind.LocalId: n.sampleFx.present = true; n.sampleFx.identifier = rr % 2 == 0 ? "0A" : "0V"; break;
                                case FieldKind.LocalValue: n.sampleFx.valuePresent = true; n.sampleFx.value = v; break;
                                case FieldKind.EffectId: x.command.present = true; x.command.identifier = rr % 2 == 0 ? "0L" : "0P"; x.command.scope = CommandScope.Track; break;
                                case FieldKind.EffectValue: x.command.valuePresent = true; x.command.value = v; break;
                                case FieldKind.Event: e.present = true; e.payload = "ev" + rr; break;
                            }
                        });
                    }
                });
            }
            string Name(int c) { var f = window.fields[c]; return FieldName(f.kind) + (f.kind == FieldKind.EffectId || f.kind == FieldKind.EffectValue ? " FX" + (f.column + 1) + (window.BoundId(f) != null ? " (bound, bars)" : "") : f.kind == FieldKind.Envelope ? " ENV" + (f.column + 1) : ""); }
            try
            {
                song.schemaVersion = 1; song.model = TrackerEngineCheck.FixtureSongNew(instrument, lines: 32);
                var lead = song.model.tracks[0]; lead.name = "Lead";
                lead.columns = new List<ColumnVisibility> { new ColumnVisibility { volume = true, pan = true, delay = true, sampleFx = true } };
                lead.visibleEffectColumns = 2; lead.effectColumnViews.Add(new EffectColumnView { column = 1, command = "0M", bar = true });
                lead.envelopeColumns.Add(new EnvelopeColumnData { uid = "env-a", command = "0P" });
                var events = new TrackData { id = "events", name = "Events", kind = TrackKind.Event, visibleNoteColumns = 0, visibleEffectColumns = 0, outputTrackId = song.model.tracks[1].id };
                song.model.tracks.Insert(1, events); song.model.patterns[0].tracks.Add(new PatternTrack { trackId = "events" });
                window.song = song; window.instrument = instrument; window.pane = 0; window.editMode = true; window.Show(); window.position = new Rect(60, 60, 1300, 820); window.CreateGUI(); Rebuild();
                // Every column type the grid offers, once each (the bound FX column is its own row of the matrix).
                var columns = new List<int>();
                for (int c = 0; c < window.fields.Count; c++) { var f = window.fields[c]; if (f.track == 0 || f.track == 1) columns.Add(c); }
                Need(columns.Select(c => window.fields[c].kind).Distinct().Count() == 11, "the fixture does not show all eleven column types: " + string.Join(",", columns.Select(c => window.fields[c].kind).Distinct()));
                var rows = new List<string>();
                foreach (int c in columns)
                {
                    string name = Name(c);
                    Check(name + ": select, copy, cut, paste, Paste Insert, clear, each undone byte for byte", () =>
                    {
                        foreach (var pt in window.Pattern.tracks) { pt.lines.Clear(); pt.automation.Clear(); }
                        Rebuild(); Fill(c, 0, 4); Rebuild(); string empty = Sig(30, c);
                        var original = Enumerable.Range(0, 4).Select(r => Sig(r, c)).ToList();
                        Need(original.All(s => s != empty), "fixture did not fill the column: " + string.Join("|", original));
                        // select: keyboard block from row 0 to row 3
                        window.Select(0, c, false); for (int i = 0; i < 3; i++) Key(KeyCode.DownArrow, EventModifiers.Shift);
                        for (int r = 0; r < 4; r++) { var label = window.labels[new Vector2Int(r, c)]; Need(label.ClassListContains("tracker-selected") || label.ClassListContains("tracker-picked"), "row " + r + " is not drawn selected"); }
                        Need(!window.labels[new Vector2Int(4, c)].ClassListContains("tracker-selected"), "row 4 is drawn selected");
                        // copy + paste
                        Key(KeyCode.C, EventModifiers.Control); string before = Json();
                        window.Select(8, c, false); Key(KeyCode.V, EventModifiers.Control);
                        Need(Enumerable.Range(0, 4).All(i => Sig(8 + i, c) == original[i]), "paste: " + string.Join("|", Enumerable.Range(8, 4).Select(r => Sig(r, c))));
                        Undo.PerformUndo(); Need(Json() == before, "Undo of paste is not byte for byte");
                        // Paste Insert at row 1 (rows 1… move down by 4)
                        window.Select(1, c, false); Key(KeyCode.V, EventModifiers.Control | EventModifiers.Shift);
                        Need(Sig(0, c) == original[0] && Enumerable.Range(0, 4).All(i => Sig(1 + i, c) == original[i]) && Sig(5, c) == original[1] && Sig(7, c) == original[3], "Paste Insert: " + string.Join("|", Enumerable.Range(0, 8).Select(r => Sig(r, c))));
                        Undo.PerformUndo(); Need(Json() == before, "Undo of Paste Insert is not byte for byte");
                        // cut, then paste the cut cells elsewhere
                        window.Select(0, c, false); window.Select(3, c, true); Key(KeyCode.X, EventModifiers.Control);
                        Need(Enumerable.Range(0, 4).All(r => Sig(r, c) == empty), "cut left cells behind: " + string.Join("|", Enumerable.Range(0, 4).Select(r => Sig(r, c))));
                        window.Select(20, c, false); Key(KeyCode.V, EventModifiers.Control);
                        Need(Enumerable.Range(0, 4).All(i => Sig(20 + i, c) == original[i]), "cut cells did not paste");
                        Undo.PerformUndo(); Undo.PerformUndo(); Need(Json() == before, "Undo of cut and paste is not byte for byte");
                        // clear
                        window.Select(0, c, false); window.Select(3, c, true); Key(KeyCode.Delete);
                        Need(Enumerable.Range(0, 4).All(r => Sig(r, c) == empty), "Delete left cells behind");
                        Undo.PerformUndo(); Need(Json() == before, "Undo of clear is not byte for byte");
                        rows.Add(name);
                    });
                }

                Check("a mixed selection across every column of both tracks copies, pastes, Paste Inserts and clears as one block", () =>
                {
                    foreach (var pt in window.Pattern.tracks) { pt.lines.Clear(); pt.automation.Clear(); }
                    Rebuild(); foreach (int c in columns) Fill(c, 0, 3); Rebuild();
                    var original = columns.ToDictionary(c => c, c => Enumerable.Range(0, 3).Select(r => Sig(r, c)).ToList());
                    window.Select(0, columns.First(), false); window.Select(2, columns.Last(), true); Key(KeyCode.C, EventModifiers.Control);
                    window.Select(10, columns.First(), false); Key(KeyCode.V, EventModifiers.Control);
                    foreach (int c in columns) Need(Enumerable.Range(0, 3).All(i => Sig(10 + i, c) == original[c][i]), Name(c) + " did not paste in the mixed block");
                    string before = Json(); window.Select(1, columns.First(), false); Key(KeyCode.V, EventModifiers.Control | EventModifiers.Shift);
                    foreach (int c in columns) Need(Sig(1, c) == original[c][0] && Sig(4, c) == original[c][1], Name(c) + " did not Paste Insert in the mixed block");
                    Undo.PerformUndo(); Need(Json() == before, "Undo of the mixed Paste Insert is not byte for byte");
                    window.Select(10, columns.First(), false); window.Select(12, columns.Last(), true); Key(KeyCode.Delete);
                    foreach (int c in columns) Need(Sig(10, c) == Sig(30, c), Name(c) + " was not cleared in the mixed block");
                    Need(window.patternNotice == "", "a same-shape paste raised a notice: " + window.patternNotice);
                });

                Check("across column types: volume↔pan and sample↔track commands paste; mismatches are refused and named on the status line", () =>
                {
                    foreach (var pt in window.Pattern.tracks) { pt.lines.Clear(); pt.automation.Clear(); }
                    Rebuild();
                    int note = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.Note), vol = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.Volume), pan = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.Pan);
                    int lid = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.LocalId), fid = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.EffectId && f.column == 0), env = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.Envelope);
                    Fill(vol, 0, 2); Fill(lid, 0, 2); Fill(window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.LocalValue), 0, 2); Fill(note, 0, 2); Fill(env, 0, 2); Rebuild();
                    window.Select(0, vol, false); window.Select(1, vol, true); Key(KeyCode.C, EventModifiers.Control); window.Select(5, pan, false); Key(KeyCode.V, EventModifiers.Control);
                    Need(Sig(5, pan) == Sig(0, vol) && Sig(6, pan) == Sig(1, vol) && window.patternNotice == "", "volume did not paste into pan");
                    window.Select(0, lid, false); window.Select(1, lid + 1, true); Key(KeyCode.C, EventModifiers.Control); window.Select(5, fid, false); Key(KeyCode.V, EventModifiers.Control);
                    Need(Sig(5, fid) == Sig(0, lid) && Sig(5, fid + 1) == Sig(0, lid + 1) && Sig(6, fid) == Sig(1, lid), "sample command did not paste into the track FX column");
                    string before = Json();
                    window.Select(0, note, false); window.Select(1, note, true); Key(KeyCode.C, EventModifiers.Control); window.Select(10, vol, false); Key(KeyCode.V, EventModifiers.Control);
                    Need(Json() == before && window.help.text.Contains("Paste skipped 2 cells") && window.help.text.Contains("note") && window.help.text.Contains("volume"), "a note into a volume column was not refused with a note: " + window.help.text);
                    window.Select(0, env, false); Key(KeyCode.C, EventModifiers.Control); window.Select(10, fid + 1, false); Key(KeyCode.V, EventModifiers.Control | EventModifiers.Shift);
                    Need(window.help.text.Contains("skipped") && window.help.text.Contains("envelope"), "an envelope cell into an FX value column was not refused: " + window.help.text);
                    song.model.tracks[0].envelopeColumns.Add(new EnvelopeColumnData { uid = "env-b" }); Rebuild(); int unbound = window.fields.FindIndex(f => f.track == 0 && f.kind == FieldKind.Envelope && f.column == 1);
                    window.Select(0, env, false); Key(KeyCode.C, EventModifiers.Control); window.Select(3, unbound, false); Key(KeyCode.V, EventModifiers.Control);
                    Need(window.help.text.Contains("not bound"), "pasting into an unbound envelope column was not refused clearly: " + window.help.text);
                    window.Select(4, note, false); Need(window.help.text == "" || !window.help.text.Contains("skipped"), "the notice did not clear on the next cursor move");
                });
                report.AppendLine("MATRIX column types: " + string.Join("; ", rows));
            }
            finally { window.bindMenu?.Close(); window.StopPreview(); window.Close(); DestroyImmediate(window); Undo.ClearUndo(song); DestroyImmediate(song); DestroyImmediate(instrument); UnityEngine.Object.DestroyImmediate(clip); }
            report.AppendLine($"TOTAL passed={passed} failed={failed}"); return report.ToString();
        }
    }
}
