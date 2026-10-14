using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.ZTracker.Model
{
    /// <summary>
    /// One instrument column per track (T-0648). A song from before this change keeps an instrument cell after every note
    /// column and each column remembers its own instrument. From <see cref="SongData.instrumentColumnVersion"/> 1 a track
    /// has a single instrument column: it is stored on the cell of note column 0 (an instrument-only cell when that column
    /// has no note), and a value there is the instrument of every note column of the track from that row on. The engine is
    /// unchanged: the compiler expands the track column into the per-column selections it already keeps. A song converts
    /// once, losslessly (checked by replaying the whole sequence under both rules); a track whose columns really play
    /// different instruments at once is split into one track per note column instead.
    /// </summary>
    public static class ZTrackerTrackInstruments
    {
        public const int CurrentVersion = 1;

        /// <summary>True when the song uses the one-column rule (a song with a newer version is left as it is).</summary>
        public static bool Applies(SongData song) => song != null && song.instrumentColumnVersion >= 1;

        /// <summary>The instrument in effect at the end of <paramref name="row"/> on a track of this pattern, or -1 when the pattern has named none by then.</summary>
        public static int InstrumentAt(PatternTrack track, int row)
        {
            int result = -1;
            if (track?.lines == null) return result;
            foreach (var line in track.lines)
            {
                if (line == null || line.line > row) continue;
                var cell = line.notes?.Find(n => n != null && !n.inactive && n.column == 0 && n.instrumentPresent);
                if (cell != null) result = cell.instrument;
            }
            return result;
        }

        /// <summary>A converted copy of an older song for showing and editing; the song itself is not touched (a read never changes an asset).</summary>
        public static SongData ViewOf(SongData song)
        {
            if (song == null) return null;
            var view = JsonUtility.FromJson<SongData>(JsonUtility.ToJson(song));
            Migrate(view);
            return view;
        }

        /// <summary>Converts the song to the one-column rule. Returns the notes for the song's diagnostics (empty when nothing needed saying).</summary>
        public static List<string> Migrate(SongData song)
        {
            var notes = new List<string>();
            if (song == null || song.instrumentColumnVersion >= CurrentVersion) return notes;
            foreach (var track in song.tracks.ToList())
            {
                if (track == null || track.kind != TrackKind.Sequencer) continue;
                if (Convert(song, track)) continue;
                var added = Split(song, track);
                notes.Add("TRACK_SPLIT " + track.name + " played different instruments in its note columns at once; split into " + (added + 1) + " tracks of one note column each");
                foreach (var part in new[] { track }.Concat(SplitParts(song, track, added)))
                    if (!Convert(song, part)) throw new InvalidOperationException("A single-column track did not convert: " + part.name);
            }
            song.instrumentColumnVersion = CurrentVersion;
            foreach (var n in notes) if (!song.diagnostics.Contains(n)) song.diagnostics.Add(n);
            return notes;
        }

        // The tracks Split made right after the original, in order.
        static IEnumerable<TrackData> SplitParts(SongData song, TrackData origin, int count)
        {
            int at = song.tracks.IndexOf(origin);
            for (int i = 1; i <= count; i++) yield return song.tracks[at + i];
        }

        // ───────────── the conversion of one track ─────────────

        static bool Convert(SongData song, TrackData track)
        {
            var candidates = new Dictionary<PatternData, PatternTrack>();
            foreach (var pattern in song.patterns)
            {
                var pt = pattern?.tracks?.Find(t => t != null && t.trackId == track.id);
                if (pt == null) continue;
                var copy = JsonUtility.FromJson<PatternTrack>(JsonUtility.ToJson(pt));
                foreach (var line in copy.lines)
                {
                    var active = line.notes.Where(n => n != null && !n.inactive && n.instrumentPresent).ToList();
                    if (active.Count == 0) continue;
                    if (active.Select(n => n.instrument).Distinct().Count() > 1) return false;
                    var first = line.notes.Find(n => n != null && !n.inactive && n.column == 0);
                    if (first == null) { first = new NoteCell { column = 0 }; line.notes.Insert(0, first); }
                    first.instrumentPresent = true; first.instrument = active[0].instrument;
                    foreach (var n in active) if (n.column != 0) { n.instrumentPresent = false; n.instrument = 0; }
                }
                candidates[pattern] = copy;
            }
            if (!Same(song, track, candidates)) return false;
            foreach (var pair in candidates)
            {
                int at = pair.Key.tracks.FindIndex(t => t != null && t.trackId == track.id);
                pair.Key.tracks[at] = pair.Value;
            }
            return true;
        }

        // Replays the song's order under both rules and compares the instrument every note plays.
        static bool Same(SongData song, TrackData track, Dictionary<PatternData, PatternTrack> candidate)
        {
            var oldState = new Dictionary<int, int>();
            int newState = -1;
            // Two passes over the order, so what a pattern leaves behind for the next time round the loop counts too.
            for (int pass = 0; pass < 2; pass++)
                foreach (var slot in song.sequence)
                {
                    var pattern = song.patterns.Find(p => p != null && p.id == slot?.patternId);
                    var original = pattern?.tracks?.Find(t => t != null && t.trackId == track.id);
                    if (original == null) continue;
                    candidate.TryGetValue(pattern, out var converted);
                    var newLines = converted?.lines.ToDictionary(l => l.line) ?? new Dictionary<int, PatternLine>();
                    foreach (var line in original.lines.OrderBy(l => l.line))
                    {
                        newLines.TryGetValue(line.line, out var other);
                        var cells = line.notes.Where(n => n != null && !n.inactive).OrderBy(n => n.column).ToList();
                        foreach (var n in cells) if (n.instrumentPresent) oldState[n.column] = n.instrument;
                        var zero = other?.notes.Find(n => n != null && !n.inactive && n.column == 0);
                        if (zero != null && zero.instrumentPresent) newState = zero.instrument;
                        foreach (var n in cells)
                            if (n.note == NoteKind.Note)
                            {
                                int was = oldState.TryGetValue(n.column, out int w) ? w : -1;
                                if (was != newState) return false;
                            }
                    }
                }
            return true;
        }

        // ───────────── splitting a track whose columns disagree ─────────────

        // Moves every note column but the first onto a track of its own, placed right after the original. Returns how many.
        static int Split(SongData song, TrackData track)
        {
            int columns = track.visibleNoteColumns;
            int at = song.tracks.IndexOf(track), made = 0;
            for (int c = 1; c < columns; c++)
            {
                var part = JsonUtility.FromJson<TrackData>(JsonUtility.ToJson(track));
                part.id = Guid.NewGuid().ToString("N");
                part.name = track.name + " " + (c + 1);
                part.visibleNoteColumns = 1; part.visibleEffectColumns = 1;
                part.columns = new List<ColumnVisibility> { c < track.columns.Count ? JsonUtility.FromJson<ColumnVisibility>(JsonUtility.ToJson(track.columns[c])) : new ColumnVisibility() };
                part.effectColumnViews = new List<EffectColumnView>();
                part.envelopeColumns = new List<EnvelopeColumnData>();
                song.tracks.Insert(at + 1 + made, part);
                foreach (var pattern in song.patterns)
                {
                    var pt = pattern?.tracks?.Find(t => t != null && t.trackId == track.id);
                    if (pt == null) continue;
                    var moved = new PatternTrack { trackId = part.id };
                    foreach (var line in pt.lines)
                    {
                        var cell = line.notes.Find(n => n != null && n.column == c);
                        if (cell == null) continue;
                        line.notes.Remove(cell);
                        cell.column = 0;
                        moved.lines.Add(new PatternLine { line = line.line, notes = new List<NoteCell> { cell } });
                    }
                    pt.lines.RemoveAll(l => !l.HasPayload);
                    pattern.tracks.Insert(pattern.tracks.IndexOf(pt) + 1 + made, moved);
                }
                foreach (var slot in song.sequence)
                    if (slot?.mutedTrackIds != null && slot.mutedTrackIds.Contains(track.id)) slot.mutedTrackIds.Add(part.id);
                made++;
            }
            track.visibleNoteColumns = 1;
            return made;
        }
    }
}
