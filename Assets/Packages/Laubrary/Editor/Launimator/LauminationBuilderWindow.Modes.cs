using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.LaunimatorZounds.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        private enum WorkspaceMode { Sheet, Sprites, Animate }
        private enum AnimateTool { Timing, Events, Layers, Phases }
        private enum SpriteTool { Align, Transform }
        [SerializeField] private WorkspaceMode _workspaceMode;
        [SerializeField] private AnimateTool _animateTool;
        [SerializeField] private SpriteTool _spriteTool;
        private int _acquireMode, _paletteFilter, _paletteSort, _selectedEvent = -1;
        private string _paletteSearch = "";
        private bool _detectMode;
        private readonly List<Rect> _detectedCells = new List<Rect>();
        private Rect _detectedBox;
        private Label _timingReadout;
        private VisualElement _workspaceStage, _inspectorContent, _browserContent, _timelineHost;
        private ScrollView _inspectorScroll, _browserScroll;
        private Button _undoButton, _redoButton, _saveButton;
        private Label _identityLabel, _dirtyLabel;

        protected override void BuildUI(VisualElement root)
        {
            root.AddToClassList("lau-animation-builder");
            root.AddToClassList("lau-tool-shell");
            root.AddToClassList("lau-builder-modes");
            root.focusable = true;
            root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            _bannerHost = Z.Row();
            _bannerHost.AddToClassList("lau-builder-modes__header");
            _identityLabel = Z.Text("", ZuiText.Section, "The animation and destination edited by this window.");
            _identityLabel.AddToClassList("lau-builder-modes__identity");
            _bannerHost.Add(_identityLabel);
            _dirtyLabel = Z.Text("", ZuiText.Small, "Unsaved animation or slicing changes.");
            _dirtyLabel.W(16);
            _bannerHost.Add(_dirtyLabel);
            _undoButton = Z.IconButton("arrow-counter-clockwise", "Undo the last edit in this animation (Ctrl+Z).", PerformUndo, 24);
            _redoButton = Z.IconButton("arrow-clockwise", "Redo the last undone edit (Ctrl+Y).", PerformRedo, 24);
            _bannerHost.Add(_undoButton); _bannerHost.Add(_redoButton);
            _saveButton = Z.Button("Save", "Save this animation. Sheet slicing has its own Save slices action.", () => { TrySaveDocument(); Refresh(); }).W(54);
            _bannerHost.Add(_saveButton);
            _bannerHost.Add(Z.Segmented((int)_workspaceMode, new[] { "Sheet", "Sprites", "Animate" },
                "Sheet identifies sprites; Sprites aligns them; Animate authors the ordered frames and gameplay data.",
                v => { _workspaceMode = (WorkspaceMode)v; PausePreview(); Refresh(); }));
            _statusLabel = Z.Text("i", ZuiText.Small, Disclaimer);
            _statusLabel.W(20); _bannerHost.Add(_statusLabel);
            root.Add(_bannerHost);

            _topHost = Z.Row();
            _topHost.AddToClassList("lau-builder-modes__toolbar");
            root.Add(_topHost);
            _splitRow = Z.Row(); _splitRow.AddToClassList("lau-builder-modes__body");
            root.Add(_splitRow);
            _browserScroll = new ScrollView(ScrollViewMode.Vertical);
            _browserScroll.AddToClassList("lau-builder-modes__browser");
            _browserContent = _browserScroll.contentContainer;
            _splitRow.Add(_browserScroll);
            _inspectorScroll = new ScrollView(ScrollViewMode.Vertical);
            _inspectorScroll.AddToClassList("lau-builder-modes__inspector");
            _inspectorContent = _inspectorScroll.contentContainer;
            _workspaceStage = new VisualElement(); _workspaceStage.AddToClassList("lau-builder-modes__stage");
            var split = Z.Split("launimator.builder.inspector", 280, _inspectorScroll, _workspaceStage);
            split.AddToClassList("lau-builder-modes__work"); _splitRow.Add(split);
            _timelineHost = new VisualElement(); _timelineHost.AddToClassList("lau-builder-modes__timeline");
            root.Add(_timelineHost);
            RefreshWorkspace();
        }

        private void RefreshWorkspace()
        {
            if (_workspaceStage == null) return;
            var scroll = _inspectorScroll.scrollOffset;
            var browserScroll = _browserScroll.scrollOffset;
            _topHost.Clear(); _inspectorContent.Clear(); _browserContent.Clear(); _workspaceStage.Clear();
            _canvasIM = _regCanvasIM = _playIM = _paletteGridIM = null;
            BuildAnimationRail(_browserContent);
            BuildWorkspaceToolbar(_topHost);
            if (_workspaceMode == WorkspaceMode.Sheet)
            {
                BuildSourceControls(_inspectorContent);
                if (_sheet != null) BuildSheetTools(_inspectorContent);
                _canvasIM = Stage(DrawCanvasGUI, "Slice the sheet with the selected tool. Middle-drag pans; the wheel zooms.");
                _workspaceStage.Add(_canvasIM);
                if (_sheet == null) _workspaceStage.Add(Z.Text("No sheet", ZuiText.Subtle, "Choose a Project image, import a File or enter a URL in the source controls."));
            }
            else if (_workspaceMode == WorkspaceMode.Sprites)
            {
                BuildSpriteInspector(_inspectorContent);
                _regCanvasIM = Stage(DrawRegistrationCanvasGUI, _spriteTool == SpriteTool.Align ? "Drag the selected sprite to align its pivot. Changes affect every use of this source sprite in this animation." : "Preview the selected sprite's transform. Use the transform controls to rotate, mirror or scale it; middle-drag pans.");
                _workspaceStage.Add(_regCanvasIM);
                BuildCandidateShelf(_workspaceStage);
            }
            else
            {
                SyncMetaFrames();
                switch (_animateTool)
                {
                    case AnimateTool.Timing: BuildTimingInspector(_inspectorContent); break;
                    case AnimateTool.Events: BuildEventInspector(_inspectorContent); break;
                    case AnimateTool.Layers: BuildLayerInspector(_inspectorContent); break;
                    case AnimateTool.Phases: BuildPhaseInspector(_inspectorContent); break;
                }
                _playIM = Stage(DrawPlayAreaGUI, "Preview the animation, or paint the selected layer on the selected frame. Middle-drag pans the layer view.");
                _workspaceStage.Add(_playIM);
            }
            _timelineHost.Clear();
            if (_workspaceMode == WorkspaceMode.Sheet) BuildCandidateShelf(_timelineHost);
            else BuildFrameTimeline(_timelineHost);
            UpdateWorkspaceStatus();
            _inspectorScroll.schedule.Execute(() => _inspectorScroll.scrollOffset = scroll);
            _browserScroll.schedule.Execute(() => _browserScroll.scrollOffset = browserScroll);
            Dirty();
        }

        private static IMGUIContainer Stage(Action draw, string tooltip)
        {
            var stage = new IMGUIContainer(draw) { tooltip = tooltip };
            stage.AddToClassList("lau-builder-modes__canvas");
            return stage;
        }

        private void UpdateWorkspaceStatus()
        {
            if (_identityLabel == null) return;
            _identityLabel.text = (_boundLauminary != null ? _boundLauminary.lauminaryName + " / " : "Standalone / ") + _animName;
            _identityLabel.tooltip = _identityLabel.text;
            _dirtyLabel.text = IsDocumentDirty ? "*" : "";
            _undoButton.SetEnabled(_undo.Count > 0); _redoButton.SetEnabled(_redo.Count > 0);
            _saveButton.SetEnabled(_sequence.Count > 0);
            if (_timingReadout != null)
            {
                float pct = _frameZeroSel ? _frameZero.pct : _seqSelected >= 0 && _seqSelected < _sequence.Count ? _sequence[_seqSelected].pct : 0;
                _timingReadout.text = $"{FrameRef.TimingFactorOf(pct):0.##}× {FrameMsOf(pct):0}ms";
            }
            if (_statusLabel != null) _statusLabel.tooltip = string.IsNullOrEmpty(_status) ? Disclaimer : _status;
        }

        private void BuildWorkspaceToolbar(VisualElement root)
        {
            if (_workspaceMode == WorkspaceMode.Sheet)
            {
                root.Add(Z.Segmented(_detectMode ? 3 : (int)_toolMode, new[] { "Grid", "Box", "Pick", "Detect" },
                    "Grid slices a marquee; Box adds one sprite; Pick finds a connected sprite; Detect finds every sprite in a marquee.",
                    v => { _detectMode = v == 3; _toolMode = v == 3 ? ToolMode.Grid : (ToolMode)v; Refresh(); }));
                root.Add(Z.Button("Fit", "Fit the sheet to the available stage.", () => { _zoomInitialized = false; Dirty(); }).W(40));
                root.Add(Z.Button("Save slices", "Save the sheet's identified cells, pivots and transforms to its slicing sidecar.", () => { SaveState(); Dirty(); }).W(90));
                root.Add(Z.Button("Reload slices", "Reload the saved slices, preserving this animation. Undo restores the working slices.", () => { LoadStateFromSidecar(false); Refresh(); }).W(94));
                root.Add(Z.Button("Clear slices", "Delete this sheet's saved slicing data after confirmation, preserving the animation.", () => { ClearSavedSlicing(); Refresh(); }).W(86));
            }
            else if (_workspaceMode == WorkspaceMode.Sprites)
            {
                root.Add(Z.Segmented((int)_spriteTool, new[] { "Align", "Transform" }, "Align pivots or transform the selected source sprites.", v => { _spriteTool = (SpriteTool)v; Refresh(); }));
                var add = Z.Button("Add sequence", "Append the selected sprites to this animation's frame sequence.", () => { AddSelectedToSequence(); Refresh(); });
                add.SetEnabled(HasSelectedCell()); root.Add(add);
                var edit = Z.Button("Aseprite", "Open the selected sprites in an owned Aseprite document.", OpenSelectionInAseprite);
                edit.SetEnabled(HasSelectedCell()); root.Add(edit);
                var sync = Z.Button("Sync", "Read edits from the owned Aseprite document.", () => { RecordUndo("Sync sprites"); SyncFromAseprite(); Refresh(); });
                sync.SetEnabled(!string.IsNullOrEmpty(_editAsePath)); root.Add(sync);
                root.Add(Z.Button("Fit", "Fit all registration content into the stage.", () => FitPreviewStage()).W(40));
                root.Add(Z.Button("1:1", "Show one source pixel per editor point.", () => FitPreviewStage(true)).W(40));
                root.Add(Z.HelpIcon("Sprite changes affect every use of that source sprite in this working animation. Duplicate the sprite for an independent transform."));
            }
            else
            {
                root.Add(Z.Segmented((int)_animateTool, new[] { "Timing", "Events", "Layers", "Phases" }, "Choose what to author against the shared frame timeline.",
                    v => { _animateTool = (AnimateTool)v; PausePreview(); Refresh(); }));
                root.Add(BuildPreviewTransport(_animateTool == AnimateTool.Phases));
            }
        }

        private void BuildCandidateShelf(VisualElement root)
        {
            var shelf = new VisualElement(); shelf.AddToClassList("lau-builder-modes__shelf");
            shelf.Add(WrapRow(Z.TextInput(_paletteSearch, "Filter sprites by their region or source image name.", v => { _paletteSearch = v; _paletteGridIM?.MarkDirtyRepaint(); }, 100),
                Z.Segmented(_paletteFilter, new[] { "All", "Used here", "This sheet" }, "Limit the candidate sprites by their use in this animation or source sheet.", v => { _paletteFilter = v; Dirty(); }),
                Z.Segmented(_paletteSort, new[] { "Source order", "Name" }, "Sort candidate sprites by their source order or region name.", v => { _paletteSort = v; Dirty(); })));
            _paletteGridIM = new IMGUIContainer(DrawPaletteGridGUI) { tooltip = PaletteHelp };
            _paletteGridIM.AddToClassList("lau-builder-modes__palette"); shelf.Add(_paletteGridIM); root.Add(shelf);
        }

        private List<CellRef> VisiblePaletteCells()
        {
            IEnumerable<CellRef> cells = FlattenCells();
            if (_paletteFilter == 1) cells = cells.Where(c => _sequence.Any(f => f.region == c.region && f.cell == c.cell));
            if (_paletteFilter == 2) cells = cells.Where(c => _regions[c.region].sourceTextureGuid == CurrentSheetGuid());
            if (!string.IsNullOrWhiteSpace(_paletteSearch)) cells = cells.Where(c => (_regions[c.region].label + " " + AssetDatabase.GUIDToAssetPath(_regions[c.region].sourceTextureGuid)).IndexOf(_paletteSearch, StringComparison.OrdinalIgnoreCase) >= 0);
            if (_paletteSort == 1) cells = cells.OrderBy(c => _regions[c.region].label).ThenBy(c => c.cell);
            return cells.ToList();
        }

        private void BuildSourceControls(VisualElement root)
        {
            root.Add(Z.Segmented(_acquireMode, new[] { "Project", "File", "URL" }, "Choose an image already in the project, import a local image, or download one.", v => { _acquireMode = v; Refresh(); }));
            if (_acquireMode == 0)
                root.Add(Z.Object<Texture2D>(_sheet, "Choose an image to start a new animation, after resolving the current document.", t => { if (t == null || t == _sheet || !ConfirmDocumentTransition()) return; StartNewAnimation(SuggestNewAnimName()); LoadSheet(t); BeginCleanDocument(); Refresh(); }, 240));
            else if (_acquireMode == 1)
                root.Add(Z.Button("Import image…", "Choose an image from your computer and copy it into the project's sprite sheets.", () => ImportExternalImage(false)));
            else
            {
                root.Add(Z.TextInput(_sheetUrl, "Address of the image to download.", v => _sheetUrl = v, 240));
                root.Add(Z.TextInput(_downloadName, "Optional filename for the imported image.", v => _downloadName = v, 180));
                root.Add(Z.Button("Download", "Download an image for a new animation after resolving unsaved changes.", () => { if (!ConfirmDocumentTransition()) return; DownloadSheetFromUrl(); Refresh(); }));
            }
        }

        private void ImportExternalImage(bool append)
        {
            string path = EditorUtility.OpenFilePanel("Import image", "", "png,jpg,jpeg,bmp,tga");
            if (string.IsNullOrEmpty(path)) return;
            if (!append && !ConfirmDocumentTransition()) return;
            try
            {
                Directory.CreateDirectory(Path.GetFullPath(SheetLibrary.Folder));
                string target = AssetDatabase.GenerateUniqueAssetPath(SheetLibrary.Folder + "/" + Path.GetFileName(path));
                File.Copy(path, Path.GetFullPath(target));
                AssetDatabase.ImportAsset(target); ConfigureSheetTexture(target);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                if (texture == null) { SetStatus("The imported file is not a supported image."); return; }
                if (append) { RecordUndo("Import frame"); AppendStandaloneSpriteFrame(texture); }
                else { StartNewAnimation(SuggestNewAnimName()); LoadSheet(texture); BeginCleanDocument(); }
                Refresh();
            }
            catch (Exception ex) { SetStatus("Import failed: " + ex.Message); }
        }
    }
}
