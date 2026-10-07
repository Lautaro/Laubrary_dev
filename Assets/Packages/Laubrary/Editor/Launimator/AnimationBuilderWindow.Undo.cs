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
            public LoopDivider loopDivider; public float loopPause; public CellRef idleRef;
            public bool fixedFrame; public int frameW, frameH; public Vector2 framePivot;
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
                id = L.id, color = L.color,
                frames = (L.frames ?? new List<MetaFrame>()).Select(mf => mf != null ? mf.Clone() : new MetaFrame()).ToList(),
            }).ToList();

        private Snapshot Capture() => new Snapshot
        {
            regions = CloneRegions(_regions),
            sequence = new List<CellRef>(_sequence),
            metaLayers = CloneLayers(_metaLayers),
            metaEnabled = _metaEnabled, activeLayer = _activeLayer,
            events = _events.Select(e => new FrameEvent { frame = e.frame, name = e.name }).ToList(),
            zones = _zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList(),
            zonesEnabled = _zonesEnabled,
            animName = _animName, animFps = _animFps,
            loopDivider = _loopDivider, loopPause = _loopPause, idleRef = _idleRef,
            fixedFrame = _fixedFrame, frameW = _frameW, frameH = _frameH, framePivot = _framePivot,
        };

        private void Apply(Snapshot s)
        {
            _regions.Clear(); _regions.AddRange(CloneRegions(s.regions));
            _sequence.Clear(); _sequence.AddRange(s.sequence);
            _metaLayers = CloneLayers(s.metaLayers);
            _metaEnabled = s.metaEnabled;
            _activeLayer = _metaLayers.Count > 0 ? Mathf.Clamp(s.activeLayer, 0, _metaLayers.Count - 1) : -1;
            _events.Clear(); _events.AddRange(s.events.Select(e => new FrameEvent { frame = e.frame, name = e.name }));
            _zones.Clear(); _zones.AddRange(s.zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }));
            _zonesEnabled = s.zonesEnabled;
            _animName = s.animName; _animFps = s.animFps;
            _loopDivider = s.loopDivider; _loopPause = s.loopPause; _idleRef = s.idleRef;
            _fixedFrame = s.fixedFrame; _frameW = s.frameW; _frameH = s.frameH; _framePivot = s.framePivot;

            // Invalidate derived/cache state and selection so nothing dangles at the old indices.
            _previewHash = -1; ClearMaskCache(); ClearThumbCache();
            ClearSelection();
            _seqMultiSel.Clear(); _seqSelected = -1; _seqAnchor = -1; _animFrame = 0;
            Refresh();
        }

        private void PushUndo(string label)
        {
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
