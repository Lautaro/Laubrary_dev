using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.LaunimatorZounds.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// The Laumination Builder. Load a sprite sheet, identify its sprites in one of two explicit modes
    /// — <b>Grid</b> (uniform sheets: marquee a box, give it cols×rows) or <b>Pick</b> (scattered sprites:
    /// click each one, it's flood-filled to a tight bbox) — then build an ordered <b>sequence</b> from those
    /// sprites, preview it looping, nudge each sprite's registration, and save it as a named <b>animation</b>
    /// into a lauminary's editable draft (see <see cref="LauminaryRepo"/>). Two-column layout: left =
    /// sheet/grid/canvas, right = identified sprites (#4) + animation preview &amp; save (#5).
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): every CONTROL surface is a Laubrary.Zui (Z.*) retained
    /// control. Six deliberate IMGUI islands remain, all of them bespoke canvas painting or direct-manipulation
    /// gizmos that no retained element expresses and that must not regress: the sheet canvas (marquee/handles/
    /// pick/eyedropper/auto-marquee), the registration canvas (onion-skin + drag-to-place), the play box, the
    /// meta paint editor (per-pixel painting with pan/zoom), the sprite-palette thumbnail grid and the sequence
    /// strip (thumbnail grids with badges, multi-select and drag-reorder), and the zone bar. Retained-mode
    /// discipline: <see cref="Refresh"/> rebuilds the control hosts after a structural change, <see cref="Dirty"/>
    /// only repaints the islands, and per-frame playback repaints the play box instead of the whole window.
    /// </summary>
    public partial class LauminationBuilderWindow : ZuiWindow
    {
        private const string Disclaimer =
            "Prototyping only. Sprites may derive from copyrighted rips and must not be shipped or redistributed.";

        public enum ToolMode { Grid, Box, Pick }

        // ── Source ───────────────────────────────────────────────────────────
        private Texture2D _sheet;
        private string _sheetPath;
        private int _texW, _texH;

        // ── Aseprite edit round-trip (Edit in Aseprite → edit → Sync) ────────
        private string _editAsePath;            // last .aseprite exported for editing
        private string _editSheetPath;          // the sheet it was exported from
        private readonly List<Rect> _editRects = new List<Rect>();  // edited cell rects, in frame order
        private string _sheetUrl = "";       // download-into-SpriteSheets URL field
        private string _downloadName = "";   // friendly display name for a downloaded sheet
        private string _sheetDisplayName = ""; // editable display name of the loaded sheet (registry-backed)

        // ── Background colour key (sheets with a solid-colour background instead of alpha) ──
        private bool _bgKeyEnabled;
        private Color32 _bgKey = new Color32(0, 0, 0, 255);
        private int _bgTolerance = 12;
        private bool _pickingBgColor;        // armed eyedropper: next canvas click samples the key colour
        private Texture2D _displaySheet;      // keyed copy used for canvas/preview when a bg key is active

        // ── Mode + grid params ───────────────────────────────────────────────
        private ToolMode _toolMode = ToolMode.Grid;
        private RegionSlicer.GridMode _mode = RegionSlicer.GridMode.FixedColsRows;
        private int _cols = 3, _rows = 4;
        private int _cellW = 16, _cellH = 16;
        private int _spacing = 0, _padding = 0;
        private float _ppu = 16f;
        private GridSlicer.PivotMode _pivot = GridSlicer.PivotMode.BottomCenter;
        private Vector2 _customPivot = new Vector2(0.5f, 0f);
        private bool _alphaTrim;
        private int _alphaThreshold = 8;

        // ── Active marquee box (Grid mode; texture px, bottom-left origin) ────
        private bool _hasBox;
        private Rect _box;
        private bool _dragging;
        private Vector2 _dragStartTex;
        private enum HandleKind { None, Move, ResizeBL, ResizeBR, ResizeTL, ResizeTR, ResizeL, ResizeR, ResizeB, ResizeT }
        private HandleKind _activeHandle = HandleKind.None;
        private Vector2 _handleGrabTex;
        private Rect _boxAtGrab;

        // ── Committed cells (both modes feed the same store) ─────────────────
        private class Region
        {
            public string label;
            // Which source texture this region's cells were identified from (matches FrameRef.sourceTextureGuid,
            // set on AssetDatabase.AssetPathToGUID(sheetPath) for anything created via the normal identify-
            // sprites canvas). CRITICAL for recipe round-tripping: a Laumination's frames can legitimately come
            // from several different source textures (see FindOrCreateCellForFrame's own doc comment) — without
            // this, two frames from DIFFERENT textures that happen to share the same cell rect (e.g. two
            // standalone same-size sprite files, each its own whole-image cell at (0,0,w,h)) would be silently
            // treated as the SAME sprite, collapsing an N-direction animation down to one repeated frame.
            public string sourceTextureGuid;
            public List<Rect> cells = new List<Rect>();
            public List<Vector2> pivots = new List<Vector2>();   // per-cell registration (normalized, BL origin)
            public List<CellTransform> transforms = new List<CellTransform>(); // per-cell flip/rotate/scale
            public Rect bounds;
            public void SyncPivots(Vector2 fill)
            {
                while (pivots.Count < cells.Count) pivots.Add(fill);
                while (pivots.Count > cells.Count) pivots.RemoveAt(pivots.Count - 1);
                while (transforms.Count < cells.Count) transforms.Add(CellTransform.Identity);
                while (transforms.Count > cells.Count) transforms.RemoveAt(transforms.Count - 1);
            }
        }
        private readonly List<Region> _regions = new List<Region>();
        private const string PickedLabel = "picked";
        private const string BoxLabel = "box";

        // ── Selection (#4) ───────────────────────────────────────────────────
        // _selRegion/_selCell is the PRIMARY selection (single-sprite ops: registration drag, idle).
        // _multiSel is the full set (batch ops: trim/baseline/delete/nudge). A single click keeps them in sync.
        private int _selRegion = -1, _selCell = -1;
        private readonly HashSet<long> _multiSel = new HashSet<long>();
        private CellRef _selAnchor = new CellRef(-1, -1); // shift-range anchor

        // ── Registration (per-animation frame box + drag-to-place) ───────────
        private bool _fixedFrame;
        private int _frameW = 32, _frameH = 32;
        private Vector2 _framePivot = new Vector2(0.5f, 0f);
        private bool _regDragging;

        // Onion-skin (view only): faint neighbouring frames behind the one being aligned.
        private float _ghostOpacity = 0.25f;
        private int _ghostBefore = 2, _ghostAfter = 2;

        // ── Sequence (the animation being authored) ──────────────────────────
        private struct CellRef
        {
            public int region, cell;
            // This frame's timing relative to the fps, in percent (0 = one normal tick; see
            // FrameRef.timingPercent). Lives on the sequence entry so a reorder, duplicate or undo snapshot
            // carries it with the frame. Never part of "same cell" checks, which compare region/cell explicitly.
            public float pct;
            public CellRef(int r, int c) { region = r; cell = c; pct = 0f; }
        }
        private readonly List<CellRef> _sequence = new List<CellRef>();
        private int _seqSelected = -1;
        private int _seqDragFrom = -1;
        // Batch selection over the sequence (by frame INDEX — a sprite may appear more than once, so we
        // can't key on the cell). _seqSelected stays the "primary" frame; _seqAnchor seeds shift-range.
        private readonly HashSet<int> _seqMultiSel = new HashSet<int>();
        private int _seqAnchor = -1;

        // ── Preview playback ─────────────────────────────────────────────────
        private float _animFps = 8f;
        private bool _animPlaying = true;
        private int _animFrame;
        private double _animLastStep;
        private bool _inDivider;
        private double _dividerUntil;
        private enum LoopDivider { None, EmptyPause }
        private LoopDivider _loopDivider = LoopDivider.None;
        private float _loopPause = 0.4f;

        // Frame 0: a sprite the preview shows before frame 1 on every loop (e.g. the pose the move starts from),
        // so the transition can be judged. Saved with the animation but never baked; the game never sees it.
        private bool _frameZeroOn;
        private CellRef _frameZero = new CellRef(-1, -1);   // pct = how long it shows
        private bool _frameZeroSel;                          // the strip's slot 0 is the selected frame
        private bool _showingFrameZero;                      // the preview is on frame 0 (playing or parked)
        private double _frameZeroUntil;
        private List<Sprite> _previewFrameZero;              // frame 0, baked with the sequence so it registers alike
        private const float DefaultFrameZeroPct = 400f;      // five normal frames: long enough to read the pose
        private const float CrosshairNX = 0.5f, CrosshairNY = 0.4f;
        private const float RotStepDeg = 5f; // increment for the stepwise rotation −/+ buttons

        // The PLAY preview runs the shared AnimationPlayback over a LIVE in-memory bake of the current
        // sequence — i.e. the exact same player and the exact same baked frames the game and Lauminary Browser
        // use, so the looping preview cannot wobble or differ from the shipped result. (The registration
        // CANVAS below is a separate authoring surface — it deliberately shows raw cells so pivots can be
        // edited; that is NOT playback and is correctly not routed through the player.)
        private readonly AnimationPlayback _previewPlayer = new AnimationPlayback();
        private Laumination _previewDef;        // wraps the live-baked frames for the player
        private List<Sprite> _previewFrames;
        private Texture2D _previewTex;           // owned; destroyed on rebake/disable
        private int _previewHash = -1;           // re-bake only when the sequence/pivots/key/box change
        private double _animNow;                 // current tick time (for loop-gap scheduling)

        // Cached transformed preview of the SELECTED sprite, so flip/rotate/scale shows in the canvas even
        // before the sprite is sequenced (sequenced sprites show their transform via the baked frames).
        private Texture2D _selXformTex;
        private int _selXformHash = -1;
        private Vector2 _selXformPivot;
        private int _selXformW, _selXformH;

        // Cached transformed THUMBNAILS for the palette/sequence grids, so an edited sprite's tile reflects its
        // flip/rotate/scale (not just the registration preview). Built lazily, only for non-identity cells.
        private struct ThumbEntry { public int hash; public Texture2D tex; }
        private readonly Dictionary<long, ThumbEntry> _thumbCache = new Dictionary<long, ThumbEntry>();

        // ── Meta-layers (gameplay overlays drawn over the sequence) ──────────
        private bool _metaEnabled;
        private List<MetaLayer> _metaLayers = new List<MetaLayer>();
        private int _activeLayer = -1;
        private int _paintValue = 5;           // current 1–10 palette value (5 = default). Erase = right-click.
        private bool _metaShowValues;          // reveal the 1–10 value palette (off = always paint value 5)
        private int _brushW = 1, _brushH = 1;  // paint brush footprint (1×1, 1×2, 2×1, 2×2)
        private float _metaZoom;              // zero fits the current frame; positive values are explicit zoom
        private Vector2 _metaPan;              // paint-editor pan (middle-drag), screen px
        private bool _metaPainting; private int _metaLastX = -1, _metaLastY = -1;
        private bool _metaPanning; private Vector2 _metaPanLast;
        private struct MaskEntry { public int hash; public Texture2D tex; }
        private readonly Dictionary<long, MaskEntry> _maskCache = new Dictionary<long, MaskEntry>();

        // Vector-mode drag state on the preview canvas: 0 = idle, 1 = dragging the origin dot, 2 = dragging
        // the arrowhead (re-aiming, and length too when the layer allows it).
        private int _vecDragMode;

        // Preview/paint canvas size — user-resizable via the grip in its bottom-right corner (drag-adjustable,
        // not persisted across window reopens, same as _metaZoom/_metaPan).
        private float _playW = 320f, _playH = 220f;
        private bool _playResizing; private Vector2 _playResizeStart; private float _playResizeStartW, _playResizeStartH;

        // Single in-memory clipboard (last-copied wins), static so it survives closing/reopening the window —
        // same pattern PyreWindow.Modifiers.cs uses for its modifier clipboard. Common case this exists for:
        // a point (or mask) that's the same on most frames — copy once, "Paste to all", then repaint outliers.
        private static MetaFrame _metaFrameClipboard;

        // ── Save target ──────────────────────────────────────────────────────
        // Bound mode: opened from the Lauminary Browser — save writes back to that lauminary's draft
        // animation. Orphan mode (no bound lauminary): save writes a standalone orphaned AnimationAsset that
        // can later be included into a lauminary from the Lauminary Browser.
        private string _animName = "Idle";
        private List<FrameEvent> _events = new List<FrameEvent>(); // authored per-frame events (e.g. "hit")
        private Lauminary _boundLauminary;
        private string _boundAnimName;
        private AnimationAsset _orphanAsset;   // the orphan being edited (null = a fresh orphan)

        // ── Pending open request (applied after Show) ────────────────────────
        private Lauminary _pendingEditLauminary;
        private string _pendingEditAnim;
        private AnimationAsset _pendingOrphan;

        // ── Canvas / view ────────────────────────────────────────────────────
        private float _zoom = 1f;
        private bool _zoomInitialized;
        private Vector2 _canvasScroll;
        private Rect _lastImageRect;

        private string _status;
        private Vector2 _cellsScroll, _seqScroll;

        // ── retained UI hosts + islands (UI Toolkit) ─────────────────────────
        // Each host is cleared + rebuilt by Refresh(); each island only needs MarkDirtyRepaint().
        private VisualElement _bannerHost, _topHost, _leftControlsHost, _paletteHost, _animHost;
        private VisualElement _leftPane, _splitRow;
        private Label _statusLabel;
        private IMGUIContainer _canvasIM, _regCanvasIM, _paletteGridIM, _playIM, _seqStripIM, _zoneBarIM;
        private IntegerField _boxLField, _boxTField, _boxWField, _boxHField;
        private Button _addRegionButton, _playToggleButton, _addEventButton;
        private Label _metaParamLabel;
        private TextField _metaParamField;

        // ── section toggle bar (T-0084) ───────────────────────────────────────
        // See the scope note above BuildUI for WHICH panels this addresses and why the other steps stay
        // out of it. The roster is rebuilt from scratch every time the barred hosts are (re)built — by
        // BuildUI on a full rebuild AND by Refresh() on its lighter in-place rebuild, since Refresh()
        // replaces the ZuiSection instances those hosts contain (see RebuildBar's own comment).
        private readonly List<(string label, ZuiSection section)> _barUnits = new List<(string label, ZuiSection section)>();
        private VisualElement _barHost;
        private float _barReservedW, _barReservedH;

        [MenuItem("Laubrary/Laumination Builder")]
        public static void Open()
        {
            var w = GetWindow<LauminationBuilderWindow>("Laumination Builder");
            w.minSize = new Vector2(900, 640);
            w.Show();
        }

        /// <summary>Open the Laumination Builder bound to a lauminary's draft animation (called by the Lauminary
        /// Builder). Saving writes back to that lauminary.</summary>
        public static void OpenForEdit(Lauminary lauminary, string animName)
        {
            var w = GetWindow<LauminationBuilderWindow>("Laumination Builder");
            if (!w.ConfirmDocumentTransition()) return;
            w.minSize = new Vector2(900, 640);
            w.Show();
            w._pendingEditLauminary = lauminary;
            w._pendingEditAnim = animName;
            w._pendingOrphan = null;
            w.TryApplyPending();
        }

        /// <summary>Open the Laumination Builder editing a standalone orphaned animation (called by the Lauminary
        /// Builder's library). Saving updates that orphan.</summary>
        public static void OpenForOrphan(AnimationAsset orphan)
        {
            var w = GetWindow<LauminationBuilderWindow>("Laumination Builder");
            if (!w.ConfirmDocumentTransition()) return;
            w.minSize = new Vector2(900, 640);
            w.Show();
            w._pendingOrphan = orphan;
            w._pendingEditLauminary = null;
            w._pendingEditAnim = null;
            w.TryApplyPending();
        }

        private void OnEnable() { _animLastStep = EditorApplication.timeSinceStartup; EditorApplication.update += AnimTick; }
        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= AnimTick; DestroyDisplaySheet(); DestroyPreviewBake(); ClearThumbCache(); ClearMaskCache();
            if (_selXformTex != null) { Object.DestroyImmediate(_selXformTex); _selXformTex = null; }
        }

        private void AnimTick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _animLastStep);
            _animLastStep = now; _animNow = now;
            if (!_animPlaying || _sequence.Count == 0) return;

            EnsurePreviewBake();
            if (_previewFrames == null || _previewFrames.Count == 0) return;

            if (_phasePreviewMode)
            {
                EnsurePhasePreview();
                if (_phasePreviewPlayer == null) return;
                if (_phasePreviewPlayer.CurrentClip != _previewDef.name) StartPhasePreview();
                _phasePreviewPlayer.Tick(dt);
                _animFrame = _phasePreviewPlayer.CurrentFrame;
                _animPlaying = _phasePreviewPlayer.IsPlaying;
                RefreshFrameLabels(); SyncFrameTimelinePlayhead(); UpdatePreviewTransport();
                _playIM?.MarkDirtyRepaint(); _seqStripIM?.MarkDirtyRepaint(); _zoneBarIM?.MarkDirtyRepaint();
                return;
            }

            if (_inDivider)
            {
                if (now >= _dividerUntil) { _inDivider = false; RestartPreview(); }
                _playIM?.MarkDirtyRepaint();
                return;
            }
            if (_showingFrameZero)
            {
                if (!FrameZeroActive) _showingFrameZero = false;
                else if (now >= _frameZeroUntil) { _showingFrameZero = false; StartClip(); }
                _playIM?.MarkDirtyRepaint();
                _seqStripIM?.MarkDirtyRepaint();
                return;
            }

            // Make sure the shared player is running our live-baked clip with the right loop mode.
            if (_previewPlayer.Anim != _previewDef || (!_previewPlayer.IsPlaying && !_inDivider)) RestartPreview();
            _previewPlayer.Tick(dt, 1f);
            _animFrame = _previewPlayer.Frame;
            // Playback only moves the playhead — repaint the islands that show it, never the whole window.
            RefreshFrameLabels(); SyncFrameTimelinePlayhead();
            _playIM?.MarkDirtyRepaint();
            _seqStripIM?.MarkDirtyRepaint();
            _zoneBarIM?.MarkDirtyRepaint();
        }

        // What a frame with this timing lasts at the current FPS, in milliseconds.
        private float FrameMsOf(float pct) => _animFps > 0f ? 1000f / _animFps * FrameRef.TimingFactorOf(pct) : 0f;

        // Frame 0 is on and points at a sprite whose texture can be read.
        private bool FrameZeroActive => _frameZeroOn && SeqRefValid(_frameZero) && RegionSourceUsable(_frameZero.region, out _);

        private FrameRef FrameRefOf(CellRef cr)
        {
            var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
            return new FrameRef { sourceTextureGuid = reg.sourceTextureGuid, cell = reg.cells[cr.cell], pivot = reg.pivots[cr.cell], transform = reg.transforms[cr.cell], timingPercent = cr.pct };
        }

        // Point frame 0 at a sprite, keeping its own length once it has one.
        private void SetFrameZero(int region, int cell)
        {
            RecordUndo("Set frame 0");
            float pct = SeqRefValid(_frameZero) ? _frameZero.pct : DefaultFrameZeroPct;
            _frameZero = new CellRef(region, cell) { pct = pct };
            _frameZeroOn = true;
            _status = "Frame 0 set. It shows before frame 1 in the preview only.";
            RestartPreview();
            Refresh();
        }

        // Copy each sequence frame's timing onto the preview's recipe, which is what the player times frames by.
        private void SyncPreviewTimings()
        {
            if (_previewDef?.recipe == null) return;
            for (int i = 0; i < _previewDef.recipe.Count && i < _sequence.Count; i++)
                if (_previewDef.recipe[i] != null) _previewDef.recipe[i].timingPercent = _sequence[i].pct;
        }

        // ── live in-memory bake feeding the shared player ────────────────────────
        // Re-bakes ONLY when something that affects the frames changes (sequence, pivots, colour key, frame
        // box). The result is the same baked, uniform frames the game plays — so the preview is WYSIWYG.
        private void EnsurePreviewBake()
        {
            int h = PreviewHash();
            if (h == _previewHash && _previewFrames != null)
            {
                if (_previewDef != null)
                {
                    _previewDef.fps = Mathf.Max(1f, _animFps); // FPS is a free preview knob
                    SyncPreviewTimings();                      // so are frame durations: no re-bake needed
                }
                return;
            }
            _previewHash = h;
            DestroyPreviewBake();

            var recipe = BuildRecipe();
            if (recipe == null || recipe.Count == 0) { _previewDef = null; return; }

            var box = new AtlasBaker.FrameBox { fixedSize = _fixedFrame, w = _frameW, h = _frameH, pivot = _framePivot };
            var frames = AtlasBaker.BakeInMemory(recipe, 16f, out string err, out var tex, CurrentColorKey(), box);
            if (frames == null) { _status = "Preview bake: " + err; _previewDef = null; return; }
            if (FrameZeroActive)
            {
                // An editor-only lead-in must never resize the real frames (and their metadata grid).
                // Bake it independently and register both by their own pivot at the same screen point.
                _previewFrameZero = AtlasBaker.BakeInMemory(new List<FrameRef> { FrameRefOf(_frameZero) },
                    16f, out var zeroError, out _previewZeroTex, CurrentColorKey());
                if (_previewFrameZero == null) _status = "Lead-in preview: " + zeroError;
            }

            _previewTex = tex;
            _previewFrames = frames;
            _previewDef = new Laumination { name = "__preview", fps = Mathf.Max(1f, _animFps), frames = frames, events = _events, recipe = recipe };
            RestartPreview();
        }

        private void DestroyPreviewBake()
        {
            DestroyPhasePreview();
            if (_previewFrames != null) foreach (var sprite in _previewFrames) if (sprite != null) Object.DestroyImmediate(sprite);
            if (_previewFrameZero != null) foreach (var sprite in _previewFrameZero) if (sprite != null) Object.DestroyImmediate(sprite);
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
            if (_previewZeroTex != null) { Object.DestroyImmediate(_previewZeroTex); _previewZeroTex = null; }
            _previewFrames = null; _previewDef = null; _previewFrameZero = null;
            _previewPlayer.Stop();
        }

        // Loop gap (pause / idle frame between loops) is a preview-only nicety: play the clip non-looping and
        // schedule the gap from its completion, else just loop continuously. It wraps the player, never forks it.
        private void RestartPreview()
        {
            if (_previewDef == null) return;
            _previewPausedAt = 0;
            if (_phasePreviewMode) { StartPhasePreview(); return; }
            if (FrameZeroActive && _previewFrameZero != null)
            {
                _showingFrameZero = true;
                _frameZeroUntil = EditorApplication.timeSinceStartup + FrameMsOf(_frameZero.pct) / 1000f;
                _previewPlayer.Stop();
                return;
            }
            StartClip();
        }

        private void StartClip()
        {
            if (_previewDef == null) return;
            // Frame 0 must come back before every loop, so the clip then ends instead of wrapping by itself.
            bool loop = !FrameZeroActive && (_loopDivider == LoopDivider.None || _loopPause <= 0f);
            _previewPlayer.Play(_previewDef, loop, 1f, loop ? (System.Action)null : OnPreviewLoopComplete);
            _animFrame = _previewPlayer.Frame;
        }

        private void OnPreviewLoopComplete()
        {
            if (_loopDivider != LoopDivider.None && _loopPause > 0f)
            { _inDivider = true; _dividerUntil = _animNow + _loopPause; }
            else RestartPreview();
        }

        /// <summary>Hash of everything that affects the baked frames — drives re-bake on edit.</summary>
        private int PreviewHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (_fixedFrame ? 1 : 0);
                h = h * 31 + _frameW; h = h * 31 + _frameH; h = h * 31 + _framePivot.GetHashCode();
                h = h * 31 + (_bgKeyEnabled ? 1 : 0);
                h = h * 31 + (_bgKey.r << 24 | _bgKey.g << 16 | _bgKey.b << 8 | _bgKey.a);
                h = h * 31 + _bgTolerance;
                foreach (var cr in _sequence)
                {
                    if (!SeqRefValid(cr)) continue;
                    var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                    h = h * 31 + cr.region; h = h * 31 + cr.cell;
                    h = h * 31 + reg.cells[cr.cell].GetHashCode();
                    h = h * 31 + reg.pivots[cr.cell].GetHashCode();
                    h = h * 31 + TransformHash(reg.transforms[cr.cell]);
                    // WHICH TEXTURE this region resolves to must be part of the cache key too — region/cell
                    // INDICES alone don't change when a fix only affects texture RESOLUTION behind those same
                    // indices (exactly what happened here: BuildRecipe's texture-guid bug was fixed without
                    // any region/cell index ever changing, so this hash never invalidated the stale preview
                    // baked from the wrong texture, even after the underlying bug was already fixed).
                    h = h * 31 + (reg.sourceTextureGuid != null ? reg.sourceTextureGuid.GetHashCode() : 0);
                }
                if (FrameZeroActive)
                {
                    var z = FrameRefOf(_frameZero);
                    h = h * 31 + z.cell.GetHashCode(); h = h * 31 + z.pivot.GetHashCode();
                    h = h * 31 + TransformHash(z.transform);
                    h = h * 31 + (z.sourceTextureGuid != null ? z.sourceTextureGuid.GetHashCode() : 0);
                }
                return h;
            }
        }

        // ── window shell ─────────────────────────────────────────────────────
        /// A wrapping row — controls flow onto the next line instead of ever overflowing the pane.
        private static VisualElement WrapRow(params VisualElement[] children)
        {
            var row = Z.Row(children);
            row.AddToClassList("zui-row--wrap");
            return row;
        }

        protected override void OnBeforeRebuild()
        {
            _bannerHost = _topHost = _leftControlsHost = _paletteHost = _animHost = null;
            _barHost = null;
            _leftPane = _splitRow = null;
            _statusLabel = null;
            _canvasIM = _regCanvasIM = _paletteGridIM = _playIM = _seqStripIM = _zoneBarIM = null;
            _boxLField = _boxTField = _boxWField = _boxHField = null;
            _addRegionButton = _playToggleButton = _addEventButton = null;
            _metaParamLabel = null; _metaParamField = null;
        }

        /// The handful of labels/fields that name the CURRENT frame. Playback moves the playhead every tick,
        /// so these are patched in place instead of rebuilding the panels that hold them.
        private void RefreshFrameLabels()
        {
            int cur = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : 0;
            if (_addEventButton != null) _addEventButton.text = $"+ event @ frame {cur + 1}";

            var layer = ActiveLayer();
            if (_metaParamField == null || layer == null || cur >= layer.frames.Count) return;
            if (_metaParamField.panel?.focusController?.focusedElement == _metaParamField) return;  // don't fight typing
            if (_metaParamLabel != null) _metaParamLabel.text = $"F{cur + 1} param";
            _metaParamField.SetValueWithoutNotify(layer.frames[cur].param ?? "");
        }

        private void Dirty()
        {
            UpdateWorkspaceStatus();
            _canvasIM?.MarkDirtyRepaint();
            _regCanvasIM?.MarkDirtyRepaint();
            _paletteGridIM?.MarkDirtyRepaint();
            _playIM?.MarkDirtyRepaint();
            RefreshFrameTimeline();
        }

        private void Refresh()
        {
            if (!this) return;
            if (_workspaceStage == null) { Rebuild(); return; }
            RefreshWorkspace();
        }

        private void Unit(VisualElement body, string label, System.Action<VisualElement> build) => build(body);

        /// Show a one-line result/explanation without rebuilding anything.
        private void SetStatus(string text)
        {
            _status = text;
            if (_statusLabel != null) _statusLabel.tooltip = _status ?? Disclaimer;
        }


        // ── 1 · sheet ────────────────────────────────────────────────────────

        private void DownloadSheetFromUrl()
        {
            Texture2D tex; string error;
            EditorUtility.DisplayProgressBar("Launimator", "Downloading image…", 0.5f);
            try { tex = SheetLibrary.DownloadImage(_sheetUrl, _downloadName, out error); }
            finally { EditorUtility.ClearProgressBar(); }

            if (tex == null) { SetStatus(error ?? "Download failed."); return; }
            StartNewAnimation(SuggestNewAnimName());
            LoadSheet(tex);
            BeginCleanDocument();
            _sheetUrl = ""; _downloadName = "";
            SetStatus($"Downloaded and loaded '{_sheetDisplayName}' into {SheetLibrary.Folder}.");
            EditorGUIUtility.PingObject(tex);
        }

        private static readonly string[] ToolModeLabels = { "Grid (uniform sheet)", "Box (one sprite)", "Pick (scattered sprites)" };
        private static readonly string[] GridModeLabels = { "Columns/Rows", "Cell Size" };

        // Short enough to sit on one segmented row beside PPU. Order matches GridSlicer.PivotMode.
        private static readonly string[] PivotLabels = { "Center", "Bottom", "Top Left", "Custom" };

        // NOT a section of its own. "Mode" was styled exactly like the numbered steps while sitting between 1
        // and 2 without a number, so it read as a step that had lost its place in the sequence — and the three
        // modes ARE how sprites get identified ("uniform sheet" / "one sprite" / "scattered sprites"), which is
        // step 2's entire job. It is now step 2's first row: a plain label, no competing heading.

        // ── 2 · region grid UI (mode-gated) ──────────────────────────────────

        /// The two labels/fields that mirror the live marquee — updated in place while dragging so the
        /// retained controls never lag the canvas (and no rebuild steals focus mid-drag).
        private void RefreshBoxDependentLabels()
        {
            _boxLField?.SetValueWithoutNotify(Mathf.RoundToInt(_box.x));
            _boxTField?.SetValueWithoutNotify(Mathf.RoundToInt(_texH - _box.yMax));
            _boxWField?.SetValueWithoutNotify(Mathf.RoundToInt(_box.width));
            _boxHField?.SetValueWithoutNotify(Mathf.RoundToInt(_box.height));
            if (_addRegionButton != null) _addRegionButton.text = $"Add Region ({CurrentBoxCellCount()})";
        }

        // ── 3 · canvas (IMGUI island: bespoke sheet painting + marquee/handle/pick gizmos) ────
        private void DrawCanvasGUI()
        {
            if (_canvasIM == null || _sheet == null) return;
            Rect viewport = new Rect(0f, 0f, _canvasIM.layout.width, _canvasIM.layout.height);
            if (!(viewport.width > 10f) || !(viewport.height > 10f)) return;

            if (!_zoomInitialized && _texW > 0)
            {
                _zoom = Mathf.Clamp((viewport.width - 18f) / _texW, 0.25f, 8f);
                _zoomInitialized = true;
            }

            float contentW = _texW * _zoom, contentH = _texH * _zoom;
            _canvasScroll = GUI.BeginScrollView(viewport, _canvasScroll, new Rect(0, 0, contentW, contentH));

            Rect imageRect = new Rect(0, 0, contentW, contentH);
            _lastImageRect = imageRect;
            EditorGUI.DrawRect(imageRect, new Color(0.18f, 0.18f, 0.18f));
            GUI.DrawTexture(imageRect, SheetForDisplay(), ScaleMode.StretchToFill, true);

            foreach (var reg in _regions.Where(r => r.sourceTextureGuid == CurrentSheetGuid()))
                foreach (var cell in reg.cells)
                {
                    Rect sc = TexRectToContent(cell);
                    EditorGUI.DrawRect(sc, new Color(0.2f, 0.9f, 0.4f, 0.10f));
                    DrawRectOutline(sc, new Color(0.2f, 0.9f, 0.4f, 0.5f), 1f);
                }

            if (HasSelectedCell())
            {
                Rect selSc = TexRectToContent(_regions[_selRegion].cells[_selCell]);
                EditorGUI.DrawRect(selSc, new Color(1f, 0.1f, 0.8f, 0.18f));
                DrawRectOutline(selSc, new Color(1f, 0.1f, 0.8f, 1f), 2f);
            }

            HandleCanvasInput(imageRect);

            if ((_toolMode == ToolMode.Grid || _toolMode == ToolMode.Box) && _hasBox)
            {
                Rect scBox = TexRectToContent(_box);
                DrawRectOutline(scBox, new Color(1f, 0.85f, 0.1f, 1f), 2f);
                if (_toolMode == ToolMode.Grid && !_detectMode)
                {
                    foreach (var cell in RegionSlicer.ExpandRegion(BuildActiveSpec()))
                        DrawRectOutline(TexRectToContent(cell), new Color(0.1f, 1f, 1f, 1f), 1.5f);
                    DrawHandles(scBox);
                }
            }
            else if (_toolMode == ToolMode.Pick && _hasBox)
            {
                // The right-drag auto-detect marquee (Pick mode has no persistent left-drag box of its own).
                DrawRectOutline(TexRectToContent(_box), new Color(1f, 0.85f, 0.1f, 1f), 2f);
            }

            if (_detectMode) foreach (var cell in _detectedCells)
                DrawRectOutline(TexRectToContent(cell), new Color(1f, .55f, .1f), 2f);
            GUI.EndScrollView();
        }

        private void DrawHandles(Rect scBox)
        {
            float s = 7f; Color c = new Color(1f, 0.85f, 0.1f, 1f);
            EditorGUI.DrawRect(HandleRect(scBox.xMin, scBox.yMin, s), c);
            EditorGUI.DrawRect(HandleRect(scBox.xMax, scBox.yMin, s), c);
            EditorGUI.DrawRect(HandleRect(scBox.xMin, scBox.yMax, s), c);
            EditorGUI.DrawRect(HandleRect(scBox.xMax, scBox.yMax, s), c);
        }
        private static Rect HandleRect(float cx, float cy, float s) => new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s);

        private void HandleCanvasInput(Rect imageRect)
        {
            Event e = Event.current;
            Vector2 mouse = e.mousePosition;

            // Eyedropper: the next canvas click samples the background colour, in any mode.
            if (_pickingBgColor)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && imageRect.Contains(mouse))
                {
                    Vector2 t = ContentToTex(mouse);
                    SampleBgColorAt(Mathf.FloorToInt(t.x), Mathf.FloorToInt(t.y));
                    e.Use(); DeferRefresh();
                }
                return;
            }

            // Right-button marquee → auto-detect sprites inside it. Works in EVERY mode (incl. Pick), since it
            // uses its own right-drag gesture rather than a mode's persistent box. (See the AutoSlice partial.)
            if (HandleAutoMarquee(imageRect, mouse, e)) return;

            if (_toolMode == ToolMode.Pick)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && imageRect.Contains(mouse))
                {
                    Vector2 t = ContentToTex(mouse);
                    PickSpriteAt(Mathf.FloorToInt(t.x), Mathf.FloorToInt(t.y));
                    e.Use(); DeferRefresh();
                }
                return;
            }

            // Grid + Box share marquee dragging; Grid additionally supports move/resize of a persistent box.
            bool grid = _toolMode == ToolMode.Grid;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !imageRect.Contains(mouse)) break;
                    if (grid && _hasBox)
                    {
                        HandleKind h = HitHandle(TexRectToContent(_box), mouse);
                        if (h != HandleKind.None)
                        {
                            _activeHandle = h; _handleGrabTex = ContentToTex(mouse); _boxAtGrab = _box;
                            e.Use(); break;
                        }
                    }
                    _dragging = true; _activeHandle = HandleKind.None;
                    _dragStartTex = SnapTex(ContentToTex(mouse));
                    _box = new Rect(_dragStartTex.x, _dragStartTex.y, 0, 0);
                    _hasBox = true; e.Use();
                    break;

                case EventType.MouseDrag:
                    if (_dragging)
                    {
                        Vector2 cur = SnapTex(ContentToTex(mouse));
                        float x0 = Mathf.Min(_dragStartTex.x, cur.x), y0 = Mathf.Min(_dragStartTex.y, cur.y);
                        float x1 = Mathf.Max(_dragStartTex.x, cur.x), y1 = Mathf.Max(_dragStartTex.y, cur.y);
                        _box = ClampBox(new Rect(x0, y0, x1 - x0, y1 - y0));
                        e.Use(); RefreshBoxDependentLabels(); Repaint();
                    }
                    else if (_activeHandle != HandleKind.None)
                    {
                        ApplyHandleDrag(SnapTex(ContentToTex(mouse)));
                        e.Use(); RefreshBoxDependentLabels(); Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button != 0) break;
                    if (_dragging)
                    {
                        _dragging = false;
                        bool tooSmall = _box.width < 1 || _box.height < 1;
                        // Box mode: release commits the marquee as ONE sprite, then clears for the next.
                        if (!grid && !tooSmall) AddSingleCellFromBox();
                        if (!grid || tooSmall) { _hasBox = false; _box = default; }
                        e.Use(); DeferRefresh();
                    }
                    else if (_activeHandle != HandleKind.None) { _activeHandle = HandleKind.None; e.Use(); DeferRefresh(); }
                    break;
            }
        }

        private void SampleBgColorAt(int texX, int texY)
        {
            var px = GetPixels();
            if (px == null) { _status = "Texture not readable."; return; }
            texX = Mathf.Clamp(texX, 0, _texW - 1);
            texY = Mathf.Clamp(texY, 0, _texH - 1);
            _bgKey = px[texY * _texW + texX];
            _bgKeyEnabled = true;
            _pickingBgColor = false;
            RebuildDisplaySheet();
            _status = $"Background colour set to RGB({_bgKey.r},{_bgKey.g},{_bgKey.b}); saved for this sheet, treated as transparent.";
        }

        /// <summary>Commit the current marquee box as one sprite cell (Box mode — no grid, no settings).</summary>
        private void AddSingleCellFromBox()
        {
            Rect cell = new Rect(Mathf.Round(_box.x), Mathf.Round(_box.y), Mathf.Round(_box.width), Mathf.Round(_box.height));
            if (cell.width < 1 || cell.height < 1) return;
            RecordUndo("Add sprite");

            int idx = _regions.FindIndex(r => r.label == BoxLabel && r.sourceTextureGuid == CurrentSheetGuid());
            if (idx < 0) { _regions.Add(new Region { label = BoxLabel, bounds = new Rect(0, 0, _texW, _texH), sourceTextureGuid = CurrentSheetGuid() }); idx = _regions.Count - 1; }
            var reg = _regions[idx];
            reg.cells.Add(cell); reg.SyncPivots(GlobalPivot());
            SelectSingle(idx, reg.cells.Count - 1);
            _status = $"Added {cell.width:0}×{cell.height:0}px sprite. Drag the next one.";
        }

        private void ApplyHandleDrag(Vector2 tex)
        {
            Rect b = _boxAtGrab;
            float left = b.xMin, right = b.xMax, bottom = b.yMin, top = b.yMax;
            switch (_activeHandle)
            {
                case HandleKind.Move:
                    Vector2 d = tex - _handleGrabTex;
                    Rect moved = new Rect(b.x + d.x, b.y + d.y, b.width, b.height);
                    moved.x = Mathf.Clamp(moved.x, 0, _texW - moved.width);
                    moved.y = Mathf.Clamp(moved.y, 0, _texH - moved.height);
                    _box = moved; return;
                case HandleKind.ResizeL: left = tex.x; break;
                case HandleKind.ResizeR: right = tex.x; break;
                case HandleKind.ResizeB: bottom = tex.y; break;
                case HandleKind.ResizeT: top = tex.y; break;
                case HandleKind.ResizeBL: left = tex.x; bottom = tex.y; break;
                case HandleKind.ResizeBR: right = tex.x; bottom = tex.y; break;
                case HandleKind.ResizeTL: left = tex.x; top = tex.y; break;
                case HandleKind.ResizeTR: right = tex.x; top = tex.y; break;
            }
            float ax0 = Mathf.Min(left, right), ax1 = Mathf.Max(left, right);
            float ay0 = Mathf.Min(bottom, top), ay1 = Mathf.Max(bottom, top);
            _box = ClampBox(new Rect(ax0, ay0, ax1 - ax0, ay1 - ay0));
        }

        private HandleKind HitHandle(Rect scBox, Vector2 mouse)
        {
            float s = 9f;
            bool nearL = Mathf.Abs(mouse.x - scBox.xMin) <= s, nearR = Mathf.Abs(mouse.x - scBox.xMax) <= s;
            bool nearTop = Mathf.Abs(mouse.y - scBox.yMin) <= s, nearBot = Mathf.Abs(mouse.y - scBox.yMax) <= s;
            bool inX = mouse.x >= scBox.xMin - s && mouse.x <= scBox.xMax + s;
            bool inY = mouse.y >= scBox.yMin - s && mouse.y <= scBox.yMax + s;
            if (nearL && nearTop) return HandleKind.ResizeTL;
            if (nearR && nearTop) return HandleKind.ResizeTR;
            if (nearL && nearBot) return HandleKind.ResizeBL;
            if (nearR && nearBot) return HandleKind.ResizeBR;
            if (nearL && inY) return HandleKind.ResizeL;
            if (nearR && inY) return HandleKind.ResizeR;
            if (nearTop && inX) return HandleKind.ResizeT;
            if (nearBot && inX) return HandleKind.ResizeB;
            if (scBox.Contains(mouse)) return HandleKind.Move;
            return HandleKind.None;
        }

        // ── 4 · cells preview (identified sprites) ───────────────────────────
        private const string PaletteHelp =
            "Click = select · Ctrl/Shift-click = multi-select · Double-click = add to sequence · Right-click = actions menu.\n\n" +
            "Keys (with a sprite selected): A / D = previous / next sprite · arrow keys = nudge registration.\n\n" +
            "Edit in Aseprite: bakes the selected sprite(s) into an owned .aseprite and opens Aseprite. (Set the app path under Tools ▸ Launimator.)";


        /// The sprite palette grid — an IMGUI island: a thumbnail grid with sequence badges, multi-select and a
        /// right-click actions menu. Bespoke canvas painting, exactly what the rulebook keeps raw.
        private void DrawPaletteGridGUI()
        {
            if (_paletteGridIM == null) return;
            Rect view = new Rect(0f, 0f, _paletteGridIM.layout.width, _paletteGridIM.layout.height);
            if (!(view.width > 20f)) return;

            const int cell = 56, pad = 4;
            var flat = VisiblePaletteCells();
            int perRow = Mathf.Max(1, Mathf.FloorToInt((view.width - 18f) / (cell + pad)));
            int rows = Mathf.CeilToInt(flat.Count / (float)perRow);
            Rect content = new Rect(0, 0, view.width - 18f, Mathf.Max(view.height, rows * (cell + pad)));

            _cellsScroll = GUI.BeginScrollView(view, _cellsScroll, content);
            for (int i = 0; i < flat.Count; i++)
            {
                var cr = flat[i];
                Rect r = new Rect((i % perRow) * (cell + pad), (i / perRow) * (cell + pad), cell, cell);
                EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
                DrawCellThumb(r, cr.region, cr.cell);

                bool isSel = IsCellSelected(cr.region, cr.cell);
                bool isPrimary = cr.region == _selRegion && cr.cell == _selCell;
                DrawRectOutline(r, isSel ? new Color(1f, 0.1f, 0.8f, 1f) : new Color(1f, 1f, 1f, 0.5f), isSel ? (isPrimary ? 2f : 1.5f) : 1f);

                string ord = SequenceOrdinalsFor(cr.region, cr.cell);
                if (ord != null)
                {
                    var badge = new Rect(r.x, r.y, Mathf.Max(16, 8 + ord.Length * 7), 15);
                    EditorGUI.DrawRect(badge, new Color(0.2f, 0.5f, 1f, 0.92f));
                    GUI.Label(badge, ord, EditorStyles.whiteMiniLabel);
                }

                var ev = Event.current;
                if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
                {
                    if (ev.button == 1)
                    {
                        if (!isSel) SelectSingle(cr.region, cr.cell);
                        ShowSpriteContextMenu(cr);
                    }
                    else if (ev.button == 0)
                    {
                        if (ev.clickCount == 2) AppendToSequence(cr.region, cr.cell);
                        else if (ev.control || ev.command) ToggleSelect(cr.region, cr.cell);
                        else if (ev.shift) RangeSelectTo(cr.region, cr.cell);
                        else SelectSingle(cr.region, cr.cell);
                    }
                    ev.Use();
                    // Selection drives which controls exist (single vs multi labels, enabled state) → rebuild.
                    DeferRefresh();
                }
            }
            GUI.EndScrollView();
        }

        /// <summary>"Promote to editable": export the selected palette sprites' RAW pixels to an owned .aseprite
        /// (one frame each, bottom-left placed) and open Aseprite. Records the cell rects so Sync can write edits
        /// back. Raw (untransformed/unkeyed) so the round-trip is lossless — the cell's transform/colour-key still
        /// apply at bake.</summary>
        private void OpenSelectionInAseprite()
        {
            var sel = SelectedCells();
            if (sel.Count == 0) { _status = "Select sprite(s) in #4 to edit in Aseprite."; return; }
            var px = GetPixels();
            if (px == null) { _status = "Texture not readable."; return; }

            _editRects.Clear();
            var blocks = new List<(Color32[] b, int w, int h)>();
            foreach (var cr in sel)
            {
                var cell = _regions[cr.region].cells[cr.cell];
                int x0 = Mathf.RoundToInt(cell.x), y0 = Mathf.RoundToInt(cell.y), w = Mathf.RoundToInt(cell.width), h = Mathf.RoundToInt(cell.height);
                var b = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    { int sx = x0 + x, sy = y0 + y; b[y * w + x] = (sx >= 0 && sx < _texW && sy >= 0 && sy < _texH) ? px[sy * _texW + sx] : new Color32(0, 0, 0, 0); }
                blocks.Add((b, w, h)); _editRects.Add(new Rect(x0, y0, w, h));
            }
            int W = blocks.Max(x => x.w), H = blocks.Max(x => x.h);

            var doc = new AseDoc { width = W, height = H, frameCount = blocks.Count };
            doc.layers.Add(new AseLayer { name = "layer" });
            doc.pixels = new Color32[blocks.Count][][];
            for (int f = 0; f < blocks.Count; f++)
            {
                var (b, bw, bh) = blocks[f];
                var canvas = new Color32[W * H];
                for (int y = 0; y < bh; y++) for (int x = 0; x < bw; x++) canvas[y * W + x] = b[y * bw + x];  // bottom-left
                doc.pixels[f] = new[] { canvas };
            }

            const string folder = "Assets/Launimator/_Edits";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(folder));
            string baseName = string.IsNullOrWhiteSpace(_animName) ? "palette" : _animName;
            string path = $"{folder}/{baseName}_edit.aseprite";
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(path), AsepriteIO.Write(doc));
            AssetDatabase.Refresh();
            _editAsePath = path; _editSheetPath = _sheetPath;
            _status = AsepriteLauncher.Open(path)
                ? $"Editing {sel.Count} sprite(s) in Aseprite — edit, save, then click 'Sync edits'."
                : $"Wrote {path}, but Aseprite didn't launch. Set its path via Tools ▸ Launimator ▸ Set Aseprite Path.";
        }

        /// <summary>Read the edited .aseprite back and write its pixels into an OWNED copy of the sheet at the
        /// original cell rects, then rebind to it — so the edits appear in the palette. Non-destructive (the
        /// original sheet is untouched). The sprite must keep its position/size in Aseprite (paint over it).</summary>
        private void SyncFromAseprite()
        {
            if (string.IsNullOrEmpty(_editAsePath) || !System.IO.File.Exists(System.IO.Path.GetFullPath(_editAsePath)))
            { _status = "Nothing to sync — use 'Edit in Aseprite' first."; return; }
            if (_editSheetPath != _sheetPath) { _status = "Sheet changed since editing — reload the original sheet, then Sync."; return; }
            var px = GetPixels();
            if (px == null) { _status = "Texture not readable."; return; }

            var doc = AsepriteIO.Read(System.IO.File.ReadAllBytes(System.IO.Path.GetFullPath(_editAsePath)));
            var sheet = (Color32[])px.Clone();
            int n = Mathf.Min(doc.frameCount, _editRects.Count);
            for (int f = 0; f < n; f++)
            {
                var comp = CompositeAseFrame(doc, f);
                var r = _editRects[f];
                int x0 = (int)r.x, y0 = (int)r.y, w = (int)r.width, h = (int)r.height;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (x >= doc.width || y >= doc.height) continue;      // bottom-left placement
                        int sx = x0 + x, sy = y0 + y;
                        if (sx >= 0 && sx < _texW && sy >= 0 && sy < _texH) sheet[sy * _texW + sx] = comp[y * doc.width + x];
                    }
            }

            var tex = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false);
            tex.SetPixels32(sheet); tex.Apply();
            const string folder = "Assets/Launimator/_Edits";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(folder));
            string ownedPath = $"{folder}/{System.IO.Path.GetFileNameWithoutExtension(_sheetPath)}_owned.png";
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(ownedPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.Refresh();
            ConfigureSheetTexture(ownedPath);

            var owned = AssetDatabase.LoadAssetAtPath<Texture2D>(ownedPath);
            if (owned == null) { _status = "Sync wrote the sheet but couldn't load it."; return; }
            // Rebind the source texture WITHOUT touching the regions/cells (same dimensions → rects stay valid).
            _sheet = owned; _sheetPath = ownedPath; _texW = owned.width; _texH = owned.height;
            _pixelCache = null; _pixelCacheFor = null; ClearThumbCache();
            RebuildDisplaySheet();   // the palette/canvas draw a keyed COPY of the sheet — rebuild it from the new pixels
            _previewHash = -1;       // force the animation preview to re-bake from the new sheet
            _status = $"Synced {n} sprite(s) from Aseprite into an owned sheet — {ownedPath}";
            Repaint();
        }

        private static Color32[] CompositeAseFrame(AseDoc doc, int f)
        {
            var comp = new Color32[doc.width * doc.height];
            foreach (var layer in doc.pixels[f])
            {
                if (layer == null) continue;
                for (int i = 0; i < comp.Length; i++)
                {
                    var s = layer[i]; if (s.a == 0) continue;
                    if (comp[i].a == 0) { comp[i] = s; continue; }
                    float sa = s.a / 255f;
                    comp[i] = new Color32(
                        (byte)(s.r * sa + comp[i].r * (1 - sa)), (byte)(s.g * sa + comp[i].g * (1 - sa)),
                        (byte)(s.b * sa + comp[i].b * (1 - sa)), (byte)Mathf.Max(s.a, comp[i].a));
                }
            }
            return comp;
        }

        private static void ConfigureSheetTexture(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.isReadable = true;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 8192;
            ti.SaveAndReimport();
        }


        // ── per-sprite transform edits (UI.4) ────────────────────────────────
        private void MutateSelectedTransforms(System.Func<CellTransform, CellTransform> fn, bool recordUndo = true)
        {
            if (!HasSelectedCell()) return;
            if (recordUndo) RecordUndo("Transform sprite");
            foreach (var cr in SelectedCells())
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                reg.transforms[cr.cell] = fn(reg.transforms[cr.cell]);
            }
            Repaint();
        }

        /// <summary>Append an independent copy of each selected sprite (own cell + pivot + transform) so the copy
        /// can be edited without touching the original a sequence frame references. Selects the new copies.</summary>
        private void DuplicateSelectedSprites()
        {
            var sel = SelectedCells();
            if (sel.Count == 0) return;
            RecordUndo("Duplicate sprite");
            var made = new List<CellRef>();
            foreach (var cr in sel)
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                reg.cells.Add(reg.cells[cr.cell]);
                reg.pivots.Add(reg.pivots[cr.cell]);
                reg.transforms.Add(reg.transforms[cr.cell]);
                made.Add(new CellRef(cr.region, reg.cells.Count - 1));
            }
            _multiSel.Clear();
            foreach (var m in made) _multiSel.Add(PackCell(m.region, m.cell));
            _selRegion = made[0].region; _selCell = made[0].cell; _selAnchor = made[0];
            _status = $"Duplicated {sel.Count} sprite(s) — edit the copy freely; the original is untouched.";
            Repaint();
        }

        // ── keyboard: navigate sprites + nudge registration (section 4) ──────
        // A/D step the primary selection through the flattened sprite list; arrows nudge the registration one
        // source pixel (mirrors the ←→↓↑ buttons). Suppressed while a text field is being edited so typing in
        // the name/search fields isn't hijacked.
        private void OnRootKeyDown(KeyDownEvent e)
        {
            if (IsEditingText()) return;

            // Undo/redo first so Ctrl+Z wins over any focused control's own handling.
            if (e.ctrlKey || e.commandKey)
            {
                if (e.keyCode == KeyCode.Z && !e.shiftKey) { PerformUndo(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shiftKey)) { PerformRedo(); e.StopPropagation(); }
                return;
            }

            if (!HasSelectedCell()) return;
            switch (e.keyCode)
            {
                // A/D move the PRIMARY selection, which changes which controls exist → full refresh.
                case KeyCode.A:          StepSelection(-1);         e.StopPropagation(); Refresh(); break;
                case KeyCode.D:          StepSelection(1);          e.StopPropagation(); Refresh(); break;
                // Arrows only move a pivot — nothing structural, so only the islands need repainting.
                case KeyCode.LeftArrow:  NudgeSelectedPivot(-1, 0); e.StopPropagation(); Dirty(); break;
                case KeyCode.RightArrow: NudgeSelectedPivot(1, 0);  e.StopPropagation(); Dirty(); break;
                case KeyCode.DownArrow:  NudgeSelectedPivot(0, -1); e.StopPropagation(); Dirty(); break;
                case KeyCode.UpArrow:    NudgeSelectedPivot(0, 1);  e.StopPropagation(); Dirty(); break;
            }
        }

        /// True while a text field owns focus — key nudges and the snapshot undo must not hijack typing.
        private bool IsEditingText()
        {
            if (EditorGUIUtility.editingTextField) return true;
            var f = rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }

        /// <summary>Move the primary selection <paramref name="delta"/> steps through the flattened sprite list
        /// (wraps), replacing the selection with the single landed sprite.</summary>
        private void StepSelection(int delta)
        {
            var flat = FlattenCells();
            if (flat.Count == 0) return;
            int idx = flat.FindIndex(cr => cr.region == _selRegion && cr.cell == _selCell);
            idx = idx < 0 ? 0 : (idx + delta % flat.Count + flat.Count) % flat.Count;
            SelectSingle(flat[idx].region, flat[idx].cell);
            Repaint();
        }

        // ── batch ops over the current selection ─────────────────────────────
        private void NudgeSelectedPivot(int signX, int signY)
        {
            if (!HasSelectedCell()) return;
            RecordUndo("Nudge pivot");
            foreach (var cr in SelectedCells())
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                Rect cell = reg.cells[cr.cell];
                Vector2 piv = reg.pivots[cr.cell];
                piv.x += signX / Mathf.Max(1f, cell.width);
                piv.y += signY / Mathf.Max(1f, cell.height);
                reg.pivots[cr.cell] = new Vector2(Mathf.Clamp(piv.x, -1f, 2f), Mathf.Clamp(piv.y, -1f, 2f));
            }
            Repaint();
        }

        private void BaselineSelected()
        {
            var px = GetPixels();
            if (px == null) { _status = "Texture not readable."; return; }
            RecordUndo("Baseline pivot");
            var key = CurrentColorKey();
            int n = 0;
            foreach (var cr in SelectedCells())
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                if (RegionSlicer.ContentBaselinePivot(px, _texW, _texH, reg.cells[cr.cell], _alphaThreshold, out Vector2 piv, key))
                { reg.pivots[cr.cell] = new Vector2(Mathf.Clamp01(piv.x), Mathf.Clamp01(piv.y)); n++; }
            }
            _status = $"Baseline set on {n} sprite(s).";
            Repaint();
        }

        private void TopCenterSelected()
        {
            var px = GetPixels();
            if (px == null) { _status = "Texture not readable."; return; }
            RecordUndo("Head pivot");
            var key = CurrentColorKey();
            int n = 0;
            foreach (var cr in SelectedCells())
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                if (RegionSlicer.ContentTopPivot(px, _texW, _texH, reg.cells[cr.cell], _alphaThreshold, out Vector2 piv, key))
                { reg.pivots[cr.cell] = new Vector2(Mathf.Clamp01(piv.x), Mathf.Clamp01(piv.y)); n++; }
            }
            _status = $"Head pivot set on {n} sprite(s).";
            Repaint();
        }

        private void TrimSelected()
        {
            if (!HasSelectedCell()) return;
            RecordUndo("Trim sprite");
            int n = 0;
            foreach (var cr in SelectedCells())
            {
                var reg = _regions[cr.region];
                Rect t = TrimCell(reg.cells[cr.cell], out bool empty);
                if (!empty) { reg.cells[cr.cell] = t; n++; }
            }
            _status = $"Trimmed {n} sprite(s) to content.";
            Repaint();
        }

        private void AddSelectedToSequence()
        {
            foreach (var cr in SelectedCells()) AppendToSequence(cr.region, cr.cell);
            Repaint();
        }

        private void ClearAllCells()
        {
            RemapSequence(System.Array.Empty<int>(), "Clear all sprites");
            _regions.Clear(); ClearThumbCache();
            _frameZero = new CellRef(-1, -1); _showingFrameZero = false;   // its sprite is gone too
            ClearSelection();
            _seqSelected = -1; _animFrame = 0;
            _status = "Cleared all sprites and the sequence.";
            Repaint();
        }

        /// <summary>Delete every selected cell, PRESERVING the sequence: only the frames that referenced a
        /// deleted sprite are dropped; every surviving frame is remapped to its sprite's shifted (region, cell)
        /// index. Empty regions are removed and their indices remapped too.</summary>
        private void DeleteSelectedCells()
        {
            var toDelete = SelectedCells();
            if (toDelete.Count == 0) return;
            var del = new HashSet<long>();
            foreach (var cr in toDelete) del.Add(PackCell(cr.region, cr.cell));
            int originalFrameCount = _sequence.Count;
            RemapSequence(Enumerable.Range(0, _sequence.Count)
                .Where(i => SeqRefValid(_sequence[i]) && !del.Contains(PackCell(_sequence[i].region, _sequence[i].cell))).ToList(), "Delete sprite");

            // Compact each region's cells, recording old→new cell index (−1 = deleted).
            var cellMap = new Dictionary<int, int[]>(); // region → old cell index → new index
            for (int r = 0; r < _regions.Count; r++)
            {
                var reg = _regions[r]; reg.SyncPivots(GlobalPivot());
                var map = new int[reg.cells.Count];
                var keepCells = new List<Rect>();
                var keepPivots = new List<Vector2>();
                var keepXf = new List<CellTransform>();
                for (int c = 0; c < reg.cells.Count; c++)
                {
                    if (del.Contains(PackCell(r, c))) { map[c] = -1; continue; }
                    map[c] = keepCells.Count;
                    keepCells.Add(reg.cells[c]); keepPivots.Add(reg.pivots[c]); keepXf.Add(reg.transforms[c]);
                }
                reg.cells = keepCells; reg.pivots = keepPivots; reg.transforms = keepXf;
                cellMap[r] = map;
            }

            // Drop now-empty regions, recording old→new region index (−1 = removed).
            var regionMap = new int[_regions.Count];
            var kept = new List<Region>();
            for (int r = 0; r < _regions.Count; r++)
            {
                if (_regions[r].cells.Count == 0) { regionMap[r] = -1; continue; }
                regionMap[r] = kept.Count; kept.Add(_regions[r]);
            }
            _regions.Clear(); _regions.AddRange(kept);

            // Remap the sequence: drop frames whose sprite was deleted, reindex the survivors.
            var newSeq = new List<CellRef>();
            foreach (var cr in _sequence)
            {
                if (cr.region < 0 || cr.region >= regionMap.Length) continue;
                int nr = regionMap[cr.region];
                if (nr < 0 || !cellMap.TryGetValue(cr.region, out var m) || cr.cell < 0 || cr.cell >= m.Length) continue;
                int nc = m[cr.cell];
                if (nc < 0) continue;
                newSeq.Add(new CellRef(nr, nc) { pct = cr.pct });
            }
            int removedFrames = originalFrameCount - newSeq.Count;
            _sequence.Clear(); _sequence.AddRange(newSeq);

            // Frame 0 follows its sprite to the new indices, or is cleared with it.
            if (_frameZero.region >= 0 && _frameZero.region < regionMap.Length && regionMap[_frameZero.region] >= 0
                && cellMap.TryGetValue(_frameZero.region, out var leadMap) && _frameZero.cell >= 0
                && _frameZero.cell < leadMap.Length && leadMap[_frameZero.cell] >= 0)
                _frameZero = new CellRef(regionMap[_frameZero.region], cellMap[_frameZero.region][_frameZero.cell]) { pct = _frameZero.pct };
            else { _frameZero = new CellRef(-1, -1); _showingFrameZero = false; }

            // Repair selections.
            ClearSelection();
            _seqMultiSel.Clear();
            if (_seqSelected >= _sequence.Count) _seqSelected = _sequence.Count - 1;
            if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected);
            _seqAnchor = _seqSelected;
            _animFrame = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : 0;

            ClearThumbCache(); // cell indices shifted — drop cached tiles so they rebind to the right cells
            _status = removedFrames > 0
                ? $"Deleted {toDelete.Count} sprite(s); removed {removedFrames} frame(s) that used them — rest of the sequence kept."
                : $"Deleted {toDelete.Count} sprite(s); sequence unaffected.";
            Repaint();
        }

        // ── context menus ────────────────────────────────────────────────────
        private void ShowSpriteContextMenu(CellRef cr)
        {
            var sel = SelectedCells();
            bool multi = sel.Count > 1 && _multiSel.Contains(PackCell(cr.region, cr.cell));
            var menu = new GenericMenu();
            if (multi)
            {
                menu.AddItem(new GUIContent($"Add {sel.Count} to sequence"), false, () => { AddSelectedToSequence(); Refresh(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Trim selected to content"), false, () => { TrimSelected(); Refresh(); });
                menu.AddItem(new GUIContent("Baseline selected"), false, () => { BaselineSelected(); Refresh(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Delete {sel.Count} sprites"), false, () => { DeleteSelectedCells(); Refresh(); });
            }
            else
            {
                menu.AddItem(new GUIContent("Add to sequence"), false, () => { AppendToSequence(cr.region, cr.cell); Refresh(); });
                menu.AddItem(new GUIContent("Use as frame 0"), false, () => SetFrameZero(cr.region, cr.cell));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Trim to content"), false, () => { SelectSingle(cr.region, cr.cell); TrimSelected(); Refresh(); });
                menu.AddItem(new GUIContent("Set baseline pivot"), false, () => { SelectSingle(cr.region, cr.cell); BaselineSelected(); Refresh(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete sprite"), false, () => { SelectSingle(cr.region, cr.cell); DeleteSelectedCells(); Refresh(); });
            }
            menu.ShowAsContext();
        }

        private void ShowSequenceContextMenu(int i)
        {
            if (i < 0 || i >= _sequence.Count) return;
            CellRef cr = _sequence[i];
            int selCount = _seqMultiSel.Count;
            var menu = new GenericMenu();
            if (selCount > 1)
            {
                menu.AddItem(new GUIContent($"Reverse {selCount} selected frames"), false, () => { ReverseSelectedFrames(); Refresh(); });
                menu.AddItem(new GUIContent($"Duplicate {selCount} selected"), false, () => { DuplicateSelectedFrames(); Refresh(); });
                menu.AddItem(new GUIContent($"Remove {selCount} selected"), false, () => { DeleteSelectedFrames(); Refresh(); });
            }
            else
            {
                menu.AddItem(new GUIContent("Duplicate frame"), false, () => { SeqSelectSingle(i); DuplicateSelectedFrames(); Refresh(); });
                menu.AddItem(new GUIContent("Remove frame"), false, () => { SeqSelectSingle(i); DeleteSelectedFrames(); Refresh(); });
            }
            menu.AddSeparator("");
            if (SeqRefValid(cr))
                menu.AddItem(new GUIContent("Edit source sprite (select in #4)"), false, () => { SelectSingle(cr.region, cr.cell); Refresh(); });
            if (SeqRefValid(cr))
                menu.AddItem(new GUIContent("Use as frame 0"), false, () => SetFrameZero(cr.region, cr.cell));
            menu.ShowAsContext();
        }

        // ── registration: frame box + drag-to-place ──────────────────────────
        // Registration SETTINGS only (the canvas/preview is drawn separately, to its left). See DrawCellsPreview.
        private static readonly string[] RegistrationModeLabels = { "Auto size", "Fixed box" };


        /// The registration stage — an IMGUI island: onion-skinned frame painting plus a drag-to-place gizmo.
        private void DrawRegistrationCanvasGUI()
        {
            if (_regCanvasIM == null) return;
            var canvas = new Rect(0f, 0f, _regCanvasIM.layout.width, _regCanvasIM.layout.height);
            if (!(canvas.width > 10f) || !(canvas.height > 10f)) return;
            DrawRegistrationCanvas(canvas);
        }

        private void DrawRegistrationCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.10f, 0.10f, 0.10f));
            DrawRectOutline(canvas, new Color(1f, 1f, 1f, 0.18f), 1f);
            if (_sheet == null) return;
            EnsurePreviewBake(); // align against the SAME baked frames the play preview & game use

            HandlePreviewPan(canvas);
            GUI.BeginClip(canvas);
            try
            {
            canvas = new Rect(0, 0, canvas.width, canvas.height);

            // The view box (and its registration anchor) follows the active mode.
            int vw, vh; float anchorNX, anchorNY;
            if (_fixedFrame)
            {
                vw = Mathf.Max(1, _frameW); vh = Mathf.Max(1, _frameH);
                anchorNX = _framePivot.x; anchorNY = _framePivot.y;
            }
            else
            {
                ComputeAutoBox(out int fw, out int fh, out int ax, out int ay);
                vw = fw; vh = fh; anchorNX = (float)ax / fw; anchorNY = (float)ay / fh;
            }

            Rect visibleBounds = new Rect(-anchorNX * vw, -(1f - anchorNY) * vh, vw, vh);
            visibleBounds = FramePreview.Union(visibleBounds, FramePreview.RelativeBounds(_previewFrames));
            if (HasSelectedCell())
            {
                // Include the selected unsequenced sprite, including its rotation/scale and unusual pivot.
                EnsureSelectedXform();
                if (_selXformTex != null)
                    visibleBounds = FramePreview.Union(visibleBounds, new Rect(-_selXformPivot.x * _selXformW,
                        -(1f - _selXformPivot.y) * _selXformH, _selXformW, _selXformH));
            }
            Vector2 fitAnchor = new Vector2(canvas.width * 0.5f, canvas.height * 0.78f);
            float scale = PreviewStageScale(canvas, visibleBounds, fitAnchor);
            float vwS = vw * scale, vhS = vh * scale;
            // FIXED registration point: the crosshair must NOT move when a pivot is nudged. It used to be derived
            // from the auto-box (which is computed FROM the pivots), so nudging the frame that defines the box's
            // extent shifted the box with it and the sprite looked pinned (the "← → don't nudge" bug). Pin the
            // crosshair to the canvas and let the guide box float around it instead.
            float crossX = Mathf.Round(fitAnchor.x + _stagePan.x);
            float crossY = Mathf.Round(fitAnchor.y + _stagePan.y);
            float bx0 = Mathf.Round(crossX - anchorNX * vwS);
            float by0 = Mathf.Round(crossY - (1f - anchorNY) * vhS);
            DrawRectOutline(new Rect(bx0, by0, vwS, vhS), new Color(1f, 0.85f, 0.1f, 0.8f), 1f);

            // Onion-skin: neighbouring sequence frames drawn faint, then the selected one solid on top.
            // The window is centred on the current frame's slot in the sequence; if the selected sprite isn't
            // in the sequence yet there's no anchor, so every frame ghosts (helps align before sequencing).
            // Prefer the BAKED frames (uniform size + shared pivot) — identical to UI.5 and the game, so the
            // overlay can't wobble. Frame s of the sequence is _previewFrames[s]. Fall back to the raw cell only
            // for a selected sprite that isn't in the sequence yet (no baked frame exists for it).
            bool baked = _previewFrames != null && _previewFrames.Count == _sequence.Count;

            // Ghost counts of 0/0 mean OFF, unconditionally. They used to be advisory: the window check below
            // was gated on having an anchor, so with nothing selected it was skipped and EVERY frame ghosted
            // no matter what the counts said — turning them down did nothing.
            if (_ghostOpacity > 0.001f && (_ghostBefore > 0 || _ghostAfter > 0))
            {
                // No selection = no anchor. Centre the onion-skin on the PLAYHEAD rather than falling back to
                // "ghost the whole sequence": on a 3-frame walk cycle that fallback was harmless, but on a
                // 16-direction rotation sheet it superimposes all 16 poses into an unreadable smear — which is
                // what the Builder showed on OPEN (nothing is selected yet) for every rotation-sheet asset.
                int anchor = AnchorSequenceIndex();
                if (anchor < 0) anchor = ActiveFrameIndex();
                for (int s = 0; s < _sequence.Count; s++)
                {
                    var cr = _sequence[s];
                    if (cr.region == _selRegion && cr.cell == _selCell) continue;
                    if (!SeqRefValid(cr)) continue;
                    if (anchor >= 0 && (s - anchor < -_ghostBefore || s - anchor > _ghostAfter)) continue;
                    if (baked)
                        FramePreview.DrawSpriteAtAnchor(_previewFrames[s], crossX, crossY, scale, _ghostOpacity);
                    else
                    {
                        _regions[cr.region].SyncPivots(GlobalPivot());
                        DrawCellAtAnchor(_regions[cr.region].cells[cr.cell], _regions[cr.region].pivots[cr.cell], crossX, crossY, scale, _ghostOpacity, _regions[cr.region].sourceTextureGuid);
                    }
                }
            }
            if (HasSelectedCell())
            {
                int seqIdx = AnchorSequenceIndex();
                var reg = _regions[_selRegion]; reg.SyncPivots(GlobalPivot());
                if (baked && seqIdx >= 0)
                    FramePreview.DrawSpriteAtAnchor(_previewFrames[seqIdx], crossX, crossY, scale, 1f);
                else if (!reg.transforms[_selCell].IsIdentity)
                    DrawSelectedTransformed(crossX, crossY, scale);   // not sequenced but edited — show the edit
                else
                    DrawCellAtAnchor(reg.cells[_selCell], reg.pivots[_selCell], crossX, crossY, scale, 1f, reg.sourceTextureGuid);
            }
            else if (baked)
            {
                // Nothing selected: show the frame the playhead is on, solid. Registration is judged against
                // ONE frame sitting on the crosshair — so with no selection the useful default is the current
                // frame, not an empty stage (and certainly not every frame at once, which is what this drew
                // before). Selecting a sprite still takes over exactly as before.
                int f = ActiveFrameIndex();
                if (f >= 0 && f < _previewFrames.Count)
                    FramePreview.DrawSpriteAtAnchor(_previewFrames[f], crossX, crossY, scale, 1f);
            }

            // Registration crosshair.
            var cross = new Color(0.2f, 1f, 0.5f, 0.9f);
            EditorGUI.DrawRect(new Rect(crossX - 8, crossY - 0.5f, 16, 1f), cross);
            EditorGUI.DrawRect(new Rect(crossX - 0.5f, crossY - 8, 1f, 16), cross);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && canvas.Contains(Event.current.mousePosition)) PausePreview();
            HandleRegistrationDrag(canvas, scale);
            }
            finally { GUI.EndClip(); }
        }

        private void DrawCellAtAnchor(Rect cell, Vector2 pivot, float cx, float cy, float scale, float alpha, string textureGuid = null)
            => DrawFrameRegistered(cell, pivot, cx, cy, scale, alpha, textureGuid);

        // Draw the selected sprite WITH its transform applied (cached texture), anchored by the mapped pivot.
        private void DrawSelectedTransformed(float cx, float cy, float scale)
        {
            EnsureSelectedXform();
            if (_selXformTex == null) return;
            float w = _selXformW * scale, h = _selXformH * scale;
            Rect draw = new Rect(Mathf.Round(cx - _selXformPivot.x * w), Mathf.Round(cy - (1f - _selXformPivot.y) * h), w, h);
            GUI.DrawTexture(draw, _selXformTex, ScaleMode.StretchToFill, true);
        }

        private void EnsureSelectedXform()
        {
            if (!HasSelectedCell()) return;
            var reg = _regions[_selRegion]; reg.SyncPivots(GlobalPivot());
            var t = reg.transforms[_selCell];
            var key = CurrentColorKey();
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + _selRegion; hash = hash * 31 + _selCell;
                hash = hash * 31 + reg.cells[_selCell].GetHashCode();
                hash = hash * 31 + reg.pivots[_selCell].GetHashCode();
                hash = hash * 31 + TransformHash(t);
                hash = hash * 31 + (_bgKeyEnabled ? 1 : 0);
                hash = hash * 31 + (_bgKey.r << 24 | _bgKey.g << 16 | _bgKey.b << 8 | _bgKey.a);
                hash = hash * 31 + _bgTolerance;
            }
            if (hash == _selXformHash && _selXformTex != null) return;
            _selXformHash = hash;
            if (_selXformTex != null) { Object.DestroyImmediate(_selXformTex); _selXformTex = null; }

            bool foreign = !string.IsNullOrEmpty(reg.sourceTextureGuid) && reg.sourceTextureGuid != CurrentSheetGuid();
            var px = foreign ? GetPixelsFor(reg.sourceTextureGuid) : GetPixels();
            if (px == null) return;
            var srcTex = foreign ? ResolveTexture(reg.sourceTextureGuid) : null;
            int tw = foreign ? srcTex.width : _texW, th = foreign ? srcTex.height : _texH;
            var block = AtlasBaker.TransformCell(px, tw, th, reg.cells[_selCell], t, key, reg.pivots[_selCell],
                out int w, out int h, out Vector2 pvN);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(block); tex.Apply();
            _selXformTex = tex; _selXformW = w; _selXformH = h; _selXformPivot = pvN;
        }

        /// <summary>The ONE frame visualiser for this window (registration canvas, onion-skin, AND playback).
        /// It registers a frame by the EXACT math <see cref="AtlasBaker"/> bakes with — trim to content, snap
        /// the pivot→content offset to whole SOURCE pixels — so what you align here is pixel-identical to the
        /// baked atlas the Lauminary Browser previews. Previously this positioned in screen space without the
        /// source-pixel snap, so the editor showed a sub-pixel drift the bake didn't have (un-nudge-able).</summary>
        private void DrawFrameRegistered(Rect cell, Vector2 pivot, float cx, float cy, float scale, float alpha, string textureGuid = null)
        {
            bool foreign = !string.IsNullOrEmpty(textureGuid) && textureGuid != CurrentSheetGuid();
            var px = foreign ? GetPixelsFor(textureGuid) : GetPixels();
            if (px == null) return;
            var srcTex = foreign ? ResolveTexture(textureGuid) : null;
            float tw = foreign ? srcTex.width : _texW, th = foreign ? srcTex.height : _texH;

            AtlasBaker.FrameRegistration(px, (int)tw, (int)th, cell, pivot, CurrentColorKey(), out Rect b, out Vector2 off);
            int offX = AtlasBaker.SnapOffset(off.x), offY = AtlasBaker.SnapOffset(off.y); // consistent round-half-up — same as the bake
            float w = b.width * scale, h = b.height * scale;
            // Crosshair (cx,cy) IS the pivot. Content sits offX right / offY up from it (source px → screen,
            // y inverted). Round to whole screen pixels so an integer zoom stays crisp on the pixel grid.
            Rect draw = new Rect(Mathf.Round(cx - offX * scale), Mathf.Round(cy - (b.height - offY) * scale), w, h);
            Rect uv = new Rect(b.x / tw, b.y / th, b.width / tw, b.height / th);
            Color prev = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(draw, foreign ? srcTex : SheetForDisplay(), uv, true);
            GUI.color = prev;
        }

        private void HandleRegistrationDrag(Rect canvas, float scale)
        {
            if (_spriteTool != SpriteTool.Align || !HasSelectedCell()) return;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                _registrationUndoRecorded = false;
                _regDragging = true; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _regDragging)
            {
                if (!_registrationUndoRecorded) { RecordUndo("Align sprite"); _registrationUndoRecorded = true; }
                var reg = _regions[_selRegion]; reg.SyncPivots(GlobalPivot());
                Rect cell = reg.cells[_selCell];
                float wScreen = Mathf.Max(1f, cell.width * scale), hScreen = Mathf.Max(1f, cell.height * scale);
                Vector2 piv = reg.pivots[_selCell];
                piv.x -= e.delta.x / wScreen;   // drag right → sprite right → pivot.x down
                piv.y += e.delta.y / hScreen;   // drag down  → sprite down  → pivot.y up
                reg.pivots[_selCell] = new Vector2(Mathf.Clamp(piv.x, -1f, 2f), Mathf.Clamp(piv.y, -1f, 2f));
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && _regDragging)
            {
                _regDragging = false; e.Use(); if (_registrationUndoRecorded) Dirty();
            }
        }

        /// <summary>Auto frame box for the current sequence: the smallest box holding every frame's trimmed
        /// content around the shared registration anchor — the same math the auto-size bake uses.</summary>
        private void ComputeAutoBox(out int fw, out int fh, out int anchorX, out int anchorY)
        {
            // The baker owns trimming, transformed pivots and mixed-sheet registration.
            // Reuse its cached geometry during repaints; explicit Fit frame can request a fresh auto composition.
            if (!_fixedFrame && _previewHash == PreviewHash() && _previewFrames != null && _previewFrames.Count > 0 && _previewFrames[0] != null)
            {
                var sprite = _previewFrames[0];
                fw = Mathf.RoundToInt(sprite.rect.width); fh = Mathf.RoundToInt(sprite.rect.height);
                anchorX = Mathf.RoundToInt(sprite.pivot.x); anchorY = Mathf.RoundToInt(sprite.pivot.y);
                return;
            }
            var recipe = BuildRecipe();
            if (recipe.Count == 0 && HasSelectedCell()) recipe.Add(FrameRefOf(new CellRef(_selRegion, _selCell)));
            if (AtlasBaker.Compose(recipe, CurrentColorKey(), default, out var composition, out _))
            {
                fw = composition.fw; fh = composition.fh;
                anchorX = AtlasBaker.SnapOffset(composition.uniformPivot.x * fw);
                anchorY = AtlasBaker.SnapOffset(composition.uniformPivot.y * fh);
                return;
            }
            fw = fh = 1; anchorX = anchorY = 0;
        }

        private void FitFrameBox()
        {
            ComputeAutoBox(out int fw, out int fh, out int ax, out int ay);
            _frameW = fw; _frameH = fh;
            _framePivot = new Vector2((float)ax / fw, (float)ay / fh);
        }

        // ── 5 · animation (sequence + preview + save) ────────────────────────
        private static readonly string[] LoopDividerLabels = { "None", "Pause" };


        // The tools to the RIGHT of the animation preview: play, fps, loop gap, frame ops, meta toggle.

        /// The play box / meta paint editor — an IMGUI island either way (pivot-anchored atlas blits, and a
        /// zoomable per-pixel paint surface with pan).
        private void DrawPlayAreaGUI()
        {
            if (_playIM == null) return;
            var box = new Rect(0f, 0f, _playIM.layout.width, _playIM.layout.height);
            if (!(box.width > 10f) || !(box.height > 10f)) return;
            if (_workspaceMode == WorkspaceMode.Animate && _animateTool == AnimateTool.Layers && _metaEnabled) DrawMetaEditor(box); else DrawPlayBox(box);
        }

        /// <summary>Drag-resize grip in the preview/paint window's bottom-right corner.</summary>

        // ── Meta-layers: data sync + UI + paint editor ──────────────────────
        private MetaLayer ActiveLayer() => (_activeLayer >= 0 && _activeLayer < _metaLayers.Count) ? _metaLayers[_activeLayer] : null;

        /// <summary>Keep each layer's frame list aligned 1:1 with the sequence (pad/trim at the end).</summary>
        private void SyncMetaFrames()
        {
            int n = _sequence.Count;
            foreach (var L in _metaLayers)
            {
                if (L.frames == null) L.frames = new List<MetaFrame>();
                while (L.frames.Count < n) L.frames.Add(new MetaFrame());
                while (L.frames.Count > n) L.frames.RemoveAt(L.frames.Count - 1);

                // Vector mode's per-frame data lives in its own parallel list (VectorMetaFrame, not the
                // pixel-mask MetaFrame above) — kept in lockstep the same way, regardless of the layer's
                // CURRENT mode, so switching a layer's mode later doesn't need a separate backfill pass.
                if (L.vectorFrames == null) L.vectorFrames = new List<VectorMetaFrame>();
                while (L.vectorFrames.Count < n) L.vectorFrames.Add(new VectorMetaFrame { authored = false });
                while (L.vectorFrames.Count > n) L.vectorFrames.RemoveAt(L.vectorFrames.Count - 1);
            }
            if (_activeLayer >= _metaLayers.Count) _activeLayer = _metaLayers.Count - 1;
        }

        private bool BakedFrameSize(int f, out int w, out int h)
        {
            w = h = 0;
            if (_previewFrames != null && f >= 0 && f < _previewFrames.Count && _previewFrames[f] != null)
            {
                var r = _previewFrames[f].rect;
                w = Mathf.Max(1, Mathf.RoundToInt(r.width)); h = Mathf.Max(1, Mathf.RoundToInt(r.height));
                return true;
            }
            return false;
        }

        private static readonly string[] BrushLabels = { "1×1", "1×2", "2×1", "2×2" };
        private static readonly Vector2Int[] BrushSizes =
            { new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(2, 1), new Vector2Int(2, 2) };
        private static readonly string[] PaintValueLabels =
            { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };


        /// Vector-mode's side-panel controls. The actual drawing surface is NOT here — it's the same preview
        /// canvas (DrawMetaEditor/HandleVectorInput) Shape/Point paint into; this is just this mode's options +
        /// readout, same shape as the Brush/Values controls Shape/Point show above.
        /// <summary>Zoom + recentre for the paint view. Shared by EVERY layer mode: Vector needs it at least as
        /// much as Shape/Point does, since placing a muzzle origin is pixel-precise work on a sprite that is
        /// only tens of pixels across. (It used to live inline in the Shape/Point path only, below
        /// <see cref="BuildVectorLayerUI"/>'s early return — so a Vector layer had no way to zoom or recentre
        /// at all, and a stray middle-drag pan left the sprite off-view with nothing to undo it.)</summary>


        private void ClearActiveFrame()
        {
            var L = ActiveLayer(); if (L == null) return;
            int f = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : -1;
            if (f < 0 || f >= L.frames.Count) return;
            var mf = L.frames[f]; if (mf == null || mf.cells == null) return;
            RecordUndo("Clear frame");
            System.Array.Clear(mf.cells, 0, mf.cells.Length);
            ClearMaskCache();
        }

        private int ActiveFrameIndex() => _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : -1;

        /// <summary>Paste <see cref="_metaFrameClipboard"/> onto <paramref name="L"/>'s frame at <paramref
        /// name="frameIndex"/>, resampling to that frame's own baked size (nearest-neighbor, via MetaFrame's
        /// own EnsureSize) so copying between frames of different dimensions degrades gracefully instead of
        /// corrupting the grid.</summary>
        private void PasteMetaFrameInto(MetaLayer L, int frameIndex)
        {
            if (_metaFrameClipboard == null || L == null) return;
            if (frameIndex < 0 || frameIndex >= L.frames.Count) return;
            if (!BakedFrameSize(frameIndex, out int fw, out int fh)) return;
            var pasted = _metaFrameClipboard.Clone();
            pasted.EnsureSize(fw, fh);
            L.frames[frameIndex] = pasted;
        }

        /// <summary>The preview box turned into a zoomable paint/mark editor for the active layer on the
        /// current (scrubbed) frame: baked sprite underneath, then whatever the layer's MODE calls for on top —
        /// Shape = a multi-cell brush mask, Point = a single movable marker, Vector = an origin+arrow. A layer's
        /// mode is a hard gate here, not just a hint in the side panel: a Vector layer never lets you paint
        /// pixels, and a Point layer never lets you paint more than one.</summary>
        private void DrawMetaEditor(Rect box)
        {
            EditorGUI.DrawRect(box, new Color(0.08f, 0.08f, 0.08f));
            DrawRectOutline(box, new Color(1f, 1f, 1f, 0.18f), 1f);
            if (_sequence.Count == 0)
            { GUI.Label(box, "Add sprites to the sequence", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter }); return; }

            EnsurePreviewBake();
            int f = Mathf.Clamp(_animFrame, 0, _sequence.Count - 1);
            if (!BakedFrameSize(f, out int fw, out int fh))
            { GUI.Label(box, "Baking…", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter }); return; }

            var layer = ActiveLayer();
            bool isVector = layer != null && layer.mode == MetaLayerMode.Vector;
            MetaFrame mf = null;
            if (!isVector && layer != null && f < layer.frames.Count) mf = layer.frames[f];

            float z = _metaZoom > 0 ? _metaZoom : Mathf.Min((box.width - 16) / fw, (box.height - 16) / fh);
            z = Mathf.Max(.01f, z);
            float dw = fw * z, dh = fh * z;
            GUI.BeginClip(box);
            float ox = Mathf.Round((box.width - dw) * 0.5f + _metaPan.x), oy = Mathf.Round((box.height - dh) * 0.5f + _metaPan.y);

            var sp = _previewFrames[f];
            if (sp != null && sp.texture != null)
            {
                var t = sp.texture; if (t.filterMode != FilterMode.Point) t.filterMode = FilterMode.Point;
                Rect uv = new Rect(sp.rect.x / t.width, sp.rect.y / t.height, sp.rect.width / t.width, sp.rect.height / t.height);
                var pc = GUI.color; GUI.color = new Color(1f, 1f, 1f, (mf != null || isVector) ? 0.6f : 1f); // dim under the overlay
                GUI.DrawTextureWithTexCoords(new Rect(ox, oy, dw, dh), t, uv, true);
                GUI.color = pc;
            }
            if (mf != null)
            {
                var mtex = MaskTexture(_activeLayer, f);
                if (mtex != null) GUI.DrawTexture(new Rect(ox, oy, dw, dh), mtex, ScaleMode.StretchToFill, true);
                HandleMetaPaint(box, ox, oy, z, fw, fh, mf, layer.mode == MetaLayerMode.Point);
            }
            else if (isVector && f < layer.vectorFrames.Count)
            {
                var vf = layer.vectorFrames[f];
                DrawVectorOverlay(box, ox, oy, z, fw, fh, layer, vf);
                HandleVectorInput(box, ox, oy, z, fw, fh, layer, vf);
            }
            GUI.EndClip();

            GUI.Label(new Rect(box.x + 4, box.yMax - 16, 320, 16),
                layer != null ? $"Editing '{layer.id}' · frame {f + 1}/{_sequence.Count}" : "Add a layer below to draw.",
                EditorStyles.whiteMiniLabel);
        }

        /// <param name="singlePoint">Point mode: every paint stroke first clears the mask, so there's never more
        /// than one marked cell — placing a new one just moves the old one, it never accumulates a shape.</param>
        private void HandleMetaPaint(Rect box, float ox, float oy, float z, int fw, int fh, MetaFrame mf, bool singlePoint)
        {
            var e = Event.current;
            Vector2 m = e.mousePosition; // local to the clip
            bool insideBox = m.x >= 0 && m.y >= 0 && m.x < box.width && m.y < box.height;

            // Middle-drag pans the view (so you can reach the edges while zoomed in).
            if (e.type == EventType.MouseDown && e.button == 2 && insideBox)
            { _metaPanning = true; _metaPanLast = m; e.Use(); }
            else if (e.type == EventType.MouseDrag && _metaPanning)
            { _metaPan += m - _metaPanLast; _metaPanLast = m; e.Use(); Repaint(); }
            else if (e.type == EventType.MouseUp && e.button == 2 && _metaPanning)
            { _metaPanning = false; e.Use(); }

            int cx = Mathf.FloorToInt((m.x - ox) / z);
            int cy = fh - 1 - Mathf.FloorToInt((m.y - oy) / z); // screen top-down → grid bottom-up
            bool inCell = insideBox && cx >= 0 && cx < fw && cy >= 0 && cy < fh;

            if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1) && inCell)
            {
                PausePreview(); RecordUndo("Paint"); mf.EnsureSize(fw, fh);
                if (singlePoint)
                {
                    if (mf.cells != null) System.Array.Clear(mf.cells, 0, mf.cells.Length);
                    if (e.button == 0 && !e.control) mf.Set(cx, cy, _paintValue);
                }
                else PaintBrush(mf, cx, cy, fw, fh, (e.button == 1 || e.control) ? 0 : _paintValue);
                _metaPainting = true; _metaLastX = cx; _metaLastY = cy; e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && _metaPainting && inCell)
            {
                if (cx != _metaLastX || cy != _metaLastY)
                {
                    if (singlePoint)
                    {
                        if (mf.cells != null) System.Array.Clear(mf.cells, 0, mf.cells.Length);
                        if (e.button == 0 && !e.control) mf.Set(cx, cy, _paintValue); // moves the one point
                    }
                    else PaintBrush(mf, cx, cy, fw, fh, (e.button == 1 || e.control) ? 0 : _paintValue);
                    _metaLastX = cx; _metaLastY = cy;
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && _metaPainting) { _metaPainting = false; e.Use(); Dirty(); }
        }

        /// <summary>Draws Vector mode's origin dot + aim arrow directly on the preview canvas, in the layer's
        /// own colour — the same window Shape/Point paint into, not a separate box. The drawing and the gestures
        /// are the shared <see cref="ZuiVectorMarker"/> (Pyre's anchor uses the same one).</summary>
        private void DrawVectorOverlay(Rect box, float ox, float oy, float z, int fw, int fh, MetaLayer layer, VectorMetaFrame vf)
        {
            var canvas = VectorCanvas(box, ox, oy, z, fw, fh);
            var data = VectorData(vf);
            ZuiVectorMarker.Draw(canvas, data, VectorOptions(layer));
        }

        // Mouse positions inside GUI.BeginClip are clip-local, so the canvas box is the clip's own extent.
        private static ZuiVectorMarker.Canvas VectorCanvas(Rect box, float ox, float oy, float z, int fw, int fh)
            => new ZuiVectorMarker.Canvas(new Rect(0f, 0f, box.width, box.height), ox, oy, z, fw, fh);

        private static ZuiVectorMarker.Data VectorData(VectorMetaFrame vf)
            => new ZuiVectorMarker.Data { authored = vf.authored, origin01 = vf.origin, direction = vf.direction, length = vf.length };

        private static ZuiVectorMarker.Options VectorOptions(MetaLayer layer)
            => new ZuiVectorMarker.Options
            {
                arrow = true,
                length = layer.vectorAllowLength,
                erase = true,
                // 0 = the marker snaps nothing, which is what every layer that hasn't opted in reports.
                angleSnapDivisions = layer.vectorSnapAngle ? Mathf.Clamp(layer.vectorSnapDivisions, 2, 64) : 0,
                color = layer.color
            };

        /// <summary>Click-to-place, drag-the-dot-to-move, drag-the-arrowhead-to-aim, right-click-to-erase — all
        /// on the same preview canvas Shape/Point paint into. No pixel mask involved for Vector mode.</summary>
        private void HandleVectorInput(Rect box, float ox, float oy, float z, int fw, int fh, MetaLayer layer, VectorMetaFrame vf)
        {
            var e = Event.current;
            Vector2 m = e.mousePosition; // local to the clip
            bool insideBox = m.x >= 0 && m.y >= 0 && m.x < box.width && m.y < box.height;

            // Middle-drag pans, same as Shape/Point.
            if (e.type == EventType.MouseDown && e.button == 2 && insideBox)
            { _metaPanning = true; _metaPanLast = m; e.Use(); return; }
            else if (e.type == EventType.MouseDrag && _metaPanning)
            { _metaPan += m - _metaPanLast; _metaPanLast = m; e.Use(); Repaint(); return; }
            else if (e.type == EventType.MouseUp && e.button == 2 && _metaPanning)
            { _metaPanning = false; e.Use(); return; }

            var canvas = VectorCanvas(box, ox, oy, z, fw, fh);
            var data = VectorData(vf);
            bool changed = ZuiVectorMarker.Handle(canvas, ref data, VectorOptions(layer), ref _vecDragMode, RecordUndo);
            if (!changed) return;
            vf.authored = data.authored; vf.origin = data.origin01; vf.direction = data.direction; vf.length = data.length;
            Dirty(); Repaint();
        }

        /// <summary>Stamp the brush footprint (anchored at cx,cy, growing right/up) into the mask.</summary>
        private void PaintBrush(MetaFrame mf, int cx, int cy, int fw, int fh, int value)
        {
            for (int dy = 0; dy < _brushH; dy++)
                for (int dx = 0; dx < _brushW; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < fw && y >= 0 && y < fh) mf.Set(x, y, value);
                }
        }

        /// <summary>Cached point-filtered texture of a layer-frame's value mask (bottom-left origin, so it
        /// overlays the baked sprite upright). Rebuilt only when the mask or colour changes.</summary>
        private Texture2D MaskTexture(int layerIndex, int seqIndex)
        {
            if (layerIndex < 0 || layerIndex >= _metaLayers.Count) return null;
            var L = _metaLayers[layerIndex];
            if (seqIndex < 0 || seqIndex >= L.frames.Count) return null;
            var mf = L.frames[seqIndex];
            if (mf == null || mf.cells == null || mf.w <= 0 || mf.h <= 0 || !mf.HasAny()) return null;

            long key = ((long)layerIndex << 40) ^ ((long)(uint)seqIndex << 8);
            int hash;
            unchecked
            {
                hash = 17; hash = hash * 31 + mf.w; hash = hash * 31 + mf.h; hash = hash * 31 + L.color.GetHashCode();
                for (int i = 0; i < mf.cells.Length; i++) hash = hash * 31 + mf.cells[i];
            }
            if (_maskCache.TryGetValue(key, out var e) && e.hash == hash && e.tex != null) return e.tex;
            if (e.tex != null) Object.DestroyImmediate(e.tex);

            var tex = new Texture2D(mf.w, mf.h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[mf.w * mf.h];
            for (int i = 0; i < px.Length; i++) px[i] = MetaLayer.CellColor(L.color, mf.cells[i]);
            tex.SetPixels32(px); tex.Apply();
            _maskCache[key] = new MaskEntry { hash = hash, tex = tex };
            return tex;
        }

        private void ClearMaskCache()
        {
            foreach (var e in _maskCache.Values) if (e.tex != null) Object.DestroyImmediate(e.tex);
            _maskCache.Clear();
        }

        // Author per-frame EVENTS (metadata the game reacts to). "hit" is the canonical one — it lets a
        // consumer sync weapon damage / a projectile to the swing's contact frame via LauminaryPlayer.OnFrameEvent.
        // Frames are shown 1-based to match the sequence strip badges; stored 0-based.

        private void DrawPlayBox(Rect box)
        {
            EditorGUI.DrawRect(box, new Color(0.10f, 0.10f, 0.10f));
            DrawRectOutline(box, new Color(1f, 1f, 1f, 0.18f), 1f);
            if (_sequence.Count == 0)
            {
                GUI.Label(box, "Add sprites to the sequence", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            EnsurePreviewBake();
            HandlePreviewPan(box);
            GUI.BeginClip(box);
            try
            {
                box = new Rect(0, 0, box.width, box.height);
                Vector2 anchor = new Vector2(box.width * CrosshairNX, box.height * (1f - CrosshairNY));
                var bounds = FramePreview.RelativeBounds(_previewFrames);
                if (!_phasePreviewMode) bounds = FramePreview.Union(bounds, FramePreview.RelativeBounds(_previewFrameZero));
                float scale = PreviewStageScale(box, bounds, anchor, 8f);
                anchor += _stagePan;
                if (!_inDivider)
                {
                    if (_showingFrameZero && FrameZeroActive && _previewFrameZero != null)
                        FramePreview.DrawSpriteAtAnchor(_previewFrameZero[0], anchor.x, anchor.y, scale, 1f);
                    else if (_previewFrames != null && _previewFrames.Count > 0)
                    {
                        int shown = Mathf.Clamp(_animFrame, 0, _previewFrames.Count - 1);
                        FramePreview.DrawSpriteAtAnchor(_previewFrames[shown], anchor.x, anchor.y, scale, 1f);
                    }
                }
                var cross = new Color(0.2f, 1f, 0.5f, 0.9f);
                EditorGUI.DrawRect(new Rect(anchor.x - 8, anchor.y - 0.5f, 16, 1f), cross);
                EditorGUI.DrawRect(new Rect(anchor.x - 0.5f, anchor.y - 8, 1f, 16), cross);
            }
            finally { GUI.EndClip(); }
        }

        /// The sequence strip — an IMGUI island: a thumbnail grid with zone borders, playhead/selection
        /// outlines, ordinal badges and drag-to-reorder.
        private void DrawSequenceStripGUI()
        {
            if (_seqStripIM == null) return;
            Rect view = new Rect(0f, 0f, _seqStripIM.layout.width, _seqStripIM.layout.height);
            if (!(view.width > 20f)) return;

            const int cell = 46, pad = 4;
            int lead = _frameZeroOn ? 1 : 0;   // slot 0 sits in front of frame 1 while Frame 0 is on
            int perRow = Mathf.Max(1, Mathf.FloorToInt((view.width - 18f) / (cell + pad)));
            int rows = Mathf.CeilToInt((_sequence.Count + lead) / (float)perRow);
            Rect content = new Rect(0, 0, view.width - 18f, Mathf.Max(view.height, rows * (cell + pad)));

            _seqScroll = GUI.BeginScrollView(view, _seqScroll, content);
            if (lead == 1) DrawFrameZeroSlot(new Rect(0, 0, cell, cell));
            for (int i = 0; i < _sequence.Count; i++)
            {
                int slot = i + lead;
                Rect r = new Rect((slot % perRow) * (cell + pad), (slot / perRow) * (cell + pad), cell, cell);
                EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
                DrawCellThumb(r, _sequence[i].region, _sequence[i].cell);
                if (_metaEnabled && _activeLayer >= 0)
                {
                    var mtex = MaskTexture(_activeLayer, i);
                    if (mtex != null) GUI.DrawTexture(r, mtex, ScaleMode.ScaleToFit, true);
                }

                // Zone border: a thick outline in the frame's zone colour (drawn under the selection outline).
                if (TryFrameZone(i, out Color zcol)) DrawRectOutline(r, zcol, 3f);

                bool sel = !_frameZeroSel && IsSeqSelected(i), primary = i == _seqSelected,
                     playing = i == _animFrame && !_inDivider && !_showingFrameZero;
                DrawRectOutline(r, sel ? new Color(1f, 0.85f, 0.1f, 1f) : playing ? new Color(0.2f, 1f, 0.5f, 0.9f) : new Color(1f, 1f, 1f, 0.4f), sel ? (primary ? 2.5f : 1.5f) : playing ? 2f : 1f);
                var badge = new Rect(r.x, r.y, 16, 14);
                EditorGUI.DrawRect(badge, new Color(0.2f, 0.5f, 1f, 0.92f));
                GUI.Label(badge, (i + 1).ToString(), EditorStyles.whiteMiniLabel);

                // Every frame states how long it lasts; a frame whose timing differs from the FPS is orange.
                {
                    float pct = _sequence[i].pct;
                    string msText = FrameMsOf(pct).ToString("0") + "ms";
                    var msSize = EditorStyles.whiteMiniLabel.CalcSize(new GUIContent(msText));
                    var msBadge = new Rect(r.xMax - msSize.x - 2f, r.yMax - 14f, msSize.x + 2f, 14f);
                    EditorGUI.DrawRect(msBadge, pct != 0f ? new Color(0.85f, 0.45f, 0.1f, 0.92f) : new Color(0f, 0f, 0f, 0.55f));
                    string tip = pct != 0f
                        ? $"Frame {i + 1} shows for {msText} ({pct:+0;-0}% of a normal frame at {_animFps:0.#} FPS)."
                        : $"Frame {i + 1} shows for {msText} (one normal frame at {_animFps:0.#} FPS).";
                    GUI.Label(msBadge, new GUIContent(msText, tip), EditorStyles.whiteMiniLabel);
                }

                HandleSeqDrag(i, r);
            }
            GUI.EndScrollView();
        }

        // Slot 0: frame 0, drawn dimmer with a grey badge so it never reads as part of the animation.
        private void DrawFrameZeroSlot(Rect r)
        {
            bool has = SeqRefValid(_frameZero);
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f));
            if (has)
            {
                DrawCellThumb(r, _frameZero.region, _frameZero.cell);
                EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.35f));   // dimmed: not an animation frame
            }
            else GUI.Label(r, "?", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });

            Color line = _frameZeroSel ? new Color(1f, 0.85f, 0.1f, 1f)
                       : _showingFrameZero ? new Color(0.2f, 1f, 0.5f, 0.9f) : new Color(1f, 1f, 1f, 0.25f);
            DrawRectOutline(r, line, _frameZeroSel ? 2.5f : _showingFrameZero ? 2f : 1f);
            var badge = new Rect(r.x, r.y, 16, 14);
            EditorGUI.DrawRect(badge, new Color(0.35f, 0.35f, 0.35f, 0.92f));
            GUI.Label(badge, "0", EditorStyles.whiteMiniLabel);

            string tip;
            if (has)
            {
                string msText = FrameMsOf(_frameZero.pct).ToString("0") + "ms";
                var msSize = EditorStyles.whiteMiniLabel.CalcSize(new GUIContent(msText));
                var msBadge = new Rect(r.xMax - msSize.x - 2f, r.yMax - 14f, msSize.x + 2f, 14f);
                EditorGUI.DrawRect(msBadge, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(msBadge, msText, EditorStyles.whiteMiniLabel);
                tip = $"Frame 0 shows for {msText} before frame 1, in this preview only; the game never plays it. " +
                      "Click to select it and set its length with Frame time %. Drag a frame here to replace it; " +
                      "right-click to clear it.";
            }
            else tip = "Frame 0 has no sprite yet. Drag a frame from the sequence here, or right-click a sprite in " +
                       "the palette → Use as frame 0.";
            GUI.Label(r, new GUIContent("", tip));

            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                _frameZeroSel = true; _seqMultiSel.Clear(); _seqSelected = -1;
                _animPlaying = false; _inDivider = false; _showingFrameZero = has;
                if (_playToggleButton != null) _playToggleButton.text = "▶";
                if (e.button == 1)
                {
                    var menu = new GenericMenu();
                    if (HasSelectedCell()) menu.AddItem(new GUIContent("Use selected palette sprite"), false, () => SetFrameZero(_selRegion, _selCell));
                    else menu.AddDisabledItem(new GUIContent("Use selected palette sprite"));
                    if (has) menu.AddItem(new GUIContent("Clear frame 0"), false, () =>
                    {
                        RecordUndo("Clear frame 0");
                        _frameZero = new CellRef(-1, -1); _showingFrameZero = false; _previewHash = -1; Refresh();
                    });
                    menu.ShowAsContext();
                }
                e.Use();
                DeferRefresh();
            }
            else if (e.type == EventType.MouseUp && _seqDragFrom >= 0 && r.Contains(e.mousePosition))
            {
                // Dropping a sequence frame here copies it into frame 0; the sequence itself is untouched.
                if (_seqDragFrom < _sequence.Count) { var src = _sequence[_seqDragFrom]; _seqDragFrom = -1; SetFrameZero(src.region, src.cell); }
                _seqDragFrom = -1; e.Use();
            }
        }

        // ── sequence batch selection (mirrors the Sprite Palette's multi-select) ──────
        private bool IsSeqSelected(int i) => _seqMultiSel.Contains(i);

        private void SeqSelectSingle(int i)
        {
            _seqMultiSel.Clear(); _seqMultiSel.Add(i);
            _seqSelected = i; _seqAnchor = i;
        }
        private void SeqToggle(int i)
        {
            if (!_seqMultiSel.Remove(i)) { _seqMultiSel.Add(i); _seqSelected = i; }
            else if (_seqSelected == i) _seqSelected = _seqMultiSel.Count > 0 ? _seqMultiSel.Min() : -1;
            _seqAnchor = i;
        }
        private void SeqRangeTo(int i)
        {
            if (_seqAnchor < 0) { SeqSelectSingle(i); return; }
            _seqMultiSel.Clear();
            for (int k = Mathf.Min(_seqAnchor, i); k <= Mathf.Max(_seqAnchor, i); k++) _seqMultiSel.Add(k);
            _seqSelected = i;
        }

        /// <summary>Reverse the ORDER of the selected frames, keeping their slot positions — frame contents at
        /// the selected indices are mirrored (e.g. select 2·4·6 → contents become 6·4·2). The selection and
        /// primary follow their slots.</summary>
        private void ReverseSelectedFrames()
        {
            var idx = _seqMultiSel.Where(k => k >= 0 && k < _sequence.Count).OrderBy(k => k).ToList();
            if (idx.Count < 2) return;
            var mapping = Enumerable.Range(0, _sequence.Count).ToList();
            for (int j = 0; j < idx.Count; j++) mapping[idx[j]] = idx[idx.Count - 1 - j];
            RemapSequence(mapping, "Reverse frames");
            _status = $"Reversed {idx.Count} frames.";
            Repaint();
        }

        private void DuplicateSelectedFrames()
        {
            var idx = _seqMultiSel.Where(k => k >= 0 && k < _sequence.Count).OrderBy(k => k).ToList();
            if (idx.Count == 0) return;
            // Copy the selected frames as ONE contiguous block (in sequence order) and insert it right AFTER the
            // last selected frame, pushing everything past it forward. (Not interleaved per-original.)
            int insertAt = idx[idx.Count - 1] + 1;
            var mapping = Enumerable.Range(0, _sequence.Count).ToList();
            mapping.InsertRange(insertAt, idx);
            RemapSequence(mapping, "Duplicate frames");

            // Select the freshly-inserted block.
            _seqMultiSel.Clear();
            for (int j = 0; j < idx.Count; j++) _seqMultiSel.Add(insertAt + j);
            _seqSelected = insertAt + idx.Count - 1;
            _seqAnchor = insertAt;
            _status = $"Duplicated {idx.Count} frame(s) after the selection.";
            Repaint();
        }

        private void DeleteSelectedFrames()
        {
            var idx = _seqMultiSel.Where(k => k >= 0 && k < _sequence.Count).OrderByDescending(k => k).ToList();
            if (idx.Count == 0) return;
            int first = idx.Min();
            RemapSequence(Enumerable.Range(0, _sequence.Count).Where(i => !idx.Contains(i)).ToList(), "Delete frames");
            _seqMultiSel.Clear();
            _seqSelected = _sequence.Count == 0 ? -1 : Mathf.Clamp(first, 0, _sequence.Count - 1);
            if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected);
            _seqAnchor = _seqSelected;
            Repaint();
        }

        private void HandleSeqDrag(int i, Rect r)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                // Scrub the playhead to the clicked frame and pause, so the preview parks on this sprite.
                _animFrame = i; _animPlaying = false; _inDivider = false;
                _frameZeroSel = false; _showingFrameZero = false;
                if (e.button == 1)
                {
                    if (!IsSeqSelected(i)) SeqSelectSingle(i); // right-click outside the selection isolates it
                    ShowSequenceContextMenu(i);
                }
                else if (e.control || e.command) SeqToggle(i);   // add/remove this frame
                else if (e.shift) SeqRangeTo(i);                 // extend from the anchor
                else { SeqSelectSingle(i); _seqDragFrom = i; }   // plain click: single-select + arm drag
                e.Use();
                if (_playToggleButton != null) _playToggleButton.text = "▶";   // the click paused playback
                DeferRefresh();
            }
            else if (e.type == EventType.MouseUp && _seqDragFrom >= 0 && r.Contains(e.mousePosition))
            {
                if (_seqDragFrom != i && _seqDragFrom < _sequence.Count)
                {
                    MoveSequenceFrame(_seqDragFrom, i);
                }
                _seqDragFrom = -1; e.Use(); DeferRefresh();
            }
        }

        /// Rebuild the control hosts AFTER the current IMGUI pass — an island must never destroy itself
        /// while it is drawing.
        private void DeferRefresh() => EditorApplication.delayCall += Refresh;


        /// <summary>The recipe (per-frame source rect + pivot) for the current sequence. The source sheet is
        /// NOT modified — the lauminary/orphan bakes its own atlas from this (see AtlasBaker).</summary>
        private List<FrameRef> BuildRecipe()
        {
            // Each cell's OWN region says which texture it actually came from (Region.sourceTextureGuid) —
            // NEVER assume every frame belongs to the currently-loaded sheet. That was the real bug behind
            // "the Builder shows nonsense" (2026-08-17): every FrameRef got stamped with _sheetPath's guid
            // regardless of which texture its cell rect was actually identified from, so both the live
            // preview bake and — far more seriously — DoSave() itself would silently corrupt a real
            // multi-texture recipe (like a rotation sheet's 16 separate source images) down to reading every
            // frame's rect against the ONE wrong texture the moment Save was clicked.
            var recipe = new List<FrameRef>(_sequence.Count);
            foreach (var cr in _sequence)
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                recipe.Add(new FrameRef { sourceTextureGuid = reg.sourceTextureGuid, cell = reg.cells[cr.cell], pivot = reg.pivots[cr.cell], transform = reg.transforms[cr.cell], timingPercent = cr.pct });
            }
            return recipe;
        }

        private void DoSave() => TrySaveDocument();

        private bool TrySaveDocument()
        {
            if (string.IsNullOrWhiteSpace(_animName)) { _status = "Name the animation first."; return false; }
            if (_sequence.Count == 0) { _status = "Sequence is empty — add sprites first."; return false; }
            if (HasInvalidPhaseRanges) { _status = "Not saved: fix the highlighted phase ranges first."; return false; }
            if (_boundLauminary != null && !string.Equals(_boundAnimName, _animName, System.StringComparison.OrdinalIgnoreCase)
                && LauminaryRepo.GetDraftAnimation(_boundLauminary, _animName) != null)
            {
                _status = $"Not saved: an animation named '{_animName}' already exists. Choose another name.";
                return false;
            }
            for (int i = 0; i < _sequence.Count; i++)
                if (!RegionSourceUsable(_sequence[i].region, out string why))
                {
                    _status = $"Not saved: frame {i + 1} can't be read ({why}). Remove it or add it again from its sheet.";
                    return false;
                }

            try
            {
            var def = new Laumination
            {
                name = _animName, fps = _animFps, recipe = BuildRecipe(),
                events = CloneEvents(_events),
                sourceTextureGuid = AssetDatabase.AssetPathToGUID(_sheetPath),
                bgKeyEnabled = _bgKeyEnabled, bgKey = _bgKey, bgKeyTolerance = _bgTolerance,
                fixedFrame = _fixedFrame, frameWidth = _frameW, frameHeight = _frameH, framePivot = _framePivot,
                metaLayersEnabled = _metaEnabled, metaLayers = CloneLayers(_metaLayers),
                zonesEnabled = _zonesEnabled, zones = ZonesForSave(),
                // Frame 0 rides along for the builder's preview; nothing bakes or plays it.
                previewFrameZero = _frameZeroOn, asepriteSourcePath = _animationAsepriteSourcePath,
                frameZero = SeqRefValid(_frameZero) && RegionSourceUsable(_frameZero.region, out _) ? FrameRefOf(_frameZero) : null
            };
                if (_boundLauminary != null)
                {
                    // Prefer the no-rebake path. When only meta-layers/zones/events/fps changed, the atlas is
                    // provably unaffected (it is a pure function of the recipe), so re-baking it would be pure
                    // risk — that rebuild carries a reimport race that has silently merged and dropped frames
                    // on real assets. Falls through to the full save the moment any pixel-affecting field
                    // differs, so this can never skip a bake that was actually needed.
                    bool dataOnly = string.Equals(_boundAnimName, _animName, System.StringComparison.Ordinal)
                        && LauminaryRepo.TrySaveAnimationDataOnly(_boundLauminary, def);
                    if (!dataOnly) LauminaryRepo.SaveAnimationToDraft(_boundLauminary, def);
                    // Preserve the old entry until the new name has actually saved successfully.
                    if (!string.IsNullOrEmpty(_boundAnimName) &&
                        !string.Equals(_boundAnimName, _animName, System.StringComparison.OrdinalIgnoreCase))
                        LauminaryRepo.RemoveAnimationFromDraft(_boundLauminary, _boundAnimName);
                    _boundAnimName = _animName;
                    _status = dataOnly
                        ? $"Saved '{_animName}' ({def.recipe.Count} frames) to '{_boundLauminary.lauminaryName}' — data only, no re-bake."
                        : $"Saved '{_animName}' ({def.recipe.Count} frames) to '{_boundLauminary.lauminaryName}'.";
                    EditorGUIUtility.PingObject(_boundLauminary);
                }
                else
                {
                    _orphanAsset = AnimationLibrary.Save(def, _orphanAsset);
                    _status = $"Saved orphaned animation '{_animName}' ({def.recipe.Count} frames). Include it from the Lauminary Browser.";
                    EditorGUIUtility.PingObject(_orphanAsset);
                }
                // NOTE: deliberately do NOT write the sheet's slicing sidecar here. An animation's frames live
                // on its Laumination (and restore via the recipe on Edit); writing them into the SHEET metadata
                // conflated the two and accumulated every animation's sprites into the sheet (the #4-pollution
                // bug). The sidecar is now written only by the explicit "Save" slicing button.
                foreach (var w in Resources.FindObjectsOfTypeAll<LauminaryBrowserWindow>())
                    w.ExternalRefresh();
                MarkAnimationSaved();
                return true;
            }
            catch (System.Exception ex)
            {
                _status = "Save failed: " + ex.Message; Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>Rebuild the sequence (and committed cells) from an animation's recipe, for editing. Handles
        /// an empty recipe (a freshly-created animation) by just clearing and keeping the name/fps.</summary>
        private void LoadAnimationIntoSequence(Laumination def)
        {
            _detectedCells.Clear(); _detectedBox = default;
            _editAsePath = null; _editSheetPath = null; _editRects.Clear();
            _animName = def.name;
            _animFps = def.fps <= 0f ? 12f : def.fps;
            _sequence.Clear();
            ClearSelection();

            int assumed = 0;   // frames saved without a source texture, read from the animation's sheet
            if (def.recipe != null && def.recipe.Count > 0)
            {
                // Load the source texture for its pixels, then drop every restored region EXCEPT the sheet's
                // palette: LoadSheet restores the sheet's saved slicing sidecar, which can hold the LAST-edited
                // animation's working set — leaving that would append the previous animation's sprites in front
                // of this one's (the reported bug). The palette is different: it is every sprite the lauminary
                // uses (LauminarySources.WritePalette), kept so any animation can pick from all of them, and the
                // recipe below matches this animation's frames onto its boxes.
                var tex = !string.IsNullOrEmpty(def.sourceTextureGuid)
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(def.sourceTextureGuid))
                    : null;
                if (tex != null) LoadSheet(tex);
                _regions.RemoveAll(r => r.label != LauminarySources.PaletteRegionLabel);
                // The sidecar also restores its own fps; this animation's fps wins.
                _animFps = def.fps <= 0f ? 12f : def.fps;

                foreach (var original in def.recipe)
                {
                    // A frame saved without its source texture (an older builder could do that) is read from the
                    // animation's own source sheet, which is where its rect was measured.
                    var f = original;
                    if (string.IsNullOrEmpty(f.sourceTextureGuid) && !string.IsNullOrEmpty(def.sourceTextureGuid))
                    {
                        f = new FrameRef { sourceTextureGuid = def.sourceTextureGuid, cell = f.cell, pivot = f.pivot,
                                           transform = f.transform, timingPercent = f.timingPercent };
                        assumed++;
                    }
                    CellRef cr = FindOrCreateCellForFrame(f);
                    cr.pct = f.timingPercent;
                    if (SeqRefValid(cr)) _sequence.Add(cr);
                }
            }
            else
            {
                _regions.Clear();
            }

            _frameZeroOn = def.previewFrameZero;
            _frameZeroSel = false; _showingFrameZero = false;
            _frameZero = new CellRef(-1, -1);
            var z = def.frameZero;
            if (z != null && !string.IsNullOrEmpty(z.sourceTextureGuid) && z.cell.width > 0f && _regions.Count > 0)
            {
                _frameZero = FindOrCreateCellForFrame(z);
                _frameZero.pct = z.timingPercent;
            }

            _seqSelected = _sequence.Count > 0 ? 0 : -1;
            _seqMultiSel.Clear(); if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected); _seqAnchor = _seqSelected;
            _animFrame = 0;
            // The animation's own saved key + registration win over the sheet's sidecar.
            _bgKeyEnabled = def.bgKeyEnabled; _bgKey = def.bgKey; _bgTolerance = def.bgKeyTolerance;
            _fixedFrame = def.fixedFrame; _frameW = Mathf.Max(1, def.frameWidth); _frameH = Mathf.Max(1, def.frameHeight); _framePivot = def.framePivot;
            LoadZonesFrom(def);
            _events = CloneEvents(def.events);
            _animationAsepriteSourcePath = def.asepriteSourcePath;
            _metaEnabled = def.metaLayersEnabled;
            _metaLayers = CloneLayers(def.metaLayers ?? new List<MetaLayer>());
            _activeLayer = _metaLayers.Count > 0 ? 0 : -1; ClearMaskCache();
            RebuildDisplaySheet();
            _status = _sequence.Count > 0
                ? $"Loaded '{def.name}' ({_sequence.Count} frames) for editing."
                : $"'{def.name}' is empty — load a sheet and build it.";
            if (assumed > 0)
                _status += $" {assumed} frame(s) had no source texture saved; read from the animation's own sheet — Save to keep that.";
            BeginCleanDocument();
            Refresh();
        }

        /// <summary>Match a recipe frame to an existing committed cell (by rect AND transform), else synthesize
        /// one so it shows in #4. Matching on the transform too keeps a duplicated-then-edited sprite (same
        /// source rect, different transform) independent across save/reload. Restores pivot + transform.</summary>
        private CellRef FindOrCreateCellForFrame(FrameRef f)
        {
            // Match by SOURCE TEXTURE first, THEN rect+transform — two frames from different textures that
            // happen to share the same cell rect (the common case for standalone same-size sprite files, each
            // its own whole-image cell at (0,0,w,h)) must never be folded into one shared cell. This is the
            // fix for the real bug found 2026-08-17: an animation built from several single-sprite source
            // files was silently collapsing to one frame repeated N times, because the old match ignored which
            // texture a cell actually came from.
            //
            // The PIVOT is part of a sprite's identity too: a duplicated sprite (same rect, its own pivot) used
            // to fold onto the first match on reload, which then took the last frame's pivot for every frame
            // using it. So: an exact match (pivot included) is reused; otherwise a same-rect sprite no frame of
            // this load has claimed yet adopts this frame's pivot (palette boxes carry a default pivot);
            // otherwise the frame gets its own copy beside it.
            CellRef adopt = new CellRef(-1, -1);
            for (int ri = 0; ri < _regions.Count; ri++)
            {
                if (_regions[ri].sourceTextureGuid != f.sourceTextureGuid) continue;
                _regions[ri].SyncPivots(GlobalPivot());
                for (int ci = 0; ci < _regions[ri].cells.Count; ci++)
                {
                    if (!RectApprox(_regions[ri].cells[ci], f.cell) || !TransformEq(_regions[ri].transforms[ci], f.transform)) continue;
                    if ((_regions[ri].pivots[ci] - f.pivot).sqrMagnitude < 1e-8f) return new CellRef(ri, ci);
                    if (!CellClaimed(ri, ci)) { if (adopt.cell < 0) adopt = new CellRef(ri, ci); }
                    else if (adopt.region < 0) adopt = new CellRef(ri, -1);   // only claimed ones so far: copy into this region
                }
            }
            if (adopt.region >= 0 && adopt.cell >= 0)
            {
                _regions[adopt.region].pivots[adopt.cell] = f.pivot;
                return adopt;
            }
            if (adopt.region >= 0)
            {
                var host = _regions[adopt.region];
                host.cells.Add(f.cell);
                host.pivots.Add(f.pivot);
                host.transforms.Add(f.transform);
                return new CellRef(adopt.region, host.cells.Count - 1);
            }

            int idx = _regions.FindIndex(r => r.label == "imported" && r.sourceTextureGuid == f.sourceTextureGuid);
            if (idx < 0)
            {
                _regions.Add(new Region { label = "imported", bounds = f.cell, sourceTextureGuid = f.sourceTextureGuid });
                idx = _regions.Count - 1;
            }
            var reg = _regions[idx]; reg.SyncPivots(GlobalPivot());
            reg.cells.Add(f.cell);
            reg.pivots.Add(f.pivot);
            reg.transforms.Add(f.transform);
            return new CellRef(idx, reg.cells.Count - 1);
        }

        // A sprite already used by a frame loaded so far (sequence or frame 0).
        private bool CellClaimed(int region, int cell)
        {
            foreach (var cr in _sequence) if (cr.region == region && cr.cell == cell) return true;
            return _frameZero.region == region && _frameZero.cell == cell;
        }

        private static bool RectApprox(Rect a, Rect b)
            => Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
               && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        private static bool TransformEq(CellTransform a, CellTransform b)
            => a.flipX == b.flipX && a.flipY == b.flipY && (a.rot90 & 3) == (b.rot90 & 3)
               && Mathf.Abs(a.angle - b.angle) < 0.01f
               && Mathf.Abs(a.SX - b.SX) < 1e-4f && Mathf.Abs(a.SY - b.SY) < 1e-4f && a.smooth == b.smooth;

        private static int TransformHash(CellTransform t)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (t.flipX ? 1 : 0); h = h * 31 + (t.flipY ? 2 : 0);
                h = h * 31 + (t.rot90 & 3); h = h * 31 + (t.smooth ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(t.angle * 100f);
                h = h * 31 + Mathf.RoundToInt(t.SX * 1000f); h = h * 31 + Mathf.RoundToInt(t.SY * 1000f);
                return h;
            }
        }

        private void TryApplyPending()
        {
            if (_pendingEditLauminary != null && !string.IsNullOrEmpty(_pendingEditAnim))
            {
                var def = LauminaryRepo.GetDraftAnimation(_pendingEditLauminary, _pendingEditAnim);
                if (def != null)
                {
                    _boundLauminary = _pendingEditLauminary;
                    _boundAnimName = _pendingEditAnim;
                    _orphanAsset = null;
                    LoadAnimationIntoSequence(def);
                }
            }
            else if (_pendingOrphan != null)
            {
                _orphanAsset = _pendingOrphan;
                _boundLauminary = null; _boundAnimName = null;
                LoadAnimationIntoSequence(_pendingOrphan.animation);
            }
            _pendingEditLauminary = null; _pendingEditAnim = null; _pendingOrphan = null;
        }

        // ── cell store helpers ───────────────────────────────────────────────
        private List<CellRef> FlattenCells()
        {
            var list = new List<CellRef>();
            for (int ri = 0; ri < _regions.Count; ri++)
                for (int ci = 0; ci < _regions[ri].cells.Count; ci++)
                    list.Add(new CellRef(ri, ci));
            return list;
        }
        private int TotalCells() { int t = 0; foreach (var r in _regions) t += r.cells.Count; return t; }
        private Vector2 GlobalPivot() => RegionSlicer.ResolvePivot(_pivot, _customPivot);

        private bool HasSelectedCell()
            => _selRegion >= 0 && _selRegion < _regions.Count && _selCell >= 0 && _selCell < _regions[_selRegion].cells.Count;

        /// <summary>The sequence slot the selected sprite occupies — the centre of the onion-skin window.
        /// Prefers the explicitly selected sequence frame, else the first slot using this sprite, else -1
        /// (sprite not in the sequence, so the ghost window has no anchor).</summary>
        private int AnchorSequenceIndex()
        {
            if (!HasSelectedCell()) return -1;
            if (_seqSelected >= 0 && _seqSelected < _sequence.Count
                && _sequence[_seqSelected].region == _selRegion && _sequence[_seqSelected].cell == _selCell)
                return _seqSelected;
            for (int i = 0; i < _sequence.Count; i++)
                if (_sequence[i].region == _selRegion && _sequence[i].cell == _selCell) return i;
            return -1;
        }
        private bool SeqRefValid(CellRef cr)
            => cr.region >= 0 && cr.region < _regions.Count && cr.cell >= 0 && cr.cell < _regions[cr.region].cells.Count;

        // ── selection helpers ────────────────────────────────────────────────
        private static long PackCell(int region, int cell) => ((long)region << 32) | (uint)cell;
        private bool IsCellSelected(int region, int cell) => _multiSel.Contains(PackCell(region, cell));

        /// <summary>The selected cells (validity-filtered), in flattened display order.</summary>
        private List<CellRef> SelectedCells()
            => FlattenCells().Where(cr => _multiSel.Contains(PackCell(cr.region, cr.cell))).ToList();

        private void ClearSelection()
        {
            _multiSel.Clear(); _selRegion = -1; _selCell = -1; _selAnchor = new CellRef(-1, -1);
        }

        private void SelectSingle(int region, int cell)
        {
            _multiSel.Clear(); _multiSel.Add(PackCell(region, cell));
            _selRegion = region; _selCell = cell; _selAnchor = new CellRef(region, cell);
        }

        private void ToggleSelect(int region, int cell)
        {
            long p = PackCell(region, cell);
            if (_multiSel.Remove(p))
            {
                if (_selRegion == region && _selCell == cell) PromotePrimaryFromSelection();
            }
            else
            {
                _multiSel.Add(p); _selRegion = region; _selCell = cell;
            }
            _selAnchor = new CellRef(region, cell);
        }

        /// <summary>Shift-range: select every cell between the anchor and (region,cell) in flattened order.</summary>
        private void RangeSelectTo(int region, int cell)
        {
            var flat = FlattenCells();
            int to = flat.FindIndex(cr => cr.region == region && cr.cell == cell);
            int from = _selAnchor.region < 0 ? to : flat.FindIndex(cr => cr.region == _selAnchor.region && cr.cell == _selAnchor.cell);
            if (to < 0) return;
            if (from < 0) from = to;
            _multiSel.Clear();
            for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                _multiSel.Add(PackCell(flat[i].region, flat[i].cell));
            _selRegion = region; _selCell = cell;
        }

        private void PromotePrimaryFromSelection()
        {
            foreach (var cr in SelectedCells()) { _selRegion = cr.region; _selCell = cr.cell; return; }
            _selRegion = -1; _selCell = -1;
        }

        /// Append a whole standalone image file as ONE new frame — its own region (scoped to its own texture,
        /// per Region.sourceTextureGuid), one cell covering the full image. This is the "single sprites" half
        /// of "a Laumination can use sprites from several sheets or single sprites": it goes through the exact
        /// same Region/CellRef data path as sheet-sliced sprites (so Zones, Meta Layers, and everything else
        /// downstream just works on it identically), it just never needs the loaded-sheet canvas at all.
        private void AppendStandaloneSpriteFrame(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) { _status = $"'{tex.name}' has no asset path — can't reference it."; return; }
            tex = CrispenTextureImport(tex, path, out string sizeWarning);
            string guid = AssetDatabase.AssetPathToGUID(path);
            var whole = new Rect(0, 0, tex.width, tex.height);

            var idx = _regions.FindIndex(r => r.label == "single" && r.sourceTextureGuid == guid);
            if (idx < 0)
            {
                _regions.Add(new Region { label = "single", bounds = whole, sourceTextureGuid = guid });
                idx = _regions.Count - 1;
            }
            var reg = _regions[idx]; reg.SyncPivots(GlobalPivot());
            // A standalone single-sprite file is authored once per texture — reuse its existing cell if this
            // exact texture was already added before, instead of piling up duplicate identical cells.
            int cellIdx = reg.cells.FindIndex(c => RectApprox(c, whole));
            if (cellIdx < 0)
            {
                reg.cells.Add(whole);
                reg.pivots.Add(GlobalPivot());
                reg.transforms.Add(CellTransform.Identity);
                cellIdx = reg.cells.Count - 1;
            }

            _sequence.Add(new CellRef(idx, cellIdx));
            SeqSelectSingle(_sequence.Count - 1);
            if (_animFrame >= _sequence.Count) _animFrame = 0;
            _status = !string.IsNullOrEmpty(sizeWarning) ? sizeWarning : $"Added '{tex.name}' as frame {_sequence.Count}.";
        }

        private void AppendToSequence(int region, int cell)
        {
            if (!RegionSourceUsable(region, out string why))
            {
                _status = $"Can't add this sprite: {why}. Identify it again on its sheet.";
                return;
            }
            RecordUndo("Add frame");
            _sequence.Add(new CellRef(region, cell));
            SeqSelectSingle(_sequence.Count - 1);
            if (_animFrame >= _sequence.Count) _animFrame = 0;
        }
        private string SequenceOrdinalsFor(int region, int cell)
        {
            string s = null;
            for (int i = 0; i < _sequence.Count; i++)
                if (_sequence[i].region == region && _sequence[i].cell == cell)
                    s = s == null ? (i + 1).ToString() : s + "," + (i + 1);
            return s;
        }

        private int CurrentBoxCellCount() => _hasBox ? RegionSlicer.ExpandRegion(BuildActiveSpec()).Count : 0;
        private RegionSlicer.RegionSpec BuildActiveSpec() => new RegionSlicer.RegionSpec
        {
            boxX = Mathf.RoundToInt(_box.x), boxY = Mathf.RoundToInt(_box.y),
            boxW = Mathf.RoundToInt(_box.width), boxH = Mathf.RoundToInt(_box.height),
            mode = _mode, cols = _cols, rows = _rows, cellW = _cellW, cellH = _cellH,
            spacingPx = _spacing, paddingPx = _padding
        };

        private void AddRegion()
        {
            var cells = RegionSlicer.ExpandRegion(BuildActiveSpec());
            if (cells.Count == 0) { _status = "Box produced no cells."; return; }
            RecordUndo("Add region");
            string label = _mode == RegionSlicer.GridMode.FixedCellSize ? $"{_cellW}×{_cellH}px" : $"{_cols}×{_rows}";
            var region = new Region { label = label, cells = cells, bounds = _box, sourceTextureGuid = CurrentSheetGuid() };
            region.SyncPivots(GlobalPivot());
            _regions.Add(region);
            _status = $"Added {cells.Count} cells. Marquee the next group.";
            Repaint();
        }

        private void PickSpriteAt(int texX, int texY)
        {
            var px = GetPixels();
            if (px == null) { _status = "Pick failed: texture not readable."; return; }
            Rect whole = new Rect(0, 0, _texW, _texH);
            Rect bbox = RegionSlicer.FloodFillBBox(px, _texW, _texH, whole, texX, texY, _alphaThreshold, out bool empty, CurrentColorKey());
            if (empty) { _status = $"({texX},{texY}) is transparent — click on a sprite's pixels."; return; }
            RecordUndo("Pick sprite");

            int idx = _regions.FindIndex(r => r.label == PickedLabel && r.sourceTextureGuid == CurrentSheetGuid());
            if (idx < 0) { _regions.Add(new Region { label = PickedLabel, bounds = whole, sourceTextureGuid = CurrentSheetGuid() }); idx = _regions.Count - 1; }
            var reg = _regions[idx];
            reg.cells.Add(bbox); reg.SyncPivots(GlobalPivot());
            SelectSingle(idx, reg.cells.Count - 1);
            _status = $"Picked {bbox.width:0}×{bbox.height:0}px sprite. Double-click it in #4 to add to the sequence.";
        }

        /// Force pixel-art-correct import settings (readable, point-filtered, uncompressed, no mipmaps/NPOT
        /// scaling, capped at Unity's max rather than silently downsampled) on ANY texture this window is
        /// about to read pixels from — the loaded sheet (LoadSheet) or a standalone single-sprite file
        /// (AppendStandaloneSpriteFrame). Returns the (possibly reloaded, if reimported) texture; sizeWarning
        /// is non-null only if the source was too large and got downscaled anyway.
        private static Texture2D CrispenTextureImport(Texture2D tex, string path, out string sizeWarning)
        {
            sizeWarning = null;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return tex;

            bool changed = false;
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            // Rips are often very tall; cap at Unity's max so the sheet isn't downsampled — which blurs the
            // pixel art and makes every slice imprecise.
            if (importer.maxTextureSize < 16384) { importer.maxTextureSize = 16384; changed = true; }
            if (changed) { importer.SaveAndReimport(); tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path); }

            importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
            if (tex.width < srcW || tex.height < srcH)
                sizeWarning = $"⚠ '{tex.name}' is {srcW}×{srcH}, larger than Unity's {importer.maxTextureSize}px limit, so it was " +
                              $"downscaled to {tex.width}×{tex.height} — it will look blurry and slice imprecisely. " +
                              "Split the image into smaller files before extracting.";
            return tex;
        }

        // ── sheet load + importer crispening + auto-restore ──────────────────
        private void LoadSheet(Texture2D tex)
        {
            _detectedCells.Clear(); _detectedBox = default;
            _editAsePath = null; _editSheetPath = null; _editRects.Clear();
            _sheet = tex;
            _regions.Clear(); ClearThumbCache(); ClearMaskCache();
            _metaEnabled = false; _metaLayers = new List<MetaLayer>(); _activeLayer = -1;
            _hasBox = false; _box = default; _status = null;
            _zoomInitialized = false; _pixelCache = null; _pixelCacheFor = null;
            ClearSelection(); _sequence.Clear(); _seqSelected = -1; _animFrame = 0;
            _seqMultiSel.Clear(); _seqAnchor = -1; _seqDragFrom = -1;
            _events = new List<FrameEvent>(); _zones.Clear(); _zonesEnabled = false;
            _animationAsepriteSourcePath = "";
            _frameZero = new CellRef(-1, -1); _frameZeroOn = false; _frameZeroSel = false; _showingFrameZero = false;
            _animPlaying = false; _inDivider = false; _previewHash = -1;
            _bgKeyEnabled = false; _pickingBgColor = false; DestroyDisplaySheet();

            if (_sheet == null) { _sheetPath = null; _texW = _texH = 0; _sheetDisplayName = ""; return; }
            _sheetPath = AssetDatabase.GetAssetPath(_sheet);
            _sheetDisplayName = SheetRegistry.GetDisplayName(_sheet);

            _sheet = CrispenTextureImport(_sheet, _sheetPath, out string sizeWarning);
            if (!string.IsNullOrEmpty(sizeWarning)) _status = sizeWarning;
            _texW = _sheet.width; _texH = _sheet.height;
            if (string.IsNullOrEmpty(_status)) _status = $"Loaded {_texW}×{_texH} sheet.";

            if (RegionSlicerPersistence.Exists(_sheetPath))
            {
                var saved = RegionSlicerPersistence.Load(_sheetPath, out _);
                if (saved != null) { ApplyState(saved); _status = $"Loaded sheet — restored {_regions.Count} region(s), {TotalCells()} sprite(s)."; }
            }
            else if (TryAutoDetectBgKey()) // fresh/just-downloaded sheet with no saved settings → guess the bg
            {
                _status += $" Auto-detected background RGB({_bgKey.r},{_bgKey.g},{_bgKey.b}) — treated as transparent (toggle off if wrong).";
            }
        }

        // ── persistence (slicing state only; animations live on the lauminary) ─
        private RegionSlicerPersistence.StateDto BuildState()
        {
            var s = new RegionSlicerPersistence.StateDto
            {
                textureGuid = string.IsNullOrEmpty(_sheetPath) ? null : AssetDatabase.AssetPathToGUID(_sheetPath),
                texturePath = _sheetPath, texW = _texW, texH = _texH,
                alphaTrim = _alphaTrim, alphaThreshold = _alphaThreshold,
                ppu = _ppu, pivotMode = (int)_pivot, customPivot = new RegionSlicerPersistence.Vec2Dto(_customPivot),
                gridMode = (int)_mode, cols = _cols, rows = _rows, cellW = _cellW, cellH = _cellH, spacing = _spacing, padding = _padding,
                bgKeyEnabled = _bgKeyEnabled, bgKeyR = _bgKey.r, bgKeyG = _bgKey.g, bgKeyB = _bgKey.b, bgKeyTolerance = _bgTolerance,
            };
            Vector2 g = GlobalPivot();
            foreach (var reg in _regions)
            {
                var rd = new RegionSlicerPersistence.RegionDto { label = reg.label, sourceTextureGuid = reg.sourceTextureGuid, bounds = new RegionSlicerPersistence.RectDto(reg.bounds) };
                foreach (var c in reg.cells) rd.cells.Add(new RegionSlicerPersistence.RectDto(c));
                for (int i = 0; i < reg.cells.Count; i++)
                {
                    rd.pivots.Add(new RegionSlicerPersistence.Vec2Dto(i < reg.pivots.Count ? reg.pivots[i] : g));
                    rd.transforms.Add(new RegionSlicerPersistence.TransformDto(i < reg.transforms.Count ? reg.transforms[i] : CellTransform.Identity));
                }
                s.regions.Add(rd);
            }
            return s;
        }

        private void ApplyState(RegionSlicerPersistence.StateDto s)
        {
            // Slicing restore is scoped to the sheet. Preserve the working animation and reconnect its
            // recipe afterwards rather than silently emptying it when the palette is replaced.
            var recipe = _sequence.Select(DocumentFrame).ToList();
            var leadIn = SeqRefValid(_frameZero) ? DocumentFrame(_frameZero) : null;
            RegionSlicerPersistence.Normalize(s);
            _alphaTrim = s.alphaTrim; _alphaThreshold = Mathf.Clamp(s.alphaThreshold, 0, 255);
            _ppu = s.ppu <= 0f ? 16f : s.ppu;
            _pivot = (GridSlicer.PivotMode)s.pivotMode;
            _customPivot = s.customPivot != null ? s.customPivot.ToVec2() : new Vector2(0.5f, 0f);
            _mode = (RegionSlicer.GridMode)s.gridMode;
            _cols = Mathf.Max(1, s.cols); _rows = Mathf.Max(1, s.rows);
            _cellW = Mathf.Max(1, s.cellW); _cellH = Mathf.Max(1, s.cellH);
            _spacing = Mathf.Max(0, s.spacing); _padding = Mathf.Max(0, s.padding);
            _bgKeyEnabled = s.bgKeyEnabled;
            _bgKey = new Color32((byte)Mathf.Clamp(s.bgKeyR, 0, 255), (byte)Mathf.Clamp(s.bgKeyG, 0, 255), (byte)Mathf.Clamp(s.bgKeyB, 0, 255), 255);
            _bgTolerance = Mathf.Clamp(s.bgKeyTolerance, 0, 255);

            _regions.Clear();
            _frameZero = new CellRef(-1, -1); _showingFrameZero = false;   // region indices start over
            string sheetGuid = CurrentSheetGuid();
            foreach (var rd in s.regions)
            {
                var reg = new Region { label = rd.label ?? "", sourceTextureGuid = string.IsNullOrEmpty(rd.sourceTextureGuid) ? sheetGuid : rd.sourceTextureGuid };
                if (rd.bounds != null) reg.bounds = rd.bounds.ToRect();
                foreach (var c in rd.cells) reg.cells.Add(c.ToRect());
                foreach (var p in rd.pivots) reg.pivots.Add(p.ToVec2());
                foreach (var t in rd.transforms) reg.transforms.Add(t.ToTransform());
                reg.SyncPivots(GlobalPivot());
                _regions.Add(reg);
            }
            ClearSelection(); _sequence.Clear();
            foreach (var frame in recipe)
            {
                var cell = FindOrCreateCellForFrame(frame); cell.pct = frame.timingPercent;
                _sequence.Add(cell);
            }
            if (leadIn != null) { _frameZero = FindOrCreateCellForFrame(leadIn); _frameZero.pct = leadIn.timingPercent; }
            _seqSelected = _sequence.Count > 0 ? 0 : -1; _animFrame = 0;
            _seqMultiSel.Clear(); if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected); _seqAnchor = _seqSelected;
            _previewHash = -1; ClearThumbCache(); ClearMaskCache();
            RebuildDisplaySheet();
        }

        private void SaveState(bool silent = false)
        {
            if (_sheet == null || string.IsNullOrEmpty(_sheetPath)) { if (!silent) _status = "Load a sheet first."; return; }
            try { string path = RegionSlicerPersistence.Save(_sheetPath, BuildState()); MarkSlicingSaved(); if (!silent) _status = $"Saved slicing state → {path}"; }
            catch (System.Exception ex) { if (!silent) _status = "Save failed: " + ex.Message; }
        }

        /// <summary>Wipe this sheet's saved slicing sidecar and empty the palette (#4). Saved animations are
        /// untouched — their frames live on the Laumination recipe, not the sheet metadata. Use it to clean a
        /// sheet whose sidecar got polluted before the animation-save/sidecar coupling was removed.</summary>
        private void ClearSavedSlicing()
        {
            if (_sheet == null || string.IsNullOrEmpty(_sheetPath)) { _status = "Load a sheet first."; return; }
            if (!EditorUtility.DisplayDialog("Clear saved slicing?",
                "Delete this sheet's saved slices and clear unused sprites from the working palette?\n\nSprites used by the current animation and its lead-in stay available. The animation is kept.",
                "Clear", "Cancel")) return;

            var empty = BuildState(); empty.regions.Clear();
            try
            {
                RegionSlicerPersistence.Delete(_sheetPath);
                RecordUndo("Clear saved slices");
                ApplyState(empty);
                // Kept animation sprites are working content, not saved slices. Do not falsely mark
                // those entries as persisted after deleting the sidecar.
                _savedSlicingState = Newtonsoft.Json.JsonConvert.SerializeObject(empty);
                _status = TotalCells() > 0
                    ? "Cleared saved slices; kept the current animation's source sprites. Save slices stores the working palette again."
                    : "Cleared saved slices and the working palette.";
            }
            catch (System.Exception ex) { _status = "Clear slices failed: " + ex.Message; }
        }

        /// <summary>If no background key is active, try to auto-detect a solid background colour (no-alpha rips)
        /// and apply it. Returns true when one was detected &amp; enabled. No-op if a key is already set or the
        /// sheet uses real alpha. Saving the slices explicitly also saves this key.</summary>
        private bool TryAutoDetectBgKey()
        {
            if (_bgKeyEnabled || _sheet == null) return false;
            var px = GetPixels();
            if (px == null) return false;
            if (!RegionSlicer.TryDetectBackgroundColor(px, _texW, _texH, out Color32 c)) return false;
            _bgKey = c; _bgKeyEnabled = true;
            RebuildDisplaySheet();
            return true;
        }

        private void LoadStateFromSidecar(bool silentIfMissing)
        {
            if (_sheet == null) { if (!silentIfMissing) _status = "Load a sheet first."; return; }
            var s = RegionSlicerPersistence.Load(_sheetPath, out string error);
            if (s == null) { if (!silentIfMissing) _status = "Restore: " + (error ?? "no saved state."); return; }
            RecordUndo("Reload slices");
            ApplyState(s);
            MarkSlicingSaved();
            _status = $"Restored {_regions.Count} region(s), {TotalCells()} sprite(s).";
        }

        // ── background colour key ────────────────────────────────────────────
        private RegionSlicer.ColorKey CurrentColorKey() => new RegionSlicer.ColorKey
        {
            enabled = _bgKeyEnabled, color = _bgKey, tolerance = _bgTolerance
        };

        /// <summary>The texture to DRAW (canvas/preview): a keyed copy with the background made transparent
        /// when a colour key is active, else the raw sheet.</summary>
        private Texture2D SheetForDisplay()
        {
            var t = _displaySheet != null ? _displaySheet : _sheet;
            // Point filtering keeps magnified pixel-art crisp in the canvas/preview (bilinear smear reads as
            // sub-pixel misalignment, which is what made nudges feel like fractional steps).
            if (t != null && t.filterMode != FilterMode.Point) t.filterMode = FilterMode.Point;
            return t;
        }

        private void DestroyDisplaySheet()
        {
            if (_displaySheet != null) { Object.DestroyImmediate(_displaySheet); _displaySheet = null; }
        }

        /// <summary>Rebuild the keyed display copy from the raw pixels (background key → transparent). Cheap
        /// enough for editor use; called only when the key or sheet changes.</summary>
        private void RebuildDisplaySheet()
        {
            DestroyDisplaySheet();
            if (!_bgKeyEnabled || _sheet == null) return;
            var src = GetPixels();
            if (src == null) return;

            var key = CurrentColorKey();
            var outPx = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
                outPx[i] = key.IsBackground(src[i]) ? new Color32(0, 0, 0, 0) : src[i];

            _displaySheet = new Texture2D(_texW, _texH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _displaySheet.SetPixels32(outPx);
            _displaySheet.Apply();
        }

        // ── pixels + trim ────────────────────────────────────────────────────
        private Color32[] _pixelCache;
        private Texture2D _pixelCacheFor;
        private Color32[] GetPixels()
        {
            if (_sheet == null) return null;
            if (_pixelCache == null || _pixelCacheFor != _sheet)
            {
                try { _pixelCache = _sheet.GetPixels32(); } catch { _pixelCache = null; }
                _pixelCacheFor = _sheet;
            }
            return _pixelCache;
        }

        private string CurrentSheetGuid() => string.IsNullOrEmpty(_sheetPath) ? null : AssetDatabase.AssetPathToGUID(_sheetPath);

        /// Can the pixels behind this region be read? A frame whose source texture is unknown or missing makes
        /// every preview and save of its animation fail, so such a frame is refused at the door.
        private bool RegionSourceUsable(int region, out string why)
        {
            why = null;
            if (region < 0 || region >= _regions.Count) { why = "it no longer exists"; return false; }
            string guid = _regions[region].sourceTextureGuid;
            if (string.IsNullOrEmpty(guid)) { why = "its source texture isn't known"; return false; }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            { why = "its source texture is missing"; return false; }
            return true;
        }

        // ── cross-texture resolution (a Laumination's frames may come from several source textures — see
        // Region.sourceTextureGuid's own doc comment) — only used by the PALETTE/SEQUENCE thumbnail path,
        // never by the Identify-Sprites canvas itself (that stays scoped to whichever ONE sheet is loaded,
        // same as before this existed; you can only marquee/pick from what's actually on screen).
        private readonly Dictionary<string, Texture2D> _foreignTexCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Color32[]> _foreignPixelCache = new Dictionary<string, Color32[]>();

        private Texture2D ResolveTexture(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid == CurrentSheetGuid()) return _sheet;
            if (_foreignTexCache.TryGetValue(guid, out var t) && t != null) return t;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            t = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            _foreignTexCache[guid] = t;
            return t;
        }

        private Color32[] GetPixelsFor(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid == CurrentSheetGuid()) return GetPixels();
            if (_foreignPixelCache.TryGetValue(guid, out var px) && px != null) return px;
            var tex = ResolveTexture(guid);
            if (tex == null) return null;
            try { px = tex.GetPixels32(); } catch { px = null; }
            _foreignPixelCache[guid] = px;
            return px;
        }
        private Rect TrimCell(Rect cell, out bool empty)
        {
            empty = false; var px = GetPixels();
            if (px == null) return cell;
            return RegionSlicer.TrimToContent(px, _texW, _texH, cell, _alphaThreshold, out empty, CurrentColorKey());
        }

        // ── draw + coords ────────────────────────────────────────────────────
        // textureGuid null/current-sheet = the fast, existing SheetForDisplay() path (a bg-keyed preview
        // texture kept in sync with _sheet); any OTHER guid draws the raw resolved texture directly — a
        // foreign sheet has no keyed preview prepared for it, which is fine, bg-key is a per-sheet setting.
        private void DrawCellTex(Rect dst, Rect texCell, string textureGuid = null)
        {
            bool foreign = !string.IsNullOrEmpty(textureGuid) && textureGuid != CurrentSheetGuid();
            Texture2D tex = foreign ? ResolveTexture(textureGuid) : SheetForDisplay();
            if (tex == null) return;
            float tw = foreign ? tex.width : _texW, th = foreign ? tex.height : _texH;
            Rect uv = new Rect(texCell.x / tw, texCell.y / th, texCell.width / tw, texCell.height / th);
            float aspect = texCell.width / Mathf.Max(1f, texCell.height);
            Rect draw = aspect > 1f
                ? new Rect(dst.x, dst.y + (dst.height - dst.width / aspect) / 2, dst.width, dst.width / aspect)
                : new Rect(dst.x + (dst.width - dst.height * aspect) / 2, dst.y, dst.height * aspect, dst.height);
            GUI.DrawTextureWithTexCoords(draw, tex, uv, true);
        }

        /// <summary>Draw a palette/sequence tile for (<paramref name="region"/>,<paramref name="cell"/>) WITH its
        /// edit applied, so an edited sprite's tile looks edited. Identity cells use the cheap raw path; edited
        /// cells draw a cached transformed texture (rebuilt only when the cell or its transform changes).
        /// Resolves EACH region's OWN source texture (see Region.sourceTextureGuid) — a cell from a sheet that
        /// isn't currently loaded still renders correctly here, it's just not paintable/re-identifiable until
        /// that sheet is loaded again.</summary>
        private void DrawCellThumb(Rect dst, int region, int cell)
        {
            var reg = _regions[region]; reg.SyncPivots(GlobalPivot());
            Rect texCell = reg.cells[cell];
            var t = reg.transforms[cell];
            if (t.IsIdentity) { DrawCellTex(dst, texCell, reg.sourceTextureGuid); return; }

            var tex = ThumbTexture(region, cell, texCell, t, reg.sourceTextureGuid);
            if (tex == null) { DrawCellTex(dst, texCell, reg.sourceTextureGuid); return; }
            float aspect = tex.width / Mathf.Max(1f, tex.height);
            Rect draw = aspect > 1f
                ? new Rect(dst.x, dst.y + (dst.height - dst.width / aspect) / 2, dst.width, dst.width / aspect)
                : new Rect(dst.x + (dst.width - dst.height * aspect) / 2, dst.y, dst.height * aspect, dst.height);
            GUI.DrawTexture(draw, tex, ScaleMode.StretchToFill, true);
        }

        private Texture2D ThumbTexture(int region, int cell, Rect texCell, CellTransform t, string textureGuid = null)
        {
            long key = PackCell(region, cell);
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + texCell.GetHashCode();
                hash = hash * 31 + TransformHash(t);
                hash = hash * 31 + (_bgKeyEnabled ? 1 : 0);
                hash = hash * 31 + (_bgKey.r << 24 | _bgKey.g << 16 | _bgKey.b << 8 | _bgKey.a);
                hash = hash * 31 + _bgTolerance;
            }
            if (_thumbCache.TryGetValue(key, out var e) && e.hash == hash && e.tex != null) return e.tex;

            if (e.tex != null) Object.DestroyImmediate(e.tex);
            bool foreign = !string.IsNullOrEmpty(textureGuid) && textureGuid != CurrentSheetGuid();
            var px = foreign ? GetPixelsFor(textureGuid) : GetPixels();
            if (px == null) { _thumbCache.Remove(key); return null; }
            var srcTex = foreign ? ResolveTexture(textureGuid) : null;
            int tw = foreign ? srcTex.width : _texW, th = foreign ? srcTex.height : _texH;
            var block = AtlasBaker.TransformCell(px, tw, th, texCell, t, CurrentColorKey(),
                new Vector2(0.5f, 0.5f), out int w, out int h, out _);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(block); tex.Apply();
            _thumbCache[key] = new ThumbEntry { hash = hash, tex = tex };
            return tex;
        }

        private void ClearThumbCache()
        {
            foreach (var e in _thumbCache.Values) if (e.tex != null) Object.DestroyImmediate(e.tex);
            _thumbCache.Clear();
        }

        private Vector2 ContentToTex(Vector2 content) => new Vector2(content.x / _zoom, _texH - content.y / _zoom);
        private Rect TexRectToContent(Rect texRect)
            => new Rect(texRect.x * _zoom, (_texH - texRect.yMax) * _zoom, texRect.width * _zoom, texRect.height * _zoom);
        private Vector2 SnapTex(Vector2 t) => new Vector2(Mathf.Round(t.x), Mathf.Round(t.y));
        private Rect ClampBox(Rect b)
        {
            float x0 = Mathf.Clamp(b.xMin, 0, _texW), y0 = Mathf.Clamp(b.yMin, 0, _texH);
            float x1 = Mathf.Clamp(b.xMax, 0, _texW), y1 = Mathf.Clamp(b.yMax, 0, _texH);
            return new Rect(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }
        private static void DrawRectOutline(Rect r, Color color, float t)
        {
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - t, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, t, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.yMin, t, r.height), color);
        }

        // ── Undo: RecordUndo() throughout; the snapshot stack lives in LauminationBuilderWindow.Undo.cs ──
        private void RecordUndo(string label) => PushUndo(label);

        /// <summary>A pop-up that shows help/instruction text (moved out of the always-on UI).</summary>
        private class InfoPopup : PopupWindowContent
        {
            private readonly string _text; private readonly float _w;
            public InfoPopup(string text, float w = 380f) { _text = text; _w = w; }
            public override Vector2 GetWindowSize()
            {
                float h = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(_text), _w - 16f);
                return new Vector2(_w, h + 14f);
            }
            public override void OnGUI(Rect rect)
            {
                GUILayout.Space(6);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(8);
                    GUILayout.Label(_text, EditorStyles.wordWrappedLabel, GUILayout.Width(_w - 16f));
                    GUILayout.Space(8);
                }
            }
        }

        /// <summary>A pop-up offering the 5 predetermined layer colours.</summary>
        private class ColorPalettePopup : PopupWindowContent
        {
            private readonly Color _current;
            private readonly System.Action<Color> _onPick;
            public ColorPalettePopup(Color current, System.Action<Color> onPick) { _current = current; _onPick = onPick; }
            public override Vector2 GetWindowSize() => new Vector2(MetaLayer.Palette.Length * 30 + 12, 40);
            public override void OnGUI(Rect rect)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(6);
                    for (int i = 0; i < MetaLayer.Palette.Length; i++)
                    {
                        var c = MetaLayer.Palette[i];
                        Rect sw = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                        sw.y += 8;
                        EditorGUI.DrawRect(sw, c);
                        bool sel = Mathf.Abs(c.r - _current.r) < 0.02f && Mathf.Abs(c.g - _current.g) < 0.02f && Mathf.Abs(c.b - _current.b) < 0.02f;
                        DrawRectOutline(sw, sel ? Color.white : Color.black, sel ? 2f : 1f);
                        if (GUI.Button(sw, GUIContent.none, GUIStyle.none)) { _onPick(c); editorWindow.Close(); }
                        GUILayout.Space(6);
                    }
                }
            }
        }
    }
}
