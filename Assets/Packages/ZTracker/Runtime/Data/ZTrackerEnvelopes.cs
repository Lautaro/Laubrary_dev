using System;
using System.Collections.Generic;
using System.Linq;

namespace Laubrary.ZTracker.Model
{
    /// <summary>
    /// Envelope columns (T-0601): the one place that knows how a column's points are stored and read. A track's
    /// envelope column keeps its points, per pattern, in that pattern track's automation lane whose id is
    /// <see cref="LaneId"/>; the lane targets the column's command (kind Dynamic: a song command's uid, or a built-in
    /// id). Points sit on rows (0…lineCount−1), values are 0…1 across the command's range, and each point says how
    /// the line leaves it (straight or held). The command is driven only between a lane's first and last point; see
    /// Documentation~/COMMAND_MODEL_DESIGN.md section 10.
    /// </summary>
    public static class ZTrackerEnvelopes
    {
        public const string LanePrefix = "envelope:";
        public const int MaxColumns = 8;
        /// <summary>Built-in commands an envelope column can drive: they set a continuous value (track pre gain, pre pan).</summary>
        public static readonly string[] Builtins = { "0L", "0P" };

        public static string LaneId(string columnUid) => LanePrefix + columnUid;
        public static bool IsEnvelopeLane(AutomationLane lane) => lane?.id != null && lane.id.StartsWith(LanePrefix, StringComparison.Ordinal);
        public static bool IsBuiltin(string command) => command != null && Array.IndexOf(Builtins, command) >= 0;

        public static EnvelopeColumnData Column(TrackData track, int index)
            => track?.envelopeColumns != null && index >= 0 && index < track.envelopeColumns.Count ? track.envelopeColumns[index] : null;

        /// <summary>The lane a column keeps in one pattern track, or null when it has no points there yet.</summary>
        public static AutomationLane Lane(PatternTrack pt, EnvelopeColumnData column)
        {
            if (pt?.automation == null || column == null) return null;
            string id = LaneId(column.uid);
            foreach (var lane in pt.automation) if (lane != null && lane.id == id) return lane;
            return null;
        }

        /// <summary>The lane, created (empty, aimed at the column's command) when missing. A data edit: call inside an Undo step.</summary>
        public static AutomationLane EnsureLane(PatternTrack pt, TrackData track, EnvelopeColumnData column)
        {
            var lane = Lane(pt, column);
            if (lane != null) return lane;
            if (pt.automation == null) pt.automation = new List<AutomationLane>();
            lane = new AutomationLane { id = LaneId(column.uid), interpolation = AutomationInterpolation.Linear, target = Target(track, column.command) };
            pt.automation.Add(lane);
            return lane;
        }

        public static ParameterTarget Target(TrackData track, string command)
            => new ParameterTarget { kind = ParameterKind.Dynamic, trackId = track?.id ?? "", parameter = command ?? "", units = "normalized" };

        /// <summary>Re-aims every lane of a column, in every pattern, at the column's current command.</summary>
        public static void Retarget(SongData song, TrackData track, EnvelopeColumnData column)
        {
            foreach (var pattern in song.patterns)
            {
                var lane = Lane(pattern.tracks.Find(t => t.trackId == track.id), column);
                if (lane != null) lane.target = Target(track, column.command);
            }
        }

        /// <summary>Removes a column and its lanes from every pattern.</summary>
        public static void Remove(SongData song, TrackData track, EnvelopeColumnData column)
        {
            foreach (var pattern in song.patterns)
                pattern.tracks.Find(t => t.trackId == track.id)?.automation?.RemoveAll(l => l != null && l.id == LaneId(column.uid));
            track.envelopeColumns.Remove(column);
        }

        static bool StepFrom(AutomationLane lane, AutomationPoint a)
            => a.segment == AutomationShape.Step || a.segment == AutomationShape.Lane && lane.interpolation == AutomationInterpolation.Step;

        /// <summary>
        /// The drawn value at <paramref name="line"/>, or null outside the lane's span (before its first or after its
        /// last point), where the column does not drive its command. <paramref name="before"/> asks for the value just
        /// before the line, which differs from the value at it only where a held segment jumps.
        /// </summary>
        public static float? ValueAt(AutomationLane lane, double line, bool before = false)
        {
            var points = lane?.points;
            if (points == null || points.Count == 0) return null;
            int count = points.Count;
            if (line < points[0].line || line > points[count - 1].line || before && line == points[0].line) return null;
            for (int i = 0; i < count - 1; i++)
            {
                var a = points[i]; var b = points[i + 1];
                if (line < b.line || before && line == b.line)
                {
                    if (StepFrom(lane, a) || b.line == a.line) return a.value;
                    return (float)(a.value + (b.value - a.value) * ((line - a.line) / (b.line - a.line)));
                }
            }
            return points[count - 1].value;
        }

        /// <summary>The point at an exact row, or null.</summary>
        public static AutomationPoint PointAt(AutomationLane lane, int row) => lane?.points?.FirstOrDefault(p => p != null && p.line == row);

        /// <summary>The point a segment starts from at <paramref name="line"/> (the last point at or before it, with a later point), or null.</summary>
        public static AutomationPoint SegmentAt(AutomationLane lane, double line)
        {
            var points = lane?.points;
            if (points == null) return null;
            for (int i = 0; i < points.Count - 1; i++) if (points[i].line <= line && line < points[i + 1].line) return points[i];
            return null;
        }

        /// <summary>Puts a point on a row (replacing one already there) and keeps the lane sorted. A data edit.</summary>
        public static AutomationPoint Set(AutomationLane lane, int row, float value, AutomationShape? segment = null)
        {
            var point = PointAt(lane, row);
            if (point == null) { point = new AutomationPoint { line = row }; lane.points.Add(point); lane.points.Sort((x, y) => x.line.CompareTo(y.line)); }
            point.value = Math.Max(0f, Math.Min(1f, value));
            if (segment.HasValue) point.segment = segment.Value;
            return point;
        }
    }
}
