using System;
using UnityEngine;

namespace Laubrary.Zui
{
    /// <summary>Interaction and authored decorations for the shared retained-mode envelope canvas.</summary>
    public sealed class ZuiEnvelopeConfiguration
    {
        public int minimumPoints = 1;
        public bool addOnLinePress = true;
        public bool rightClickRemovesPoint;
        public bool dragAfterEmptyInsert;
        public bool segmentVerticalOnly;
        public bool moveSelectionFromEmpty = true;
        public bool showReadout = true;
        public bool nearestPointHit;
        public bool geometricSegmentHit;
        public bool usePixelBend;
        public bool requireShiftDuringDrag = true;
        public bool logicalStrokeWidths;
        public bool roundCoordinates;
        public float lineHitDistance = 6f;
        /// <summary>Per-view permission override, without changing saved point permissions.</summary>
        public Func<int, ZUIEnvelopeEditState> pointState;
        public Action onSelectionChanged;
        public bool showFrameLines;
        public int frameCount;
        /// Frame START positions across the span (0..1, ascending), for frames of UNEVEN length. When set it
        /// replaces the evenly spaced frameCount lines: one line per start plus the end, labelled from 1.
        public float[] frameStarts01;
        /// A vertical playhead line at this position across the span (0..1). NaN = none.
        public float playhead01 = float.NaN;
        public string xAxisLabel, yAxisLabel;
        /// <summary>Authored value-to-colour mapping; this is content, not theme.</summary>
        public Func<float, Color> yColorFor;
    }
}
