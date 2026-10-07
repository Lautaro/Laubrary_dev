using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    // Custom snapshot-based undo for the Builder. The window edits a plain in-memory model (regions, sequence,
    // meta-layers, zones, events, settings) — none of it UnityEngine.Objects — so Unity's Undo can't track it.
    // RecordUndo("…") is called BEFORE each mutation throughout the window; it snapshots the whole authoring
    // state. Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) walk the stacks.
    public partial class LauminationBuilderWindow
    {
        private class Snapshot
        {
            public List<Region> regions;
            public List<CellRef> sequence;
            public List<MetaLayer> metaLayers;
            public bool metaEnabled; public int activeLayer;
            public List<FrameEvent> events;
            public List<AnimZone> zones; public bool zonesEnabled;
            public string animName; public float animFps;
            public LoopDivider loopDivider; public float loopPause; public bool frameZeroOn; public CellRef frameZero;
            public bool fixedFrame; public int frameW, frameH; public Vector2 framePivot;
            public Texture2D sheet; public string sheetPath, sheetDisplayName; public int texW, texH;
            public bool bgKeyEnabled; public Color32 bgKey; public int bgTolerance;
            public bool alphaTrim; public int alphaThreshold; public float ppu;
            public GridSlicer.PivotMode pivot; public Vector2 customPivot;
            public RegionSlicer.GridMode gridMode; public int cols, rows, cellW, cellH, spacing, padding;
            public bool hasBox; public Rect box;
            public string animationAsepriteSourcePath;
        }

        private readonly List<(string label, Snapshot snap)> _undo = new List<(string, Snapshot)>();
        private readonly List<(string label, Snapshot snap)> _redo = new List<(string, Snapshot)>();
        private const int UndoCap = 80;

        private static List<Region> CloneRegions(List<Region> src) =>
            src.Select(r => new Region
            {
                // The sheet link must survive undo/redo: without it every frame picked from the restored
                // region reads as "source texture unreadable" and the animation vanishes from the preview.
                label = r.label, bounds = r.bounds, sourceTextureGuid = r.sourceTextureGuid,
                cells = new List<Rect>(r.cells),
                pivots = new List<Vector2>(r.pivots),
                transforms = new List<CellTransform>(r.transforms),
            }).ToList();

        private static List<MetaLayer> CloneLayers(List<MetaLayer> src) =>
            src.Select(L => new MetaLayer
            {
                id = L.id, color = L.color, mode = L.mode,
                vectorAllowLength = L.vectorAllowLength, vectorSnapAngle = L.vectorSnapAngle,
                vectorSnapDivisions = L.vectorSnapDivisions,
                vectorFrames = (L.vectorFrames ?? new List<VectorMetaFrame>()).Select(v => v?.Clone() ?? new VectorMetaFrame()).ToList(),
                frames = (L.frames ?? new List<MetaFrame>()).Select(mf => mf != null ? mf.Clone() : new MetaFrame()).ToList(),
            }).ToList();

        private Snapshot Capture() => new Snapshot
        {
            regions = CloneRegions(_regions),
            sequence = new List<CellRef>(_sequence),
            metaLayers = CloneLayers(_metaLayers),
            metaEnabled = _metaEnabled, activeLayer = _activeLayer,
            events = CloneEvents(_events),
            zones = _zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList(),
            zonesEnabled = _zonesEnabled,
            animName = _animName, animFps = _animFps,
            loopDivider = _loopDivider, loopPause = _loopPause, frameZeroOn = _frameZeroOn, frameZero = _frameZero,
            fixedFrame = _fixedFrame, frameW = _frameW, frameH = _frameH, framePivot = _framePivot,
            sheet = _sheet, sheetPath = _sheetPath, sheetDisplayName = _sheetDisplayName, texW = _texW, texH = _texH,
            bgKeyEnabled = _bgKeyEnabled, bgKey = _bgKey, bgTolerance = _bgTolerance,
            alphaTrim = _alphaTrim, alphaThreshold = _alphaThreshold, ppu = _ppu,
            pivot = _pivot, customPivot = _customPivot, gridMode = _mode,
            cols = _cols, rows = _rows, cellW = _cellW, cellH = _cellH, spacing = _spacing, padding = _padding,
            hasBox = _hasBox, box = _box,
            animationAsepriteSourcePath = _animationAsepriteSourcePath,
        };

        private void Apply(Snapshot s)
        {
            _regions.Clear(); _regions.AddRange(CloneRegions(s.regions));
            _sequence.Clear(); _sequence.AddRange(s.sequence);
            _metaLayers = CloneLayers(s.metaLayers);
            _metaEnabled = s.metaEnabled;
            _activeLayer = _metaLayers.Count > 0 ? Mathf.Clamp(s.activeLayer, 0, _metaLayers.Count - 1) : -1;
            _events = CloneEvents(s.events);
            _zones.Clear(); _zones.AddRange(s.zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }));
            _zonesEnabled = s.zonesEnabled;
            _animName = s.animName; _animFps = s.animFps;
            _loopDivider = s.loopDivider; _loopPause = s.loopPause; _frameZeroOn = s.frameZeroOn; _frameZero = s.frameZero;
            _frameZeroSel = false; _showingFrameZero = false;
            _fixedFrame = s.fixedFrame; _frameW = s.frameW; _frameH = s.frameH; _framePivot = s.framePivot;
            _sheet = s.sheet; _sheetPath = s.sheetPath; _sheetDisplayName = s.sheetDisplayName; _texW = s.texW; _texH = s.texH;
            _bgKeyEnabled = s.bgKeyEnabled; _bgKey = s.bgKey; _bgTolerance = s.bgTolerance;
            _alphaTrim = s.alphaTrim; _alphaThreshold = s.alphaThreshold; _ppu = s.ppu;
            _pivot = s.pivot; _customPivot = s.customPivot; _mode = s.gridMode;
            _cols = s.cols; _rows = s.rows; _cellW = s.cellW; _cellH = s.cellH; _spacing = s.spacing; _padding = s.padding;
            _hasBox = s.hasBox; _box = s.box;
            _animationAsepriteSourcePath = s.animationAsepriteSourcePath;
            RebuildDisplaySheet();

            // Invalidate derived/cache state and selection so nothing dangles at the old indices.
            _previewHash = -1; ClearMaskCache(); ClearThumbCache();
            ClearSelection();
            _seqMultiSel.Clear(); _seqSelected = -1; _seqAnchor = -1; _animFrame = 0;
            Refresh();
        }

        private void PushUndo(string label)
        {
            EnsureDocumentBaseline();
            _undo.Add((label, Capture()));
            if (_undo.Count > UndoCap) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void PerformUndo()
        {
            if (_undo.Count == 0) return;
            var cur = Capture();
            var (label, snap) = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            _redo.Add((label, cur));
            Apply(snap);
            _status = $"Undo: {label}";
        }

        private void PerformRedo()
        {
            if (_redo.Count == 0) return;
            var cur = Capture();
            var (label, snap) = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            _undo.Add((label, cur));
            Apply(snap);
            _status = $"Redo: {label}";
        }

        // Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) are dispatched from the window root's KeyDownEvent handler
        // (OnRootKeyDown), which trickles down so a focused IMGUI island can't swallow them first.
    }
}
