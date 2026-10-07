using System;
using System.Collections.Generic;
using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker
{
    public enum ZTrackerAccentKind
    {
        /// <summary>The first row of every beat (rows 0, LPB, 2·LPB, …): the "1"s.</summary>
        BeatStart,
        /// <summary>Every row where a note starts on one track (a melody, a drum part).</summary>
        TrackNotes,
    }

    /// <summary>Which rows of a playing song count as accents a player can try to hit.</summary>
    [Serializable]
    public struct ZTrackerAccentSource
    {
        public ZTrackerAccentKind kind;
        /// <summary>For <see cref="ZTrackerAccentKind.TrackNotes"/>: index into the song's tracks.</summary>
        public int track;

        public static ZTrackerAccentSource Beats => new ZTrackerAccentSource { kind = ZTrackerAccentKind.BeatStart, track = -1 };
        public static ZTrackerAccentSource Notes(int track) => new ZTrackerAccentSource { kind = ZTrackerAccentKind.TrackNotes, track = track };
    }

    /// <summary>The result of judging one moment against a song's accents.</summary>
    public struct ZTrackerAccentHit
    {
        /// <summary>False when the song clock could not answer (nothing playing, song just starting).</summary>
        public bool HasClock;
        /// <summary>False when there is no accent near enough to measure against (e.g. a track with no notes here).</summary>
        public bool HasAccent;
        /// <summary>True when the moment lies inside the nearest accent's window.</summary>
        public bool Hit;
        /// <summary>Distance from the nearest accent in rows and milliseconds. Negative = early, positive = late.</summary>
        public double OffsetRows, OffsetMs;
        /// <summary>Where the nearest accent is: its order slot and row (use with TryGetAccentNote).</summary>
        public int AccentOrder, AccentRow;
        public ZTrackerSongPosition Position;
    }

    /// <summary>
    /// Finds accent rows in the playing song and judges moments against them. An accent's window runs from
    /// <c>windowRows</c> before the accent's row starts to <c>windowRows</c> after; 0.5 means half a row either side.
    /// The neighbouring order slots are included, so a press just before a pattern ends can hit the next pattern's
    /// first accent. Pattern breaks and jumps are not followed (the next slot in the order list is assumed).
    /// </summary>
    public static class ZTrackerAccents
    {
        /// <summary>The accent rows of one order slot, ascending.</summary>
        public static void RowsIn(ZTrackerSongClock clock, int order, in ZTrackerAccentSource source, int linesPerBeat, List<int> rows)
        {
            rows.Clear();
            var song = clock?.Song;
            var pattern = clock?.PatternAt(order);
            if (song == null || pattern == null) return;
            if (source.kind == ZTrackerAccentKind.BeatStart)
            {
                int lpb = Math.Max(1, linesPerBeat);
                for (int r = 0; r < pattern.lineCount; r += lpb) rows.Add(r);
                return;
            }
            var track = FindPatternTrack(song, pattern, source.track);
            if (track == null) return;
            foreach (var line in track.lines)
                if (line.line >= 0 && line.line < pattern.lineCount && StartsNote(line)) rows.Add(line.line);
        }

        /// <summary>The order slot after <paramref name="order"/>, wrapping like a looping song.</summary>
        public static int NextOrder(SongData song, int order) => song == null || song.sequence.Count == 0 ? 0 : (order + 1) % song.sequence.Count;
        public static int PreviousOrder(SongData song, int order) => song == null || song.sequence.Count == 0 ? 0 : (order - 1 + song.sequence.Count) % song.sequence.Count;

        /// <summary>Accents around a position, as row positions measured from the start of the position's order slot:
        /// the previous slot's accents are negative, the next slot's lie past this slot's length. For drawing.</summary>
        public static void Around(ZTrackerSongClock clock, in ZTrackerSongPosition p, in ZTrackerAccentSource source, List<double> result, List<int> scratch)
        {
            result.Clear();
            var song = clock?.Song;
            var here = clock?.PatternAt(p.Order);
            if (song == null || here == null) return;
            int prev = PreviousOrder(song, p.Order), next = NextOrder(song, p.Order);
            var before = clock.PatternAt(prev);
            if (before != null) { RowsIn(clock, prev, source, p.LinesPerBeat, scratch); foreach (int r in scratch) result.Add(r - before.lineCount); }
            RowsIn(clock, p.Order, source, p.LinesPerBeat, scratch); foreach (int r in scratch) result.Add(r);
            RowsIn(clock, next, source, p.LinesPerBeat, scratch); foreach (int r in scratch) result.Add(r + here.lineCount);
        }

        /// <summary>Judges the moment the player heard at <paramref name="realtime"/> (Time.realtimeSinceStartupAsDouble
        /// timeline, e.g. an input event's time) against the nearest accent.</summary>
        public static ZTrackerAccentHit Judge(ZTrackerSongClock clock, double realtime, in ZTrackerAccentSource source, double windowRows, List<int> scratch)
        {
            var hit = new ZTrackerAccentHit();
            if (clock == null || !clock.TryGetHeardAt(realtime, out var p)) return hit;
            hit.HasClock = true; hit.Position = p;
            var song = clock.Song; var here = clock.PatternAt(p.Order);
            if (song == null || here == null) return hit;
            double at = p.Row + p.RowFraction, best = double.MaxValue;
            int prev = PreviousOrder(song, p.Order), next = NextOrder(song, p.Order);
            var before = clock.PatternAt(prev);
            void Consider(int order, int row, double position)
            {
                double offset = at - position;
                if (Math.Abs(offset) < Math.Abs(best)) { best = offset; hit.AccentOrder = order; hit.AccentRow = row; hit.HasAccent = true; }
            }
            if (before != null) { RowsIn(clock, prev, source, p.LinesPerBeat, scratch); foreach (int r in scratch) Consider(prev, r, r - before.lineCount); }
            RowsIn(clock, p.Order, source, p.LinesPerBeat, scratch); foreach (int r in scratch) Consider(p.Order, r, r);
            RowsIn(clock, next, source, p.LinesPerBeat, scratch); foreach (int r in scratch) Consider(next, r, r + here.lineCount);
            if (!hit.HasAccent) return hit;
            hit.OffsetRows = best;
            hit.OffsetMs = best * p.DurationMs;
            hit.Hit = best >= -windowRows && best <= windowRows;
            return hit;
        }

        /// <summary>The note that starts at an accent row on a track: its pitch, the instrument it plays (the last one named
        /// on that column if the cell names none) and its velocity 0..1.</summary>
        public static bool TryGetAccentNote(ZTrackerSongClock clock, int order, int row, int trackIndex, out int pitch, out int instrument, out float velocity)
        {
            pitch = -1; instrument = -1; velocity = 1f;
            var song = clock?.Song; var pattern = clock?.PatternAt(order);
            var track = FindPatternTrack(song, pattern, trackIndex);
            if (track == null) return false;
            NoteCell found = null;
            foreach (var line in track.lines)
            {
                if (line.line > row) break;
                foreach (var cell in line.notes)
                {
                    if (cell == null || cell.inactive) continue;
                    if (cell.instrumentPresent && (found == null || line.line < row)) instrument = cell.instrument;
                    if (line.line == row && found == null && cell.note == NoteKind.Note) { found = cell; if (cell.instrumentPresent) instrument = cell.instrument; }
                }
            }
            if (found == null) return false;
            pitch = found.pitch;
            if (instrument < 0) instrument = 0;
            if (found.volume != null && found.volume.kind == ValueKind.Value) velocity = Math.Max(0f, Math.Min(1f, found.volume.value / 128f));
            return true;
        }

        /// <summary>The row after <paramref name="row"/> where the track's next note or note-off starts, or the slot's length.</summary>
        public static int NextEventRow(ZTrackerSongClock clock, int order, int row, int trackIndex)
        {
            var song = clock?.Song; var pattern = clock?.PatternAt(order);
            var track = FindPatternTrack(song, pattern, trackIndex);
            if (pattern == null) return row + 1;
            if (track != null)
                foreach (var line in track.lines)
                    if (line.line > row) foreach (var cell in line.notes) if (cell != null && !cell.inactive && (cell.note == NoteKind.Note || cell.note == NoteKind.Off)) return line.line;
            return pattern.lineCount;
        }

        static PatternTrack FindPatternTrack(SongData song, PatternData pattern, int trackIndex)
        {
            if (song == null || pattern == null || trackIndex < 0 || trackIndex >= song.tracks.Count) return null;
            string id = song.tracks[trackIndex].id;
            foreach (var t in pattern.tracks) if (t.trackId == id) return t;
            return null;
        }

        static bool StartsNote(PatternLine line)
        {
            foreach (var cell in line.notes) if (cell != null && !cell.inactive && cell.note == NoteKind.Note) return true;
            return false;
        }
    }
}
