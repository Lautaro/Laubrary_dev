using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        // The saved baselines belong to this window's working document. Undo changes the working
        // content, never the baselines: undoing all the way back to saved content becomes clean again.
        private string _savedAnimationState, _savedSlicingState;
        private string _animationAsepriteSourcePath = "";
        private bool _registrationUndoRecorded;

        private static FrameEvent CloneEvent(FrameEvent e, int frame) => new FrameEvent
        {
            frame = frame, name = e.name, zoundName = e.zoundName,
            hasPosition = e.hasPosition, position = e.position
        };

        private static List<FrameEvent> CloneEvents(IEnumerable<FrameEvent> source) =>
            (source ?? Enumerable.Empty<FrameEvent>()).Where(e => e != null).Select(e => CloneEvent(e, e.frame)).ToList();

        private FrameRef DocumentFrame(CellRef cr)
        {
            if (!SeqRefValid(cr)) return new FrameRef { sourceTextureGuid = "invalid", timingPercent = cr.pct };
            var region = _regions[cr.region];
            return new FrameRef
            {
                sourceTextureGuid = region.sourceTextureGuid, cell = region.cells[cr.cell],
                pivot = cr.cell < region.pivots.Count ? region.pivots[cr.cell] : GlobalPivot(),
                transform = cr.cell < region.transforms.Count ? region.transforms[cr.cell] : CellTransform.Identity,
                timingPercent = cr.pct
            };
        }

        private string AnimationStateFingerprint()
        {
            // A baked atlas and playback/selection are derived view state, so neither makes a document dirty.
            var data = new Laumination
            {
                name = _animName, fps = _animFps, recipe = _sequence.Select(DocumentFrame).ToList(),
                events = _events, metaLayers = _metaLayers, metaLayersEnabled = _metaEnabled,
                zones = _zones, zonesEnabled = _zonesEnabled,
                bgKeyEnabled = _bgKeyEnabled, bgKey = _bgKey, bgKeyTolerance = _bgTolerance,
                fixedFrame = _fixedFrame, frameWidth = _frameW, frameHeight = _frameH, framePivot = _framePivot,
                previewFrameZero = _frameZeroOn, frameZero = SeqRefValid(_frameZero) ? DocumentFrame(_frameZero) : null,
                asepriteSourcePath = _animationAsepriteSourcePath
            };
            return JsonUtility.ToJson(data);
        }

        private string SlicingStateFingerprint()
        {
            var data = BuildState();
            return JsonConvert.SerializeObject(data);
        }

        private void EnsureDocumentBaseline()
        {
            if (_savedAnimationState == null) _savedAnimationState = AnimationStateFingerprint();
            if (_savedSlicingState == null) _savedSlicingState = SlicingStateFingerprint();
        }

        private bool IsAnimationDirty
        {
            get { EnsureDocumentBaseline(); return _savedAnimationState != AnimationStateFingerprint(); }
        }

        private bool IsSlicingDirty
        {
            get { EnsureDocumentBaseline(); return _savedSlicingState != SlicingStateFingerprint(); }
        }

        private bool IsDocumentDirty => IsAnimationDirty || IsSlicingDirty;

        private void MarkAnimationSaved() => _savedAnimationState = AnimationStateFingerprint();
        private void MarkSlicingSaved() => _savedSlicingState = SlicingStateFingerprint();

        private void BeginCleanDocument()
        {
            _undo.Clear(); _redo.Clear();
            MarkAnimationSaved(); MarkSlicingSaved();
            _animPlaying = false; _inDivider = false; _showingFrameZero = false;
            _previewHash = -1;
        }

        private bool ConfirmDocumentTransition()
        {
            if (!IsDocumentDirty) return true;
            string changed = IsAnimationDirty && IsSlicingDirty ? "animation and slicing" : IsAnimationDirty ? "animation" : "slicing";
            int choice = EditorUtility.DisplayDialogComplex("Unsaved changes",
                $"Save changes to the current {changed} before leaving?", "Save", "Cancel", "Discard");
            if (choice == 2) DiscardDocumentChanges();
            return ResolveDocumentTransition(choice, TrySaveAllDocumentChanges);
        }

        private void DiscardDocumentChanges()
        {
            EnsureDocumentBaseline();
            var animation = JsonUtility.FromJson<Laumination>(_savedAnimationState);
            var slicing = JsonConvert.DeserializeObject<RegionSlicerPersistence.StateDto>(_savedSlicingState);
            // Restore saved slicing first, then the saved animation's own registration and payloads.
            // This matters when New keeps the source palette: Discard must not carry unsaved edits
            // into the next animation and silently relabel them as clean.
            _sequence.Clear(); _frameZero = new CellRef(-1, -1);
            _sheetPath = slicing.texturePath;
            _sheet = string.IsNullOrEmpty(_sheetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(_sheetPath);
            _texW = _sheet != null ? _sheet.width : slicing.texW;
            _texH = _sheet != null ? _sheet.height : slicing.texH;
            _sheetDisplayName = _sheet != null ? SheetRegistry.GetDisplayName(_sheet) : "";
            ApplyState(slicing);
            _animName = animation.name; _animFps = animation.fps;
            _events = CloneEvents(animation.events);
            _metaLayers = CloneLayers(animation.metaLayers ?? new List<MetaLayer>());
            _metaEnabled = animation.metaLayersEnabled;
            _activeLayer = _metaLayers.Count > 0 ? 0 : -1;
            LoadZonesFrom(animation);
            _bgKeyEnabled = animation.bgKeyEnabled; _bgKey = animation.bgKey; _bgTolerance = animation.bgKeyTolerance;
            _fixedFrame = animation.fixedFrame; _frameW = animation.frameWidth; _frameH = animation.frameHeight; _framePivot = animation.framePivot;
            _animationAsepriteSourcePath = animation.asepriteSourcePath;
            foreach (var frame in animation.recipe ?? new List<FrameRef>())
            {
                var cell = FindOrCreateCellForFrame(frame); cell.pct = frame.timingPercent;
                _sequence.Add(cell);
            }
            _frameZeroOn = animation.previewFrameZero;
            if (animation.frameZero != null)
            {
                _frameZero = FindOrCreateCellForFrame(animation.frameZero);
                _frameZero.pct = animation.frameZero.timingPercent;
            }
            _frameZeroSel = false; _showingFrameZero = false;
            _seqMultiSel.Clear(); _seqSelected = _sequence.Count > 0 ? 0 : -1; _seqAnchor = _seqSelected;
            if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected);
            _animFrame = 0; _detectedCells.Clear(); _hasBox = false;
            RebuildDisplaySheet(); ClearMaskCache(); ClearThumbCache();
            BeginCleanDocument();
        }

        // Pure decision seam makes the critical failed-save policy testable without a modal dialog.
        internal static bool ResolveDocumentTransition(int choice, Func<bool> save) =>
            choice == 2 || (choice == 0 && save());

        private bool TrySaveAllDocumentChanges()
        {
            if (IsAnimationDirty && !TrySaveDocument()) return false;
            if (IsSlicingDirty)
            {
                try
                {
                    RegionSlicerPersistence.Save(_sheetPath, BuildState());
                    MarkSlicingSaved();
                }
                catch (Exception ex)
                {
                    _status = "Slicing save failed: " + ex.Message;
                    return false;
                }
            }
            return true;
        }

        private bool HasInvalidPhaseRanges => _zonesEnabled && _zones.Any(z =>
            z == null || z.startFrame < 0 || z.endFrame < z.startFrame || z.endFrame >= _sequence.Count);

        private List<string> GetPhaseConflicts()
        {
            var result = new List<string>();
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var phase in _zones)
            {
                if (phase == null) { result.Add("A phase is missing its data."); continue; }
                string name = string.IsNullOrWhiteSpace(phase.name) ? "Unnamed phase" : phase.name;
                if (string.IsNullOrWhiteSpace(phase.name)) result.Add("Give every phase a name so it can be selected for playback.");
                else if (!named.Add(phase.name)) result.Add($"More than one phase is named '{name}'. Give each phase its own name.");
                if (phase.startFrame < 0 || phase.endFrame < phase.startFrame || phase.endFrame >= _sequence.Count)
                    result.Add($"'{name}' has an invalid range ({phase.startFrame + 1}–{phase.endFrame + 1}); choose frames within 1–{_sequence.Count}.");
            }
            var valid = _zones.Where(z => z != null && z.startFrame >= 0 && z.endFrame >= z.startFrame && z.endFrame < _sequence.Count)
                .OrderBy(z => z.startFrame).ToList();
            int coveredTo = -1;
            foreach (var phase in valid)
            {
                if (phase.startFrame <= coveredTo) result.Add($"'{phase.name}' overlaps another phase. Playback uses phase order, so check the boundary.");
                else if (phase.startFrame > coveredTo + 1) result.Add($"Frames {coveredTo + 2}–{phase.startFrame} are outside every phase.");
                coveredTo = Math.Max(coveredTo, phase.endFrame);
            }
            if (valid.Count > 0 && coveredTo < _sequence.Count - 1)
                result.Add($"Frames {coveredTo + 2}–{_sequence.Count} are outside every phase.");
            return result;
        }
    }
}
