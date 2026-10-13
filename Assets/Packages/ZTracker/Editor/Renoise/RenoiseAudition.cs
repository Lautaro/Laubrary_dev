using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>A mapper-owned clean renderer. No reference to, or stop command for, the song's renderer.</summary>
    internal sealed class RenoiseAudition : IDisposable
    {
        GameObject owner;
        TrackerSapGenerator host;
        PlaybackSnapshot snapshot;
        double expires;
        public void Play(ZTrackerSong song, RenoiseMapping mapping, RenoiseUse use, int firstRow, int endRow)
        {
            Dispose();
            snapshot = PlaybackSnapshot.CreateClean(mapping.instrument, 12);
            TrackerPreparedSong prepared = null;
            try
            {
                Fill(snapshot.Data, song?.model, mapping.source.InstrumentIndex, use, firstRow, endRow);
                prepared = TrackerPreparedSong.Prepare(snapshot.Data, AudioSettings.outputSampleRate);
                owner = new GameObject("Renoise mapper clean preview") { hideFlags = HideFlags.HideAndDontSave };
                host = owner.AddComponent<TrackerSapGenerator>(); host.Configure(prepared, false); prepared = null;
                var source = owner.GetComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0; source.generator = host; source.Play();
                host.SendCommand(new TrackerCommand { kind = TrackerCommandKind.Play, a = 0 });
                expires = EditorApplication.timeSinceStartup + 300;
                EditorApplication.update += Pump;
            }
            catch { Dispose(); throw; }
            finally { prepared?.Dispose(); }
        }

        internal static void Fill(SongData clean, SongData original, int instrument, RenoiseUse use, int firstRow, int endRow)
        {
            var target = clean.patterns[0]; target.tracks[0].lines.Clear();
            if (original == null || use == null)
            {
                target.lineCount = 8;
                target.tracks[0].WriteLine(new PatternLine { line = 0, notes = new List<NoteCell> { new NoteCell { note = NoteKind.Note, pitch = 48, instrumentPresent = true, instrument = 0 } } });
                target.tracks[0].WriteLine(new PatternLine { line = 4, notes = new List<NoteCell> { new NoteCell { note = NoteKind.Off } } });
                return;
            }
            var source = original.patterns[use.pattern];
            firstRow = Mathf.Clamp(firstRow, 0, source.lineCount - 1); endRow = Mathf.Clamp(endRow, firstRow + 1, source.lineCount);
            target.lineCount = endRow - firstRow;
            clean.bpm = original.bpm; clean.linesPerBeat = original.linesPerBeat; clean.ticksPerLine = original.ticksPerLine;
            var columnInstrument = new Dictionary<int, int>();
            string trackId = original.tracks[use.track].id;
            // Resolve carried instrument selections from sequence history, including repeated patterns.
            for (int seq = 0; seq <= use.sequence; seq++)
            {
                var slot = original.sequence[seq]; var pattern = original.patterns.First(p => p.id == slot.patternId);
                int limit = seq == use.sequence ? firstRow : pattern.lineCount;
                foreach (var pt in pattern.tracks)
                {
                    if (slot.mutedTrackIds.Contains(pt.trackId)) continue;
                    foreach (var line in pt.lines.Where(l => l.line < limit).OrderBy(l => l.line))
                    {
                        if (pt.trackId == trackId) foreach (var n in line.notes.Where(n => !n.inactive && n.instrumentPresent)) columnInstrument[n.column] = n.instrument;
                    }
                }
                // Timing winners are applied row-by-row in source track order.
                for (int row = 0; row < limit; row++) foreach (var t in original.tracks)
                {
                    if (slot.mutedTrackIds.Contains(t.id) || t.triggerMute) continue;
                    var line = pattern.tracks.FirstOrDefault(p => p.trackId == t.id)?.lines.FirstOrDefault(l => l.line == row);
                    if (line == null) continue;
                    foreach (var effect in line.effects.Where(e => !e.inactive).OrderBy(e => e.column)) SetTiming(clean, effect.command);
                }
            }
            var sourceTrack = source.tracks.First(p => p.trackId == trackId);
            var active = new HashSet<int>();
            foreach (var line in sourceTrack.lines.Where(l => l.line >= firstRow && l.line < endRow).OrderBy(l => l.line))
            {
                var output = new PatternLine { line = line.line - firstRow };
                foreach (var note in line.notes.Where(n => !n.inactive).OrderBy(n => n.column))
                {
                    if (note.instrumentPresent) columnInstrument[note.column] = note.instrument;
                    bool wanted = columnInstrument.TryGetValue(note.column, out int selected) && selected == instrument;
                    if (wanted)
                    {
                        var n = ZTrackerMigration.Copy(note); if (n.instrumentPresent || n.note == NoteKind.Note) { n.instrumentPresent = true; n.instrument = 0; }
                        n.parameterSetId = ""; output.notes.Add(n);
                        if (n.note == NoteKind.Note) active.Add(n.column); else if (n.note == NoteKind.Off) active.Remove(n.column);
                    }
                    else if (active.Contains(note.column) && (note.note == NoteKind.Note || note.note == NoteKind.Off))
                    { output.notes.Add(new NoteCell { column = note.column, note = NoteKind.Off, delayPresent = note.delayPresent, delay = note.delay }); active.Remove(note.column); }
                }
                // A clean audition excludes source routing and arbitrary device commands.
                foreach (var effect in line.effects.Where(e => !e.inactive && e.command.identifier.StartsWith("0", StringComparison.Ordinal) && !e.command.unsupported))
                    if ("0A 0U 0D 0G 0V 0I 0O 0T 0C 0S 0B 0E 0N 0Q 0Y 0R".Split(' ').Contains(effect.command.identifier)) output.effects.Add(ZTrackerMigration.Copy(effect));
                target.tracks[0].WriteLine(output);
            }
            // Carry supported tempo changes from every source track into the clean excerpt's master.
            for (int row = firstRow; row < endRow; row++)
            {
                var line = new PatternLine { line = row - firstRow };
                foreach (var t in original.tracks)
                {
                    if (original.sequence[use.sequence].mutedTrackIds.Contains(t.id) || t.triggerMute) continue;
                    var src = source.tracks.FirstOrDefault(p => p.trackId == t.id)?.lines.FirstOrDefault(l => l.line == row);
                    if (src == null) continue;
                    foreach (var e in src.effects.Where(e => !e.inactive && IsTiming(e.command)).OrderBy(e => e.column))
                    { var copy = ZTrackerMigration.Copy(e); line.effects.RemoveAll(old => old.command.identifier == copy.command.identifier); line.effects.Add(copy); }
                }
                for (int i = 0; i < line.effects.Count; i++) line.effects[i].column = i;
                if (line.effects.Count > 0) target.tracks[1].WriteLine(line);
            }
            clean.tracks[1].visibleEffectColumns = Math.Min(8, Math.Max(1, target.tracks[1].lines.Select(l => l.effects.Count).DefaultIfEmpty(0).Max()));
        }

        static bool IsTiming(CommandData c) => c.present && c.valuePresent && !c.unsupported && (c.identifier == "ZT" || c.identifier == "ZL" || c.identifier == "ZK");
        static void SetTiming(SongData data, CommandData c)
        {
            if (!IsTiming(c)) return;
            if (c.identifier == "ZT" && c.value >= 32) data.bpm = c.value;
            if (c.identifier == "ZL" && c.value > 0) data.linesPerBeat = c.value;
            if (c.identifier == "ZK" && c.value > 0 && c.value <= 16) data.ticksPerLine = c.value;
        }
        void Pump()
        {
            if (host == null || EditorApplication.timeSinceStartup >= expires) { Dispose(); return; }
            host.PollRetirement();
            while (host.ReadEvent(out var e)) if (e.kind == TrackerEventKind.Stopped) { expires = Math.Min(expires, EditorApplication.timeSinceStartup + 2); }
        }
        public void Dispose()
        {
            EditorApplication.update -= Pump;
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            owner = null; host = null; snapshot?.Dispose(); snapshot = null;
        }
    }
}
