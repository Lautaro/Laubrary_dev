using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Classical-CV sprite detection for the Animation Builder's "auto add sprites from marquee" (right-click a
    /// marquee → Add/Replace Sprites). Projection-profile segmentation bounded to a box: no LLM, no network, no
    /// writes to the sheet. (The old standalone whole-sheet "Auto-Scavenge" grouping window was a dead end and
    /// was removed; only this marquee helper remains.)
    /// </summary>
    public static class AutoScavenger
    {
        /// <summary>Detection tunables. Coordinates everywhere are texture px, bottom-left origin.</summary>
        public struct Settings
        {
            public int alphaThreshold;          // a pixel is content when alpha > this (and not the bg key)
            public RegionSlicer.ColorKey key;   // optional solid-background-colour key (no-alpha rips)
            public int minRowGap;               // empty scanlines needed to split two rows (>=1)
            public int minColGap;               // empty columns needed to split two frames within a row (>=1)
            public int minFrameW, minFrameH;    // drop specks/noise smaller than this
            public bool trimFrames;             // store each frame's tight content bbox (else the raw cell)

            public static Settings Default => new Settings
            {
                alphaThreshold = 0,
                key = default,
                minRowGap = 1,
                minColGap = 2,
                minFrameW = 2,
                minFrameH = 2,
                trimFrames = true,
            };
        }

        /// <summary>
        /// Detect sprite cells inside a SUB-REGION of the texture (a marquee) via projection profiles: find
        /// content rows separated by blank scanlines, then split each row into frames at blank columns. Returns
        /// the cells in natural reading order — top-to-bottom rows, left-to-right within each row.
        /// </summary>
        public static List<Rect> DetectCellsInBox(Color32[] px, int texW, int texH, RectInt box, Settings s)
        {
            var cells = new List<Rect>();
            int bx0 = Mathf.Clamp(box.xMin, 0, texW);
            int by0 = Mathf.Clamp(box.yMin, 0, texH);
            int bx1 = Mathf.Clamp(box.xMax, 0, texW);
            int by1 = Mathf.Clamp(box.yMax, 0, texH);
            if (px == null || bx1 <= bx0 || by1 <= by0) return cells;

            int bw = bx1 - bx0, bh = by1 - by0;

            // Row projection within the box.
            var rowHas = new bool[bh];
            for (int yy = 0; yy < bh; yy++)
            {
                int rowBase = (by0 + yy) * texW;
                for (int x = bx0; x < bx1; x++)
                    if (RegionSlicer.IsContent(px[rowBase + x], s.alphaThreshold, s.key)) { rowHas[yy] = true; break; }
            }

            var rowRuns = FindRuns(rowHas, Mathf.Max(1, s.minRowGap));
            // Visual top-to-bottom = descending y, so walk the row runs in reverse.
            for (int ri = rowRuns.Count - 1; ri >= 0; ri--)
            {
                int y0 = by0 + rowRuns[ri].start, y1 = by0 + rowRuns[ri].end; // [y0, y1)

                // Column projection restricted to this row band.
                var colHas = new bool[bw];
                for (int xx = 0; xx < bw; xx++)
                {
                    int x = bx0 + xx;
                    for (int y = y0; y < y1; y++)
                        if (RegionSlicer.IsContent(px[y * texW + x], s.alphaThreshold, s.key)) { colHas[xx] = true; break; }
                }

                var colRuns = FindRuns(colHas, Mathf.Max(1, s.minColGap));
                foreach (var cr in colRuns)
                {
                    var cell = new Rect(bx0 + cr.start, y0, cr.end - cr.start, y1 - y0);
                    if (TryFinalizeCell(px, texW, texH, cell, s, out Rect outCell))
                        cells.Add(outCell);
                }
            }
            return cells;
        }

        /// <summary>Optionally trim a candidate cell to its content bbox, then reject it if empty or below the
        /// min-size floor. Returns the cell to store (trimmed or raw) in <paramref name="outCell"/>.</summary>
        private static bool TryFinalizeCell(Color32[] px, int texW, int texH, Rect cell, Settings s, out Rect outCell)
        {
            outCell = cell;
            if (s.trimFrames)
            {
                var trimmed = RegionSlicer.TrimToContent(px, texW, texH, cell, s.alphaThreshold, out bool empty, s.key);
                if (empty) return false;
                outCell = trimmed;
            }
            return outCell.width >= Mathf.Max(1, s.minFrameW) && outCell.height >= Mathf.Max(1, s.minFrameH);
        }

        /// <summary>Maximal runs of <c>true</c> in <paramref name="has"/>, merging any two runs separated by
        /// FEWER than <paramref name="minGap"/> consecutive <c>false</c> entries (so small internal holes don't
        /// over-split). Returns half-open ranges [start, end) in ascending index order.</summary>
        private static List<(int start, int end)> FindRuns(bool[] has, int minGap)
        {
            var runs = new List<(int start, int end)>();
            if (has == null) return runs;
            int n = has.Length;
            int i = 0;
            while (i < n)
            {
                if (!has[i]) { i++; continue; }
                int start = i;
                while (i < n && has[i]) i++;
                int end = i; // exclusive
                if (runs.Count > 0 && start - runs[runs.Count - 1].end < minGap)
                    runs[runs.Count - 1] = (runs[runs.Count - 1].start, end); // bridge the small gap
                else
                    runs.Add((start, end));
            }
            return runs;
        }
    }
}
