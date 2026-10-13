using System;
using System.Collections.Generic;
using System.Text;
using Laubrary.ZTracker.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Exercises the actual cell refresh with transient data only. Does not open a window, play audio, access MIDI or save assets.</summary>
        public static string CheckNoteDisplayMatrix()
        {
            var report = new StringBuilder();
            var window = CreateInstance<ZTrackerModelWindow>();
            var song = CreateInstance<ZTrackerSong>();
            var synth = CreateInstance<ZTrackerInstrument>();
            var kit = CreateInstance<ZTrackerInstrument>();
            var unresolved = CreateInstance<ZTrackerInstrument>();
            var legacyKit = CreateInstance<ZTrackerInstrument>();
            int checks = 0, failures = 0;
            void Need(bool value, string description)
            {
                checks++;
                if (value) return;
                failures++;
                if (failures <= 20) report.AppendLine("FAIL " + description);
            }
            try
            {
                synth.schemaVersion = kit.schemaVersion = 1;
                synth.model = new InstrumentData { id = "plain", family = InstrumentFamily.Synth };
                kit.model = new InstrumentData { id = "kit", family = InstrumentFamily.DrumKit };
                kit.model.drumKit.mappings.Add(new DrumMapping { id = "kick", name = "Kick", tag = "Kick", noteMin = 24, noteMax = 24 });
                unresolved.schemaVersion = 999;
                legacyKit.type = InstrumentType.Kit;
                legacyKit.kitEntries = new[] { new ZTrackerInstrument.KitEntry { midiNote = 24, displayName = "BD" }, new ZTrackerInstrument.KitEntry { midiNote = 48, displayName = "" } };
                song.schemaVersion = 1;
                song.model = new SongData { id = "display-check", instruments = new List<ZTrackerInstrument> { synth, kit, null, unresolved } };
                var trackData = new TrackData { id = "track", name = "Display", visibleNoteColumns = 12 };
                song.model.tracks.Add(trackData);
                var record = new PatternTrack { trackId = trackData.id };
                var pattern = new PatternData { id = "pattern", lineCount = 4, tracks = new List<PatternTrack> { record } };
                song.model.patterns.Add(pattern);
                song.model.sequence.Add(new SequenceSlot { id = "slot", patternId = pattern.id });
                window.song = song;
                var label = new Label();
                int[] pitches = { 0, 24, 47, 48, 49, 119 };
                string[] expected = { "C-0", "C-2", "B-3", "C-4", "C#4", "B-9" };
                // Invalid instrument slots, unsupported schema, inactive cells, bus/event track kinds and all 12
                // note columns must retain exactly the same pitch identity as an ordinary synth sequencer cell.
                foreach (TrackKind kind in Enum.GetValues(typeof(TrackKind)))
                foreach (bool drums in new[] { false, true })
                foreach (bool inactive in new[] { false, true })
                foreach (int instrumentIndex in new[] { -1, 0, 1, 2, 3, 99 })
                for (int column = 0; column < 12; column++)
                for (int p = 0; p < pitches.Length; p++)
                {
                    trackData.kind = kind; trackData.drumTrack = drums;
                    record.lines.Clear();
                    var cell = new NoteCell { column = column, note = NoteKind.Note, pitch = pitches[p], instrumentPresent = true, instrument = instrumentIndex, inactive = inactive };
                    record.lines.Add(new PatternLine { line = 0, notes = new List<NoteCell> { cell } });
                    var field = new FieldSlot(0, column, FieldKind.Note);
                    window.RefreshNoteDisplay(label, 0, field);
                    string context = kind + "/drums=" + drums + "/inactive=" + inactive + "/instrument=" + instrumentIndex + "/column=" + column + "/pitch=" + pitches[p];
                    Need(label.text == expected[p], context + " rendered '" + label.text + "'");
                    bool mapped = drums && !inactive && instrumentIndex == 1 && pitches[p] == 24;
                    Need((label.Q<VisualElement>("drum-icon") != null) == mapped, context + " stale or missing mapped icon");
                    Need(window.Width(field) - label.style.paddingLeft.value.value >= 30, context + " icon consumes the pitch width");
                }
                trackData.drumTrack = true;
                foreach (TrackKind trackKind in Enum.GetValues(typeof(TrackKind)))
                foreach (NoteKind kind in new[] { NoteKind.Empty, NoteKind.Note, NoteKind.Off, NoteKind.Legacy, (NoteKind)999 })
                foreach (bool inactive in new[] { false, true })
                for (int column = 0; column < 12; column++)
                foreach (int value in new[] { -1, 24, 48, 119, 120, 126, 127, int.MinValue, int.MaxValue })
                {
                    trackData.kind = trackKind;
                    var cell = new NoteCell { column = column, note = kind, pitch = value, legacyNote = value, inactive = inactive, instrumentPresent = true, instrument = 0 };
                    record.lines.Clear(); record.lines.Add(new PatternLine { line = 0, notes = new List<NoteCell> { cell } });
                    window.RefreshNoteDisplay(label, 0, new FieldSlot(0, column, FieldKind.Note));
                    string wanted = kind == NoteKind.Empty ? "---" : kind == NoteKind.Off || kind == NoteKind.Legacy && value == 127 ? "OFF"
                        : (kind == NoteKind.Note && value >= 0 && value <= 119 || kind == NoteKind.Legacy && value >= 0 && value <= 126)
                            ? noteNames[value % 12] + value / 12 : "ERR";
                    Need(label.text == wanted, "retained " + trackKind + "/" + kind + "/" + value + "/column=" + column + " rendered '" + label.text + "'");
                    if (inactive) Need(label.tooltip.Contains("inactive"), "inactive note must retain an explanation");
                    if (kind == NoteKind.Legacy) Need(label.tooltip.Contains(value.ToString()), "raw note value missing from tooltip");
                }
                Need(ZTrackerNoteDisplay.Legacy(24, legacyKit) == "BD", "legacy mapped drum name");
                Need(ZTrackerNoteDisplay.Legacy(48, legacyKit) == "C-4", "legacy empty drum name must fall back to pitch");
                Need(ZTrackerNoteDisplay.Legacy(49, legacyKit) == "C#4", "legacy unmapped drum key must fall back to pitch");
                Need(ZTrackerNoteDisplay.Legacy(24, synth) == "C-2", "legacy non-kit instrument pitch");
                Need(ZTrackerNoteDisplay.Legacy(48, null) == "C-4", "legacy unresolved instrument pitch");
                Need(ZTrackerNoteDisplay.Legacy(127, legacyKit) == "OFF", "legacy OFF unaffected by mapping");
                Need(ZTrackerNoteDisplay.Legacy(int.MaxValue, legacyKit) == "ERR", "legacy invalid pitch must not clamp into an apparently valid note");
                Need(ZTrackerWindow.NoteName(120) == "C-10" && ZTrackerWindow.NoteName(int.MaxValue) == "ERR", "legacy pitch formatter upper range");
            }
            catch (Exception error) { failures++; report.AppendLine("FAIL unexpected exception: " + error); }
            finally
            {
                window.song = null;
                DestroyImmediate(window); DestroyImmediate(song); DestroyImmediate(synth); DestroyImmediate(kit); DestroyImmediate(unresolved); DestroyImmediate(legacyKit);
            }
            report.AppendLine("Note display checks=" + checks + " failures=" + failures + "; transient cells only; no playback, MIDI, asset writes or visible window; laid-out visual verification remains separate.");
            return report.ToString();
        }
    }
}
