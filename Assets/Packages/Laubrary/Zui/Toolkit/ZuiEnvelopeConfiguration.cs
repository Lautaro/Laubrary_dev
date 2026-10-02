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
        public string xAxisLabel, yAxisLabel;
        /// <summary>Authored value-to-colour mapping; this is content, not theme.</summary>
        public Func<float, Color> yColorFor;
    }
}
