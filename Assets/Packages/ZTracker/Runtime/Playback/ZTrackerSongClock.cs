using System.Collections.Generic;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using UnityEngine;

namespace Laubrary.ZTracker
{
    /// <summary>
    /// Where a playing song is at one moment: the row, how far into it, and the tempo that applies to it.
    /// Durations are given in samples, milliseconds and ticks so game code can use whichever reads best.
    /// </summary>
    public struct ZTrackerSongPosition
    {
        /// <summary>Index into the song's order list (the sequence), and the row inside that slot's pattern.</summary>
        public int Order, Row;
        /// <summary>Id of the pattern playing at <see cref="Order"/> (look it up in <see cref="ZTrackerSongClock.Song"/>).</summary>
        public string PatternId;
        public double Bpm;
        /// <summary>Lines (rows) per beat and ticks per line in force for this row. Both can change mid-song.</summary>
        public int LinesPerBeat, TicksPerLine;
        /// <summary>Row index within its beat (0 = the first row of a beat, the "1") and the beat index within the pattern.</summary>
        public int LineInBeat, BeatInPattern;
        /// <summary>0..1 through the current row, and 0..1 through the current beat.</summary>
        public double RowFraction, BeatFraction;
        /// <summary>The tick inside the row (0..TicksPerLine-1) and 0..1 through that tick.</summary>
        public int Tick;
        public double TickFraction;
        /// <summary>How long the row has been current, how long until the next row, and the row's whole length.</summary>
        public double ElapsedSamples, RemainingSamples, DurationSamples;
        public double ElapsedMs, RemainingMs, DurationMs;
        public double ElapsedTicks, RemainingTicks;
        /// <summary>How many times this exact order/row has been played so far (0 the first time; counts loops).</summary>
        public long Occurrence;
        /// <summary>True while a held row (ZDxx) repeats.</summary>
        public bool Held;
        /// <summary>True when the time asked about lies past the newest row the renderer has published (the next row
        /// has not been rendered yet). The row is then the last known one, clamped just short of its end.</summary>
        public bool Extrapolated;
        /// <summary>The position on the renderer's transport sample timeline this answer is for.</summary>
        public double TransportSample;
        public int SampleRate;
    }

    /// <summary>
    /// Main-thread song clock for one playback. The renderer publishes every row it enters, with its exact start
    /// sample and length, plus how far it has rendered. This class smooths the rendered position against real
    /// time (the renderer works in audio blocks, so it moves in steps) and subtracts the audio output delay, so
    /// <see cref="TryGetHeard"/> answers "which row is the player hearing right now". Nothing is consumed: reading
    /// the clock never takes events away from <see cref="ZTrackerPlayer.EventReceived"/>.
    ///
    /// The output delay is an estimate (Unity's DSP buffer size and count). Speakers, drivers and Bluetooth add
    /// delay Unity cannot see, so a game should let the player tune <see cref="ExtraLatencySeconds"/>.
    /// </summary>
    public sealed class ZTrackerSongClock
    {
        // How strongly one new rendered-position reading pulls the smoothed clock. Low keeps it steady against the
        // block-sized steps; a jump bigger than SnapSeconds (pause, seek, a hitch) is taken at once instead.
        const double Gain = 0.06, SnapSeconds = 0.12;

        readonly ZTrackerPlayback playback;
        double anchorTime, anchorPosition;
        bool haveAnchor, frozen;
        long lastBlocks = -1;
        int sampleRate = 48000;
        SongData indexedSong;
        readonly Dictionary<string, PatternData> patternsById = new Dictionary<string, PatternData>();
        readonly Dictionary<string, int> trackIndexById = new Dictionary<string, int>();

        internal ZTrackerSongClock(ZTrackerPlayback playback) { this.playback = playback; }

        /// <summary>Added to the estimated output delay. Positive when the sound reaches the player later than
        /// estimated (e.g. Bluetooth headphones). Tune by ear or with a tap-along calibration.</summary>
        public double ExtraLatencySeconds { get; set; }

