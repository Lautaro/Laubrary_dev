using System.Collections.Generic;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.ZTracker
{
    /// <summary>Colours and sizes for <see cref="ZTrackerTimeline"/>.</summary>
    public struct ZTrackerTimelineStyle
    {
        public Color background, beatBand, rowTick, beatTick, window, windowActive, accent, playhead, hit, miss, label;
        public float labelPoints;

        public static ZTrackerTimelineStyle Default => new ZTrackerTimelineStyle
        {
            background = new Color(0f, 0f, 0f, 0.7f),
            beatBand = new Color(1f, 1f, 1f, 0.05f),
            rowTick = new Color(1f, 1f, 1f, 0.18f),
            beatTick = new Color(1f, 1f, 1f, 0.45f),
            window = new Color(0.35f, 0.95f, 0.55f, 0.28f),
            windowActive = new Color(0.35f, 0.95f, 0.55f, 0.6f),
            accent = new Color(0.55f, 1f, 0.7f, 1f),
            playhead = new Color(1f, 0.95f, 0.55f, 1f),
            hit = new Color(0.55f, 0.95f, 1f, 1f),
            miss = new Color(1f, 0.45f, 0.4f, 1f),
            label = new Color(1f, 1f, 1f, 0.55f),
            labelPoints = 10f,
        };
    }

    /// <summary>
    /// A rhythm timeline for a playing ZTracker song, drawn immediate-mode (call from OnGUI). It shows one pattern —
    /// up to <see cref="MaxRows"/> rows, longer patterns page by 64 — with every row as a tick (beats stronger and
    /// numbered in hex like the tracker), each accent's hit window shaded, the accents marked, and the position the
    /// player hears as a cursor. The window under the cursor brightens, so the player sees when a press would count.
    /// </summary>
    public static class ZTrackerTimeline
    {
        public const int MaxRows = 64;

        static readonly List<double> s_accents = new List<double>();
        static readonly List<int> s_scratch = new List<int>();

        /// <summary>Optional last-press marker: where (in rows from the slot start) and whether it hit.</summary>
        public struct Mark { public bool show, hit; public double row; public float alpha; }

        /// <summary>Draws the pattern the player hears now, with the accents of <paramref name="source"/>. Returns false
        /// (and draws nothing) when the clock cannot answer.</summary>
        public static bool Draw(Rect rect, ZTrackerSongClock clock, in ZTrackerAccentSource source, double windowRows, Mark mark = default)
            => Draw(rect, clock, source, windowRows, mark, ZTrackerTimelineStyle.Default);

        public static bool Draw(Rect rect, ZTrackerSongClock clock, in ZTrackerAccentSource source, double windowRows, Mark mark, in ZTrackerTimelineStyle style)
        {
            if (clock == null || !clock.TryGetHeard(out var p)) return false;
            var pattern = clock.PatternAt(p.Order);
            if (pattern == null) return false;
            ZTrackerAccents.Around(clock, p, source, s_accents, s_scratch);
            double at = p.Row + p.RowFraction;
            int first = (p.Row / MaxRows) * MaxRows;
            int count = Mathf.Min(MaxRows, pattern.lineCount - first);
            DrawStrip(rect, first, count, p.LinesPerBeat, at, s_accents, windowRows, mark, style);
            return true;
        }

        /// <summary>The drawing itself, for callers that compute their own accents. Rows are measured from the start
        /// of the order slot; <paramref name="firstRow"/>..firstRow+rowCount is the visible span.</summary>
        public static void DrawStrip(Rect rect, int firstRow, int rowCount, int linesPerBeat, double playheadRow,
            IReadOnlyList<double> accents, double windowRows, Mark mark, in ZTrackerTimelineStyle style)
        {
            rowCount = Mathf.Clamp(rowCount, 1, MaxRows);
            int lpb = Mathf.Max(1, linesPerBeat);
            float labelH = UIScale.Font(style.labelPoints) * 1.3f;
            var strip = new Rect(rect.x, rect.y, rect.width, Mathf.Max(8f, rect.height - labelH));
            float rowW = strip.width / rowCount;
            float X(double row) => strip.x + (float)((row - firstRow) * rowW);

            Zui.FillRect(strip, style.background);
            for (int b = 0; b * lpb < rowCount; b += 2)
            {
                float x0 = X(firstRow + b * lpb), x1 = Mathf.Min(strip.xMax, X(firstRow + (b + 1) * lpb));
                Zui.FillRect(new Rect(x0, strip.y, x1 - x0, strip.height), style.beatBand);
            }

            // Hit windows, clipped to the strip. The one under the cursor is drawn brighter.
            for (int i = 0; i < accents.Count; i++)
            {
                double a = accents[i];
                float x0 = Mathf.Max(strip.x, X(a - windowRows)), x1 = Mathf.Min(strip.xMax, X(a + windowRows));
                if (x1 <= x0) continue;
                bool active = playheadRow >= a - windowRows && playheadRow <= a + windowRows;
                Zui.FillRect(new Rect(x0, strip.y, x1 - x0, strip.height), active ? style.windowActive : style.window);
            }

            var labelStyle = Zui.TextStyle(style.labelPoints, style.label, TextAnchor.UpperLeft, false, wrap: false);
            for (int r = 0; r <= rowCount; r++)
            {
                int row = firstRow + r;
                bool beat = row % lpb == 0;
                float x = X(row);
                Zui.FillRect(new Rect(x - (beat ? 1f : 0.5f), strip.y, beat ? 2f : 1f, strip.height), beat ? style.beatTick : style.rowTick);
                if (beat && r < rowCount) GUI.Label(new Rect(x + 2f, strip.yMax, rowW * lpb, labelH), row.ToString("X2"), labelStyle);
            }

            // Accents: a bright marker at the start of the accented row.
            float markH = strip.height * 0.55f;
            for (int i = 0; i < accents.Count; i++)
            {
                float x = X(accents[i]);
                if (x < strip.x - 1f || x > strip.xMax + 1f) continue;
                Zui.FillRect(new Rect(x - 2f, strip.y + (strip.height - markH) * 0.5f, 4f, markH), style.accent);
            }

            if (mark.show && mark.alpha > 0f)
            {
                var c = mark.hit ? style.hit : style.miss; c.a *= mark.alpha;
                float x = Mathf.Clamp(X(mark.row), strip.x, strip.xMax);
                Zui.FillDisc(new Rect(x - 6f, strip.y - 6f, 12f, 12f), c);
            }

            float px = Mathf.Clamp(X(playheadRow), strip.x, strip.xMax);
            Zui.FillRect(new Rect(px - 1.5f, strip.y - 4f, 3f, strip.height + 8f), style.playhead);
        }
    }
}
