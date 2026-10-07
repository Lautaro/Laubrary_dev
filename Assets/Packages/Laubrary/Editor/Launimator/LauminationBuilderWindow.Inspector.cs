using System;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.LaunimatorZounds.Editor;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        private bool _numericGestureActive;
        private bool _numericGestureRecorded;

        private void EditValue(string label, Action mutation, bool rebuild = false)
        {
            if (!_numericGestureActive || !_numericGestureRecorded)
            {
                RecordUndo(label);
                _numericGestureRecorded = _numericGestureActive;
            }
            mutation(); _previewHash = -1;
            if (rebuild) Refresh(); else Dirty();
        }

        // ZUI's native Undo grouping cannot collapse this document's snapshot history. Observe the
        // enclosing field so both its label and its built-in numeric scrub grip share one snapshot.
        private VisualElement NumericField(string label, string tooltip, VisualElement control)
        {
            var field = Z.Field(label, tooltip, control);
            field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _numericGestureActive = true; _numericGestureRecorded = false;
            }, TrickleDown.TrickleDown);
            void EndNumericGesture() { _numericGestureActive = false; _numericGestureRecorded = false; }
            field.RegisterCallback<PointerUpEvent>(_ => EndNumericGesture(), TrickleDown.TrickleDown);
            field.RegisterCallback<PointerCaptureOutEvent>(_ => EndNumericGesture(), TrickleDown.TrickleDown);
            field.RegisterCallback<DetachFromPanelEvent>(_ => EndNumericGesture());
            return field;
        }

        private void BuildSheetTools(VisualElement root)
        {
            if (_toolMode == ToolMode.Grid && !_detectMode)
            {
                root.Add(Z.Segmented((int)_mode, new[] { "Count", "Cell size" }, "Divide the marquee by a row and column count, or fixed cell dimensions.", v => EditValue("Grid mode", () => _mode = (RegionSlicer.GridMode)v, true)));
                root.Add(_mode == RegionSlicer.GridMode.FixedColsRows
                    ? WrapRow(NumericField("Columns", "Number of columns inside the marquee.", Z.Int(_cols, "Number of columns.", v => EditValue("Columns", () => _cols = Mathf.Max(1, v)), 48)), NumericField("Rows", "Number of rows inside the marquee.", Z.Int(_rows, "Number of rows.", v => EditValue("Rows", () => _rows = Mathf.Max(1, v)), 48)))
                    : WrapRow(NumericField("Width", "Cell width in pixels.", Z.Int(_cellW, "Cell width in pixels.", v => EditValue("Cell width", () => _cellW = Mathf.Max(1, v)), 48)), NumericField("Height", "Cell height in pixels.", Z.Int(_cellH, "Cell height in pixels.", v => EditValue("Cell height", () => _cellH = Mathf.Max(1, v)), 48))));
                root.Add(WrapRow(NumericField("Gap", "Pixels between cells.", Z.Int(_spacing, "Pixels between cells.", v => EditValue("Cell gap", () => _spacing = Mathf.Max(0, v)), 48)), NumericField("Inset", "Pixels removed inside each cell boundary.", Z.Int(_padding, "Pixels removed inside each boundary.", v => EditValue("Cell inset", () => _padding = Mathf.Max(0, v)), 48))));
                root.Add(Z.Toggle("Trim alpha", "Trim cells to their visible content after slicing.", _alphaTrim, v => EditValue("Alpha trim", () => _alphaTrim = v)));
                _addRegionButton = Z.Button("Add slices", "Add the marquee's grid cells to the candidate sprites.", () => { AddRegion(); Refresh(); });
                _addRegionButton.SetEnabled(_hasBox); root.Add(_addRegionButton);
            }
            if (_detectMode)
            {
                root.Add(Z.Button("Detect sprites", "Preview detected cuts inside the marquee, or the whole image when no marquee exists.", () =>
                {
                    if (!_hasBox) { _box = new Rect(0, 0, _texW, _texH); _hasBox = true; }
                    _detectedBox = _box;
                    var pixels = GetPixels(); if (pixels == null) return;
                    var settings = AutoScavenger.Settings.Default; settings.alphaThreshold = _alphaThreshold; settings.key = CurrentColorKey();
                    _detectedCells.Clear(); _detectedCells.AddRange(AutoScavenger.DetectCellsInBox(pixels, _texW, _texH,
                        new RectInt(Mathf.RoundToInt(_box.x), Mathf.RoundToInt(_box.y), Mathf.RoundToInt(_box.width), Mathf.RoundToInt(_box.height)), settings));
                    Refresh();
                }));
                var addCuts = Z.Button($"Add {_detectedCells.Count} slices", "Accept the highlighted detected cuts into the sprite candidates.", () => CommitDetectedCuts(false));
                addCuts.SetEnabled(_detectedCells.Count > 0); root.Add(addCuts);
                var sequenceCuts = Z.Button("Use as sequence", "Accept the highlighted cuts and replace this palette and sequence in one undoable operation.", () => CommitDetectedCuts(true));
                sequenceCuts.SetEnabled(_detectedCells.Count > 0); root.Add(sequenceCuts);
            }
            root.Add(Z.MiniRadio((int)_pivot, PivotLabels, "Registration point assigned to newly identified sprites.", v => EditValue("Default pivot", () => _pivot = (GridSlicer.PivotMode)v, true)));
            if (_pivot == GridSlicer.PivotMode.Custom)
                root.Add(Z.Vector2Field("Pivot", () => _customPivot, v => _customPivot = v, this,
                    new ZuiValue2DControl.Options().WithRange(0, 1, 0, 1).WithPlotSize(90), "Registration point inside each new sprite.", Dirty, () => RecordUndo("Default pivot")));
            root.Add(Z.MicroSlider("Alpha threshold", _alphaThreshold, 0, 255, "Pixels above this alpha value count as visible content.", v => { _alphaThreshold = Mathf.RoundToInt(v); Dirty(); }, 230, decimals: 0, onBeforeMutate: () => RecordUndo("Alpha threshold")));
            root.Add(WrapRow(Z.Toggle("Key colour", "Treat pixels near the selected colour as transparent.", _bgKeyEnabled, v => EditValue("Colour key", () => { _bgKeyEnabled = v; RebuildDisplaySheet(); })),
                Z.Color(_bgKey, "The sheet colour treated as transparent.", v => EditValue("Colour key", () => { _bgKey = (Color32)v; RebuildDisplaySheet(); }), 52, showAlpha: false),
                Z.Button("Pick", "Sample the next pixel clicked on the sheet as the transparent colour.", () => { _pickingBgColor = true; Dirty(); })));
            root.Add(Z.MicroSlider("Tolerance", _bgTolerance, 0, 255, "Maximum difference per colour channel for a pixel to become transparent.", v => { _bgTolerance = Mathf.RoundToInt(v); RebuildDisplaySheet(); Dirty(); }, 230, decimals: 0, onBeforeMutate: () => RecordUndo("Colour tolerance")));
            root.Add(Z.MicroSlider("Zoom", _zoom, .1f, 8, "Magnification of the sheet stage.", v => { _zoom = v; Dirty(); }, 230));
        }

        private void CommitDetectedCuts(bool asSequence)
        {
            if (_detectedCells.Count == 0 || _sheet == null) return;
            if (!asSequence) RecordUndo("Accept detected slices");
            if (asSequence)
            {
                RemapSequence(Array.Empty<int>(), "Accept detected slices");
                _regions.Clear(); ClearSelection(); ClearThumbCache();
                _frameZero = new CellRef(-1, -1); _frameZeroOn = false; _frameZeroSel = false;
            }
            var region = new Region { label = "detected", sourceTextureGuid = CurrentSheetGuid(), bounds = _detectedBox };
            region.cells.AddRange(_detectedCells); region.SyncPivots(GlobalPivot());
            int index = _regions.Count; _regions.Add(region);
            if (asSequence) for (int i = 0; i < region.cells.Count; i++) _sequence.Add(new CellRef(index, i));
            SelectSingle(index, 0); _detectedCells.Clear(); SyncMetaFrames(); _previewHash = -1; Refresh();
        }

        private void BuildSpriteInspector(VisualElement root)
        {
            if (_spriteTool == SpriteTool.Align)
            {
                root.Add(Z.Segmented(_fixedFrame ? 1 : 0, new[] { "Auto size", "Fixed box" }, "Fit baked frames around their content, or keep an exact output size.", v => EditValue("Frame sizing", () => { bool old = _fixedFrame; _fixedFrame = v == 1; if (_fixedFrame && !old) FitFrameBox(); }, true)));
                if (_fixedFrame)
                {
                    root.Add(WrapRow(NumericField("Width", "Baked frame width in pixels.", Z.Int(_frameW, "Baked frame width.", v => EditValue("Frame width", () => _frameW = Mathf.Max(1, v)), 48)), NumericField("Height", "Baked frame height in pixels.", Z.Int(_frameH, "Baked frame height.", v => EditValue("Frame height", () => _frameH = Mathf.Max(1, v)), 48))));
                    root.Add(Z.Button("Fit box", "Resize the output box around every frame at its current registration.", () => EditValue("Fit frame box", FitFrameBox, true)));
                }
                root.Add(WrapRow(Z.Button("Feet", "Align selected sprites at their visible bottom centre.", () => { BaselineSelected(); Dirty(); }), Z.Button("Head", "Align selected sprites at their visible top centre.", () => { TopCenterSelected(); Dirty(); })));
                root.Add(Z.MicroSlider("Ghosts before", _ghostBefore, 0, 8, "Neighbouring frames shown behind the selection.", v => { _ghostBefore = Mathf.RoundToInt(v); Dirty(); }, 230, decimals: 0));
                root.Add(Z.MicroSlider("Ghosts after", _ghostAfter, 0, 8, "Neighbouring frames shown after the selection.", v => { _ghostAfter = Mathf.RoundToInt(v); Dirty(); }, 230, decimals: 0));
                root.Add(Z.MicroSlider("Ghost opacity", _ghostOpacity, 0, 1, "Opacity of neighbouring registration ghosts.", v => { _ghostOpacity = v; Dirty(); }, 230));
            }
            else if (HasSelectedCell())
            {
                var transform = _regions[_selRegion].transforms[_selCell];
                root.Add(WrapRow(Z.Button("Flip H", "Mirror the selected sprites horizontally.", () => { MutateSelectedTransforms(t => { t.flipX = !t.flipX; return t; }); Dirty(); }), Z.Button("Flip V", "Mirror the selected sprites vertically.", () => { MutateSelectedTransforms(t => { t.flipY = !t.flipY; return t; }); Dirty(); }),
                    Z.Button("90°", "Rotate the selected sprites ninety degrees counter-clockwise.", () => { MutateSelectedTransforms(t => { t.rot90 = (t.rot90 + 1) % 4; return t; }); Dirty(); })));
                root.Add(Z.MicroSlider("Angle", transform.angle, -180, 180, "Rotate the selected sprites counter-clockwise in degrees.", v => { MutateSelectedTransforms(t => { t.angle = v; return t; }, false); Dirty(); }, 230, onBeforeMutate: () => RecordUndo("Rotate sprites")));
                root.Add(WrapRow(NumericField("Scale X", "Horizontal scale of the selected sprites.", Z.Float(transform.SX, "Horizontal scale.", v => EditValue("Scale sprites", () => MutateSelectedTransforms(t => { t.scaleX = Mathf.Max(.01f, v); return t; }, false)), 55)), NumericField("Y", "Vertical scale of the selected sprites.", Z.Float(transform.SY, "Vertical scale.", v => EditValue("Scale sprites", () => MutateSelectedTransforms(t => { t.scaleY = Mathf.Max(.01f, v); return t; }, false)), 55))));
                root.Add(Z.Toggle("Smooth", "Use smooth interpolation for arbitrary rotation and scaling.", transform.smooth, v => { MutateSelectedTransforms(t => { t.smooth = v; return t; }); Dirty(); }));
                root.Add(Z.Button("Reset transform", "Remove every transform from the selected sprites.", () => { MutateSelectedTransforms(_ => CellTransform.Identity); Refresh(); }));
            }
            var selection = WrapRow(Z.Button("Duplicate", "Create independent copies of the selected source sprites.", () => { DuplicateSelectedSprites(); Refresh(); }), Z.Button("Trim", "Trim the selected source sprites to visible content.", () => { TrimSelected(); Refresh(); }), Z.IconButton("delete", "Delete selected sprites and every sequence frame using them.", () => { DeleteSelectedCells(); Refresh(); }, 24));
            selection.SetEnabled(HasSelectedCell()); root.Add(selection);
            root.Add(Z.Button("Import frame…", "Import an image from your computer as a new sequence frame.", () => ImportExternalImage(true)));
        }

        private void BuildTimingInspector(VisualElement root)
        {
            root.Add(Z.Field("Name", "The animation name used by its consumers.", Z.TextInput(_animName, "The animation name.", v => EditValue("Animation name", () => _animName = v), 165)));
            root.Add(Z.MicroSlider("FPS", _animFps, 1, 60, "Base frame rate. Individual timing offsets multiply the base frame duration.", v => { _animFps = v; SyncPreviewTimings(); Dirty(); }, 230, onBeforeMutate: () => RecordUndo("Frame rate")));
            bool lead = _frameZeroSel && _frameZeroOn && SeqRefValid(_frameZero);
            bool selected = _seqSelected >= 0 && _seqSelected < _sequence.Count;
            float value = lead ? _frameZero.pct : selected ? _sequence[_seqSelected].pct : 0;
            var timing = Z.MicroSlider("Timing offset %", value, FrameRef.MinTimingPercent, FrameRef.MaxTimingPercent,
                "0 is one FPS tick; +100 is twice as long; -100 is half as long. Applies to every selected frame.", v =>
                {
                    v = Mathf.Round(v);
                    if (lead) _frameZero.pct = v;
                    else if (_seqMultiSel.Count > 0) foreach (int index in _seqMultiSel.Where(i => i >= 0 && i < _sequence.Count)) { var f = _sequence[index]; f.pct = v; _sequence[index] = f; }
                    else if (selected) { var f = _sequence[_seqSelected]; f.pct = v; _sequence[_seqSelected] = f; }
                    SyncPreviewTimings(); Dirty();
                }, 160, decimals: 0, onBeforeMutate: () => RecordUndo("Frame timing"));
            timing.SetEnabled(lead || selected);
            _timingReadout = Z.Text("", ZuiText.Small, "Duration multiplier and milliseconds at the current frame rate.").W(76);
            root.Add(Z.Row(timing, _timingReadout));
            timing.tooltip += $" Current duration: {FrameRef.TimingFactorOf(value):0.###}×, {FrameMsOf(value):0.##} ms.";
            root.Add(Z.Toggle("Lead-in", "Show a separate starting pose before each editor preview loop. It is saved for preview only and is not a game frame.", _frameZeroOn, v => EditValue("Lead-in", () =>
            {
                _frameZeroOn = v; _frameZeroSel = false; _showingFrameZero = false;
                if (v && !SeqRefValid(_frameZero) && HasSelectedCell()) _frameZero = new CellRef(_selRegion, _selCell) { pct = DefaultFrameZeroPct };
            }, true)));
            var use = Z.Button("Use selection", "Use the selected source sprite as the separate lead-in pose.", () => { SetFrameZero(_selRegion, _selCell); Refresh(); });
            use.SetEnabled(HasSelectedCell()); root.Add(use);
            root.Add(Z.Segmented((int)_loopDivider, new[] { "Seamless", "Pause" }, "Preview loops continuously or leaves an empty pause between repeats.", v => { _loopDivider = (LoopDivider)v; Refresh(); }));
            if (_loopDivider != LoopDivider.None) root.Add(Z.Field("Pause seconds", "Preview-only gap between loops.", Z.Float(_loopPause, "Gap between preview loops.", v => _loopPause = Mathf.Max(0, v), 65)));
        }

        private void BuildEventInspector(VisualElement root)
        {
            var list = WrapRow();
            for (int i = 0; i < _events.Count; i++)
            {
                int index = i; var ev = _events[i];
                var button = Z.Button(ev.name + " · " + (ev.frame + 1), "Select this event and show its frame.", () => { _selectedEvent = index; SeekPreviewFrame(ev.frame); Refresh(); });
                if (i == _selectedEvent) button.AddToClassList("zui-radio__on"); list.Add(button);
            }
            root.Add(list);
            var add = Z.Button("Add event", "Add a named gameplay event to the current frame.", () => EditValue("Add event", () => { _events.Add(new FrameEvent { frame = Mathf.Max(0, ActiveFrameIndex()), name = "event" }); _selectedEvent = _events.Count - 1; }, true));
            add.SetEnabled(_sequence.Count > 0); root.Add(add);
            if (_selectedEvent < 0 || _selectedEvent >= _events.Count) return;
            var current = _events[_selectedEvent];
            root.Add(Z.TextInput(current.name, "Declare the event's name; consumers pick this name from the animation.", v => EditValue("Event name", () => current.name = v), 190));
            root.Add(Z.MicroSlider("Frame", current.frame + 1, 1, Mathf.Max(1, _sequence.Count), "The frame that fires this event, numbered from one.", v => { current.frame = Mathf.RoundToInt(v) - 1; Dirty(); }, 230, decimals: 0, onBeforeMutate: () => RecordUndo("Event frame")));
            var sound = Z.Button(string.IsNullOrEmpty(current.zoundName) ? "Choose sound…" : current.zoundName, "Pick a sound to play when the event fires.", null);
            sound.clicked += () => ZoundPickerPopup.Show(new Vector2(sound.worldBound.x, sound.worldBound.yMax), name => EditValue("Event sound", () => current.zoundName = name, true));
            root.Add(WrapRow(sound, Z.IconButton("close", "Remove the event's sound assignment.", () => EditValue("Event sound", () => current.zoundName = null, true), 24)));
            root.Add(Z.Toggle("Position", "Give the event an explicit pixel position on its frame.", current.hasPosition, v => EditValue("Event position", () => current.hasPosition = v, true)));
            if (current.hasPosition)
            {
                BakedFrameSize(current.frame, out int w, out int h);
                root.Add(Z.Vector2Field("Pixel", () => current.position, v => current.position = Vector2Int.RoundToInt(v), current,
                    new ZuiValue2DControl.Options().WithRange(0, Mathf.Max(1, w - 1), 0, Mathf.Max(1, h - 1)).WithPlotSize(130), "Position of this event within its frame in pixels.", Dirty, () => RecordUndo("Event position")));
            }
            root.Add(Z.IconButton("delete", "Remove this event.", () => EditValue("Delete event", () => { _events.RemoveAt(_selectedEvent); _selectedEvent = -1; }, true), 24));
        }

        private void BuildLayerInspector(VisualElement root)
        {
            root.Add(Z.Toggle("Layers enabled", "Save and expose these metadata layers to the game.", _metaEnabled, v => EditValue("Layers enabled", () => { _metaEnabled = v; PausePreview(); }, true)));
            var list = WrapRow();
            for (int i = 0; i < _metaLayers.Count; i++)
            {
                int index = i; var layer = _metaLayers[i];
                var button = Z.Button(layer.id, "Select this metadata layer to edit its current frame.", () => { _activeLayer = index; PausePreview(); Refresh(); });
                if (i == _activeLayer) button.AddToClassList("zui-radio__on"); list.Add(button);
            }
            root.Add(list);
            root.Add(Z.Button("Add layer", "Add a named Shape, Point or Vector metadata layer.", () => EditValue("Add layer", () =>
            {
                _metaEnabled = true; _metaLayers.Add(new MetaLayer { id = "layer" + (_metaLayers.Count + 1), color = MetaLayer.Palette[_metaLayers.Count % MetaLayer.Palette.Length] });
                _activeLayer = _metaLayers.Count - 1; SyncMetaFrames(); PausePreview();
            }, true)));
            var active = ActiveLayer(); if (active == null) return;
            root.Add(WrapRow(Z.TextInput(active.id, "Declare this layer's name for consumers to pick.", v => EditValue("Layer name", () => active.id = v), 175), Z.IconButton("delete", "Delete this layer and its data on every frame.", () => EditValue("Delete layer", () => { _metaLayers.RemoveAt(_activeLayer); _activeLayer = Mathf.Min(_activeLayer, _metaLayers.Count - 1); }, true), 24)));
            root.Add(Z.Segmented((int)active.mode, new[] { "Shape", "Point", "Vector" }, "Shape paints a mask; Point keeps one pixel per frame; Vector places an origin and direction. Converting a shape to a point keeps its first painted pixel.", v => EditValue("Layer kind", () =>
            {
                active.mode = (MetaLayerMode)v;
                if (active.mode == MetaLayerMode.Point) foreach (var frame in active.frames)
                {
                    if (frame?.cells == null) continue;
                    bool kept = false;
                    for (int i = 0; i < frame.cells.Length; i++) if (frame.cells[i] != 0)
                    { if (kept) frame.cells[i] = 0; else kept = true; }
                }
                ClearMaskCache();
            }, true)));
            root.Add(Z.Color(active.color, "The metadata colour and overlay opacity.", v => EditValue("Layer colour", () => { active.color = v; ClearMaskCache(); }), 110));
            if (active.mode == MetaLayerMode.Vector)
            {
                root.Add(Z.Toggle("Allow length", "Dragging the arrow tip changes its length as well as direction.", active.vectorAllowLength, v => EditValue("Vector length mode", () => active.vectorAllowLength = v)));
                root.Add(Z.Toggle("Snap angle", "Aim at evenly spaced angles around a circle.", active.vectorSnapAngle, v => EditValue("Vector snap", () => active.vectorSnapAngle = v, true)));
                var divisions = Z.MicroSlider("Divisions", active.vectorSnapDivisions, 2, 64, "Number of directions on the snap circle.", v => { active.vectorSnapDivisions = Mathf.RoundToInt(v); Dirty(); }, 230, decimals: 0, onBeforeMutate: () => RecordUndo("Vector divisions"));
                divisions.SetEnabled(active.vectorSnapAngle); root.Add(divisions);
            }
            else
            {
                if (active.mode == MetaLayerMode.Shape)
                    root.Add(Z.MiniRadio(Mathf.Max(0, Array.FindIndex(BrushSizes, b => b.x == _brushW && b.y == _brushH)), BrushLabels, "Brush size in source pixels.", v => { _brushW = BrushSizes[v].x; _brushH = BrushSizes[v].y; }));
                root.Add(Z.MicroSlider("Paint value", _paintValue, 1, 10, "Value written into each painted cell. Right-click erases.", v => _paintValue = Mathf.RoundToInt(v), 230, decimals: 0));
                int frame = ActiveFrameIndex();
                if (frame >= 0 && frame < active.frames.Count) root.Add(Z.TextInput(active.frames[frame].param, "Optional free-form parameter on this frame of the selected layer.", v => EditValue("Layer parameter", () => active.frames[frame].param = v), 210));
            }
            root.Add(Z.Button("Clear frame", "Erase the selected layer on the current frame.", () =>
            {
                if (active.mode == MetaLayerMode.Vector) EditValue("Clear vector", () => { int f = ActiveFrameIndex(); if (f >= 0 && f < active.vectorFrames.Count) active.vectorFrames[f].authored = false; });
                else { ClearActiveFrame(); Dirty(); }
            }));
        }

        private void BuildPhaseInspector(VisualElement root)
        {
            root.Add(Z.Toggle("Phases enabled", "Use the authored frame ranges for phase-based playback.", _zonesEnabled, v => EditValue("Phases enabled", () => _zonesEnabled = v, true)));
            var list = WrapRow();
            for (int i = 0; i < _zones.Count; i++)
            {
                int index = i; var phase = _zones[i];
                var button = Z.Button(phase.name, $"Select phase covering frames {phase.startFrame + 1}–{phase.endFrame + 1}.", () => { TimelineSelectedPhase = index; Refresh(); });
                if (i == TimelineSelectedPhase) button.AddToClassList("zui-radio__on"); list.Add(button);
            }
            root.Add(list);
            var add = Z.Button("Add phase", "Create a phase over the selected frames, or the whole sequence.", () => EditValue("Add phase", () => { _zonesEnabled = true; AddZone(); TimelineSelectedPhase = _zones.Count - 1; }, true));
            add.SetEnabled(_sequence.Count > 0); root.Add(add);
            if (TimelineSelectedPhase < 0 || TimelineSelectedPhase >= _zones.Count) return;
            var active = _zones[TimelineSelectedPhase];
            root.Add(WrapRow(Z.TextInput(active.name, "Declare the phase name used by playback and the game.", v => EditValue("Phase name", () => active.name = v), 175), Z.IconButton("delete", "Remove this phase boundary range.", () => EditValue("Delete phase", () => { _zones.RemoveAt(TimelineSelectedPhase); TimelineSelectedPhase = Mathf.Min(TimelineSelectedPhase, _zones.Count - 1); }, true), 24)));
            root.Add(Z.Segmented((int)active.behavior, new[] { "Play through", "Loop" }, "Play through proceeds to the next phase; Loop repeats until Advance is requested.", v => EditValue("Phase playback", () => active.behavior = (ZoneBehavior)v)));
            root.Add(Z.MicroMinMax("Frames", active.startFrame + 1, active.endFrame + 1, 1, Mathf.Max(1, _sequence.Count), "Inclusive phase boundaries, numbered from one. They remain explicit when frames are moved or removed.", (low, high) => { active.startFrame = Mathf.RoundToInt(low) - 1; active.endFrame = Mathf.RoundToInt(high) - 1; Dirty(); }, width: 230, decimals: 0, onBeforeMutate: () => RecordUndo("Phase range")));
            var selection = Z.Button("Use selection", "Set this phase's endpoints to the first and last selected frames.", () => EditValue("Phase range", () => { active.startFrame = _seqMultiSel.Min(); active.endFrame = _seqMultiSel.Max(); }, true));
            selection.SetEnabled(_seqMultiSel.Count > 0); root.Add(selection);
            var conflicts = GetPhaseConflicts();
            if (conflicts.Count > 0) root.Add(Z.HelpIcon(string.Join("\n", conflicts)));
        }
    }
}
