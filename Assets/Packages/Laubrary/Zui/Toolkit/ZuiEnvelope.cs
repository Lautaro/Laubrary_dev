using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    /// Per-instance envelope configuration — the UI Toolkit analog of ZUIEnvelopeRuntime.
    public class ZuiEnvelopeOptions
    {
        public float xMin = 0f, xMax = 1f;
        public float yMin = 0f, yMax = 1f;
        /// Force first + last points to NotEditable regardless of their own editState.
        public bool anchorsLocked = false;
        public bool editable = true;
        public bool allowAddPoints = true;
        public bool allowRemovePoints = true;
        public bool allowExponentEdit = true;
        public bool allowBoxSelect = true;
        public bool allowSegmentDrag = true;
        public bool showGrid = true;
        public int gridRows = 4;
        public int minPoints = 1;
        /// Draw each point's value as small text beside it.
        public bool showValueLabels = false;
        public Color curveColor = new Color(0.3f, 0.8f, 1f);
        /// Vertical frame-boundary markers. When showFrameLines is on and frameCount > 1, draw a faint
        /// vertical line at each animation frame's position across the envelope's life span, labelled with
        /// the frame index — so an author can read exactly which frame a part of the curve lands on. The
        /// LINES are drawn per-frame; the NUMBERS auto-thin (every Nth frame) when they'd otherwise overlap,
        /// so a long animation doesn't become an unreadable smear of digits.
        public bool showFrameLines = false;
        public int frameCount = 0;
        /// Frame start positions (0..1, ascending) for frames of uneven length — replaces the even spacing of
        /// frameCount when set. Lines are labelled from 1, matching the Launimator sequence strip.
        public float[] frameStarts01 = null;
        /// What the vertical markers MEAN. Frame (default) = animation-frame boundaries (0…frameCount-1) —
        /// the task-#34 behaviour, unchanged. Index = particle-index positions (0…frameCount-1) for a curve
        /// whose X axis is a particle INDEX rather than time (e.g. scale-by-index): the SAME line/label/
        /// thinning code, just relabelled as indices; frame lines are never drawn in this mode (the count IS
        /// the index count). Both modes share evenly-spaced numbered verticals.
        public enum MarkerMode { Frame, Index }
        public MarkerMode markerMode = MarkerMode.Frame;
        /// Optional axis labels drawn on the envelope: xAxisLabel along the bottom, yAxisLabel up the left.
        /// Null (default) = no caption.
        public string xAxisLabel = null;
        public string yAxisLabel = null;
        /// Optional Y-axis colour legend. Maps a Y-axis VALUE (anywhere in [yMin..yMax]) to the colour it
        /// resolves to — e.g. a gradient / palette lookup the envelope is driving. When non-null the painter:
        ///   • draws a thin VERTICAL colour-gradient strip in the left gutter, sampled top (yMax) → bottom
        ///     (yMin) through yColorFor, so an author reads at a glance which colour each height maps to; and
        ///   • TINTS every control-point handle with yColorFor(point.value) (a state-coloured ring keeps the
        ///     hover / selected / locked feedback), so each point wears the very colour it will produce and
        ///     lines up with the strip at its own height.
        /// Colours are shown OPAQUE (hue is the signal, so a low-alpha colour still reads). Null (default) =
        /// no strip; handle colours then come from the envelope's presentation.
        public Func<float, Color> yColorFor = null;
    }

    /// <summary>Generic-envelope API adapter over the shared retained-mode canvas.</summary>
    public class ZuiEnvelope : ZuiSkinEnvelope
    {
        readonly ZuiEnvelopeOptions _options;
        public Action OnBeforeMutate;
        public Action OnChanged;
        public Action OnSelectionChanged;
        public IReadOnlyList<int> Selected => SelectedPoints;
        public bool HasLayout => !float.IsNaN(contentRect.width) && contentRect.width > 0f
            && !float.IsNaN(contentRect.height) && contentRect.height > 0f;

        public ZuiEnvelope(List<ZUIEnvelopePoint> points, ZuiEnvelopeOptions options,
            string tooltip, float width = float.NaN, float height = float.NaN)
            : base(points ?? throw new ArgumentNullException(nameof(points)), Color.white, new ZUIEnvelopeDef(), new ZUIEnvelopeRuntime())
        {
            _options = options ?? new ZuiEnvelopeOptions();
            AddToClassList("zui-envelope--standard");
            if (!float.IsNaN(width)) style.width = width;
            if (!float.IsNaN(height)) style.height = height;
            this.tooltip = tooltip;
            configuration.addOnLinePress = false;
            configuration.rightClickRemovesPoint = true;
            configuration.dragAfterEmptyInsert = true;
            configuration.segmentVerticalOnly = true;
            configuration.moveSelectionFromEmpty = false;
            configuration.showReadout = false;
            configuration.nearestPointHit = true;
            configuration.geometricSegmentHit = true;
            configuration.usePixelBend = true;
            configuration.requireShiftDuringDrag = false;
            configuration.logicalStrokeWidths = true;
            configuration.roundCoordinates = true;
            configuration.onSelectionChanged = () => OnSelectionChanged?.Invoke();
            rt.onDragStarted = () => OnBeforeMutate?.Invoke();
            rt.onDragUpdated = rt.onMutated = () => OnChanged?.Invoke();
            Prepare();
        }

        protected override void Prepare()
        {
            if (_options == null) return;
            rt.xMin = _options.xMin; rt.xMax = _options.xMax;
            rt.yMin = _options.yMin; rt.yMax = _options.yMax;
            rt.anchorsLocked = _options.anchorsLocked; rt.editable = _options.editable;
            rt.allowAddPoints = _options.allowAddPoints; rt.allowRemovePoints = _options.allowRemovePoints;
            rt.allowExponentEdit = _options.allowExponentEdit; rt.allowBoxSelect = _options.allowBoxSelect;
            rt.allowSegmentDrag = _options.allowSegmentDrag; rt.showGrid = _options.showGrid;
            rt.showValueLabels = _options.showValueLabels;
            def.gridRows = _options.gridRows;
            curveColor = _options.curveColor;
            configuration.minimumPoints = _options.minPoints;
            configuration.showFrameLines = _options.showFrameLines;
            configuration.frameCount = _options.frameCount;
            configuration.frameStarts01 = _options.frameStarts01;
            configuration.xAxisLabel = _options.xAxisLabel; configuration.yAxisLabel = _options.yAxisLabel;
            configuration.yColorFor = _options.yColorFor;
        }

        /// <summary>Repaint after another editor or Undo changed the caller-owned points.</summary>
        public void Refresh() => Repaint();

        /// Replace the uneven frame lines (see <see cref="ZuiEnvelopeOptions.frameStarts01"/>); null hides them.
        public void SetFrameStarts(float[] starts01)
        {
            _options.frameStarts01 = starts01; _options.showFrameLines = starts01 != null;
            configuration.frameStarts01 = starts01; configuration.showFrameLines = starts01 != null;
            Repaint();
        }

        /// Move the playhead line (0..1 across the span); NaN hides it. View state only — never a mutation.
        public void SetPlayhead(float t01)
        {
            configuration.playhead01 = t01;
            MarkDirtyRepaint();
        }
    }
}
