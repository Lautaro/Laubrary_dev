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
        /// Several playheads at once (0..1 across the span), one per instance playing the curve; the first
        /// <see cref="playheadCount"/> entries are drawn. An entry flagged in <see cref="playheadsFaded"/> is drawn
        /// fainter (for example a note that has been released). View state only.
        public float[] playheads01;
        public bool[] playheadsFaded;
        public int playheadCount;
        /// A loop region (0..1 across the span), drawn as a shaded band between two draggable markers.
        public bool loopShown;
        public float loopStart01, loopEnd01;
        public bool loopPingPong;
        /// A sustain (hold) position (0..1 across the span), drawn as a dashed draggable marker.
        public bool sustainShown;
        public float sustain01;
        /// Markers the user may drag: a marker press wins over adding points, but a press on a point still takes the point.
        public bool markersEditable = true;
        /// A marker was pressed (0 = loop start, 1 = loop end, 2 = sustain): the Undo hook. Then each move reports the
        /// marker and its new position (0..1), and the release ends the gesture.
        public Action<int> onMarkerDragStarted;
        public Action<int, float> onMarkerMoved;
        public Action onMarkerDragEnded;
        /// Labels under the plot: a value at each of <see cref="xTickCount"/> + 1 evenly spaced positions (0..1), for
        /// example seconds or rows. Null = no tick labels.
        public Func<float, string> xTickLabel;
        public int xTickCount = 4;
        /// Whether the axis captions (xAxisLabel / yAxisLabel) are drawn; a view preference a host's menu can switch.
        public bool showAxisLegends = true;
        /// A right-click on the plot that is not on a point (which a host can use for a context menu), with the
        /// pointer's world position.
        public Action<Vector2> onEmptyContext;
        /// A second, read-only line drawn over the plot: the value at each point along it (0..1), in the curve's own
        /// units, for example the curve with an LFO merged into it (T-0641). Clamped to the plot. Null = none.
        public Func<float, float> overlay;
        public string xAxisLabel, yAxisLabel;
        /// <summary>Authored value-to-colour mapping; this is content, not theme.</summary>
        public Func<float, Color> yColorFor;
    }
}