        /// <summary>Estimated delay between the renderer producing audio and it leaving the output device.</summary>
        public double OutputLatencySeconds
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out int count);
                int rate = sampleRate > 0 ? sampleRate : AudioSettings.outputSampleRate;
                return rate > 0 ? (double)length * count / rate : 0;
            }
        }

        public double TotalLatencySeconds => OutputLatencySeconds + ExtraLatencySeconds;

        /// <summary>The detached song data that is actually playing (a copy, not the asset). Read only: its tracks,
        /// patterns, order list, instruments and their models. Edits to it do not reach the playing audio.</summary>
        public SongData Song => playback != null ? playback.PlayingSong : null;

        public bool IsPlaying => playback != null && playback.IsPlaying;

        /// <summary>The row the player hears right now (rendered position minus the output delay).</summary>
        public bool TryGetHeard(out ZTrackerSongPosition position) => TryGetHeardAt(Time.realtimeSinceStartupAsDouble, out position);

        /// <summary>The row the player heard at a given moment on the <c>Time.realtimeSinceStartupAsDouble</c> timeline.
        /// Pass an Input System event or callback time to judge a button press at the instant it happened rather than
        /// at the start of the frame that processed it.</summary>
        public bool TryGetHeardAt(double realtime, out ZTrackerSongPosition position)
        {
            position = default;
            if (!TryRenderHead(realtime, out double head)) return false;
            return TryResolve(head - TotalLatencySeconds * sampleRate, out position);
        }

        /// <summary>The newest row the renderer has produced, ignoring the output delay. Runs ahead of what is heard.</summary>
        public bool TryGetRendered(out ZTrackerSongPosition position)
        {
            position = default;
            if (!TryRenderHead(Time.realtimeSinceStartupAsDouble, out double head)) return false;
            return TryResolve(head, out position);
        }

        /// <summary>The cells of one row of one track in the playing song, without allocating. False when the order,
        /// track or row does not exist or the row is empty.</summary>
        public bool TryGetLine(int order, int row, int trackIndex, out PatternLine line)
        {
            line = null;
            var song = Song;
            if (song == null || order < 0 || order >= song.sequence.Count || trackIndex < 0 || trackIndex >= song.tracks.Count) return false;
            Index(song);
            if (!patternsById.TryGetValue(song.sequence[order].patternId, out var pattern) || row < 0 || row >= pattern.lineCount) return false;
            string trackId = song.tracks[trackIndex].id;
            for (int t = 0; t < pattern.tracks.Count; t++)
            {
                var track = pattern.tracks[t];
                if (track.trackId != trackId) continue;
                var lines = track.lines;
                int lo = 0, hi = lines.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1, at = lines[mid].line;
                    if (at == row) { line = lines[mid]; return true; }
                    if (at < row) lo = mid + 1; else hi = mid - 1;
                }
                return false;
            }
            return false;
        }

        /// <summary>Track index for a track id or name in the playing song, or -1.</summary>
        public int FindTrack(string idOrName)
        {
            var song = Song;
            if (song == null || string.IsNullOrEmpty(idOrName)) return -1;
            Index(song);
            if (trackIndexById.TryGetValue(idOrName, out int index)) return index;
            for (int i = 0; i < song.tracks.Count; i++) if (song.tracks[i].name == idOrName) return i;
            return -1;
        }

        /// <summary>Pattern data for an order slot of the playing song, or null.</summary>
        public PatternData PatternAt(int order)
        {
            var song = Song;
            if (song == null || order < 0 || order >= song.sequence.Count) return null;
            Index(song);
            return patternsById.TryGetValue(song.sequence[order].patternId, out var pattern) ? pattern : null;
        }

        void Index(SongData song)
        {
            if (ReferenceEquals(song, indexedSong)) return;
            indexedSong = song; patternsById.Clear(); trackIndexById.Clear();
            foreach (var p in song.patterns) if (p != null && p.id != null) patternsById[p.id] = p;
            for (int i = 0; i < song.tracks.Count; i++) trackIndexById[song.tracks[i].id] = i;
        }

        // The rendered position, smoothed against real time. The renderer finishes one audio block at a time, so
        // its own position is a staircase; this follows the staircase's slope with a small steady lag, which the
        // latency setting absorbs.
        bool TryRenderHead(double now, out double head)
        {
            head = 0;
            var generator = playback != null && playback.IsPlaying ? playback.Generator : null;
            if (generator == null || !generator.TryReadClockHead(out var h)) return false;
            if (h.sampleRate > 0) sampleRate = h.sampleRate;
            if (h.playing == 0 || h.paused != 0)
            {
                anchorPosition = h.position; anchorTime = now; frozen = true; haveAnchor = true; lastBlocks = h.blocks;
                head = h.position; return h.playing != 0;
            }
            if (!haveAnchor || frozen)
            {
                anchorPosition = h.position; anchorTime = now; frozen = false; haveAnchor = true; lastBlocks = h.blocks;
                head = h.position; return true;
            }
            double predicted = anchorPosition + (now - anchorTime) * sampleRate;
            if (h.blocks != lastBlocks && now >= anchorTime)
            {
                lastBlocks = h.blocks;
                double error = h.position - predicted;
                if (System.Math.Abs(error) > SnapSeconds * sampleRate) predicted = h.position;
                else predicted += error * Gain;
                anchorPosition = predicted; anchorTime = now;
            }
            head = predicted;
            return true;
        }

        bool TryResolve(double at, out ZTrackerSongPosition position)
        {
            position = default;
            var generator = playback != null ? playback.Generator : null;
            if (generator == null) return false;
            long written = generator.ClockRowsWritten;
            long oldest = System.Math.Max(0, written - TrackerEventRing.ClockRowCapacity);
            for (long serial = written - 1; serial >= oldest; serial--)
            {
                if (!generator.TryReadClockRow(serial, out var row)) continue;
                if (row.start > at) continue;
                bool newest = serial == written - 1;
                Fill(row, at, newest, out position);
                return true;
            }
            return false; // before the first published row (the song is just starting) or history overrun
        }

        void Fill(in TrackerClockRow row, double at, bool newest, out ZTrackerSongPosition p)
        {
            double duration = row.duration > 0 ? row.duration : 1;
            double elapsed = at - row.start;
            bool extrapolated = false;
            if (elapsed >= duration) { extrapolated = newest; elapsed = duration * 0.999999; }
            int lpb = row.linesPerBeat > 0 ? row.linesPerBeat : 1, tpl = row.ticksPerLine > 0 ? row.ticksPerLine : 1;
            double fraction = elapsed / duration, tickLength = duration / tpl;
            int tick = System.Math.Min(tpl - 1, (int)(elapsed / tickLength));
            double toMs = 1000.0 / sampleRate;
            var song = Song;
            p = new ZTrackerSongPosition
            {
                Order = row.sequence, Row = row.row,
                PatternId = song != null && row.sequence >= 0 && row.sequence < song.sequence.Count ? song.sequence[row.sequence].patternId : null,
                Bpm = row.bpm, LinesPerBeat = lpb, TicksPerLine = tpl,
                LineInBeat = row.row % lpb, BeatInPattern = row.row / lpb,
                RowFraction = fraction, BeatFraction = (row.row % lpb + fraction) / lpb,
                Tick = tick, TickFraction = elapsed / tickLength - tick,
                ElapsedSamples = elapsed, RemainingSamples = duration - elapsed, DurationSamples = duration,
                ElapsedMs = elapsed * toMs, RemainingMs = (duration - elapsed) * toMs, DurationMs = duration * toMs,
                ElapsedTicks = elapsed / tickLength, RemainingTicks = (duration - elapsed) / tickLength,
                Occurrence = row.occurrence, Held = row.held != 0, Extrapolated = extrapolated,
                TransportSample = at, SampleRate = sampleRate,
            };
        }
    }
}
