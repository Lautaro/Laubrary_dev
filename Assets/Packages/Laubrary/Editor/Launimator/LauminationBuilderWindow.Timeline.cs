using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        ZuiLanes _frameTimeline;
        ScrollView _frameTimelineScroll;
        Button _timelineCopy, _timelinePaste, _timelineLead;
        Image _timelineLeadImage;
        Label _timelineConflict;
        int _timelineSelectedPhase;
        int TimelineSelectedPhase
        {
            get => Mathf.Clamp(_timelineSelectedPhase, 0, Mathf.Max(0, _zones.Count - 1));
            set { _timelineSelectedPhase = value; RefreshFrameTimeline(); }
        }

        enum TimelineClipboardKind { None, Timing, Shape, Point, Vector, Events, Phase }
        sealed class TimelineClipboard
        {
            public TimelineClipboardKind Kind;
            public float Timing;
            public MetaFrame Mask;
            public VectorMetaFrame Vector;
            public List<FrameEvent> Events;
            public AnimZone Phase;
        }
        static TimelineClipboard _timelineClipboard = new TimelineClipboard();

        void BuildFrameTimeline(VisualElement host)
        {
            Vector2 scrollOffset = _frameTimelineScroll?.scrollOffset ?? Vector2.zero;
            host.Clear();
            var commands = Z.Row();
            commands.style.flexWrap = Wrap.NoWrap;
            commands.style.height = 25;
            commands.style.flexShrink = 0;
            commands.Add(Z.Button("Reverse", "Reverse the selected frames with all their timings, layers and events.", () => { ReverseSelectedFrames(); SelectTimelineSource(); Refresh(); }).W(64));
            commands.Add(Z.Button("Duplicate", "Copy selected frames after the selection, including all their per-frame data.", () => { DuplicateSelectedFrames(); SelectTimelineSource(); Refresh(); }).W(72));
            commands.Add(Z.IconButton("trash", "Remove selected frames and their attached data. Undo restores them.", () => { DeleteSelectedFrames(); SelectTimelineSource(); Refresh(); }, 24));
            _timelineCopy = Z.IconButton("copy", "Copy the active tool's typed content at the selected frame.", CopyTimelineContent, 24);
            _timelinePaste = Z.IconButton("clipboard", "Paste matching typed content onto selected frames. Incompatible content is never converted.", PasteTimelineContent, 24);
            commands.Add(_timelineCopy); commands.Add(_timelinePaste);
            _timelineLead = Z.Button("Lead-in", "Select the preview-only lead-in. It sits outside the real frame ruler and never carries gameplay metadata.", () =>
            {
                _frameZeroSel = true; _showingFrameZero = SeqRefValid(_frameZero); _seqSelected = -1;
                if (SeqRefValid(_frameZero)) SelectSingle(_frameZero.region, _frameZero.cell); else ClearSelection();
                _seqMultiSel.Clear(); _animPlaying = false; Refresh();
            }).W(88);
            _timelineLead.style.flexDirection = FlexDirection.Row;
            _timelineLeadImage = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _timelineLeadImage.style.width = 20; _timelineLeadImage.style.height = 20;
            _timelineLead.Insert(0, _timelineLeadImage);
            commands.Add(_timelineLead);
            _timelineConflict = Z.Text("", ZuiText.Small, "Phase diagnostics.");
            _timelineConflict.style.width = 24;
            commands.Add(_timelineConflict);
            host.Add(commands);

            _frameTimelineScroll = new ScrollView(ScrollViewMode.Horizontal);
            _frameTimelineScroll.style.flexGrow = 1;
            _frameTimelineScroll.style.minHeight = 0;
            _frameTimelineScroll.contentContainer.style.minWidth = Length.Percent(100);
            _frameTimelineScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _frameTimeline = Z.Lanes(Mathf.Max(1, _sequence.Count),
                "Shared frame columns. Click to preview; drag a picture to reorder. Ctrl adds to the selection; Shift selects a range. Phase edges resize explicit boundaries.",
                gutterWidth: 104);
            _frameTimeline.OnFrameSelected = (frame, additive, range) =>
            {
                _frameZeroSel = false;
                SeekPreviewFrame(frame);
                if (additive) SeqToggle(frame); else if (range) SeqRangeTo(frame); else SeqSelectSingle(frame);
                SelectTimelineSource();
                Refresh();
            };
            _frameTimeline.OnFrameMoved = (from, to) => { MoveSequenceFrame(from, to); SelectTimelineSource(); Refresh(); };
            _frameTimeline.OnFrameContext = frame =>
            {
                if (!IsSeqSelected(frame)) SeqSelectSingle(frame);
                SelectTimelineSource();
                ShowSequenceContextMenu(frame);
            };
            _frameTimeline.OnFrameLaneSelected = lane =>
            {
                _workspaceMode = WorkspaceMode.Animate;
                _animateTool = (AnimateTool)Mathf.Clamp(lane, 0, 3);
                if (_animateTool == AnimateTool.Layers) _animPlaying = false;
                Refresh();
            };
            _frameTimeline.OnFrameRangeChanged = (lane, span, first, last) =>
            {
                if (lane != 3 || _zones.Count == 0) return;
                var phase = _zones[TimelineSelectedPhase];
                if (phase.startFrame == first && phase.endFrame == last) return;
                RecordUndo("Resize phase"); phase.startFrame = first; phase.endFrame = last;
                Refresh();
            };
            _frameTimelineScroll.Add(_frameTimeline);
            host.Add(_frameTimelineScroll);
            RefreshFrameTimeline();
            _frameTimelineScroll.schedule.Execute(() => { if (_frameTimelineScroll != null) _frameTimelineScroll.scrollOffset = scrollOffset; });
        }

        void SyncFrameTimelinePlayhead() => _frameTimeline?.SetFrameWithoutNotify(_animFrame);

        // Registration and transforms edit source sprites. Keep their primary selection synchronized with
        // the timeline's primary frame, otherwise the stage can keep showing/editing an earlier palette pick.
        void SelectTimelineSource()
        {
            if (_seqSelected >= 0 && _seqSelected < _sequence.Count && SeqRefValid(_sequence[_seqSelected]))
            {
                var source = _sequence[_seqSelected];
                SelectSingle(source.region, source.cell);
            }
            else ClearSelection();
        }

        void RefreshFrameTimeline()
        {
            if (_frameTimeline == null) return;
            var pictures = new List<ZuiFramePicture>(_sequence.Count);
            for (int i = 0; i < _sequence.Count; i++)
            {
                var frame = _sequence[i];
                Texture2D texture = null;
                if (SeqRefValid(frame))
                {
                    var region = _regions[frame.region]; region.SyncPivots(GlobalPivot());
                    texture = ThumbTexture(frame.region, frame.cell, region.cells[frame.cell], region.transforms[frame.cell], region.sourceTextureGuid);
                }
                pictures.Add(new ZuiFramePicture(texture, new Rect(0, 0, 1, 1), $"Frame {i + 1}: {FrameMsOf(frame.pct):0} ms. Drag to reorder this frame with its attached data."));
            }
            var timing = new ZuiFrameLane("Timing", "Select the timing tool. Each column reports this frame's duration.");
            var events = new ZuiFrameLane("Events", "Select the events tool. Marks belong to their frame and follow it when reordered.");
            MetaLayer layer = _activeLayer >= 0 && _activeLayer < _metaLayers.Count ? _metaLayers[_activeLayer] : null;
            var layers = new ZuiFrameLane(layer == null ? "Layers" : layer.id, "Select the layers tool. Occupancy of the selected named layer.", !_metaEnabled);
            var phases = new ZuiFrameLane(_zones.Count > 0 ? _zones[TimelineSelectedPhase].name : "Phases", "Select the phases tool. Drag the highlighted range's edges to edit explicit boundaries.", !_zonesEnabled);
            for (int i = 0; i < _sequence.Count; i++)
            {
                timing.Spans.Add(new ZuiFrameSpan(i, i, $"{FrameMsOf(_sequence[i].pct):0}ms", _sequence[i].pct == 0 ? Color.gray : new Color(1, .65f, .2f),
                    $"Frame {i + 1}: timing offset {_sequence[i].pct:+0;-0;0}%, {FrameRef.TimingFactorOf(_sequence[i].pct):0.###}× normal duration."));
                var names = _events.Where(e => e != null && e.frame == i).Select(e => e.name).ToArray();
                if (names.Length > 0) events.Spans.Add(new ZuiFrameSpan(i, i, "●", new Color(1, .8f, .3f), string.Join(", ", names)));
                if (layer != null)
                {
                    bool occupied = layer.mode == MetaLayerMode.Vector
                        ? i < layer.vectorFrames.Count && layer.vectorFrames[i] != null && layer.vectorFrames[i].authored
                        : i < layer.frames.Count && layer.frames[i] != null && (layer.frames[i].HasAny() || !string.IsNullOrEmpty(layer.frames[i].param));
                    if (occupied) layers.Spans.Add(new ZuiFrameSpan(i, i, "●", layer.color, $"{layer.id}: {layer.mode} content on frame {i + 1}."));
                }
            }
            if (_zones.Count > 0)
            {
                var phase = _zones[TimelineSelectedPhase];
                phases.Spans.Add(new ZuiFrameSpan(phase.startFrame, phase.endFrame, phase.name, ZoneColor(TimelineSelectedPhase),
                    $"{phase.name}: frames {phase.startFrame + 1}–{phase.endFrame + 1}. Drag either edge to resize.", true));
            }
            // A minimum readable column width is honest horizontal scrolling, shared by every row and ruler.
            _frameTimeline.style.minWidth = Mathf.Max(260, 104 + _sequence.Count * 46);
            _frameTimeline.SetFrames(pictures, new[] { timing, events, layers, phases });
            _frameTimeline.SetFrameSelection(_frameZeroSel ? null : _seqMultiSel);
            _frameTimeline.SetFrameWithoutNotify(_animFrame);
            _timelineLead.style.visibility = _frameZeroOn ? Visibility.Visible : Visibility.Hidden;
            if (_frameZeroOn && SeqRefValid(_frameZero))
            {
                var leadRegion = _regions[_frameZero.region]; leadRegion.SyncPivots(GlobalPivot());
                _timelineLeadImage.image = ThumbTexture(_frameZero.region, _frameZero.cell, leadRegion.cells[_frameZero.cell], leadRegion.transforms[_frameZero.cell], leadRegion.sourceTextureGuid);
            }
            else _timelineLeadImage.image = null;
            _timelineCopy.SetEnabled(CanCopyTimeline());
            _timelinePaste.SetEnabled(CanPasteTimeline());
            _timelinePaste.tooltip = CanPasteTimeline() ? "Paste the copied " + _timelineClipboard.Kind.ToString().ToLowerInvariant() + " content onto the selection. Undo restores the prior content." : "Copy content of this tool and layer kind, then select a destination frame. Incompatible types are never converted.";
            string diagnostic = string.Join("\n", GetPhaseConflicts());
            _timelineConflict.text = string.IsNullOrEmpty(diagnostic) ? "" : "⚠";
            _timelineConflict.tooltip = diagnostic;
        }

        TimelineClipboardKind CurrentTimelineKind()
        {
            if (_animateTool == AnimateTool.Timing) return TimelineClipboardKind.Timing;
            if (_animateTool == AnimateTool.Events) return TimelineClipboardKind.Events;
            if (_animateTool == AnimateTool.Phases) return TimelineClipboardKind.Phase;
            if (_activeLayer < 0 || _activeLayer >= _metaLayers.Count) return TimelineClipboardKind.None;
            return _metaLayers[_activeLayer].mode == MetaLayerMode.Vector ? TimelineClipboardKind.Vector
                : _metaLayers[_activeLayer].mode == MetaLayerMode.Point ? TimelineClipboardKind.Point : TimelineClipboardKind.Shape;
        }

        bool CanCopyTimeline() => !_frameZeroSel && _seqSelected >= 0 && _seqSelected < _sequence.Count
            && CurrentTimelineKind() != TimelineClipboardKind.None && (CurrentTimelineKind() != TimelineClipboardKind.Phase || _zones.Count > 0);
        bool CanPasteTimeline() => !_frameZeroSel && _seqSelected >= 0 && _seqSelected < _sequence.Count
            && CurrentTimelineKind() != TimelineClipboardKind.None && CurrentTimelineKind() == _timelineClipboard.Kind;

        static FrameEvent CopyTimelineEvent(FrameEvent value, int frame) => new FrameEvent
        { frame = frame, name = value.name, zoundName = value.zoundName, hasPosition = value.hasPosition, position = value.position };

        void CopyTimelineContent()
        {
            if (!CanCopyTimeline()) return;
            var copy = new TimelineClipboard { Kind = CurrentTimelineKind() };
            int f = _seqSelected;
            if (copy.Kind == TimelineClipboardKind.Timing) copy.Timing = _sequence[f].pct;
            else if (copy.Kind == TimelineClipboardKind.Events) copy.Events = _events.Where(e => e != null && e.frame == f).Select(e => CopyTimelineEvent(e, 0)).ToList();
            else if (copy.Kind == TimelineClipboardKind.Phase)
            {
                var phase = _zones[TimelineSelectedPhase];
                copy.Phase = new AnimZone { name = phase.name, startFrame = 0, endFrame = phase.endFrame - phase.startFrame, behavior = phase.behavior };
            }
            else
            {
                var layer = _metaLayers[_activeLayer];
                if (copy.Kind == TimelineClipboardKind.Vector)
                    copy.Vector = layer.vectorFrames != null && f < layer.vectorFrames.Count ? layer.vectorFrames[f]?.Clone() ?? new VectorMetaFrame() : new VectorMetaFrame();
                else copy.Mask = layer.frames != null && f < layer.frames.Count ? layer.frames[f]?.Clone() ?? new MetaFrame() : new MetaFrame();
            }
            _timelineClipboard = copy;
            RefreshFrameTimeline();
        }

        void PasteTimelineContent()
        {
            if (!CanPasteTimeline()) return;
            var targets = _seqMultiSel.Where(f => f >= 0 && f < _sequence.Count).OrderBy(f => f).ToList();
            if (targets.Count == 0) targets.Add(_seqSelected);
            var copy = _timelineClipboard;
            RecordUndo("Paste " + copy.Kind.ToString().ToLowerInvariant());
            if (copy.Kind == TimelineClipboardKind.Phase)
            {
                // Boundaries are authored, not silently clipped. Diagnostics immediately explain an overrun.
                _zones.Add(new AnimZone { name = copy.Phase.name, startFrame = _seqSelected, endFrame = _seqSelected + copy.Phase.endFrame, behavior = copy.Phase.behavior });
                _timelineSelectedPhase = _zones.Count - 1;
            }
            else
            {
                SyncMetaFrames();
                foreach (int f in targets)
                {
                    if (copy.Kind == TimelineClipboardKind.Timing)
                    { var frame = _sequence[f]; frame.pct = copy.Timing; _sequence[f] = frame; }
                    else if (copy.Kind == TimelineClipboardKind.Events)
                    { _events.RemoveAll(e => e != null && e.frame == f); _events.AddRange(copy.Events.Select(e => CopyTimelineEvent(e, f))); }
                    else if (copy.Kind == TimelineClipboardKind.Vector) _metaLayers[_activeLayer].vectorFrames[f] = copy.Vector.Clone();
                    else _metaLayers[_activeLayer].frames[f] = copy.Mask.Clone();
                }
            }
            ClearMaskCache(); _previewHash = -1; Refresh();
        }
    }
}
