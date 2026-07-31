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
    /// The Animation Builder. Load a sprite sheet, identify its sprites in one of two explicit modes
    /// — <b>Grid</b> (uniform sheets: marquee a box, give it cols×rows) or <b>Pick</b> (scattered sprites:
    /// click each one, it's flood-filled to a tight bbox) — then build an ordered <b>sequence</b> from those
    /// sprites, preview it looping, nudge each sprite's registration, and save it as a named <b>animation</b>
    /// into a reel's editable draft (see <see cref="ReelRepo"/>). Two-column layout: left =
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
    public partial class AnimationBuilderWindow : ZuiWindow
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
            public CellRef(int r, int c) { region = r; cell = c; }
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
        private enum LoopDivider { None, EmptyPause, IdleSprite }
        private LoopDivider _loopDivider = LoopDivider.None;
        private float _loopPause = 0.4f;
        private CellRef _idleRef = new CellRef(-1, -1);
        private const float CrosshairNX = 0.5f, CrosshairNY = 0.4f;
        private const float RotStepDeg = 5f; // increment for the stepwise rotation −/+ buttons

        // The PLAY preview runs the shared AnimationPlayback over a LIVE in-memory bake of the current
        // sequence — i.e. the exact same player and the exact same baked frames the game and Reel Browser
        // use, so the looping preview cannot wobble or differ from the shipped result. (The registration
        // CANVAS below is a separate authoring surface — it deliberately shows raw cells so pivots can be
        // edited; that is NOT playback and is correctly not routed through the player.)
        private readonly AnimationPlayback _previewPlayer = new AnimationPlayback();
        private AnimationDef _previewDef;        // wraps the live-baked frames for the player
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
        private float _metaZoom = 6f;          // paint-editor zoom (screen px per source px)
        private Vector2 _metaPan;              // paint-editor pan (middle-drag), screen px
        private bool _metaPainting; private int _metaLastX = -1, _metaLastY = -1;
        private bool _metaPanning; private Vector2 _metaPanLast;
        private struct MaskEntry { public int hash; public Texture2D tex; }
        private readonly Dictionary<long, MaskEntry> _maskCache = new Dictionary<long, MaskEntry>();

        // ── Save target ──────────────────────────────────────────────────────
        // Bound mode: opened from the Reel Browser — save writes back to that reel's draft
        // animation. Orphan mode (no bound reel): save writes a standalone orphaned AnimationAsset that
        // can later be included into a reel from the Reel Browser.
        private string _animName = "Idle";
        private List<FrameEvent> _events = new List<FrameEvent>(); // authored per-frame events (e.g. "hit")
        private Reel _boundReel;
        private string _boundAnimName;
        private AnimationAsset _orphanAsset;   // the orphan being edited (null = a fresh orphan)

        // ── Pending open request (applied after Show) ────────────────────────
        private Reel _pendingEditReel;
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

        [MenuItem("Laubrary/Animation Builder")]
        public static void Open()
        {
            var w = GetWindow<AnimationBuilderWindow>("Animation Builder");
            w.minSize = new Vector2(900, 640);
            w.Show();
        }

        /// <summary>Open the Animation Builder bound to a reel's draft animation (called by the Reel
        /// Builder). Saving writes back to that reel.</summary>
        public static void OpenForEdit(Reel reel, string animName)
        {
            var w = GetWindow<AnimationBuilderWindow>("Animation Builder");
            w.minSize = new Vector2(900, 640);
            w.Show();
            w._pendingEditReel = reel;
            w._pendingEditAnim = animName;
            w._pendingOrphan = null;
            w.TryApplyPending();
        }

        /// <summary>Open the Animation Builder editing a standalone orphaned animation (called by the Reel
        /// Builder's library). Saving updates that orphan.</summary>
        public static void OpenForOrphan(AnimationAsset orphan)
        {
            var w = GetWindow<AnimationBuilderWindow>("Animation Builder");
            w.minSize = new Vector2(900, 640);
            w.Show();
            w._pendingOrphan = orphan;
            w._pendingEditReel = null;
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

            if (_inDivider)
            {
                if (now >= _dividerUntil) { _inDivider = false; RestartPreview(); }
                _playIM?.MarkDirtyRepaint();
                return;
            }

            // Make sure the shared player is running our live-baked clip with the right loop mode.
            if (_previewPlayer.Anim != _previewDef || (!_previewPlayer.IsPlaying && !_inDivider)) RestartPreview();
            _previewPlayer.Tick(dt, 1f);
            _animFrame = _previewPlayer.Frame;
            // Playback only moves the playhead — repaint the islands that show it, never the whole window.
            RefreshFrameLabels();
            _playIM?.MarkDirtyRepaint();
            _seqStripIM?.MarkDirtyRepaint();
            _zoneBarIM?.MarkDirtyRepaint();
        }

        // ── live in-memory bake feeding the shared player ────────────────────────
        // Re-bakes ONLY when something that affects the frames changes (sequence, pivots, colour key, frame
        // box). The result is the same baked, uniform frames the game plays — so the preview is WYSIWYG.
        private void EnsurePreviewBake()
        {
            int h = PreviewHash();
            if (h == _previewHash && _previewFrames != null)
            {
                if (_previewDef != null) _previewDef.fps = Mathf.Max(1f, _animFps); // FPS is a free preview knob
                return;
            }
            _previewHash = h;
            DestroyPreviewBake();

            var recipe = BuildRecipe();
            if (recipe == null || recipe.Count == 0) { _previewDef = null; return; }

            var box = new AtlasBaker.FrameBox { fixedSize = _fixedFrame, w = _frameW, h = _frameH, pivot = _framePivot };
            var frames = AtlasBaker.BakeInMemory(recipe, 16f, out string err, out var tex, CurrentColorKey(), box);
            if (frames == null) { _status = "Preview bake: " + err; _previewDef = null; return; }

            _previewTex = tex;
            _previewFrames = frames;
            _previewDef = new AnimationDef { name = "__preview", fps = Mathf.Max(1f, _animFps), frames = frames, events = _events };
            RestartPreview();
        }

        private void DestroyPreviewBake()
        {
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
            _previewFrames = null; _previewDef = null;
            _previewPlayer.Stop();
        }

        // Loop gap (pause / idle frame between loops) is a preview-only nicety: play the clip non-looping and
        // schedule the gap from its completion, else just loop continuously. It wraps the player, never forks it.
        private void RestartPreview()
        {
            if (_previewDef == null) return;
            bool loop = _loopDivider == LoopDivider.None || _loopPause <= 0f;
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
                }
                return h;
            }
        }

        // ── window shell ─────────────────────────────────────────────────────
        /// A wrapping row — controls flow onto the next line instead of ever overflowing the pane.
        private static VisualElement WrapRow(params VisualElement[] children)
        {
            var row = Z.Row(children);
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        protected override void OnBeforeRebuild()
        {
            _bannerHost = _topHost = _leftControlsHost = _paletteHost = _animHost = null;
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

        protected override void BuildUI(VisualElement root)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // Sprite keys (A/D + arrow nudge) and the window's own snapshot Undo are handled on the ROOT with
            // TrickleDown so they win over a focused IMGUI island (the canvas would otherwise eat Left/Right).
            // Rebuild() clears the root's CHILDREN but keeps the root itself, so unregister before
            // registering or every rebuild would stack another handler.
            root.focusable = true;
            root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

            root.Add(Z.Help(Disclaimer, HelpBoxMessageType.Warning));

            _bannerHost = new VisualElement();
            BuildBindingBanner(_bannerHost);
            root.Add(_bannerHost);

            _topHost = new VisualElement();
            BuildTopSection(_topHost);
            root.Add(_topHost);

            _statusLabel = Z.Text(_status ?? "", ZuiText.Subtle, "The last thing this window did, or why it couldn't.");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;

            if (_sheet == null) { root.Add(_statusLabel); return; }

            _splitRow = new VisualElement();
            _splitRow.style.flexDirection = FlexDirection.Row;
            _splitRow.style.flexGrow = 1f;
            _splitRow.style.minHeight = 0f;
            root.Add(_splitRow);

            if (!_leftCollapsed)
            {
                _leftPane = new VisualElement();
                _leftPane.style.width = new Length(56f, LengthUnit.Percent);
                _leftPane.style.minWidth = 360f;
                _leftPane.style.flexShrink = 0f;
                _leftPane.style.minHeight = 0f;
                var leftScroll = new ScrollView(ScrollViewMode.Vertical);
                leftScroll.style.flexGrow = 1f;
                leftScroll.style.minHeight = 0f;

                _leftControlsHost = new VisualElement();
                BuildRegionGridUI(_leftControlsHost);
                leftScroll.contentContainer.Add(_leftControlsHost);

                _canvasIM = new IMGUIContainer(DrawCanvasGUI)
                {
                    tooltip = "The sheet. Left-drag marquees (Grid/Box) or picks a sprite (Pick); right-drag auto-detects sprites inside the marquee."
                };
                _canvasIM.style.height = Mathf.Max(240f, position.height * 0.48f);
                _canvasIM.style.flexShrink = 0f;
                leftScroll.contentContainer.Add(_canvasIM);

                _leftPane.Add(leftScroll);
                _splitRow.Add(_leftPane);
            }

            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 0f;
            rightPane.style.minHeight = 0f;
            rightPane.style.marginLeft = 4f;
            var rightScroll = new ScrollView(ScrollViewMode.Vertical);
            rightScroll.style.flexGrow = 1f;
            rightScroll.style.minHeight = 0f;

            _paletteHost = new VisualElement();
            BuildPaletteSection(_paletteHost);
            rightScroll.contentContainer.Add(_paletteHost);

            _animHost = new VisualElement();
            BuildAnimationSection(_animHost);
            rightScroll.contentContainer.Add(_animHost);

            rightPane.Add(rightScroll);
            _splitRow.Add(rightPane);

            root.Add(_statusLabel);
        }

        /// Keep the canvas viewport proportional as the window resizes (it was position.height-derived).
        private void OnRootGeometryChanged(GeometryChangedEvent _)
        {
            if (_canvasIM != null) _canvasIM.style.height = Mathf.Max(240f, position.height * 0.48f);
        }

        /// Repaint the bespoke IMGUI islands — for edits that change what's DRAWN but not which controls exist.
        private void Dirty()
        {
            if (_statusLabel != null) _statusLabel.text = _status ?? "";
            _canvasIM?.MarkDirtyRepaint();
            _regCanvasIM?.MarkDirtyRepaint();
            _paletteGridIM?.MarkDirtyRepaint();
            _playIM?.MarkDirtyRepaint();
            _seqStripIM?.MarkDirtyRepaint();
            _zoneBarIM?.MarkDirtyRepaint();
        }

        /// Structural change (selection, sequence, layers, modes…) → rebuild the control hosts in place.
        /// Cheap at editor-window scale, and the one reliable way to keep retained controls truthful.
        private void Refresh()
        {
            if (!this) return;   // a deferred refresh can outlive the window
            if (_bannerHost == null) { Dirty(); return; }
            // Gaining/losing a sheet changes the window's STRUCTURE (the two-column body only exists with
            // one), so a host-level refresh can't express it — rebuild the whole tree instead.
            if ((_sheet != null) != (_splitRow != null)) { Rebuild(); return; }
            _bannerHost.Clear(); BuildBindingBanner(_bannerHost);
            _topHost.Clear(); BuildTopSection(_topHost);
            if (_leftControlsHost != null) { _leftControlsHost.Clear(); BuildRegionGridUI(_leftControlsHost); }
            if (_paletteHost != null) { _paletteHost.Clear(); BuildPaletteSection(_paletteHost); }
            if (_animHost != null) { _animHost.Clear(); BuildAnimationSection(_animHost); }
            SetStatus(_status);
            Dirty();
        }

        /// Show a one-line result/explanation without rebuilding anything.
        private void SetStatus(string text)
        {
            _status = text;
            if (_statusLabel != null) _statusLabel.text = _status ?? "";
        }

        private void BuildBindingBanner(VisualElement root)
        {
            root.Add(Z.Help(_boundReel != null
                ? $"Editing animation '{_animName}' for reel '{_boundReel.reelName}'. Saving writes back to that reel's draft."
                : _orphanAsset != null
                    ? $"Editing orphaned animation '{_animName}'. Include it into a reel from the Reel Browser."
                    : "Authoring a new orphaned animation (not tied to a reel). Include it later from the Reel Browser."));
        }

        // ── 1 · sheet ────────────────────────────────────────────────────────
        private void BuildSheetSection(VisualElement root)
        {
            Texture2D picked = _sheet;
            var sheetField = Z.Object<Texture2D>(_sheet, "The sprite sheet to slice sprites out of.",
                v => picked = v, 200f);

            var recentButton = Z.Button("Recent ▾",
                "Pick a sheet from Assets/SpriteSheets (downloaded or previously sliced). Entries are deletable.", null).W(76f);
            recentButton.clicked += () =>
            {
                var wb = recentButton.worldBound;
                UnityEditor.PopupWindow.Show(new Rect(wb.x, wb.y, wb.width, wb.height),
                    new RecentSheetsPopup(tex => { LoadSheet(tex); DeferRefresh(); }, _sheet));
            };

            var saveButton = Z.Button("Save", "Write slicing state (regions, cells, pivots) to a JSON sidecar.",
                () => { SaveState(); Refresh(); }).W(48f);
            saveButton.SetEnabled(_sheet != null);
            var restoreButton = Z.Button("Restore", "Reload slicing state from this sheet's sidecar.",
                () => { LoadStateFromSidecar(false); Refresh(); }).W(60f);
            var clearButton = Z.Button("Clear",
                "Delete this sheet's saved slicing sidecar and empty the palette (#4). Your saved animations are not affected.",
                () => { ClearSavedSlicing(); Refresh(); }).W(48f);
            bool hasSidecar = _sheet != null && RegionSlicerPersistence.Exists(_sheetPath);
            restoreButton.SetEnabled(hasSidecar);
            clearButton.SetEnabled(hasSidecar);

            root.Add(WrapRow(
                Z.Text("1 · Sheet", ZuiText.Section, "The source image every sprite in this animation is cut from."),
                sheetField,
                Z.Button("Load", "Load the assigned sheet (re-reads its saved slicing state).",
                    () => { LoadSheet(picked); Refresh(); }).W(48f),
                recentButton,
                _sheet != null
                    ? Z.Text($"{_texW}×{_texH}px", ZuiText.Small, "The loaded sheet's pixel size.").W(80f)
                    : null,
                Z.Flexible(),
                saveButton, restoreButton, clearButton));

            var urlField = Z.TextInput(_sheetUrl, "Download an image straight into Assets/SpriteSheets and load it.",
                v => _sheetUrl = v, 0f);
            urlField.style.width = StyleKeyword.Auto;
            urlField.style.flexGrow = 1f;
            urlField.style.minWidth = 120f;
            urlField.style.maxWidth = 520f;                        // wide (URLs are long) but never runaway-wide
            urlField.AddToClassList("zui-audit-allow-stretch");   // a URL is arbitrarily long — the rulebook's exception
            var downloadButton = Z.Button("Download", "Save the image to Assets/SpriteSheets and load it as the sheet.",
                () => { DownloadSheetFromUrl(); Refresh(); }).W(80f);
            downloadButton.SetEnabled(!string.IsNullOrWhiteSpace(_sheetUrl));
            urlField.RegisterValueChangedCallback(e => downloadButton.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));
            root.Add(Z.Row(
                Z.Text("URL", ZuiText.Body, "Download an image straight into Assets/SpriteSheets and load it.").W(40f),
                urlField,
                Z.TextInput(_downloadName, "Optional display name for the downloaded sheet.", v => _downloadName = v, 120f),
                downloadButton));

            if (_sheet != null)
                root.Add(Z.Field("Name", "Friendly display name (identity stays the asset GUID — renaming is safe).",
                    Z.TextInput(_sheetDisplayName, "Friendly display name (identity stays the asset GUID — renaming is safe).",
                        v => { _sheetDisplayName = v; SheetRegistry.SetDisplayName(_sheet, v); }, 220f)));
        }

        private void DownloadSheetFromUrl()
        {
            Texture2D tex; string error;
            EditorUtility.DisplayProgressBar("Launimator", "Downloading image…", 0.5f);
            try { tex = SheetLibrary.DownloadImage(_sheetUrl, _downloadName, out error); }
            finally { EditorUtility.ClearProgressBar(); }

            if (tex == null) { SetStatus(error ?? "Download failed."); return; }
            LoadSheet(tex);
            _sheetUrl = ""; _downloadName = "";
            SetStatus($"Downloaded and loaded '{_sheetDisplayName}' into {SheetLibrary.Folder}.");
            EditorGUIUtility.PingObject(tex);
        }

        private static readonly string[] ToolModeLabels = { "Grid (uniform sheet)", "Box (one sprite)", "Pick (scattered sprites)" };
        private static readonly string[] GridModeLabels = { "Columns/Rows", "Cell Size" };

        // Short enough to sit on one segmented row beside PPU. Order matches GridSlicer.PivotMode.
        private static readonly string[] PivotLabels = { "Center", "Bottom", "Top Left", "Custom" };

        private void BuildModeBar(VisualElement root)
        {
            root.Add(WrapRow(
                Z.Text("Mode", ZuiText.Section, "How sprites are identified on the sheet."),
                Z.MiniRadio((int)_toolMode, ToolModeLabels,
                    "Grid marquees a box and slices it into cols×rows; Box commits one marquee as one sprite; Pick flood-fills the clicked sprite.",
                    v =>
                    {
                        _toolMode = (ToolMode)v;
                        if (_toolMode != ToolMode.Grid) { _hasBox = false; _box = default; }
                        Refresh();
                    }),
                Z.Text(
                    _toolMode == ToolMode.Grid ? "Marquee a box on the canvas, set its grid, Add Region."
                    : _toolMode == ToolMode.Box ? "Drag a box around one sprite — released, it's added instantly."
                    : "Click each sprite on the canvas to extract it.",
                    ZuiText.Subtle, "What the current mode does on the canvas.")));
        }

        // ── 2 · region grid UI (mode-gated) ──────────────────────────────────
        private void BuildRegionGridUI(VisualElement root)
        {
            if (!_leftCollapsed) BuildModeBar(root);

            root.Add(Z.Text("2 · Identify Sprites", ZuiText.Section,
                "Settings that decide how the canvas marquee becomes sprite cells."));

            if (_toolMode == ToolMode.Grid)
            {
                // Segmented, not MiniRadio: two single-line options is exactly what Segmented is for, and it
                // sits INSIDE the grid row instead of claiming one of its own — the fields it switches between
                // are right beside it, so the relationship reads without a separate line.
                var modeSwitch = Z.Segmented((int)_mode, GridModeLabels,
                    "Split the marquee by a column/row count, or by a fixed cell size in pixels.",
                    v => { _mode = (RegionSlicer.GridMode)v; Refresh(); });

                var gridRow = _mode == RegionSlicer.GridMode.FixedColsRows
                    ? WrapRow(
                        modeSwitch,
                        Z.Field("Cols", "How many columns the marquee is split into.",
                            Z.Int(_cols, "How many columns the marquee is split into.",
                                v => { _cols = Mathf.Max(1, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)),
                        Z.Field("Rows", "How many rows the marquee is split into.",
                            Z.Int(_rows, "How many rows the marquee is split into.",
                                v => { _rows = Mathf.Max(1, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)))
                    : WrapRow(
                        modeSwitch,
                        Z.Field("Cell W", "Each cell's width in source pixels.",
                            Z.Int(_cellW, "Each cell's width in source pixels.",
                                v => { _cellW = Mathf.Max(1, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)),
                        Z.Field("Cell H", "Each cell's height in source pixels.",
                            Z.Int(_cellH, "Each cell's height in source pixels.",
                                v => { _cellH = Mathf.Max(1, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)));
                gridRow.Add(Z.Field("Space", "Gap px BETWEEN cells.",
                    Z.Int(_spacing, "Gap px BETWEEN cells.",
                        v => { _spacing = Mathf.Max(0, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)));
                gridRow.Add(Z.Field("Pad", "Shrink px INSIDE each cell.",
                    Z.Int(_padding, "Shrink px INSIDE each cell.",
                        v => { _padding = Mathf.Max(0, v); RefreshBoxDependentLabels(); Dirty(); }, 52f)));
                root.Add(gridRow);
            }

            // Shared: ppu + pivot.
            var pivotRow = WrapRow(
                Z.Field("PPU", "Pixels-per-unit stamped onto the sprites this sheet slices.",
                    Z.Float(_ppu, "Pixels-per-unit stamped onto the sprites this sheet slices.",
                        v => _ppu = Mathf.Max(0.01f, v), 52f)),
                // Segmented, not EnumDropdown — the layout rules name Z.EnumDropdown as THE enum anti-pattern
                // (it returns a native EnumField). Four short labels fit one line, so this costs no height and
                // shows every option at a glance instead of hiding three behind a click.
                Z.Field("Pivot", "Default registration point applied to every newly-identified sprite.",
                    Z.Segmented((int)_pivot, PivotLabels,
                        "Default registration point applied to every newly-identified sprite.",
                        v => { _pivot = (GridSlicer.PivotMode)v; Refresh(); })));
            if (_pivot == GridSlicer.PivotMode.Custom)
                pivotRow.Add(Z.Vector2Field("Custom pivot", () => _customPivot, v => _customPivot = v, this,
                    new ZuiValue2DControl.Options().WithRange(0f, 1f, 0f, 1f).WithPlotSize(64f),
                    "Where the pivot sits inside each cell (0..1, y bottom-up).", Dirty));
            root.Add(pivotRow);

            // ONE "what counts as content" row: alpha trimming, the alpha threshold, and the background-colour
            // key are all the same question asked three ways, and they were three stacked rows. Wrapping means
            // a narrow pane still breaks them sensibly instead of overflowing.
            void KeyChanged() { RebuildDisplaySheet(); SaveBgKeyForSheet(); Dirty(); }
            var keyColor = Z.Color(_bgKey, "The colour treated as transparent.",
                v => { _bgKey = (Color32)v; KeyChanged(); }, 60f, showAlpha: false);
            var keyTol = Z.Int(_bgTolerance, "Per-channel match tolerance (0..255).",
                v => { _bgTolerance = Mathf.Clamp(v, 0, 255); KeyChanged(); }, 52f);
            keyColor.SetEnabled(_bgKeyEnabled);
            keyTol.SetEnabled(_bgKeyEnabled);

            var contentRow = WrapRow();
            if (_toolMode == ToolMode.Grid)
                contentRow.Add(Z.Toggle("Alpha-trim", "Snap each cell to the tight bbox of its non-transparent pixels.",
                    _alphaTrim, v => _alphaTrim = v));
            contentRow.Add(Z.Field("α >", "Alpha above this counts as content (0..255).",
                Z.Int(_alphaThreshold, "Alpha above this counts as content (0..255).",
                    v => { _alphaThreshold = Mathf.Clamp(v, 0, 255); Dirty(); }, 52f)));
            contentRow.Add(Z.Toggle("BG color", "Treat a solid background colour as transparent (sheets with no alpha).",
                _bgKeyEnabled, v => { _bgKeyEnabled = v; KeyChanged(); Refresh(); }));
            contentRow.Add(keyColor);
            contentRow.Add(Z.Field("± tol", "Per-channel match tolerance (0..255).", keyTol));
            contentRow.Add(Z.Button(_pickingBgColor ? "Click sheet…" : "Pick ☉",
                "Eyedropper: click a background pixel on the canvas to set the colour.",
                () => { _pickingBgColor = !_pickingBgColor; if (_pickingBgColor) _bgKeyEnabled = true; Refresh(); }));
            root.Add(contentRow);

            if (_toolMode == ToolMode.Grid)
            {
                void CommitBox()
                {
                    int l = _boxLField.value, t = _boxTField.value;
                    int w = Mathf.Max(1, _boxWField.value), h = Mathf.Max(1, _boxHField.value);
                    _box = ClampBox(new Rect(l, _texH - t - h, w, h));
                    RefreshBoxDependentLabels();
                    Dirty();
                }
                _boxLField = Z.Int(Mathf.RoundToInt(_box.x), "Marquee left edge, in source pixels.", _ => CommitBox(), 52f);
                _boxTField = Z.Int(Mathf.RoundToInt(_texH - _box.yMax), "Marquee top edge, in source pixels (from the sheet's top).", _ => CommitBox(), 52f);
                _boxWField = Z.Int(Mathf.RoundToInt(_box.width), "Marquee width, in source pixels.", _ => CommitBox(), 52f);
                _boxHField = Z.Int(Mathf.RoundToInt(_box.height), "Marquee height, in source pixels.", _ => CommitBox(), 52f);

                _addRegionButton = Z.Button($"Add Region ({CurrentBoxCellCount()})",
                    "Commit the marquee's grid cells into the sprite palette (undoable).",
                    () => { AddRegion(); Refresh(); }).W(120f);

                // The marquee's numbers and the two things you do to it, on ONE row — they were two, and the
                // buttons act on exactly the rect the fields describe, so splitting them read as unrelated.
                // Both halves share the same _hasBox gate, which is the giveaway that they are one control
                // group. Four 52px fields plus two buttons fit a normal pane comfortably.
                var boxRow = WrapRow(
                    Z.Text("Box", ZuiText.Small, "The current marquee's exact rect — type to place it precisely."),
                    Z.Field("L", "Marquee left edge, in source pixels.", _boxLField),
                    Z.Field("T", "Marquee top edge, in source pixels (from the sheet's top).", _boxTField),
                    Z.Field("W", "Marquee width, in source pixels.", _boxWField),
                    Z.Field("H", "Marquee height, in source pixels.", _boxHField),
                    Z.Button("Clear Box", "Drop the current marquee.",
                        () => { _hasBox = false; _box = default; Refresh(); }).W(74f),
                    _addRegionButton);
                boxRow.SetEnabled(_hasBox);
                root.Add(boxRow);
            }

            // Zoom row (shared).
            root.Add(WrapRow(
                Z.Field("Zoom", "Canvas magnification (source pixels × zoom).",
                    Z.Slider(_zoom, 0.5f, 8f, "Canvas magnification (source pixels × zoom).",
                        v => { _zoom = v; Dirty(); }, 150f)),
                Z.Button("Fit", "Re-fit the sheet to the canvas viewport.",
                    () => { _zoomInitialized = false; Refresh(); }).W(40f)));

            root.Add(Z.Text(
                _pickingBgColor ? "3 · Canvas — click a background pixel to set the transparent colour"
                : _toolMode == ToolMode.Grid ? "3 · Canvas — drag to marquee; drag interior/edges to move/resize"
                : _toolMode == ToolMode.Box ? "3 · Canvas — drag a box around one sprite (added on release)"
                : "3 · Canvas — click a sprite to extract it", ZuiText.Section,
                "The sheet viewport below, and what a click/drag does in the current mode."));
        }

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

            foreach (var reg in _regions)
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
                if (_toolMode == ToolMode.Grid)
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
            SaveBgKeyForSheet(); // a sheet's background is constant — remember it for this sheet
            _status = $"Background colour set to RGB({_bgKey.r},{_bgKey.g},{_bgKey.b}); saved for this sheet, treated as transparent.";
        }

        /// <summary>Commit the current marquee box as one sprite cell (Box mode — no grid, no settings).</summary>
        private void AddSingleCellFromBox()
        {
            Rect cell = new Rect(Mathf.Round(_box.x), Mathf.Round(_box.y), Mathf.Round(_box.width), Mathf.Round(_box.height));
            if (cell.width < 1 || cell.height < 1) return;
            RecordUndo("Add sprite");

            int idx = _regions.FindIndex(r => r.label == BoxLabel);
            if (idx < 0) { _regions.Add(new Region { label = BoxLabel, bounds = new Rect(0, 0, _texW, _texH) }); idx = _regions.Count - 1; }
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

        private void BuildPaletteSection(VisualElement root)
        {
            int total = TotalCells();

            var aseButton = Z.Button("Edit in Aseprite",
                "Export the selected sprite(s) to an owned .aseprite and open Aseprite to edit them.",
                () => { OpenSelectionInAseprite(); Refresh(); }).W(112f);
            aseButton.SetEnabled(HasSelectedCell());
            var syncButton = Z.Button("Sync edits",
                "Pull the edited .aseprite back into the palette (writes into an owned copy of the sheet).",
                () => { SyncFromAseprite(); Refresh(); }).W(78f);
            syncButton.SetEnabled(!string.IsNullOrEmpty(_editAsePath));
            var clearButton = Z.Button("Clear all", "Empty the sprite palette and the sequence (undoable).",
                () => { ClearAllCells(); Refresh(); }).W(72f);
            clearButton.SetEnabled(total > 0);

            root.Add(WrapRow(
                Z.Text($"4 · Sprite Palette ({total})", ZuiText.Section, PaletteHelp),
                Z.HelpIcon(PaletteHelp),
                Z.Flexible(),
                aseButton, syncButton, clearButton));

            if (total == 0)
                root.Add(Z.Text(_toolMode == ToolMode.Grid ? "Marquee a box and Add Region." : "Click sprites on the canvas.",
                    ZuiText.Subtle, "Nothing identified yet."));
            else
            {
                _paletteGridIM = new IMGUIContainer(DrawPaletteGridGUI)
                {
                    tooltip = PaletteHelp
                };
                _paletteGridIM.style.height = 132f;
                _paletteGridIM.style.flexShrink = 0f;
                root.Add(_paletteGridIM);
            }

            // Preview (registration canvas) on the LEFT, all its tools/controls on the RIGHT.
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            root.Add(row);

            var previewCol = new VisualElement();
            previewCol.style.width = 300f;
            previewCol.style.flexShrink = 0f;
            _regCanvasIM = new IMGUIContainer(DrawRegistrationCanvasGUI)
            {
                tooltip = "Registration stage: drag the selected sprite to move its pivot relative to the green crosshair."
            };
            _regCanvasIM.style.height = 220f;
            previewCol.Add(_regCanvasIM);
            previewCol.Add(Z.Text(HasSelectedCell()
                    ? (_fixedFrame ? "Drag to place the sprite in the box. Faint = other frames."
                                   : "Drag to align the sprite. Faint = other frames.")
                    : "Select a sprite above to place it.",
                ZuiText.Subtle, "How to use the registration stage above."));
            row.Add(previewCol);

            var toolsCol = new VisualElement();
            toolsCol.style.flexGrow = 1f;
            toolsCol.style.minWidth = 240f;
            toolsCol.style.marginLeft = 4f;
            BuildRegistrationControls(toolsCol);
            BuildSelectedCellControls(toolsCol);
            row.Add(toolsCol);
        }

        /// The sprite palette grid — an IMGUI island: a thumbnail grid with sequence badges, multi-select and a
        /// right-click actions menu. Bespoke canvas painting, exactly what the rulebook keeps raw.
        private void DrawPaletteGridGUI()
        {
            if (_paletteGridIM == null) return;
            Rect view = new Rect(0f, 0f, _paletteGridIM.layout.width, _paletteGridIM.layout.height);
            if (!(view.width > 20f)) return;

            const int cell = 56, pad = 4;
            var flat = FlattenCells();
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

        private void BuildSelectedCellControls(VisualElement root)
        {
            var sel = SelectedCells();
            if (sel.Count == 0)
            {
                root.Add(Z.Text("Select a sprite to nudge its registration (Ctrl/Shift-click for several).",
                    ZuiText.Subtle, "Nothing is selected in the palette above."));
                return;
            }
            bool multi = sel.Count > 1;

            string selLabel = multi
                ? $"{sel.Count} selected"
                : $"Sel {_regions[_selRegion].cells[_selCell].width:0}×{_regions[_selRegion].cells[_selCell].height:0}px";
            root.Add(WrapRow(
                Z.Text(selLabel, ZuiText.Small, "What the buttons on this row act on.").W(86f),
                Z.Button("←", "Nudge the registration one source pixel left.", () => { NudgeSelectedPivot(-1, 0); Dirty(); }).W(24f),
                Z.Button("→", "Nudge the registration one source pixel right.", () => { NudgeSelectedPivot(1, 0); Dirty(); }).W(24f),
                Z.Button("↓", "Nudge the registration one source pixel down.", () => { NudgeSelectedPivot(0, -1); Dirty(); }).W(24f),
                Z.Button("↑", "Nudge the registration one source pixel up.", () => { NudgeSelectedPivot(0, 1); Dirty(); }).W(24f),
                Z.Button("Baseline", "Set pivot to content bottom-center (align feet).",
                    () => { BaselineSelected(); Dirty(); }).W(66f),
                Z.Button("Head", "Set pivot to content top-center (align heads — handy for climbing/hanging).",
                    () => { TopCenterSelected(); Dirty(); }).W(48f)));

            root.Add(WrapRow(
                Z.Button(multi ? $"Add {sel.Count} → seq" : "Add → seq",
                    "Append the selected sprite(s) to the animation sequence.",
                    () => { AddSelectedToSequence(); Refresh(); }).W(96f),
                Z.Button("Trim to content", "Shrink each selected cell to the tight bbox of its non-transparent pixels.",
                    () => { TrimSelected(); Refresh(); }).W(110f),
                Z.Button(multi ? $"Duplicate ({sel.Count})" : "Duplicate",
                    "Make an independent copy of the sprite(s) so you can flip/rotate/scale the copy without affecting the original a sequence frame uses.",
                    () => { DuplicateSelectedSprites(); Refresh(); }).W(100f),
                Z.Button(multi ? $"Delete ({sel.Count})" : "Delete sprite",
                    "Remove the selected sprite(s) from the palette (frames using them are dropped).",
                    () => { DeleteSelectedCells(); Refresh(); }).W(100f)));

            // ── per-sprite EDIT (flip / rotate / squash-stretch) — baked into the frame ──────────────
            var prim = _regions[_selRegion].transforms.Count > _selCell ? _regions[_selRegion].transforms[_selCell] : CellTransform.Identity;
            root.Add(WrapRow(
                Z.Text("Edit", ZuiText.Small, "Lossless per-sprite edits, baked into the frame.").W(30f),
                Z.Button("Flip H", "Mirror horizontally (lossless).",
                    () => { MutateSelectedTransforms(t => { t.flipX = !t.flipX; return t; }); Dirty(); }).W(48f),
                Z.Button("Flip V", "Mirror vertically (lossless).",
                    () => { MutateSelectedTransforms(t => { t.flipY = !t.flipY; return t; }); Dirty(); }).W(48f),
                Z.Button("<| 90", "Rotate 90° counter-clockwise (lossless).",
                    () => { MutateSelectedTransforms(t => { t.rot90 = ((t.rot90 + 1) % 4 + 4) % 4; return t; }); Dirty(); }).W(48f),
                Z.Button("|> 90", "Rotate 90° clockwise (lossless).",
                    () => { MutateSelectedTransforms(t => { t.rot90 = ((t.rot90 - 1) % 4 + 4) % 4; return t; }); Dirty(); }).W(48f),
                Z.Button("Reset", "Clear all edits on the selected sprite(s).",
                    () => { MutateSelectedTransforms(_ => CellTransform.Identity); Refresh(); }).W(48f)));

            root.Add(WrapRow(
                Z.Text("Rot°", ZuiText.Small, "Arbitrary rotation (degrees, CCW). Resampled — use −/+ to step, or type an exact angle.").W(30f),
                Z.Button("−", "Rotate −5° (stepwise).",
                    () => { MutateSelectedTransforms(t => { t.angle -= RotStepDeg; return t; }); Refresh(); }).W(24f),
                Z.Float(prim.angle, "Exact rotation angle in degrees (CCW).",
                    v => { MutateSelectedTransforms(t => { t.angle = v; return t; }); Dirty(); }, 48f),
                Z.Button("+", "Rotate +5° (stepwise).",
                    () => { MutateSelectedTransforms(t => { t.angle += RotStepDeg; return t; }); Refresh(); }).W(24f),
                Z.Field("Scale X", "Squash/stretch horizontally (1 = none).",
                    Z.Float(prim.SX, "Squash/stretch horizontally (1 = none).",
                        v => { MutateSelectedTransforms(t => { t.scaleX = Mathf.Max(0.01f, v); return t; }); Dirty(); }, 48f)),
                Z.Field("Y", "Squash/stretch vertically (1 = none).",
                    Z.Float(prim.SY, "Squash/stretch vertically (1 = none).",
                        v => { MutateSelectedTransforms(t => { t.scaleY = Mathf.Max(0.01f, v); return t; }); Dirty(); }, 48f)),
                Z.Toggle("Smooth", "Bilinear sampling for rotate/scale (smooth but blurs); off = crisp nearest-neighbor.",
                    prim.smooth, v => { MutateSelectedTransforms(t => { t.smooth = v; return t; }); Dirty(); })));
        }

        // ── per-sprite transform edits (UI.4) ────────────────────────────────
        private void MutateSelectedTransforms(System.Func<CellTransform, CellTransform> fn)
        {
            if (!HasSelectedCell()) return;
            RecordUndo("Transform sprite");
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
            RecordUndo("Clear all sprites");
            _regions.Clear(); ClearThumbCache();
            _sequence.Clear();
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
            RecordUndo("Delete sprite");

            var del = new HashSet<long>();
            foreach (var cr in toDelete) del.Add(PackCell(cr.region, cr.cell));

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
                newSeq.Add(new CellRef(nr, nc));
            }
            int removedFrames = _sequence.Count - newSeq.Count;
            _sequence.Clear(); _sequence.AddRange(newSeq);

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
                menu.AddItem(new GUIContent("Duplicate frame"), false, () => { RecordUndo("Duplicate frame"); _sequence.Insert(i + 1, _sequence[i]); SeqSelectSingle(i + 1); Refresh(); });
                menu.AddItem(new GUIContent("Remove frame"), false, () => { RecordUndo("Remove frame"); _sequence.RemoveAt(i); _seqMultiSel.Clear(); if (_seqSelected >= _sequence.Count) _seqSelected = _sequence.Count - 1; if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected); Refresh(); });
            }
            menu.AddSeparator("");
            if (SeqRefValid(cr))
                menu.AddItem(new GUIContent("Edit source sprite (select in #4)"), false, () => { SelectSingle(cr.region, cr.cell); Refresh(); });
            menu.AddItem(new GUIContent("Set as idle (loop gap)"), false, () => { _idleRef = cr; _loopDivider = LoopDivider.IdleSprite; Refresh(); });
            menu.ShowAsContext();
        }

        // ── registration: frame box + drag-to-place ──────────────────────────
        // Registration SETTINGS only (the canvas/preview is drawn separately, to its left). See DrawCellsPreview.
        private static readonly string[] RegistrationModeLabels = { "Auto size", "Fixed box" };

        private void BuildRegistrationControls(VisualElement root)
        {
            root.Add(WrapRow(
                Z.Text("Registration", ZuiText.Section,
                    "How big each baked frame is, and where the shared pivot sits inside it."),
                Z.MiniRadio(_fixedFrame ? 1 : 0, RegistrationModeLabels,
                    "Auto size fits the frame box to the sequence; Fixed box pins an exact W×H every frame is placed in.",
                    v =>
                    {
                        bool wasFixed = _fixedFrame;
                        _fixedFrame = v == 1;
                        // Switching INTO fixed mode: seed the box from the current auto layout so nothing jumps.
                        if (_fixedFrame && !wasFixed) FitFrameBox();
                        Refresh();
                    })));

            if (_fixedFrame)
                root.Add(WrapRow(
                    Z.Field("W", "Fixed frame width in source pixels.",
                        Z.Int(_frameW, "Fixed frame width in source pixels.",
                            v => { _frameW = Mathf.Max(1, v); Dirty(); }, 52f)),
                    Z.Field("H", "Fixed frame height in source pixels.",
                        Z.Int(_frameH, "Fixed frame height in source pixels.",
                            v => { _frameH = Mathf.Max(1, v); Dirty(); }, 52f)),
                    Z.Button("Fit", "Size the box to hold every frame at its current placement.",
                        () => { FitFrameBox(); Refresh(); }).W(40f)));

            root.Add(WrapRow(
                Z.Text("Ghosts", ZuiText.Small,
                    "Onion-skin: faint copies of the neighbouring sequence frames, drawn behind the one you're aligning.").W(46f),
                Z.Field("<", "How many frames BEFORE the current one to ghost.",
                    Z.Int(_ghostBefore, "How many frames BEFORE the current one to ghost.",
                        v => { _ghostBefore = Mathf.Clamp(v, 0, 99); Dirty(); }, 40f)),
                Z.Field(">", "How many frames AFTER the current one to ghost.",
                    Z.Int(_ghostAfter, "How many frames AFTER the current one to ghost.",
                        v => { _ghostAfter = Mathf.Clamp(v, 0, 99); Dirty(); }, 40f)),
                Z.Field("Opacity", "Ghost transparency. Drag to 0 to hide ghosts.",
                    Z.Slider(_ghostOpacity, 0f, 1f, "Ghost transparency. Drag to 0 to hide ghosts.",
                        v => { _ghostOpacity = v; Dirty(); }, 110f))));
        }

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

            // Integer zoom: one source pixel = exactly `scale` screen pixels, so a 1-px nudge is a clean,
            // visible step and pixel-art frames stay crisp (no bilinear smear that reads as "½-pixel" drift).
            float fit = Mathf.Min((canvas.width - 16f) / vw, (canvas.height - 16f) / vh);
            float scale = Mathf.Clamp(Mathf.Floor(fit), 1f, 12f);
            float vwS = vw * scale, vhS = vh * scale;
            // FIXED registration point: the crosshair must NOT move when a pivot is nudged. It used to be derived
            // from the auto-box (which is computed FROM the pivots), so nudging the frame that defines the box's
            // extent shifted the box with it and the sprite looked pinned (the "← → don't nudge" bug). Pin the
            // crosshair to the canvas and let the guide box float around it instead.
            float crossX = Mathf.Round(canvas.x + canvas.width * 0.5f);
            float crossY = Mathf.Round(canvas.y + canvas.height * 0.78f); // low: feet-registered sprites extend upward
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

            if (_ghostOpacity > 0.001f)
            {
                int anchor = AnchorSequenceIndex();
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
                        DrawCellAtAnchor(_regions[cr.region].cells[cr.cell], _regions[cr.region].pivots[cr.cell], crossX, crossY, scale, _ghostOpacity);
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
                    DrawCellAtAnchor(reg.cells[_selCell], reg.pivots[_selCell], crossX, crossY, scale, 1f);
            }

            // Registration crosshair.
            var cross = new Color(0.2f, 1f, 0.5f, 0.9f);
            EditorGUI.DrawRect(new Rect(crossX - 8, crossY - 0.5f, 16, 1f), cross);
            EditorGUI.DrawRect(new Rect(crossX - 0.5f, crossY - 8, 1f, 16), cross);

            HandleRegistrationDrag(canvas, scale);
        }

        private void DrawCellAtAnchor(Rect cell, Vector2 pivot, float cx, float cy, float scale, float alpha)
            => DrawFrameRegistered(cell, pivot, cx, cy, scale, alpha);

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

            var px = GetPixels();
            if (px == null) return;
            var block = AtlasBaker.TransformCell(px, _texW, _texH, reg.cells[_selCell], t, key, reg.pivots[_selCell],
                out int w, out int h, out Vector2 pvN);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(block); tex.Apply();
            _selXformTex = tex; _selXformW = w; _selXformH = h; _selXformPivot = pvN;
        }

        /// <summary>The ONE frame visualiser for this window (registration canvas, onion-skin, AND playback).
        /// It registers a frame by the EXACT math <see cref="AtlasBaker"/> bakes with — trim to content, snap
        /// the pivot→content offset to whole SOURCE pixels — so what you align here is pixel-identical to the
        /// baked atlas the Reel Browser previews. Previously this positioned in screen space without the
        /// source-pixel snap, so the editor showed a sub-pixel drift the bake didn't have (un-nudge-able).</summary>
        private void DrawFrameRegistered(Rect cell, Vector2 pivot, float cx, float cy, float scale, float alpha)
        {
            AtlasBaker.FrameRegistration(GetPixels(), _texW, _texH, cell, pivot, CurrentColorKey(), out Rect b, out Vector2 off);
            int offX = AtlasBaker.SnapOffset(off.x), offY = AtlasBaker.SnapOffset(off.y); // consistent round-half-up — same as the bake
            float w = b.width * scale, h = b.height * scale;
            // Crosshair (cx,cy) IS the pivot. Content sits offX right / offY up from it (source px → screen,
            // y inverted). Round to whole screen pixels so an integer zoom stays crisp on the pixel grid.
            Rect draw = new Rect(Mathf.Round(cx - offX * scale), Mathf.Round(cy - (b.height - offY) * scale), w, h);
            Rect uv = new Rect(b.x / _texW, b.y / _texH, b.width / _texW, b.height / _texH);
            Color prev = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(draw, SheetForDisplay(), uv, true);
            GUI.color = prev;
        }

        private void HandleRegistrationDrag(Rect canvas, float scale)
        {
            if (!HasSelectedCell()) return;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                _regDragging = true; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _regDragging)
            {
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
                _regDragging = false; e.Use();
            }
        }

        /// <summary>Auto frame box for the current sequence: the smallest box holding every frame's trimmed
        /// content around the shared registration anchor — the same math the auto-size bake uses.</summary>
        private void ComputeAutoBox(out int fw, out int fh, out int anchorX, out int anchorY)
        {
            float leftMax = 1, rightMax = 1, belowMax = 1, aboveMax = 1;
            var px = GetPixels();
            var key = CurrentColorKey();
            bool any = false;
            foreach (var cr in _sequence)
            {
                if (!SeqRefValid(cr)) continue;
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                Rect cell = reg.cells[cr.cell];
                Vector2 pv = reg.pivots[cr.cell];
                Rect b = px != null ? RegionSlicer.TrimToContent(px, _texW, _texH, cell, _alphaThreshold, out bool empty, key) : cell;
                if (px != null && b.width <= 0) b = cell;
                float pivPxX = cell.x + pv.x * cell.width, pivPxY = cell.y + pv.y * cell.height;
                float ox = pivPxX - b.x, oy = pivPxY - b.y;
                leftMax = Mathf.Max(leftMax, ox); rightMax = Mathf.Max(rightMax, b.width - ox);
                belowMax = Mathf.Max(belowMax, oy); aboveMax = Mathf.Max(aboveMax, b.height - oy);
                any = true;
            }
            if (!any && HasSelectedCell())
            {
                Rect cell = _regions[_selRegion].cells[_selCell];
                leftMax = cell.width * 0.5f; rightMax = cell.width * 0.5f; belowMax = 0; aboveMax = cell.height;
            }
            fw = Mathf.Max(1, Mathf.CeilToInt(leftMax + rightMax));
            fh = Mathf.Max(1, Mathf.CeilToInt(belowMax + aboveMax));
            anchorX = Mathf.RoundToInt(leftMax);
            anchorY = Mathf.RoundToInt(belowMax);
        }

        private void FitFrameBox()
        {
            ComputeAutoBox(out int fw, out int fh, out int ax, out int ay);
            _frameW = fw; _frameH = fh;
            _framePivot = new Vector2((float)ax / fw, (float)ay / fh);
        }

        // ── 5 · animation (sequence + preview + save) ────────────────────────
        private static readonly string[] LoopDividerLabels = { "None", "Pause", "Idle" };

        private void BuildAnimationSection(VisualElement root)
        {
            // keep refs valid
            _sequence.RemoveAll(cr => !SeqRefValid(cr));
            if (_sequence.Count > 0) _animFrame %= _sequence.Count; else _animFrame = 0;
            if (_seqSelected >= _sequence.Count) _seqSelected = _sequence.Count - 1;
            _seqMultiSel.RemoveWhere(k => k < 0 || k >= _sequence.Count);
            SyncMetaFrames();

            root.Add(Z.Text($"5 · Animation — sequence ({_sequence.Count})", ZuiText.Section,
                "The ordered frames this animation plays, plus everything saved alongside them."));

            // Animation PREVIEW (doubles as the meta paint editor) on the LEFT, all tools on the RIGHT.
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            root.Add(row);

            _playIM = new IMGUIContainer(DrawPlayAreaGUI)
            {
                tooltip = _metaEnabled
                    ? "Meta paint editor: left-drag paints, right-drag erases, middle-drag pans."
                    : "The live animation, played by the same AnimationPlayback the game uses."
            };
            _playIM.style.width = 320f;
            _playIM.style.height = 220f;
            _playIM.style.flexShrink = 0f;
            row.Add(_playIM);

            var toolsCol = new VisualElement();
            toolsCol.style.flexGrow = 1f;
            toolsCol.style.minWidth = 240f;
            toolsCol.style.marginLeft = 4f;
            BuildAnimationTools(toolsCol);
            if (_metaEnabled) BuildMetaLayersPanel(toolsCol);
            row.Add(toolsCol);

            _seqStripIM = new IMGUIContainer(DrawSequenceStripGUI)
            {
                tooltip = "The sequence. Drag to reorder · Ctrl/Shift-click = multi-select · Right-click → menu."
            };
            _seqStripIM.style.height = 96f;
            _seqStripIM.style.flexShrink = 0f;
            root.Add(_seqStripIM);
            root.Add(Z.Text("Drag to reorder · Ctrl/Shift-click = multi-select · Right-click → menu · (Reverse/Duplicate/Delete in the tools panel).",
                ZuiText.Subtle, "How to work the sequence strip above."));

            BuildZoneTrack(root);
            BuildEvents(root);
            BuildSaveRow(root);
        }

        // The tools to the RIGHT of the animation preview: play, fps, loop gap, frame ops, meta toggle.
        private void BuildAnimationTools(VisualElement root)
        {
            _playToggleButton = Z.Button(_animPlaying ? "❚❚" : "▶", "Play or pause the looping preview.", () =>
            {
                _animPlaying = !_animPlaying;
                _playToggleButton.text = _animPlaying ? "❚❚" : "▶";
                _playIM?.MarkDirtyRepaint();
            }).W(36f);
            root.Add(WrapRow(
                _playToggleButton,
                Z.Field("FPS", "Preview playback speed — also what the saved animation plays at.",
                    Z.Slider(_animFps, 1f, 30f, "Preview playback speed — also what the saved animation plays at.",
                        v => { _animFps = v; Dirty(); }, 170f))));

            var loopRow = WrapRow(
                Z.Text("Loop gap", ZuiText.Small,
                    "A preview-only pause between loops. Never saved into the animation.").W(58f),
                Z.MiniRadio((int)_loopDivider, LoopDividerLabels,
                    "None loops seamlessly; Pause holds an empty gap; Idle holds a chosen sprite during the gap.",
                    v => { _loopDivider = (LoopDivider)v; Refresh(); }));
            if (_loopDivider != LoopDivider.None)
                loopRow.Add(Z.Field("s", "How long the loop gap lasts, in seconds.",
                    Z.Float(Mathf.Max(0f, _loopPause), "How long the loop gap lasts, in seconds.",
                        v => { _loopPause = Mathf.Max(0f, v); Dirty(); }, 52f)));
            root.Add(loopRow);

            if (_loopDivider == LoopDivider.IdleSprite)
            {
                var idleButton = Z.Button("Set idle = selected sprite",
                    "Use the sprite selected in the palette as the frame shown during the loop gap.",
                    () => { _idleRef = new CellRef(_selRegion, _selCell); Refresh(); }).W(200f);
                idleButton.SetEnabled(HasSelectedCell());
                root.Add(idleButton);
            }

            int selCount = _seqMultiSel.Count;
            var reverse = Z.Button("Reverse", "Reverse the order of the selected frames.",
                () => { ReverseSelectedFrames(); Refresh(); }).W(74f);
            reverse.SetEnabled(selCount >= 2);
            var dupFrames = Z.Button(selCount > 1 ? $"Duplicate ({selCount})" : "Duplicate",
                "Copy the selected frames in as a block just after the selection.",
                () => { DuplicateSelectedFrames(); Refresh(); }).W(96f);
            var delFrames = Z.Button(selCount > 1 ? $"Delete ({selCount})" : "Delete",
                "Remove the selected frames from the sequence.",
                () => { DeleteSelectedFrames(); Refresh(); }).W(86f);
            dupFrames.SetEnabled(selCount > 0);
            delFrames.SetEnabled(selCount > 0);
            root.Add(WrapRow(reverse, dupFrames, delFrames));

            root.Add(WrapRow(
                Z.Toggle("Meta layers", "Gameplay overlays (hitbox/muzzle/trail) drawn over the sequence. Off keeps the UI clean.",
                    _metaEnabled, v => { _metaEnabled = v; Refresh(); }),
                Z.Flexible(),
                Z.Button("Clear seq", "Empty the sequence (undoable).", () =>
                {
                    RecordUndo("Clear sequence");
                    _sequence.Clear(); _seqSelected = -1; _animFrame = 0;
                    Refresh();
                }).W(74f)));
        }

        /// The play box / meta paint editor — an IMGUI island either way (pivot-anchored atlas blits, and a
        /// zoomable per-pixel paint surface with pan).
        private void DrawPlayAreaGUI()
        {
            if (_playIM == null) return;
            var box = new Rect(0f, 0f, _playIM.layout.width, _playIM.layout.height);
            if (!(box.width > 10f) || !(box.height > 10f)) return;
            if (_metaEnabled) DrawMetaEditor(box); else DrawPlayBox(box);
        }

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

        private void BuildMetaLayersPanel(VisualElement root)
        {
            root.Add(WrapRow(
                Z.Text("Layers", ZuiText.Small, "Gameplay overlay masks painted per frame.").W(44f),
                Z.Button("+ Layer", "Add a meta-layer (e.g. hitbox, muzzle, trail).", () =>
                {
                    RecordUndo("Add layer");
                    _metaLayers.Add(new MetaLayer { id = $"layer{_metaLayers.Count + 1}", color = MetaLayer.Palette[_metaLayers.Count % MetaLayer.Palette.Length] });
                    _activeLayer = _metaLayers.Count - 1; SyncMetaFrames();
                    Refresh();
                }).W(66f)));

            for (int i = 0; i < _metaLayers.Count; i++)
            {
                int li = i;
                var L = _metaLayers[i];
                var lrow = Z.Row();

                lrow.Add(Z.Button(i == _activeLayer ? "●" : "○", "Make this the layer you're painting.",
                    () => { _activeLayer = li; Refresh(); }).W(22f));
                lrow.Add(Z.TextInput(L.id, "This layer's id — how the game looks the mask up.",
                    v => { RecordUndo("Rename layer"); L.id = v; }, 108f));

                var solid = new Color(L.color.r, L.color.g, L.color.b, 1f);
                var swatch = Z.Button("", "Pick this layer's colour from the shared palette.", null).W(28f);
                swatch.style.height = 16f;
                swatch.style.backgroundColor = solid;
                swatch.clicked += () =>
                {
                    var wb = swatch.worldBound;
                    UnityEditor.PopupWindow.Show(new Rect(wb.x, wb.y, wb.width, wb.height), new ColorPalettePopup(solid, c =>
                    {
                        RecordUndo("Layer colour");
                        var cc = c; cc.a = _metaLayers[li].color.a; _metaLayers[li].color = cc;
                        ClearMaskCache(); Refresh();
                    }));
                };
                lrow.Add(swatch);

                lrow.Add(Z.Button("X", "Remove this layer.", () =>
                {
                    RecordUndo("Remove layer");
                    _metaLayers.RemoveAt(li);
                    if (_activeLayer >= _metaLayers.Count) _activeLayer = _metaLayers.Count - 1;
                    ClearMaskCache(); Refresh();
                }).W(22f));
                root.Add(lrow);
            }

            var layer = ActiveLayer();
            if (layer == null)
            {
                root.Add(Z.Text("No layers. \"+ Layer\" adds one.", ZuiText.Subtle, "Add a meta-layer to start painting."));
                return;
            }

            root.Add(Z.Field("Opacity", "Display transparency for this layer's mask.",
                Z.Slider(layer.color.a, 0.1f, 1f, "Display transparency for this layer's mask.",
                    v => { var c = layer.color; c.a = v; layer.color = c; ClearMaskCache(); Dirty(); }, 130f)));

            int brushIdx = System.Array.FindIndex(BrushSizes, b => b.x == _brushW && b.y == _brushH);
            root.Add(WrapRow(
                Z.Text("Brush", ZuiText.Small, "Paint footprint, in mask cells.").W(44f),
                Z.MiniRadio(Mathf.Max(0, brushIdx), BrushLabels, "Paint footprint, in mask cells.",
                    v => { _brushW = BrushSizes[v].x; _brushH = BrushSizes[v].y; })));

            root.Add(WrapRow(
                Z.Toggle("Values", "Per-pixel value 1–10 as a channel. Off = always paint value 5. Any value triggers a hit unless the consumer reads it.",
                    _metaShowValues, v => { _metaShowValues = v; if (!v) _paintValue = 5; Refresh(); }),
                Z.Flexible(),
                Z.Button("Clear frame", "Erase this layer's mask on the current frame.",
                    () => { ClearActiveFrame(); Dirty(); }).W(86f)));

            if (_metaShowValues)
                root.Add(WrapRow(
                    Z.Text("Value", ZuiText.Small, "1 = darkest … 5 = layer colour … 10 = brightest. Erase = right-click.").W(40f),
                    Z.MiniRadio(Mathf.Clamp(_paintValue - 1, 0, 9), PaintValueLabels,
                        "1 = darkest … 5 = layer colour … 10 = brightest. Erase = right-click.",
                        v => _paintValue = v + 1)));

            root.Add(WrapRow(
                Z.Field("Zoom", "Paint-editor magnification (screen px per source px).",
                    Z.Slider(_metaZoom, 2f, 24f, "Paint-editor magnification (screen px per source px).",
                        v => { _metaZoom = Mathf.Round(v); Dirty(); }, 110f)),
                Z.Button("Center", "Recentre the paint view.",
                    () => { _metaPan = Vector2.zero; Dirty(); }).W(56f)));

            int f = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : -1;
            if (f >= 0 && f < layer.frames.Count)
            {
                // The param always edits whatever frame is CURRENT (the playhead moves without a rebuild),
                // so read the index at commit time, not at build time.
                _metaParamField = Z.TextInput(layer.frames[f].param ?? "",
                    "Free-text parameter for this frame on the active layer.",
                    v =>
                    {
                        var L = ActiveLayer();
                        int at = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : -1;
                        if (L == null || at < 0 || at >= L.frames.Count) return;
                        RecordUndo("Frame param"); L.frames[at].param = v;
                    }, 160f);
                var wrap = Z.Field($"F{f + 1} param", "Free-text parameter for this frame on the active layer.", _metaParamField);
                _metaParamLabel = wrap.Q<Label>(className: "zui-field__label");
                root.Add(wrap);
            }
            root.Add(Z.Text("Left-drag = paint · right-drag = erase · middle-drag = pan.", ZuiText.Subtle,
                "How to paint on the editor to the left."));
        }

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

        /// <summary>The preview box turned into a zoomable paint editor for the active layer on the current
        /// (scrubbed) frame: baked sprite underneath, the layer's value mask on top, paint with the brush.</summary>
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
            MetaFrame mf = null;
            if (layer != null && f < layer.frames.Count) { mf = layer.frames[f]; mf.EnsureSize(fw, fh); }

            float z = _metaZoom, dw = fw * z, dh = fh * z;
            GUI.BeginClip(box);
            float ox = Mathf.Round((box.width - dw) * 0.5f + _metaPan.x), oy = Mathf.Round((box.height - dh) * 0.5f + _metaPan.y);

            var sp = _previewFrames[f];
            if (sp != null && sp.texture != null)
            {
                var t = sp.texture; if (t.filterMode != FilterMode.Point) t.filterMode = FilterMode.Point;
                Rect uv = new Rect(sp.rect.x / t.width, sp.rect.y / t.height, sp.rect.width / t.width, sp.rect.height / t.height);
                var pc = GUI.color; GUI.color = new Color(1f, 1f, 1f, mf != null ? 0.6f : 1f); // dim under the mask
                GUI.DrawTextureWithTexCoords(new Rect(ox, oy, dw, dh), t, uv, true);
                GUI.color = pc;
            }
            if (mf != null)
            {
                var mtex = MaskTexture(_activeLayer, f);
                if (mtex != null) GUI.DrawTexture(new Rect(ox, oy, dw, dh), mtex, ScaleMode.StretchToFill, true);
                HandleMetaPaint(box, ox, oy, z, fw, fh, mf);
            }
            GUI.EndClip();

            GUI.Label(new Rect(box.x + 4, box.yMax - 16, 320, 16),
                layer != null ? $"Editing '{layer.id}' · frame {f + 1}/{_sequence.Count}" : "Add a layer below to draw.",
                EditorStyles.whiteMiniLabel);
        }

        private void HandleMetaPaint(Rect box, float ox, float oy, float z, int fw, int fh, MetaFrame mf)
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
                RecordUndo("Paint");
                PaintBrush(mf, cx, cy, fw, fh, (e.button == 1 || e.control) ? 0 : _paintValue);
                _metaPainting = true; _metaLastX = cx; _metaLastY = cy; e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && _metaPainting && inCell)
            {
                if (cx != _metaLastX || cy != _metaLastY)
                { PaintBrush(mf, cx, cy, fw, fh, (e.button == 1 || e.control) ? 0 : _paintValue); _metaLastX = cx; _metaLastY = cy; }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && _metaPainting) { _metaPainting = false; e.Use(); }
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
        // consumer sync weapon damage / a projectile to the swing's contact frame via ReelPlayer.OnFrameEvent.
        // Frames are shown 1-based to match the sequence strip badges; stored 0-based.
        private void BuildEvents(VisualElement root)
        {
            const string eventsTip =
                "Authored per-frame events the game reacts to (e.g. \"hit\" to sync weapon damage to the contact frame). Fired by ReelPlayer.OnFrameEvent.";
            var box = Z.Box($"Frame events ({_events.Count})", eventsTip);

            int cur = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : 0;
            _addEventButton = Z.Button($"+ event @ frame {cur + 1}",
                "Add a named event on the frame shown in the preview, then rename it below (e.g. hit, footstep, Lift Off).",
                () =>
                {
                    RecordUndo("Add event");
                    int at = _sequence.Count > 0 ? Mathf.Clamp(_animFrame, 0, _sequence.Count - 1) : 0;
                    _events.Add(new FrameEvent { frame = at, name = "event" });
                    Refresh();
                }).W(150f);
            _addEventButton.SetEnabled(_sequence.Count > 0);
            box.Add(Z.Row(Z.Flexible(), _addEventButton));

            int maxFrame = Mathf.Max(0, _sequence.Count - 1);
            for (int e = 0; e < _events.Count; e++)
            {
                int idx = e;
                var ev = _events[e];
                var evRow = WrapRow(
                    Z.Field("frame", "Which sequence frame (1-based) this event fires on.",
                        Z.Int(ev.frame + 1, "Which sequence frame (1-based) this event fires on.",
                            v => { RecordUndo("Move event"); ev.frame = Mathf.Clamp(v, 1, maxFrame + 1) - 1; Dirty(); }, 52f)),
                    Z.TextInput(ev.name, "The event's name — what the game listens for.",
                        v => { RecordUndo("Rename event"); ev.name = v; }, 140f));

                string zoundLabel = string.IsNullOrEmpty(ev.zoundName) ? "— zound —" : ev.zoundName;
                var zoundButton = Z.Button(zoundLabel,
                    "Zound auto-played when this event fires, via the Launimator.Zounds bridge. Click to pick.", null).W(90f);
                zoundButton.clicked += () =>
                {
                    var wb = zoundButton.worldBound;
                    ZoundPickerPopup.Show(new Vector2(wb.x, wb.yMax), picked =>
                    { RecordUndo("Set event zound"); _events[idx].zoundName = picked; Refresh(); });
                };
                evRow.Add(zoundButton);
                evRow.Add(Z.Button("X", "Remove this event.",
                    () => { RecordUndo("Remove event"); _events.RemoveAt(idx); Refresh(); }).W(22f));
                box.Add(evRow);
            }

            if (_events.Count == 0)
                box.Add(Z.Text("No events. Add one to tag a frame with a named signal the game reacts to — e.g. \"hit\", \"footstep\", \"Lift Off\" (fired via OnFrameEvent).",
                    ZuiText.Subtle, eventsTip));
            root.Add(box);
        }

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
            float cx = box.x + box.width * CrosshairNX, cy = box.y + box.height * (1f - CrosshairNY);

            if (_inDivider)
            {
                // Loop-gap: idle frame (an authoring overlay — the idle sprite may be outside the sequence) or
                // an empty pause. Not the looping playback.
                if (_loopDivider == LoopDivider.IdleSprite && SeqRefValid(_idleRef))
                {
                    Rect ic = _regions[_idleRef.region].cells[_idleRef.cell];
                    float s = Mathf.Clamp(Mathf.Min((box.width - 16f) / ic.width, (box.height - 16f) / ic.height), 0.25f, 8f);
                    DrawFrameAtCrosshair(_idleRef, cx, cy, s, 1f);
                }
            }
            else if (_previewFrames != null && _previewFrames.Count > 0)
            {
                // The ONE player + the ONE frame drawer over the live-baked frames — identical to the game.
                int shown = _animPlaying ? _previewPlayer.Frame : Mathf.Clamp(_animFrame, 0, _previewFrames.Count - 1);
                FramePreview.DrawClip(box, _previewFrames, shown, CrosshairNX, 1f - CrosshairNY, 8f);
            }

            var cross = new Color(0.2f, 1f, 0.5f, 0.9f);
            EditorGUI.DrawRect(new Rect(cx - 8, cy - 0.5f, 16, 1f), cross);
            EditorGUI.DrawRect(new Rect(cx - 0.5f, cy - 8, 1f, 16), cross);
        }

        private void DrawFrameAtCrosshair(CellRef cr, float cx, float cy, float scale, float alpha)
        {
            var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
            DrawFrameRegistered(reg.cells[cr.cell], reg.pivots[cr.cell], cx, cy, scale, alpha);
        }

        /// The sequence strip — an IMGUI island: a thumbnail grid with zone borders, playhead/selection
        /// outlines, ordinal badges and drag-to-reorder.
        private void DrawSequenceStripGUI()
        {
            if (_seqStripIM == null) return;
            Rect view = new Rect(0f, 0f, _seqStripIM.layout.width, _seqStripIM.layout.height);
            if (!(view.width > 20f)) return;

            const int cell = 46, pad = 4;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((view.width - 18f) / (cell + pad)));
            int rows = Mathf.CeilToInt(_sequence.Count / (float)perRow);
            Rect content = new Rect(0, 0, view.width - 18f, Mathf.Max(view.height, rows * (cell + pad)));

            _seqScroll = GUI.BeginScrollView(view, _seqScroll, content);
            for (int i = 0; i < _sequence.Count; i++)
            {
                Rect r = new Rect((i % perRow) * (cell + pad), (i / perRow) * (cell + pad), cell, cell);
                EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
                DrawCellThumb(r, _sequence[i].region, _sequence[i].cell);
                if (_metaEnabled && _activeLayer >= 0)
                {
                    var mtex = MaskTexture(_activeLayer, i);
                    if (mtex != null) GUI.DrawTexture(r, mtex, ScaleMode.ScaleToFit, true);
                }

                // Zone border: a thick outline in the frame's zone colour (drawn under the selection outline).
                if (TryFrameZone(i, out Color zcol)) DrawRectOutline(r, zcol, 3f);

                bool sel = IsSeqSelected(i), primary = i == _seqSelected, playing = i == _animFrame && !_inDivider;
                DrawRectOutline(r, sel ? new Color(1f, 0.85f, 0.1f, 1f) : playing ? new Color(0.2f, 1f, 0.5f, 0.9f) : new Color(1f, 1f, 1f, 0.4f), sel ? (primary ? 2.5f : 1.5f) : playing ? 2f : 1f);
                var badge = new Rect(r.x, r.y, 16, 14);
                EditorGUI.DrawRect(badge, new Color(0.2f, 0.5f, 1f, 0.92f));
                GUI.Label(badge, (i + 1).ToString(), EditorStyles.whiteMiniLabel);

                HandleSeqDrag(i, r);
            }
            GUI.EndScrollView();
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
            RecordUndo("Reverse frames");
            var items = idx.Select(k => _sequence[k]).ToList();
            items.Reverse();
            for (int j = 0; j < idx.Count; j++) _sequence[idx[j]] = items[j];
            foreach (var L in _metaLayers) // mirror the reverse onto each layer's masks
            {
                var mi = idx.Where(k => k < L.frames.Count).ToList();
                var its = mi.Select(k => L.frames[k]).ToList(); its.Reverse();
                for (int j = 0; j < mi.Count; j++) L.frames[mi[j]] = its[j];
            }
            _status = $"Reversed {idx.Count} frames.";
            Repaint();
        }

        private void DuplicateSelectedFrames()
        {
            var idx = _seqMultiSel.Where(k => k >= 0 && k < _sequence.Count).OrderBy(k => k).ToList();
            if (idx.Count == 0) return;
            RecordUndo("Duplicate frames");

            // Copy the selected frames as ONE contiguous block (in sequence order) and insert it right AFTER the
            // last selected frame, pushing everything past it forward. (Not interleaved per-original.)
            int insertAt = idx[idx.Count - 1] + 1;
            var block = idx.Select(k => _sequence[k]).ToList();
            _sequence.InsertRange(insertAt, block);

            // Mirror the same block onto each meta-layer's per-frame masks so they stay aligned with the sequence.
            foreach (var L in _metaLayers)
            {
                var maskBlock = idx.Where(k => k < L.frames.Count).Select(k => L.frames[k].Clone()).ToList();
                L.frames.InsertRange(Mathf.Clamp(insertAt, 0, L.frames.Count), maskBlock);
            }

            // Select the freshly-inserted block.
            _seqMultiSel.Clear();
            for (int j = 0; j < block.Count; j++) _seqMultiSel.Add(insertAt + j);
            _seqSelected = insertAt + block.Count - 1;
            _seqAnchor = insertAt;
            _status = $"Duplicated {block.Count} frame(s) after the selection.";
            Repaint();
        }

        private void DeleteSelectedFrames()
        {
            var idx = _seqMultiSel.Where(k => k >= 0 && k < _sequence.Count).OrderByDescending(k => k).ToList();
            if (idx.Count == 0) return;
            RecordUndo("Delete frames");
            int first = idx.Min();
            foreach (int k in idx) _sequence.RemoveAt(k);
            foreach (var L in _metaLayers) foreach (int k in idx) if (k < L.frames.Count) L.frames.RemoveAt(k); // keep masks aligned
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
                    RecordUndo("Reorder frame");
                    var moved = _sequence[_seqDragFrom];
                    _sequence.RemoveAt(_seqDragFrom);
                    _sequence.Insert(Mathf.Clamp(i, 0, _sequence.Count), moved);
                    foreach (var L in _metaLayers) if (_seqDragFrom < L.frames.Count) // mask follows its frame
                    { var mfm = L.frames[_seqDragFrom]; L.frames.RemoveAt(_seqDragFrom); L.frames.Insert(Mathf.Clamp(i, 0, L.frames.Count), mfm); }
                    SeqSelectSingle(i);
                }
                _seqDragFrom = -1; e.Use(); DeferRefresh();
            }
        }

        /// Rebuild the control hosts AFTER the current IMGUI pass — an island must never destroy itself
        /// while it is drawing.
        private void DeferRefresh() => EditorApplication.delayCall += Refresh;

        private void BuildSaveRow(VisualElement root)
        {
            root.Add(Z.VSpace());
            bool bound = _boundReel != null;
            bool newOrphan = !bound && _orphanAsset == null;
            root.Add(Z.Text(bound
                    ? $"Save → reel '{_boundReel.reelName}'"
                    : (_orphanAsset != null ? "Save → orphaned animation" : "Save → new orphaned animation"),
                ZuiText.Section, "Where the Save button below writes this animation."));

            // The name only needs editing when authoring a brand-new orphan. When editing an existing
            // animation (bound or an existing orphan) the target is fixed, so show it read-only.
            if (newOrphan)
                root.Add(Z.Field("Animation name", "The name this animation is saved under.",
                    Z.TextInput(_animName, "The name this animation is saved under.",
                        v => { RecordUndo("Rename animation"); _animName = v; }, 200f)));
            else
                root.Add(Z.Text($"Animation name: {_animName}", ZuiText.Body,
                    "The animation this window is bound to — fixed while editing an existing one."));

            var saveButton = Z.Button(bound ? $"Save to '{_boundReel.reelName}'" : "Save orphaned animation",
                "Write this sequence (plus events, meta-layers and zones) to its save target.",
                () => { DoSave(); Refresh(); }).W(260f).H(26f);
            saveButton.SetEnabled(_sequence.Count > 0);
            root.Add(saveButton);

            if (!bound)
                root.Add(Z.Text("Orphaned animations are included into a reel from the Reel Browser.",
                    ZuiText.Subtle, "How an orphan later becomes part of a reel."));

            root.Add(Z.Button("Open Reel Browser", "Open the Reel Browser window.",
                () => ReelBrowserWindow.Open()).W(180f));
        }

        /// <summary>The recipe (per-frame source rect + pivot) for the current sequence. The source sheet is
        /// NOT modified — the reel/orphan bakes its own atlas from this (see AtlasBaker).</summary>
        private List<FrameRef> BuildRecipe()
        {
            string guid = AssetDatabase.AssetPathToGUID(_sheetPath);
            var recipe = new List<FrameRef>(_sequence.Count);
            foreach (var cr in _sequence)
            {
                var reg = _regions[cr.region]; reg.SyncPivots(GlobalPivot());
                recipe.Add(new FrameRef { sourceTextureGuid = guid, cell = reg.cells[cr.cell], pivot = reg.pivots[cr.cell], transform = reg.transforms[cr.cell] });
            }
            return recipe;
        }

        private void DoSave()
        {
            if (string.IsNullOrWhiteSpace(_animName)) { _status = "Name the animation first."; return; }
            if (_sequence.Count == 0) { _status = "Sequence is empty — add sprites first."; return; }

            var def = new AnimationDef
            {
                name = _animName, fps = _animFps, recipe = BuildRecipe(),
                events = new List<FrameEvent>(_events),
                sourceTextureGuid = AssetDatabase.AssetPathToGUID(_sheetPath),
                bgKeyEnabled = _bgKeyEnabled, bgKey = _bgKey, bgKeyTolerance = _bgTolerance,
                fixedFrame = _fixedFrame, frameWidth = _frameW, frameHeight = _frameH, framePivot = _framePivot,
                metaLayersEnabled = _metaEnabled, metaLayers = _metaLayers,
                zonesEnabled = _zonesEnabled, zones = ZonesForSave()
            };
            try
            {
                if (_boundReel != null)
                {
                    // Renaming a bound animation: drop the old entry so we don't leave a stale copy.
                    if (!string.IsNullOrEmpty(_boundAnimName) &&
                        !string.Equals(_boundAnimName, _animName, System.StringComparison.OrdinalIgnoreCase))
                        ReelRepo.RemoveAnimationFromDraft(_boundReel, _boundAnimName);
                    ReelRepo.SaveAnimationToDraft(_boundReel, def);
                    _boundAnimName = _animName;
                    _status = $"Saved '{_animName}' ({def.recipe.Count} frames) to '{_boundReel.reelName}'.";
                    EditorGUIUtility.PingObject(_boundReel);
                }
                else
                {
                    _orphanAsset = AnimationLibrary.Save(def, _orphanAsset);
                    _status = $"Saved orphaned animation '{_animName}' ({def.recipe.Count} frames). Include it from the Reel Browser.";
                    EditorGUIUtility.PingObject(_orphanAsset);
                }
                // NOTE: deliberately do NOT write the sheet's slicing sidecar here. An animation's frames live
                // on its AnimationDef (and restore via the recipe on Edit); writing them into the SHEET metadata
                // conflated the two and accumulated every animation's sprites into the sheet (the #4-pollution
                // bug). The sidecar is now written only by the explicit "Save" slicing button.
                foreach (var w in Resources.FindObjectsOfTypeAll<ReelBrowserWindow>())
                    w.ExternalRefresh();
            }
            catch (System.Exception ex)
            {
                _status = "Save failed: " + ex.Message; Debug.LogException(ex);
            }
        }

        /// <summary>Rebuild the sequence (and committed cells) from an animation's recipe, for editing. Handles
        /// an empty recipe (a freshly-created animation) by just clearing and keeping the name/fps.</summary>
        private void LoadAnimationIntoSequence(AnimationDef def)
        {
            _animName = def.name;
            _animFps = def.fps <= 0f ? 12f : def.fps;
            _events = def.events != null ? new List<FrameEvent>(def.events) : new List<FrameEvent>();
            _sequence.Clear();
            ClearSelection();

            if (def.recipe != null && def.recipe.Count > 0)
            {
                // Build the palette from ONLY this animation's frames. Load the source texture for its pixels,
                // but then CLEAR the regions: LoadSheet restores the sheet's saved slicing sidecar, which holds
                // the LAST-edited animation's working set — leaving it would append the previous animation's
                // sprites in front of this one's (the reported bug). The recipe below repopulates #4 cleanly.
                var tex = !string.IsNullOrEmpty(def.sourceTextureGuid)
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(def.sourceTextureGuid))
                    : null;
                if (tex != null) LoadSheet(tex);
                _regions.Clear();

                foreach (var f in def.recipe)
                {
                    CellRef cr = FindOrCreateCellForFrame(f);
                    if (SeqRefValid(cr)) _sequence.Add(cr);
                }
            }
            else
            {
                _regions.Clear();
            }

            _seqSelected = _sequence.Count > 0 ? 0 : -1;
            _seqMultiSel.Clear(); if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected); _seqAnchor = _seqSelected;
            _animFrame = 0;
            // The animation's own saved key + registration win over the sheet's sidecar.
            _bgKeyEnabled = def.bgKeyEnabled; _bgKey = def.bgKey; _bgTolerance = def.bgKeyTolerance;
            _fixedFrame = def.fixedFrame; _frameW = Mathf.Max(1, def.frameWidth); _frameH = Mathf.Max(1, def.frameHeight); _framePivot = def.framePivot;
            LoadZonesFrom(def);
            _metaEnabled = def.metaLayersEnabled;
            _metaLayers = def.metaLayers ?? new List<MetaLayer>();
            _activeLayer = _metaLayers.Count > 0 ? 0 : -1; ClearMaskCache();
            RebuildDisplaySheet();
            _status = _sequence.Count > 0
                ? $"Loaded '{def.name}' ({_sequence.Count} frames) for editing."
                : $"'{def.name}' is empty — load a sheet and build it.";
            Refresh();
        }

        /// <summary>Match a recipe frame to an existing committed cell (by rect AND transform), else synthesize
        /// one so it shows in #4. Matching on the transform too keeps a duplicated-then-edited sprite (same
        /// source rect, different transform) independent across save/reload. Restores pivot + transform.</summary>
        private CellRef FindOrCreateCellForFrame(FrameRef f)
        {
            for (int ri = 0; ri < _regions.Count; ri++)
            {
                _regions[ri].SyncPivots(GlobalPivot());
                for (int ci = 0; ci < _regions[ri].cells.Count; ci++)
                    if (RectApprox(_regions[ri].cells[ci], f.cell) && TransformEq(_regions[ri].transforms[ci], f.transform))
                    {
                        _regions[ri].pivots[ci] = f.pivot;
                        return new CellRef(ri, ci);
                    }
            }

            int idx = _regions.FindIndex(r => r.label == "imported");
            if (idx < 0) { _regions.Add(new Region { label = "imported", bounds = f.cell }); idx = _regions.Count - 1; }
            var reg = _regions[idx]; reg.SyncPivots(GlobalPivot());
            reg.cells.Add(f.cell);
            reg.pivots.Add(new Vector2(Mathf.Clamp01(f.pivot.x), Mathf.Clamp01(f.pivot.y)));
            reg.transforms.Add(f.transform);
            return new CellRef(idx, reg.cells.Count - 1);
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
            if (_pendingEditReel != null && !string.IsNullOrEmpty(_pendingEditAnim))
            {
                var def = ReelRepo.GetDraftAnimation(_pendingEditReel, _pendingEditAnim);
                if (def != null)
                {
                    _boundReel = _pendingEditReel;
                    _boundAnimName = _pendingEditAnim;
                    _orphanAsset = null;
                    LoadAnimationIntoSequence(def);
                }
            }
            else if (_pendingOrphan != null)
            {
                _orphanAsset = _pendingOrphan;
                _boundReel = null; _boundAnimName = null;
                LoadAnimationIntoSequence(_pendingOrphan.animation);
            }
            _pendingEditReel = null; _pendingEditAnim = null; _pendingOrphan = null;
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

        private void AppendToSequence(int region, int cell)
        {
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
            var region = new Region { label = label, cells = cells, bounds = _box };
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

            int idx = _regions.FindIndex(r => r.label == PickedLabel);
            if (idx < 0) { _regions.Add(new Region { label = PickedLabel, bounds = whole }); idx = _regions.Count - 1; }
            var reg = _regions[idx];
            reg.cells.Add(bbox); reg.SyncPivots(GlobalPivot());
            SelectSingle(idx, reg.cells.Count - 1);
            _status = $"Picked {bbox.width:0}×{bbox.height:0}px sprite. Double-click it in #4 to add to the sequence.";
        }

        // ── sheet load + importer crispening + auto-restore ──────────────────
        private void LoadSheet(Texture2D tex)
        {
            _sheet = tex;
            _regions.Clear(); ClearThumbCache(); ClearMaskCache();
            _metaEnabled = false; _metaLayers = new List<MetaLayer>(); _activeLayer = -1;
            _hasBox = false; _box = default; _status = null;
            _zoomInitialized = false; _pixelCache = null; _pixelCacheFor = null;
            ClearSelection(); _sequence.Clear(); _seqSelected = -1; _animFrame = 0;
            _bgKeyEnabled = false; _pickingBgColor = false; DestroyDisplaySheet();

            if (_sheet == null) { _sheetPath = null; _texW = _texH = 0; _sheetDisplayName = ""; return; }
            _sheetPath = AssetDatabase.GetAssetPath(_sheet);
            _sheetDisplayName = SheetRegistry.GetDisplayName(_sheet);

            var importer = AssetImporter.GetAtPath(_sheetPath) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (!importer.isReadable) { importer.isReadable = true; changed = true; }
                if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                // Rips are often very tall; cap at Unity's max so the sheet isn't downsampled — which blurs the
                // pixel art and makes every slice imprecise.
                if (importer.maxTextureSize < 16384) { importer.maxTextureSize = 16384; changed = true; }
                if (changed) { importer.SaveAndReimport(); _sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(_sheetPath); }

                importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
                if (_sheet.width < srcW || _sheet.height < srcH)
                    _status = $"⚠ This sheet is {srcW}×{srcH}, larger than Unity's {importer.maxTextureSize}px limit, so it was " +
                              $"downscaled to {_sheet.width}×{_sheet.height} — it will look blurry and slice imprecisely. " +
                              "Split the sheet into smaller images before extracting.";
            }
            _texW = _sheet.width; _texH = _sheet.height;
            if (string.IsNullOrEmpty(_status)) _status = $"Loaded {_texW}×{_texH} sheet.";

            if (RegionSlicerPersistence.Exists(_sheetPath))
            {
                var saved = RegionSlicerPersistence.Load(_sheetPath, out _);
                if (saved != null) { ApplyState(saved); _status = $"Loaded sheet — restored {_regions.Count} region(s), {TotalCells()} sprite(s)."; }
            }
            else if (TryAutoDetectBgKey(true)) // fresh/just-downloaded sheet with no saved settings → guess the bg
            {
                _status += $" Auto-detected background RGB({_bgKey.r},{_bgKey.g},{_bgKey.b}) — treated as transparent (toggle off if wrong).";
            }
        }

        // ── persistence (slicing state only; animations live on the reel) ─
        private RegionSlicerPersistence.StateDto BuildState()
        {
            var s = new RegionSlicerPersistence.StateDto
            {
                textureGuid = string.IsNullOrEmpty(_sheetPath) ? null : AssetDatabase.AssetPathToGUID(_sheetPath),
                texturePath = _sheetPath, texW = _texW, texH = _texH,
                animFps = _animFps, alphaTrim = _alphaTrim, alphaThreshold = _alphaThreshold,
                ppu = _ppu, pivotMode = (int)_pivot, customPivot = new RegionSlicerPersistence.Vec2Dto(_customPivot),
                gridMode = (int)_mode, cols = _cols, rows = _rows, cellW = _cellW, cellH = _cellH, spacing = _spacing, padding = _padding,
                bgKeyEnabled = _bgKeyEnabled, bgKeyR = _bgKey.r, bgKeyG = _bgKey.g, bgKeyB = _bgKey.b, bgKeyTolerance = _bgTolerance,
                fixedFrame = _fixedFrame, frameWidth = _frameW, frameHeight = _frameH, framePivotX = _framePivot.x, framePivotY = _framePivot.y,
            };
            Vector2 g = GlobalPivot();
            foreach (var reg in _regions)
            {
                reg.SyncPivots(g);
                var rd = new RegionSlicerPersistence.RegionDto { label = reg.label, bounds = new RegionSlicerPersistence.RectDto(reg.bounds) };
                foreach (var c in reg.cells) rd.cells.Add(new RegionSlicerPersistence.RectDto(c));
                foreach (var p in reg.pivots) rd.pivots.Add(new RegionSlicerPersistence.Vec2Dto(p));
                s.regions.Add(rd);
            }
            return s;
        }

        private void ApplyState(RegionSlicerPersistence.StateDto s)
        {
            RegionSlicerPersistence.Normalize(s);
            _animFps = s.animFps <= 0f ? 8f : s.animFps;
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
            _fixedFrame = s.fixedFrame;
            _frameW = Mathf.Max(1, s.frameWidth); _frameH = Mathf.Max(1, s.frameHeight);
            _framePivot = new Vector2(s.framePivotX, s.framePivotY);

            _regions.Clear();
            foreach (var rd in s.regions)
            {
                var reg = new Region { label = rd.label ?? "" };
                if (rd.bounds != null) reg.bounds = rd.bounds.ToRect();
                foreach (var c in rd.cells) reg.cells.Add(c.ToRect());
                foreach (var p in rd.pivots) reg.pivots.Add(p.ToVec2());
                reg.SyncPivots(GlobalPivot());
                _regions.Add(reg);
            }
            ClearSelection(); _sequence.Clear(); _seqSelected = -1; _animFrame = 0;
            RebuildDisplaySheet();
        }

        private void SaveState(bool silent = false)
        {
            if (_sheet == null || string.IsNullOrEmpty(_sheetPath)) { if (!silent) _status = "Load a sheet first."; return; }
            try { string path = RegionSlicerPersistence.Save(_sheetPath, BuildState()); if (!silent) _status = $"Saved slicing state → {path}"; }
            catch (System.Exception ex) { if (!silent) _status = "Save failed: " + ex.Message; }
        }

        /// <summary>Wipe this sheet's saved slicing sidecar and empty the palette (#4). Saved animations are
        /// untouched — their frames live on the AnimationDef recipe, not the sheet metadata. Use it to clean a
        /// sheet whose sidecar got polluted before the animation-save/sidecar coupling was removed.</summary>
        private void ClearSavedSlicing()
        {
            if (_sheet == null || string.IsNullOrEmpty(_sheetPath)) { _status = "Load a sheet first."; return; }
            if (!EditorUtility.DisplayDialog("Clear saved slicing?",
                "Delete this sheet's saved slicing sidecar and empty the sprite palette (#4)?\n\nYour saved animations are NOT affected — their frames are stored on the animation, not the sheet.",
                "Clear", "Cancel")) return;

            bool existed = RegionSlicerPersistence.Delete(_sheetPath);
            // The bg key is a genuine sheet property — preserve it by re-writing a minimal sidecar if it was set.
            _regions.Clear();
            _sequence.Clear(); _seqSelected = -1; _seqMultiSel.Clear(); _seqAnchor = -1; _animFrame = 0;
            ClearSelection();
            if (_bgKeyEnabled) SaveBgKeyForSheet();
            _status = existed ? "Cleared this sheet's saved slicing (palette emptied)." : "No saved slicing to clear (palette emptied).";
        }

        /// <summary>Persist the background colour key to THIS sheet's sidecar — it's a property of the sheet, so
        /// it's remembered for every future load/animation. Patches only the bg fields of an existing sidecar
        /// (slicing left intact); if none exists yet, writes a minimal sidecar carrying just the bg key.</summary>
        private void SaveBgKeyForSheet()
        {
            if (_sheet == null || string.IsNullOrEmpty(_sheetPath)) return;
            try
            {
                var s = RegionSlicerPersistence.Exists(_sheetPath)
                    ? RegionSlicerPersistence.Load(_sheetPath, out _)
                    : null;
                if (s == null) { s = BuildState(); s.regions?.Clear(); } // no sidecar yet — don't impose slicing
                s.bgKeyEnabled = _bgKeyEnabled;
                s.bgKeyR = _bgKey.r; s.bgKeyG = _bgKey.g; s.bgKeyB = _bgKey.b;
                s.bgKeyTolerance = _bgTolerance;
                RegionSlicerPersistence.Save(_sheetPath, s);
            }
            catch { /* persistence is best-effort; never block editing on a save failure */ }
        }

        /// <summary>If no background key is active, try to auto-detect a solid background colour (no-alpha rips)
        /// and apply it. Returns true when one was detected &amp; enabled. No-op if a key is already set or the
        /// sheet uses real alpha. <paramref name="persist"/> writes it to the sheet sidecar so it's remembered.</summary>
        private bool TryAutoDetectBgKey(bool persist)
        {
            if (_bgKeyEnabled || _sheet == null) return false;
            var px = GetPixels();
            if (px == null) return false;
            if (!RegionSlicer.TryDetectBackgroundColor(px, _texW, _texH, out Color32 c)) return false;
            _bgKey = c; _bgKeyEnabled = true;
            RebuildDisplaySheet();
            if (persist) SaveBgKeyForSheet();
            return true;
        }

        private void LoadStateFromSidecar(bool silentIfMissing)
        {
            if (_sheet == null) { if (!silentIfMissing) _status = "Load a sheet first."; return; }
            var s = RegionSlicerPersistence.Load(_sheetPath, out string error);
            if (s == null) { if (!silentIfMissing) _status = "Restore: " + (error ?? "no saved state."); return; }
            ApplyState(s);
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
        private Rect TrimCell(Rect cell, out bool empty)
        {
            empty = false; var px = GetPixels();
            if (px == null) return cell;
            return RegionSlicer.TrimToContent(px, _texW, _texH, cell, _alphaThreshold, out empty, CurrentColorKey());
        }

        // ── draw + coords ────────────────────────────────────────────────────
        private void DrawCellTex(Rect dst, Rect texCell)
        {
            Rect uv = new Rect(texCell.x / _texW, texCell.y / _texH, texCell.width / _texW, texCell.height / _texH);
            float aspect = texCell.width / Mathf.Max(1f, texCell.height);
            Rect draw = aspect > 1f
                ? new Rect(dst.x, dst.y + (dst.height - dst.width / aspect) / 2, dst.width, dst.width / aspect)
                : new Rect(dst.x + (dst.width - dst.height * aspect) / 2, dst.y, dst.height * aspect, dst.height);
            GUI.DrawTextureWithTexCoords(draw, SheetForDisplay(), uv, true);
        }

        /// <summary>Draw a palette/sequence tile for (<paramref name="region"/>,<paramref name="cell"/>) WITH its
        /// edit applied, so an edited sprite's tile looks edited. Identity cells use the cheap raw path; edited
        /// cells draw a cached transformed texture (rebuilt only when the cell or its transform changes).</summary>
        private void DrawCellThumb(Rect dst, int region, int cell)
        {
            var reg = _regions[region]; reg.SyncPivots(GlobalPivot());
            Rect texCell = reg.cells[cell];
            var t = reg.transforms[cell];
            if (t.IsIdentity) { DrawCellTex(dst, texCell); return; }

            var tex = ThumbTexture(region, cell, texCell, t);
            if (tex == null) { DrawCellTex(dst, texCell); return; }
            float aspect = tex.width / Mathf.Max(1f, tex.height);
            Rect draw = aspect > 1f
                ? new Rect(dst.x, dst.y + (dst.height - dst.width / aspect) / 2, dst.width, dst.width / aspect)
                : new Rect(dst.x + (dst.width - dst.height * aspect) / 2, dst.y, dst.height * aspect, dst.height);
            GUI.DrawTexture(draw, tex, ScaleMode.StretchToFill, true);
        }

        private Texture2D ThumbTexture(int region, int cell, Rect texCell, CellTransform t)
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
            var px = GetPixels();
            if (px == null) { _thumbCache.Remove(key); return null; }
            var block = AtlasBaker.TransformCell(px, _texW, _texH, texCell, t, CurrentColorKey(),
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

        // ── Undo: RecordUndo() throughout; the snapshot stack lives in AnimationBuilderWindow.Undo.cs ──
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
