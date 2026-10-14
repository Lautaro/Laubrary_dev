using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    // One instrument column per track (T-0648): the conversion of an older song is lossless (rendered PCM compared sample for
    // sample), a track that played two instruments at once is split, and a value on the track column reaches every note column.
    public sealed partial class ZTrackerModelWindow
    {
        static NoteCell TiNote(int column, int pitch, int? instrument)
        {
            var n = TrackerEngineCheck.Note(pitch, column, 128); n.instrumentPresent = instrument.HasValue; n.instrument = instrument ?? 0; return n;
        }

        static SongData TiSong(ZTrackerInstrument a, ZTrackerInstrument b, int columns, params (int row, NoteCell[] cells)[] rows)
        {
            var song = TrackerEngineCheck.FixtureSong(a, columns, 32); song.instruments.Add(b);
            foreach (var r in rows) song.patterns[0].tracks[0].WriteLine(new PatternLine { line = r.row, notes = r.cells.ToList() });
            return song;
        }

        public static string CheckTrackInstruments()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            void Check(string name, Action run) { try { run(); passed++; report.AppendLine("PASS " + name); } catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + ex.Message); } }
            void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
            var a = SlideInstrument(SlideSource.Synth); var b = SlideInstrument(SlideSource.Synth);
            b.model.parameters.waveA = 2; b.model.parameters.waveB = 2; b.model.parameters.sustain = .6f;
            try
            {
                Check("a song from before the rule keeps playing identically: one column that switches instrument over time", () =>
                {
                    SongData Build() => TiSong(a, b, 1, (0, new[] { TiNote(0, 60, 0) }), (4, new[] { TiNote(0, 64, null) }), (8, new[] { TiNote(0, 67, 1) }), (12, new[] { TiNote(0, 60, null) }), (16, new[] { new NoteCell { column = 0, instrumentPresent = true, instrument = 0 } }), (18, new[] { TiNote(0, 72, null) }));
                    var before = SlideRender(Build(), SlideRate * 3); var song = Build();
                    var notes = ZTrackerTrackInstruments.Migrate(song);
                    var after = SlideRender(song, SlideRate * 3);
                    Need(notes.Count == 0 && song.instrumentColumnVersion == 1, "Migration said " + string.Join("; ", notes));
                    Need(before.Length == after.Length && before.Zip(after, (x, y) => x == y).All(v => v), "The rendered sound changed");
                    Need(before.Max(Math.Abs) > .01f, "Silent render");
                    var track = song.patterns[0].tracks[0];
                    Need(track.ReadLine(8).notes[0].instrumentPresent && track.ReadLine(8).notes[0].instrument == 1 && track.ReadLine(16).notes[0].instrument == 0, "The instrument cells were not kept");
                });
                Check("a chord in three columns converts to one instrument cell, and plays identically", () =>
                {
                    SongData Build() => TiSong(a, b, 3, (0, new[] { TiNote(0, 60, 1), TiNote(1, 64, 1), TiNote(2, 67, 1) }), (8, new[] { TiNote(0, 62, null), TiNote(1, 65, null), TiNote(2, 69, null) }));
                    var before = SlideRender(Build(), SlideRate * 2); var song = Build();
                    var notes = ZTrackerTrackInstruments.Migrate(song); var after = SlideRender(song, SlideRate * 2);
                    Need(notes.Count == 0 && song.tracks.Count == 2, "Unexpected split: " + string.Join("; ", notes));
                    Need(before.Zip(after, (x, y) => x == y).All(v => v), "The rendered sound changed");
                    var line = song.patterns[0].tracks[0].ReadLine(0);
                    Need(line.notes.Count(n => n.instrumentPresent) == 1 && line.notes.Find(n => n.column == 0).instrumentPresent, "More than one instrument cell on the row");
                });
                Check("two columns that really play different instruments at once: the track splits into one track per note column", () =>
                {
                    SongData Build() => TiSong(a, b, 2, (0, new[] { TiNote(0, 60, 0), TiNote(1, 64, 1) }), (8, new[] { TiNote(0, 62, null), TiNote(1, 66, null) }));
                    var before = SlideRender(Build(), SlideRate * 2); var song = Build();
                    var notes = ZTrackerTrackInstruments.Migrate(song);
                    Need(notes.Count == 1 && notes[0].StartsWith("TRACK_SPLIT"), "No split note: " + string.Join("; ", notes));
                    Need(song.tracks.Count == 3 && song.tracks[0].visibleNoteColumns == 1 && song.tracks[1].visibleNoteColumns == 1, "Tracks " + song.tracks.Count);
                    Need(song.patterns[0].tracks.Count == 3 && song.diagnostics.Any(d => d.StartsWith("TRACK_SPLIT")), "Pattern tracks " + song.patterns[0].tracks.Count);
                    var after = SlideRender(song, SlideRate * 2);
                    double worst = 0; for (int i = 0; i < before.Length; i++) worst = Math.Max(worst, Math.Abs(before[i] - after[i]));
                    report.AppendLine("MEASURE split render largest sample difference " + worst.ToString("0.0E+0"));
                    Need(worst < 1e-5, "The rendered sound changed by " + worst);
                });
                Check("a value on the track column reaches every note column: an instrument-only cell, then a note in column 2", () =>
                {
                    var song = TiSong(a, b, 2, (0, new[] { TiNote(0, 60, 0) }), (4, new[] { new NoteCell { column = 0, instrumentPresent = true, instrument = 1 } }), (6, new[] { TiNote(1, 64, null) }));
                    song.instrumentColumnVersion = ZTrackerTrackInstruments.CurrentVersion;
                    var legacy = TiSong(a, b, 2, (0, new[] { TiNote(0, 60, 0) }), (4, new[] { new NoteCell { column = 0, instrumentPresent = true, instrument = 1 }, new NoteCell { column = 1, instrumentPresent = true, instrument = 1 } }), (6, new[] { TiNote(1, 64, null) }));
                    var x = SlideRender(song, SlideRate * 2); var y = SlideRender(legacy, SlideRate * 2);
                    Need(x.Zip(y, (p, q) => p == q).All(v => v), "The note in column 2 did not play the track's instrument");
                    var withoutRule = TiSong(a, b, 2, (0, new[] { TiNote(0, 60, 0) }), (4, new[] { new NoteCell { column = 0, instrumentPresent = true, instrument = 1 } }), (6, new[] { TiNote(1, 64, null) }));
                    var z = SlideRender(withoutRule, SlideRate * 2);
                    Need(!x.Zip(z, (p, q) => p == q).All(v => v), "Column 2 sounded the same without the rule, so the check measures nothing");
                });
                Check("the instrument in effect at a row, and a song made new starts on the new rule", () =>
                {
                    var song = TiSong(a, b, 1, (0, new[] { TiNote(0, 60, 0) }), (8, new[] { TiNote(0, 60, 1) }));
                    var track = song.patterns[0].tracks[0];
                    Need(ZTrackerTrackInstruments.InstrumentAt(track, 7) == 0 && ZTrackerTrackInstruments.InstrumentAt(track, 8) == 1 && ZTrackerTrackInstruments.InstrumentAt(track, -1) == -1, "InstrumentAt " + ZTrackerTrackInstruments.InstrumentAt(track, 7) + "/" + ZTrackerTrackInstruments.InstrumentAt(track, 8));
                    Need(!ZTrackerTrackInstruments.Applies(new SongData()) && ZTrackerTrackInstruments.Applies(new SongData { instrumentColumnVersion = 1 }), "Applies");
                    var again = ZTrackerTrackInstruments.Migrate(song); var json = JsonUtility.ToJson(track);
                    Need(again.Count == 0 && ZTrackerTrackInstruments.Migrate(song).Count == 0 && JsonUtility.ToJson(song.patterns[0].tracks[0]) == json, "Migrating twice changed the song");
                });
            }
            finally { DestroyImmediate(a); DestroyImmediate(b); }
            return "CheckTrackInstruments passed=" + passed + " failed=" + failed + "\n" + report;
        }

        public static string CheckTrackInstrumentsUi()
        {
            var report = new StringBuilder(); int passed = 0, failed = 0;
            var window = CreateInstance<ZTrackerModelWindow>(); var songAsset = CreateInstance<ZTrackerSong>();
            var a = SlideInstrument(SlideSource.Synth); var b = SlideInstrument(SlideSource.Synth);
            songAsset.schemaVersion = 1;
            void Fresh(int version) { songAsset.model = TiSong(a, b, 3, (0, new[] { TiNote(0, 60, 0), TiNote(1, 64, 0), TiNote(2, 67, 0) })); songAsset.model.instrumentColumnVersion = version; }
            Fresh(0); window.song = songAsset; window.pane = 0;
            window.Show(); window.position = new Rect(160, 120, 1120, 760); window.CreateGUI();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            void Need(bool value, string message) { if (!value) throw new Exception(message); }
            void Layout() { var panel = window.rootVisualElement.panel; Need(panel != null, "Window not attached"); for (int i = 0; i < 3; i++) panel.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null); }
            void Check(string name, Action action)
            {
                try { window.StopPreview(); window.pane = 0; action(); passed++; report.AppendLine("PASS " + name); }
                catch (Exception ex) { failed++; report.AppendLine("FAIL " + name + ": " + (ex.InnerException ?? ex).Message); }
            }
            try
            {
                Check("an older song is shown converted without touching the asset (the first edit saves it converted), and the grid draws one instrument column for the three note columns", () =>
                {
                    Fresh(0); window.BuildPane(); Layout();
                    Need(songAsset.model.instrumentColumnVersion == 0 && window.Data.instrumentColumnVersion == 1, "Reading must show the converted song and leave the asset alone");
                    int notes = window.fields.Count(f => f.track == 0 && f.kind == FieldKind.Note), instruments = window.fields.Count(f => f.track == 0 && f.kind == FieldKind.Instrument);
                    report.AppendLine("MEASURE track 1: " + notes + " note columns, " + instruments + " instrument column");
                    Need(notes == 3 && instruments == 1, notes + " note and " + instruments + " instrument fields");
                    Need(window.fields.First(f => f.track == 0 && f.kind == FieldKind.Instrument).column == 0, "The instrument column is not the first");
                    window.SongEdit("enter note", () => window.EditField(4, window.fields.First(f => f.track == 0 && f.kind == FieldKind.Note), (n, x, v) => { n.note = NoteKind.Note; n.pitch = 62; }));
                    Need(songAsset.model.instrumentColumnVersion == 1 && songAsset.model == window.Data && songAsset.model.patterns[0].tracks[0].ReadLine(4).notes.Count == 1, "The first edit did not adopt the converted song");
                });
                Check("entering a note in column 2 writes the instrument only when it differs from the one in effect, on the track column", () =>
                {
                    Fresh(1); window.BuildPane(); Layout();
                    var slot = window.fields.First(f => f.track == 0 && f.kind == FieldKind.Note && f.column == 1);
                    window.entryInstrument = 0;
                    window.SongEdit("enter note", () => { window.EditField(4, slot, (n, x, v) => { n.note = NoteKind.Note; n.pitch = 62; }); window.SetNoteInstrument(4, slot); });
                    var track = songAsset.model.patterns[0].tracks[0];
                    Need(!track.ReadLine(4).notes.Any(n => n.instrumentPresent), "An instrument was written although it was already in effect");
                    window.entryInstrument = 1;
                    window.SongEdit("enter note", () => { window.EditField(8, slot, (n, x, v) => { n.note = NoteKind.Note; n.pitch = 65; }); window.SetNoteInstrument(8, slot); });
                    var line = songAsset.model.patterns[0].tracks[0].ReadLine(8);
                    var zero = line.notes.Find(n => n.column == 0); var one = line.notes.Find(n => n.column == 1);
                    Need(zero != null && zero.instrumentPresent && zero.instrument == 1 && zero.note == NoteKind.Empty, "The instrument did not go to column 1 as an instrument-only cell");
                    Need(one != null && one.note == NoteKind.Note && !one.instrumentPresent, "The note in column 2 carries an instrument");
                    Need(window.TrackInstrumentAt(0, 12, 1) == 1, "TrackInstrumentAt " + window.TrackInstrumentAt(0, 12, 1));
                });
            }
            finally { window.Close(); DestroyImmediate(songAsset); DestroyImmediate(a); DestroyImmediate(b); }
            return "CheckTrackInstrumentsUi passed=" + passed + " failed=" + failed + "\n" + report;
        }
    }
}
