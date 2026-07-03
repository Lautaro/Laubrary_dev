// ZUIStyleEditorWindow.cs
// Open via:  Tools / ZUI / Zeditor

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// ── Editor window ─────────────────────────────────────────────────────────────

public class ZUIStyleEditorWindow : ZUIWindow
{
    // The style editor uses the ZUI editor sheet for its chrome, but manages preview scoping internally.
    protected override string ConsumerSheetName => ZUI.EditorSheetConsumerName;
    protected override string RootBoxStyle => null; // style editor manages its own layout

    // Dev flag — expose built-in ZUI sheets (Editor, Default) in the sheet dropdown.
    // Consumers shouldn't normally edit these, but while developing ZUI it's convenient.
    const bool k_ShowBuiltInSheetsInDropdown = true;


    // ── State ─────────────────────────────────────────────────────────────────

    private ZUIStyleSheetAsset _sheet;

    private int _activeTab;        // 0 = Buttons, 1 = Boxes, 2 = Text, 3 = Sliders, 4 = Global, 5 = Palette, 6 = Assets, 7 = Missing
    private int _globalSubTab;     // 0 = Button, 1 = Box, 2 = Layout
    private int _selectedButton;
    private int _selectedBox;
    private int _selectedText;
    private int _selectedSlider;
    private int _buttonStateTab;      // 0 = Normal, 1 = Hover, 2 = Active
    private int _sliderThumbModeTab;  // 0 = Normal (single), 1 = MinMax (two thumbs)
    private int _sliderThumbMinState; // 0 = Normal, 1 = Hover, 2 = Active  (min thumb inspector)
    private int _sliderThumbMaxState; // 0 = Normal, 1 = Hover, 2 = Active  (max thumb inspector)
    private int _sliderThumbCenterState; // 0 = Normal, 1 = Hover  (bipolar center thumb inspector)

    [SerializeField] private string _previewButtonText   = "Button";
    [SerializeField] private bool   _previewToggleValue  = false;
    [SerializeField] private bool   _previewIsToggleMode = false;  // false=Button preview, true=Toggle preview
    [SerializeField] private int    _buttonPreviewBgMode  = 0;  // 0=None, 1=Box
    [SerializeField] private int    _buttonPreviewBoxIndex = 0;
    private string _previewBoxTitle     = "Box Title";
    private string _previewBoxContent   = "Sample content that wraps when the box is narrow enough to show word wrap in action.";
    private string _previewTextContent       = "Sample text with this style";
    private int    _textPreviewBgMode        = 0;  // 0=None, 1=Box, 2=Button
    private int    _textPreviewBoxIndex      = 0;
    private int    _textPreviewButtonIndex   = 0;

    private Vector2 _listScroll;
    private Vector2 _inspectorScroll;

    private enum ListMode { Collapsed, MiniHover, FullyExpanded }
    private ListMode _listMode     = ListMode.FullyExpanded;
    private bool     _listHovered  = false;  // mouse is over the strip or list area
    private const float k_StripWidth    = 20f;
    private const float k_MiniWidth     = 130f;

    // Legacy corner preview — when true, DrawManualButton renders with r=0
    private bool _simulateLegacy;

    private GUIStyle _listItemStyle;
    private GUIStyle _listItemActiveStyle;
    private GUIStyle _tabTitleStyle;

    private const float k_ListWidthFull = 290f;  // fully expanded — name + up/down/flash/dup/copy/paste/del
    private static float k_LabelWidth => ZUI.LabelWidthWide;

    // Palette layout constants
    private const float k_PaletteNameWidth   = 120f; // palette name text field width
    private const float k_PaletteDetailIndent = 122f; // indent for auto-palette detail rows (name + 2px)
    private const float k_PaletteTrailingPad  = 44f;  // trailing space matching action buttons width
    private const float k_FlashButtonPad      = 44f;  // horizontal space matching flash button column

    // ── Foldout state ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

    bool GetFoldout(string key)  => !_foldouts.TryGetValue(key, out var v) || v;   // default = expanded
    void SetFoldout(string key, bool v) => _foldouts[key] = v;

    // ── Copy / paste clipboards ───────────────────────────────────────────────

    private static ZUIButtonDef _clipButton;
    private static ZUIBoxDef    _clipBox;

    // Shared clipboards — copy from any section, paste to any compatible section.
    private static ZUIColor _clipBg;              // backgrounds (btn normal/hover/active, box bg, right-click gradient)
    private static ZUIBorderDef _clipBorder;         // borders (btn normal/hover/active, box border)
    private static ZUITextDef  _clipText;            // text (btn normal/hover/active, box title/content)
    private static string      _clipTextStyleId;     // textStyleId at copy time ("" = inline)
    private static (int r, bool useGlobal)?   _clipShape;   // shape (btn, box)
    private static (ZUIPaddingDef pad, bool useGlobal)? _clipPadding; // padding (btn, box)
    private static ZUIDropShadowDef _clipShadow;          // shadow (btn, box)
    private static ZUIBoxDef    _clipTrack;               // slider track/trackFill
    private static ZUIButtonDef _clipThumb;               // slider thumb

    // ── Open ──────────────────────────────────────────────────────────────────

    [MenuItem("Tools/ZUI/Zeditor")]
    public static void Open() => GetWindow<ZUIStyleEditorWindow>("Zeditor");

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnZUIEnable()
    {
        var lastPath = EditorPrefs.GetString("ZUIStyleEditor_LastSheet", "");
        if (string.IsNullOrEmpty(lastPath)) lastPath = ZUI.k_DefaultSheetPath;
        var asset = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(lastPath);
        if (asset != null) SetSheet(asset);
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        ZUI.SimulateLegacyCorners = false;
        if (_sheet != null)
            EditorPrefs.SetString("ZUIStyleEditor_LastSheet", AssetDatabase.GetAssetPath(_sheet));
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    void OnUndoRedo()
    {
        // After undo/redo, Unity has restored the sheet's serialized state.
        // BumpVersion invalidates all def caches and increments the version counter
        // so ZUIWindow subclasses (Showcase, etc.) detect the change and repaint.
        if (_sheet != null) _sheet.BumpVersion();
        RepaintShowcase();
        Repaint();
    }

    // ── Undo support ─────────────────────────────────────────────────────────
    // Undo is captured via RegisterCompleteObjectUndo on MouseDown in OnZUI.
    // One snapshot per click/drag cycle — reset on MouseUp.
    [NonSerialized] bool _undoSnapshotTaken;

    // ── Deferred palette repaint ─────────────────────────────────────────────
    // During continuous color changes (color picker drag), we skip the expensive
    // RepaintShowcase/BumpVersion. Flushed when a frame passes without a color change.
    [NonSerialized] bool _paletteDirtyDeferred;
    [NonSerialized] bool _paletteChangedThisGUI;  // set during EndChangeCheck, cleared each OnZUI

    // ── OnZUI ─────────────────────────────────────────────────────────────────

    protected override void OnZUI()
    {
        // On MouseDown: capture full undo snapshot before any control handles the event
        if (_sheet != null && Event.current.type == EventType.MouseDown && !_undoSnapshotTaken)
        {
            Undo.RegisterCompleteObjectUndo(_sheet, "Edit ZUI Style");
            _undoSnapshotTaken = true;
        }
        // On MouseUp: allow next interaction to take a new snapshot
        if (Event.current.type == EventType.MouseUp)
            _undoSnapshotTaken = false;

        // Flush deferred palette repaint: if we have a pending flush and no color change
        // happened this OnGUI pass, the drag has ended — safe to do the expensive rebuild.
        // Check on Layout events only (once per OnGUI cycle, before Repaint).
        if (_paletteDirtyDeferred && !_paletteChangedThisGUI && Event.current.type == EventType.Layout)
        {
            _paletteDirtyDeferred = false;
            EditorUtility.SetDirty(_sheet);
            RepaintShowcase();
        }
        _paletteChangedThisGUI = false;
        EnsureStyles();

        // Draw "ZUI Editor Window" background across the full window
        var windowBoxDef = ZUI.EditorSheet?.FindBoxOrNull("ZUI Editor Window");
        if (windowBoxDef != null && Event.current.type == EventType.Repaint)
        {
            var fullRect = new Rect(0, 0, position.width, position.height);
            windowBoxDef.DrawBackground(fullRect);
        }

        DrawTopBar();
        DrawSkinBar();
        ZUI.VerticalSpace("V Control Gap");

        if (_sheet == null) { DrawNoSheetUI(); return; }

        DrawTabBar();

        var contentRect = new Rect(0, 56f, position.width, position.height - 56f);
        GUILayout.BeginArea(contentRect);

        if (_activeTab == 4)
        {
            using (new EditorGUI.DisabledGroupScope(IsSkinLocked))
                DrawGlobalInspector();
        }
        else if (_activeTab == 5)
        {
            DrawPaletteTab();
        }
        else if (_activeTab == 6)
        {
            DrawAssetsTab();
        }
        else if (_activeTab == 7)
        {
            DrawMissingTab();
        }
        else
        {
            GUILayout.BeginHorizontal();
            DrawListPanel(); // Selection always works; add/delete locked internally
            DrawDivider();
            DrawInspectorPanel(); // Locked internally when skin active
            GUILayout.EndHorizontal();
        }

        GUILayout.EndArea();
    }

    // ── Top bar ───────────────────────────────────────────────────────────────

    void DrawTopBar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Quick-switch dropdown for registered sheets
        var allNames = ZUI.GetRegisteredConsumerNames();
        // Exclude the editor sheet itself — it's always option 0
        var consumerNames = System.Array.FindAll(allNames, n => n != ZUI.EditorSheetConsumerName);

        // Build dropdown: "Zeditor Zheet" (+ "Default Zheet" when dev flag is on) + non-editor consumers
        bool showDefault = k_ShowBuiltInSheetsInDropdown;
        int builtInCount = showDefault ? 2 : 1;
        var sheetOptions = new string[consumerNames.Length + builtInCount];
        sheetOptions[0] = "Zeditor Zheet";
        if (showDefault) sheetOptions[1] = "Default Zheet";
        for (int i = 0; i < consumerNames.Length; i++) sheetOptions[i + builtInCount] = consumerNames[i];

        // Find current selection
        int current = 0;
        if (_sheet != null)
        {
            if (_sheet == ZUI.EditorSheet) current = 0;
            else if (showDefault && _sheet == ZUI.DefaultSheet) current = 1;
            else
            {
                for (int i = 0; i < consumerNames.Length; i++)
                {
                    if (ZUI.GetConsumerSheet(consumerNames[i]) == _sheet) { current = i + builtInCount; break; }
                }
            }
        }

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(current, sheetOptions, EditorStyles.toolbarPopup, GUILayout.Width(120f));
        if (EditorGUI.EndChangeCheck())
        {
            if (picked == 0)
                SetSheet(ZUI.EditorSheet);
            else if (showDefault && picked == 1)
                SetSheet(ZUI.DefaultSheet);
            else
                SetSheet(ZUI.GetConsumerSheet(consumerNames[picked - builtInCount]));
        }

        EditorGUILayout.LabelField("Zheet:", GUILayout.Width(40f));
        var newSheet = EditorGUILayout.ObjectField(_sheet, typeof(ZUIStyleSheetAsset), false,
            GUILayout.Width(140f)) as ZUIStyleSheetAsset;
        if (newSheet != _sheet) SetSheet(newSheet);
        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(36f)))
            CreateNewSheet();

        if (_sheet != null)
        {
            ZUI.VerticalSpace("V Section Rows");

            // ── Skin selector (replaces icon library) ────────────────────
            EditorGUILayout.LabelField("Skin:", GUILayout.Width(34f));
            var skinNames = _sheet.GetSkinNames();
            var options = new string[skinNames.Length + 1];
            options[0] = "— None (Base) —";
            for (int i = 0; i < skinNames.Length; i++) options[i + 1] = skinNames[i];
            int currentIdx = _sheet.activeSkinIndex + 1;
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(currentIdx, options, EditorStyles.toolbarPopup, GUILayout.Width(130f));
            if (EditorGUI.EndChangeCheck())
            {
                _sheet.activeSkinIndex = newIdx - 1;
                ZUI.InvalidateAllStyles();
                EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
            }

            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(20f)))
            {
                _sheet.CreateSkin($"Skin {_sheet.skins.Count + 1}");
                _sheet.activeSkinIndex = _sheet.skins.Count - 1;
                ZUI.InvalidateAllStyles();
                EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
            }

            using (new EditorGUI.DisabledGroupScope(!_sheet.IsSkinActive))
            {
                if (GUILayout.Button("−", EditorStyles.toolbarButton, GUILayout.Width(20f)) && _sheet.IsSkinActive)
                {
                    if (EditorUtility.DisplayDialog("Delete Skin",
                        $"Delete skin \"{_sheet.ActiveSkin.name}\"?", "Delete", "Cancel"))
                    {
                        _sheet.skins.RemoveAt(_sheet.activeSkinIndex);
                        _sheet.activeSkinIndex = -1;
                        ZUI.InvalidateAllStyles();
                        EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
                    }
                }
            }

            // Skin name (editable when active)
            if (_sheet.IsSkinActive)
            {
                ZUI.VerticalSpace("V Control Gap");
                EditorGUI.BeginChangeCheck();
                _sheet.ActiveSkin.name = EditorGUILayout.TextField(_sheet.ActiveSkin.name, EditorStyles.toolbarTextField, GUILayout.Width(100f));
                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_sheet);
            }
        }

        GUILayout.FlexibleSpace();

        // Production mode indicator + toggle
        if (_sheet != null)
        {
            if (_sheet.productionMode)
            {
                var prodLabel = new GUIStyle(EditorStyles.toolbarButton);
                prodLabel.normal.textColor = new Color(1f, 0.6f, 0.2f, 1f);
                GUILayout.Label("PRODUCTION", prodLabel);
                if (GUILayout.Button("🔓", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    if (EditorUtility.DisplayDialog("Unlock Production Sheet",
                        "This sheet is in production mode. Unlocking allows full editing.\n\nUnlock?",
                        "Unlock", "Cancel"))
                    {
                        _sheet.productionMode = false;
                        EditorUtility.SetDirty(_sheet);
                    }
                }
            }
            else
            {
                if (GUILayout.Button("🔒", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    _sheet.productionMode = true;
                    EditorUtility.SetDirty(_sheet);
                }
            }
        }

        if (_sheet != null && _sheet.IsSkinActive)
        {
            var skinLabel = new GUIStyle(EditorStyles.toolbarButton);
            skinLabel.normal.textColor = new Color(0.4f, 0.8f, 1f, 1f);
            GUILayout.Label("SKIN MODE", skinLabel);
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>True when the editor should lock structural/value edits to the base sheet.</summary>
    bool IsSkinLocked => _sheet != null && (_sheet.IsSkinActive || _sheet.productionMode);

    void DrawSkinBar() { } // Merged into DrawTopBar

    // ── Tab bar ───────────────────────────────────────────────────────────────

    void DrawTabBar()
    {
        bool prodLocked = _sheet != null && _sheet.productionMode;
        // In production mode, force to Palette tab
        if (prodLocked && _activeTab != 5) _activeTab = 5;

        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        var labels = new[] { "Buttons", "Boxes", "Text", "Sliders", "Global", "Palette", "Assets" };
        for (int i = 0; i < labels.Length; i++)
        {
            bool locked = prodLocked && i != 5; // only Palette is unlocked in production mode
            using (new EditorGUI.DisabledGroupScope(locked))
            {
                bool active = _activeTab == i;
                if (GUILayout.Toggle(active, labels[i], EditorStyles.toolbarButton, GUILayout.Width(70f)) && !active && !locked)
                    _activeTab = i;
            }
        }

        // Missing tab — shows badge count when there are unresolved style lookups.
        // Count is scoped to the currently-selected sheet so badge matches the tab body.
        int missingCount = _sheet != null
            ? System.Linq.Enumerable.Count(ZUIMissingStyleRegistry.EntriesForSheet(_sheet))
            : ZUIMissingStyleRegistry.Count;
        string missingLabel = missingCount > 0 ? $"Missing ({missingCount})" : "Missing";
        bool missingActive = _activeTab == 7;
        var prevColor = GUI.color;
        if (missingCount > 0) GUI.color = new Color(1f, 0.45f, 0.35f, 1f);
        if (GUILayout.Toggle(missingActive, missingLabel, EditorStyles.toolbarButton, GUILayout.Width(90f)) && !missingActive)
            _activeTab = 7;
        GUI.color = prevColor;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ── No sheet ──────────────────────────────────────────────────────────────

    void DrawNoSheetUI()
    {
        GUILayout.Space(40f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.Width(300f));
        EditorGUILayout.LabelField("No style sheet loaded.", EditorStyles.boldLabel);
        ZUI.VerticalSpace("V Control Gap");
        EditorGUILayout.LabelField("Assign an existing ZUIStyleSheetAsset above,\nor create a new one.", EditorStyles.wordWrappedLabel);
        ZUI.VerticalSpace("V Section Rows");
        if (GUILayout.Button("Create New Zheet")) CreateNewSheet();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ── List panel ────────────────────────────────────────────────────────────

    // Track the last known panel rect for hover detection (captured during Repaint)
    [NonSerialized] Rect _listPanelRect;

    void DrawListPanel()
    {
        // In skin mode: always show mini list (names only, no controls, no strip toggle)
        if (IsSkinLocked)
        {
            DrawSkinModeList();
            return;
        }

        // Capture hover state at frame start — must not change between Layout and Repaint passes
        bool hoveredThisFrame = _listHovered;
        ListMode effectiveMode = _listMode;
        if (_listMode == ListMode.Collapsed && hoveredThisFrame)
            effectiveMode = ListMode.MiniHover;

        float listW = effectiveMode == ListMode.FullyExpanded ? k_ListWidthFull
                    : effectiveMode == ListMode.MiniHover    ? k_MiniWidth
                    : 0f;
        float totalW = k_StripWidth + listW;
        bool showList = effectiveMode != ListMode.Collapsed;
        bool miniMode = effectiveMode == ListMode.MiniHover;

        // Outer vertical — fixed width, the parent horizontal splits space with the inspector
        GUILayout.BeginVertical(GUILayout.Width(totalW), GUILayout.ExpandHeight(true));

        // Top row: header label for expanded list
        GUILayout.BeginHorizontal(GUILayout.Height(16f));
        GUILayout.Space(k_StripWidth); // reserve space for the button (drawn on top of gutter later)
        GUILayout.Label(showList ? "Styles" : "", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();

        // Content row: strip gutter + scrollable list
        float contentH = position.height - 80f;
        GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true), GUILayout.MinHeight(contentH));

        // Strip gutter (styleable background column — extends under the button)
        var gutterRect = GUILayoutUtility.GetRect(k_StripWidth, contentH, GUILayout.Width(k_StripWidth), GUILayout.ExpandHeight(true));
        // Extend gutter visually to cover the button area above
        var fullGutterRect = new Rect(gutterRect.x, gutterRect.y - 16f, gutterRect.width, gutterRect.height + 16f);
        if (Event.current.type == EventType.Repaint)
        {
            var stripDef = ZUI.EditorSheet?.FindBoxOrNull("List Strip");
            if (stripDef != null)
                stripDef.DrawBackground(fullGutterRect);
            else
                EditorGUI.DrawRect(fullGutterRect, new Color(.18f, .18f, .22f, 1f));
        }
        // Draw expand button on top of the gutter
        var btnRect = new Rect(fullGutterRect.x, fullGutterRect.y, k_StripWidth, 16f);
        if (GUI.Button(btnRect, showList ? "◂" : "▸", EditorStyles.toolbarButton))
        {
            _listMode = (_listMode == ListMode.Collapsed) ? ListMode.FullyExpanded : ListMode.Collapsed;
            _listHovered = false;
            Repaint();
        }

        // Scrollable list
        if (showList)
        {
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Width(listW), GUILayout.ExpandHeight(true));

            if (_activeTab == 0)
                DrawDynamicList(_sheet.buttons, ref _selectedButton,
                    () => new ZUIButtonDef("New Button",
                        new Color(.22f, .22f, .26f, 1f), new Color(.30f, .30f, .36f, 1f),
                        new Color(.16f, .16f, .20f, 1f), new Color(.88f, .88f, .88f, 1f)),
                    miniMode);
            else if (_activeTab == 1)
                DrawDynamicList(_sheet.boxes, ref _selectedBox,
                    () => new ZUIBoxDef("New Box",
                        new Color(.18f, .18f, .22f, 1f), new Color(.90f, .90f, .90f, 1f),
                        new Color(1f, 1f, 1f, .06f), 1f, 8, 6),
                    miniMode);
            else if (_activeTab == 2)
                DrawDynamicList(_sheet.textStyles, ref _selectedText,
                    () => new ZUITextStyleDef { name = "New Text Style" },
                    miniMode);
            else
                DrawDynamicList(_sheet.sliders, ref _selectedSlider,
                    () => new ZUISliderDef { name = "New Slider" },
                    miniMode);

            GUILayout.EndScrollView();
        }

        GUILayout.EndHorizontal();

        // Capture panel rect during repaint for hover detection
        var lastRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.Repaint)
            _listPanelRect = new Rect(gutterRect.x, gutterRect.y, totalW, gutterRect.height + 16f);

        GUILayout.EndVertical();

        // ── Hover: strip area + expand button triggers mini peek ──
        // When already peeking (MiniHover), the full expanded area keeps it open
        // Only update on MouseMove to avoid changing state between Layout and Repaint passes
        if (_listMode == ListMode.Collapsed && Event.current.type == EventType.MouseMove)
        {
            float hoverW = _listHovered ? (k_StripWidth + k_MiniWidth) : k_StripWidth;
            var hoverZone = new Rect(_listPanelRect.x, _listPanelRect.y - 16f,
                                      hoverW, _listPanelRect.height + 16f);
            bool overPanel = hoverZone.Contains(Event.current.mousePosition);
            if (overPanel != _listHovered) { _listHovered = overPanel; Repaint(); }
        }
    }

    void DrawSkinModeList()
    {
        GUILayout.BeginVertical(GUILayout.Width(k_MiniWidth), GUILayout.ExpandHeight(true));
        GUILayout.Label("Styles", EditorStyles.miniLabel);
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

        if (_activeTab == 0)
            DrawDynamicList(_sheet.buttons, ref _selectedButton, null, true);
        else if (_activeTab == 1)
            DrawDynamicList(_sheet.boxes, ref _selectedBox, null, true);
        else if (_activeTab == 2)
            DrawDynamicList(_sheet.textStyles, ref _selectedText, null, true);
        else
            DrawDynamicList(_sheet.sliders, ref _selectedSlider, null, true);

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    void DrawDynamicList<T>(List<T> items, ref int selected, Func<T> createNew, bool miniMode = false) where T : class
    {
        bool dirty    = false;
        int  moveFrom = -1, moveTo = -1;
        int  duplicateAt = -1, removeAt = -1;
        bool showBtns = !IsSkinLocked && !miniMode;
        const float bw = 18f; // button width
        const float bh = 20f; // row height

        for (int i = 0; i < items.Count; i++)
        {
            string label    = GetStyleName(items[i]) ?? i.ToString();
            bool   isActive = selected == i;
            var    style    = isActive ? _listItemActiveStyle : _listItemStyle;

            GUILayout.BeginHorizontal(GUILayout.Height(bh));

            // Name button (always)
            var rect = GUILayoutUtility.GetRect(new GUIContent(label), style,
                           GUILayout.ExpandWidth(true), GUILayout.Height(bh));
            if (GUI.Button(rect, label, style)) selected = i;

            if (showBtns)
            {
                // Move up
                using (new EditorGUI.DisabledGroupScope(i == 0))
                    if (this.Button(IconMoveUp, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)))
                        { moveFrom = i; moveTo = i - 1; }
                // Move down
                using (new EditorGUI.DisabledGroupScope(i == items.Count - 1))
                    if (this.Button(IconMoveDown, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)))
                        { moveFrom = i; moveTo = i + 1; }
                // Flash
                if (this.Button(IconFlash, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)))
                {
                    string styleName = GetStyleName(items[i]);
                    if (_activeTab == 0) ZUI.StartFlash(styleName, ZUI.FlashDefType.Button, _sheet);
                    else if (_activeTab == 1) ZUI.StartFlash(styleName, ZUI.FlashDefType.Box, _sheet);
                    else if (_activeTab == 2) ZUI.StartFlash(styleName, ZUI.FlashDefType.Text, _sheet);
                    else if (_activeTab == 3) ZUI.StartFlash(styleName, ZUI.FlashDefType.Slider, _sheet);
                }
                // Duplicate
                if (this.Button(IconDuplicate, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)))
                    duplicateAt = i;
                // Copy
                if (this.Button(IconCopy, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)))
                {
                    if (_activeTab == 0) _clipButton = DeepCopy(items[i] as ZUIButtonDef);
                    else if (_activeTab == 1) _clipBox = DeepCopy(items[i] as ZUIBoxDef);
                }
                // Paste
                bool canPaste = (_activeTab == 0 && _clipButton != null) || (_activeTab == 1 && _clipBox != null);
                using (new EditorGUI.DisabledGroupScope(!canPaste))
                {
                    if (this.Button(IconPaste, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)) && canPaste)
                    {
                        if (_activeTab == 0) PasteButtonDef(items[i] as ZUIButtonDef, _clipButton);
                        else if (_activeTab == 1) PasteBoxDef(items[i] as ZUIBoxDef, _clipBox);
                        dirty = true;
                    }
                }
                // Delete
                using (new EditorGUI.DisabledGroupScope(items.Count <= 1))
                    if (this.Button(IconDelete, "IconButton", GUILayout.Width(bw), GUILayout.Height(bh)) && items.Count > 1)
                        removeAt = i;
            }

            GUILayout.EndHorizontal();
        }

        // Process deferred mutations
        if (moveFrom >= 0 && moveTo >= 0 && moveTo < items.Count)
        {
            var item = items[moveFrom];
            items.RemoveAt(moveFrom);
            items.Insert(moveTo, item);
            selected = moveTo;
            dirty = true;
        }
        if (showBtns)
        {
            if (duplicateAt >= 0)
            {
                var clone = DuplicateStyleItem(items[duplicateAt]);
                items.Insert(duplicateAt + 1, clone);
                selected = duplicateAt + 1;
                dirty = true;
            }
            if (removeAt >= 0)
            {
                items.RemoveAt(removeAt);
                if (selected >= items.Count) selected = items.Count - 1;
                dirty = true;
            }

            ZUI.VerticalSpace("V Control Gap");
            if (this.Button("+ Add Style", "TabButton"))
            {
                items.Add(createNew());
                selected = items.Count - 1;
                dirty = true;
            }
        }

        if (dirty) EditorUtility.SetDirty(_sheet);
    }

    T DuplicateStyleItem<T>(T source) where T : class
    {
        // Serialize via JSON for a deep copy (works for all ZUI def types)
        string json = JsonUtility.ToJson(source);
        var clone   = JsonUtility.FromJson<T>(json);
        // Append " (Copy)" to the name field via reflection
        var nameProp = clone.GetType().GetField("name");
        if (nameProp != null)
        {
            string n = nameProp.GetValue(clone) as string ?? "";
            if (!n.EndsWith(" (Copy)")) n += " (Copy)";
            nameProp.SetValue(clone, n);
        }
        // Invalidate cached GUIStyles if the type supports it
        clone.GetType().GetMethod("Invalidate")?.Invoke(clone, null);
        return clone;
    }

    // ── Inspector panel ───────────────────────────────────────────────────────

    void DrawInspectorPanel()
    {
        _sectionAreaOpen = false; // reset at frame start — prevents stale state from interrupted draws
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);

        // Wrap entire styleDef editor in a "StyleDef Editor" box if available
        var editorBoxDef = ZUI.EditorSheet?.FindBoxOrNull("StyleDef Editor");
        if (editorBoxDef != null)
        {
            var editorRect = EditorGUILayout.BeginVertical(editorBoxDef.GetLayoutStyle());
            editorBoxDef.DrawBackground(editorRect);
        }
        else
        {
            EditorGUILayout.BeginVertical();
        }

        // Chrome controls (toggles, section headers, toolbars) draw with EditorSheet
        // from ZUIWindow base. Only preview rendering and palette resolution need the
        // edited sheet — those scope locally via ZUI.UseSheet(_sheet) at the call site.
        // The flash system's sheet guard (line 368 in ZUI.cs) ensures only controls on
        // the matching sheet flash — SuppressFlash is no longer needed here.
        using (new EditorGUI.DisabledGroupScope(IsSkinLocked))
        {
            if (_activeTab == 0)      DrawButtonInspector();
            else if (_activeTab == 1) DrawBoxInspector();
            else if (_activeTab == 2) DrawTextStyleInspector();
            else                      DrawSliderInspector();
        }
        EndPreviousSectionArea();

        EditorGUILayout.EndVertical(); // end StyleDef Editor box

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // ── Button inspector ──────────────────────────────────────────────────────

    void DrawButtonInspector()
    {
        if (_selectedButton < 0 || _selectedButton >= _sheet.buttons.Count)
        { CenteredLabel("Select a button style."); return; }

        var def = _sheet.buttons[_selectedButton];
        bool changed = false;
        EditorGUIUtility.labelWidth = k_LabelWidth;

        TabTitle("Button Style");

        GUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        def.name = EditorGUILayout.TextField("Name", def.name);
        if (EditorGUI.EndChangeCheck()) { ZUIMissingStyleRegistry.Remove(ZUIMissingStyleRegistry.EntryType.Button, def.name); changed = true; }
        if (ChromeButton(IconFlash, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f))) ZUI.StartFlash(def.name, ZUI.FlashDefType.Button, _sheet);
        if (ChromeButton(IconCopy, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f))) _clipButton = CopyButtonDef(def);
        GUI.enabled = _clipButton != null;
        if (ChromeButton(IconPaste, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f)))
            { PasteButtonDef(def, _clipButton); def.Invalidate(); changed = true; }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");

        // ── Section visibility toggles ──
        GUILayout.BeginHorizontal();
        def.showPreview    = ZUI.Toggle(def.showPreview,    "Prv",   "Toggle", GUILayout.Height(16f));
        def.showBackground = ZUI.Toggle(def.showBackground, "Bg", "Toggle", GUILayout.Height(16f));
        def.showBorder     = ZUI.Toggle(def.showBorder, "Brd", "Toggle", GUILayout.Height(16f));
        def.showText       = ZUI.Toggle(def.showText, "Txt", "Toggle", GUILayout.Height(16f));
        def.showShape      = ZUI.Toggle(def.showShape, "Shp", "Toggle", GUILayout.Height(16f));
        def.showPadding    = ZUI.Toggle(def.showPadding, "Pad", "Toggle", GUILayout.Height(16f));
        def.showShadow     = ZUI.Toggle(def.showShadow, "Shd", "Toggle", GUILayout.Height(16f));
        def.showAnimation  = ZUI.Toggle(def.showAnimation, "Anim", "Toggle", GUILayout.Height(16f));
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");

        _previewIsToggleMode = def.previewAsToggle;

        if (def.showPreview)
        {
            if (DrawPreviewHeader("btn_preview", showRoundingToggle: true))
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Preview as", GUILayout.Width(k_LabelWidth));
                int previewMode = ZUI.MiniRadio(def.previewAsToggle ? 1 : 0,
                    new[] { "Button", "Toggle" }, "Toggle");
                if (def.previewAsToggle != (previewMode == 1)) { def.previewAsToggle = previewMode == 1; changed = true; }
                _previewIsToggleMode = def.previewAsToggle;
                GUILayout.EndHorizontal();
                ZUI.VerticalSpace("V Section Rows");

                if (_previewIsToggleMode)
                    DrawTogglePreview(def);
                else
                    DrawButtonPreview(def);
            }
            ZUI.VerticalSpace("V Section Rows");
        }

        // State tabs live outside any section — explicitly close the preview
        // section area so these controls aren't visually absorbed into it.
        EndPreviousSectionArea();
        ZUI.VerticalSpace("V Section Rows");

        var stateTabs = _previewIsToggleMode
            ? new[] { "Normal (Off)", "Toggle On" }
            : new[] { "Normal", "Hover", "Active", "Toggle On" };
        // Clamp only when out of range for the current mode (e.g. index 3 when in toggle mode which has 2 tabs).
        if (_buttonStateTab >= stateTabs.Length) _buttonStateTab = stateTabs.Length - 1;

        // State tabs + current state's Box Style picker on one row.
        GUILayout.BeginHorizontal();
        _buttonStateTab = ZUI.MiniRadio(_buttonStateTab, stateTabs, "Toggle");
        ZUI.HorizontalSpace("H Control Gap");
        bool currentHasBox;
        if (_previewIsToggleMode)
        {
            if (_buttonStateTab == 0)
                currentHasBox = DrawStateBoxStylePickerInline(ref def.boxStyle, def, ref changed);
            else
                currentHasBox = DrawStateBoxStylePickerInline(ref def.toggleOnBoxStyle, def, ref changed);
        }
        else
        {
            if (_buttonStateTab == 0)
                currentHasBox = DrawStateBoxStylePickerInline(ref def.boxStyle, def, ref changed);
            else if (_buttonStateTab == 1)
                currentHasBox = DrawStateBoxStylePickerInline(ref def.hoverBoxStyle, def, ref changed);
            else if (_buttonStateTab == 2)
                currentHasBox = DrawStateBoxStylePickerInline(ref def.activeBoxStyle, def, ref changed);
            else
                currentHasBox = DrawStateBoxStylePickerInline(ref def.toggleOnBoxStyle, def, ref changed);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");

        if (_previewIsToggleMode)
        {
            if (_buttonStateTab == 0) changed |= DrawButtonNormalState(def, currentHasBox);
            else                      changed |= DrawButtonToggleOnState(def, currentHasBox);
        }
        else
        {
            if (_buttonStateTab == 0)      changed |= DrawButtonNormalState(def, currentHasBox);
            else if (_buttonStateTab == 1) changed |= DrawButtonHoverState(def, currentHasBox);
            else if (_buttonStateTab == 2) changed |= DrawButtonActiveState(def, currentHasBox);
            else                           changed |= DrawButtonToggleOnState(def, currentHasBox);
        }

        if (changed) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }

    }

    bool DrawButtonNormalState(ZUIButtonDef def, bool hasBox)
    {
        bool changed = false;
        Action invalidate = () => { def.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        if (def.showBackground)
        {
            bool bgGlobalNew;
            if (SectionHeaderWithCopyPasteAndGlobal("Background",
                () => _clipBg = DeepCopy(def.normal),
                () => { if (_clipBg != null) { PasteGrad(def.normal, _clipBg); def.Invalidate(); changed = true; } },
                _clipBg != null, def.useGlobalBackground, out bgGlobalNew, "btn_bg"))
            {
                if (DrawBoxOverrideToggle(def, "Background", ref def.boxOverrideBg, ref changed))
                {
                    var bgSource = def.useGlobalBackground ? (_sheet?.globalButton?.normal ?? def.normal) : def.normal;
                    using (new EditorGUI.DisabledGroupScope(def.useGlobalBackground))
                    {
                        if (DrawFillField(bgSource)) { def.Invalidate(); changed = true; }
                    }

                    // ── Effect toggles row ───────────────────────────────────
                    ZUI.VerticalSpace("V Section Rows");
                    GUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    def.glow.enabled    = ZUI.Toggle(def.glow.enabled, "Glow", "Toggle", GUILayout.Height(16f));
                    def.overlayEnabled  = ZUI.Toggle(def.overlayEnabled,  "Overlay", "Toggle", GUILayout.Height(16f));
                    def.pattern.enabled = ZUI.Toggle(def.pattern.enabled, "Pattern", "Toggle", GUILayout.Height(16f));
                    if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
                    GUILayout.EndHorizontal();

                    if (def.glow.enabled)
                    {
                        ZUI.VerticalSpace("V Control Gap");
                        SubsectionTitle("Glow");
                        DrawGlowFields(def.glow, out bool gc);
                        if (gc) { def.Invalidate(); changed = true; }
                    }
                    if (def.overlayEnabled)
                    {
                        ZUI.VerticalSpace("V Control Gap");
                        SubsectionTitle("Overlay");
                        EditorGUI.BeginChangeCheck();
                        DrawFillField(def.overlay);
                        if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
                    }
                    if (def.pattern.enabled)
                    {
                        ZUI.VerticalSpace("V Control Gap");
                        SubsectionTitle("Pattern");
                        DrawPatternFields(def.pattern, out bool pc);
                        if (pc) { def.pattern.Invalidate(); def.Invalidate(); changed = true; }
                    }
                }
            }
            if (bgGlobalNew != def.useGlobalBackground) { def.useGlobalBackground = bgGlobalNew; def.Invalidate(); changed = true; }

            ZUI.VerticalSpace("V Section Rows");
        }

        if (def.showBorder)
        {
            bool borderGlobalNew;
            if (SectionHeaderWithCopyPasteAndGlobal("Border",
                () => _clipBorder = DeepCopy(def.border),
                () => { if (_clipBorder != null) { DeepPaste(def.border, _clipBorder); def.border.color.Invalidate(); changed = true; } },
                _clipBorder != null, def.useGlobalBorder, out borderGlobalNew, "btn_border"))
            {
                if (DrawBoxOverrideToggle(def, "Border", ref def.boxOverrideBorder, ref changed))
                {
                    EditorGUI.BeginChangeCheck();
                    if (def.useGlobalBorder)
                    {
                        var gb = _sheet?.globalButton;
                        using (new EditorGUI.DisabledGroupScope(true))
                            DrawBorderField(gb ?? def, null);
                    }
                    else
                        DrawBorderField(def, () => { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); });
                    if (EditorGUI.EndChangeCheck()) changed = true;
                }
            }
            if (borderGlobalNew != def.useGlobalBorder) { def.useGlobalBorder = borderGlobalNew; changed = true; }
            ZUI.VerticalSpace("V Section Rows");
        }

        if (def.showText)
        {
            bool txtGlobalNew;
            var txtForHeader = def.useGlobalText ? (_sheet?.globalButton?.text ?? def.text) : def.text;
            if (SectionHeaderText("Text",
                () => { _clipText = DeepCopy(def.text); _clipTextStyleId = def.textStyleId; },
                () => { if (_clipText != null) {
                            DeepPaste(def.text, _clipText); def.textStyleId = _clipTextStyleId;
                            def.Invalidate(); changed = true; } },
                _clipText != null, def.useGlobalText, out txtGlobalNew,
                txtForHeader, out bool txtTogChg, "btn_text"))
            {
                if (DrawBoxOverrideToggle(def, "Text", ref def.boxOverrideText, ref changed))
                {
                    var  txtSource = def.useGlobalText      ? (_sheet?.globalButton?.text ?? def.text)
                                   : !string.IsNullOrEmpty(def.textStyleId) ? (_sheet?.FindText(def.textStyleId)?.text ?? def.text)
                                   : def.text;
                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUI.DisabledGroupScope(def.useGlobalText))
                    {
                        DrawTextRowWithStyleRef(txtSource, ref def.textStyleId, out bool refChg);
                        if (refChg) { def.Invalidate(); changed = true; }
                    }
                    bool txtLocked = def.useGlobalText || !string.IsNullOrEmpty(def.textStyleId);
                    using (new EditorGUI.DisabledGroupScope(txtLocked))
                        DrawShadowTextRow(txtSource);
                    if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
                }
            }
            if (txtTogChg) { def.Invalidate(); changed = true; }
            if (txtGlobalNew != def.useGlobalText) { def.useGlobalText = txtGlobalNew; def.Invalidate(); changed = true; }
            ZUI.VerticalSpace("V Section Rows");
        }

        DrawButtonSharedSections(def, ref changed, isNormalState: true);

        return changed;
    }

    // ── Shared sections (Shape, Padding, Shadow, Animation) ─────────────────
    // These apply to all states. In Normal mode they're editable; in Hover/Active
    // they show with a note reminding the user they're shared.

    void DrawButtonSharedSections(ZUIButtonDef def, ref bool changed, bool isNormalState)
    {
        // Local flag for lambda capture — ref params can't be used in lambdas.
        bool dirty = false;

        if (!isNormalState)
        {
            ZUI.VerticalSpace("V Section Rows");
            EditorGUILayout.LabelField("Shape, Padding, Shadow, and Animation apply to all states.",
                EditorStyles.wordWrappedMiniLabel);
        }

        if (def.showShape)
        {
            ZUI.VerticalSpace("V Section Rows");
            bool shapeGlobalNewTop;
            if (SectionHeaderWithCopyPasteAndGlobal("Shape",
                () => _clipShape = (def.shape.cornerRadius, def.useGlobalShape),
                () => { if (_clipShape.HasValue)
                        { def.shape.cornerRadius = _clipShape.Value.r;
                          def.useGlobalShape = _clipShape.Value.useGlobal;
                          def.Invalidate(); dirty = true; } },
                _clipShape.HasValue, def.useGlobalShape, out shapeGlobalNewTop, "btn_shape"))
            {
                if (DrawBoxOverrideToggle(def, "Shape", ref def.boxOverrideShape, ref dirty))
                {
                    EditorGUI.BeginChangeCheck();
                    var gs = def.useGlobalShape ? _sheet?.globalButton : null;
                    using (new EditorGUI.DisabledGroupScope(def.useGlobalShape))
                        DrawShapeEditor(gs != null ? gs.shape : def.shape);
                    if (EditorGUI.EndChangeCheck()) { def.Invalidate(); dirty = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
                }
            }
            if (shapeGlobalNewTop != def.useGlobalShape) { def.useGlobalShape = shapeGlobalNewTop; def.Invalidate(); dirty = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
        }

        if (def.showPadding)
        {
            ZUI.VerticalSpace("V Section Rows");
            bool sizeGlobalNew;
            if (SectionHeaderWithCopyPasteAndGlobal("Padding",
                () => _clipPadding = (DeepCopy(def.padding), def.useGlobalPadding),
                () => { if (_clipPadding.HasValue) {
                            DeepPaste(def.padding, _clipPadding.Value.pad);
                            def.useGlobalPadding = _clipPadding.Value.useGlobal;
                            def.Invalidate(); dirty = true; } },
                _clipPadding.HasValue, def.useGlobalPadding, out sizeGlobalNew, "btn_padding"))
            {
                if (DrawBoxOverrideToggle(def, "Padding", ref def.boxOverridePadding, ref dirty))
                {
                    EditorGUI.BeginChangeCheck();
                    var gp = def.useGlobalPadding ? _sheet?.globalButton : null;
                    using (new EditorGUI.DisabledGroupScope(def.useGlobalPadding))
                        DrawPaddingEditor(gp != null ? gp.padding : def.padding, showIcon: true);
                    if (EditorGUI.EndChangeCheck()) { def.Invalidate(); dirty = true; }
                }
            }
            if (sizeGlobalNew != def.useGlobalPadding) { def.useGlobalPadding = sizeGlobalNew; def.Invalidate(); dirty = true; }
        }

        if (def.showShadow)
        {
            ZUI.VerticalSpace("V Section Rows");
            if (SectionHeaderWithToggleCopyPaste("Shadow", "btn_shadow", ref def.bgShadow.enabled, out bool shadowToggleChanged,
                () => _clipShadow = DeepCopy(def.bgShadow),
                () => { if (_clipShadow != null) { DeepPaste(def.bgShadow, _clipShadow); def.Invalidate(); dirty = true; } },
                _clipShadow != null))
            {
                if (DrawBoxOverrideToggle(def, "Shadow", ref def.boxOverrideShadow, ref dirty))
                {
                    if (def.bgShadow.enabled)
                    {
                        DrawBgShadowFields(def.bgShadow, out bool sc);
                        if (sc) { def.Invalidate(); dirty = true; }
                    }
                }
            }
            if (shadowToggleChanged) { def.Invalidate(); dirty = true; }
        }

        if (def.showAnimation)
        {
            ZUI.VerticalSpace("V Section Rows");
            if (SectionHeaderWithToggles("Animation", "btn_anim",
                    "Hover", ref def.hoverAnimEnabled,
                    "Click", ref def.clickAnimEnabled,
                    out bool animChanged))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Mode", GUILayout.Width(k_LabelWidth));
                def.animMode = (ZUIAnimMode)EditorGUILayout.EnumPopup(def.animMode);
                GUILayout.EndHorizontal();
                ZUI.VerticalSpace("V Control Gap");
                if (def.hoverAnimEnabled)
                {
                    DrawAnimationTimingRow("Hover In",  ref def.hoverInDuration,  ref def.hoverInEase);
                    DrawAnimationTimingRow("Hover Out", ref def.hoverOutDuration, ref def.hoverOutEase);
                }
                if (def.clickAnimEnabled)
                {
                    DrawAnimationTimingRow("Click In",  ref def.clickInDuration,  ref def.clickInEase);
                    DrawAnimationTimingRow("Click Out", ref def.clickOutDuration, ref def.clickOutEase);
                }
                if (EditorGUI.EndChangeCheck()) { dirty = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
            }
            if (animChanged) { dirty = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
        }

        if (dirty) changed = true;
    }

    bool DrawStateBoxStylePicker(string label, ref string stateBoxStyle, ZUIButtonDef def, ref bool changed)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(k_LabelWidth));
        bool hasBox = DrawStateBoxStylePickerInline(ref stateBoxStyle, def, ref changed);
        GUILayout.EndHorizontal();
        return hasBox;
    }

    // Inline variant — draws only the "Box Style:" label + popup, no BeginHorizontal wrap.
    // Caller owns the surrounding horizontal group.
    bool DrawStateBoxStylePickerInline(ref string stateBoxStyle, ZUIButtonDef def, ref bool changed)
    {
        EditorGUILayout.LabelField("Box Style", GUILayout.Width(60f));
        var boxNames = new List<string> { "— None —" };
        if (_sheet?.boxes != null)
            foreach (var b in _sheet.boxes) boxNames.Add(b.name);
        int curIdx = 0;
        string current = stateBoxStyle;
        if (!string.IsNullOrEmpty(current) && _sheet?.boxes != null)
            curIdx = _sheet.boxes.FindIndex(b => b.name == current) + 1;
        EditorGUI.BeginChangeCheck();
        int newIdx = EditorGUILayout.Popup(curIdx, boxNames.ToArray(), GUILayout.Width(140f));
        if (EditorGUI.EndChangeCheck())
        {
            stateBoxStyle = newIdx == 0 ? "" : boxNames[newIdx];
            def.Invalidate(); changed = true;
        }
        return !string.IsNullOrEmpty(stateBoxStyle);
    }

    bool DrawButtonHoverState(ZUIButtonDef def, bool hoverHasBox)
    {
        bool changed = false;
        Action invalidate = () => { def.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        if (!hoverHasBox)
        {
        bool hoverBgOvNew = def.hoverBgOverride;
        if (def.showBackground)
        {
        Action revertHoverBg = () => { PasteGrad(def.hover, def.normal); def.Invalidate(); changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };
        bool bgExp = SectionHeaderWithOverrideCopyPaste("Background", def.hoverBgOverride, out hoverBgOvNew,
            () => _clipBg = DeepCopy(def.hover),
            () => { if (_clipBg != null) { PasteGrad(def.hover, _clipBg); def.Invalidate(); changed = true; } },
            _clipBg != null,
            def.hoverBgOverride ? revertHoverBg : null, "btn_bg");
        if (hoverBgOvNew != def.hoverBgOverride) { def.hoverBgOverride = hoverBgOvNew; def.Invalidate(); changed = true; }
        if (bgExp)
        {
            if (def.hoverBgOverride)
            {
                if (DrawFillField(def.hover))
                    { def.Invalidate(); changed = true; }
            }
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawFillField(def.normal);
            }

            // Show effect toggles row (same layout as Normal) — disabled, inherits from Normal
            ZUI.VerticalSpace("V Section Rows");
            using (new EditorGUI.DisabledGroupScope(true))
            {
                GUILayout.BeginHorizontal();
                ZUI.Toggle(def.glow.enabled, "Glow", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.overlayEnabled, "Overlay", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.pattern.enabled, "Pattern", "Toggle", GUILayout.Height(16f));
                GUILayout.EndHorizontal();
            }
        }
        } // showBackground

        ZUI.VerticalSpace("V Section Rows");

        bool hoverBdrOvNew = def.hoverBorderOverride;
        if (def.showBorder)
        {
        Action revertHoverBdr = () => {
            PasteGrad(def.hoverBorder.color, def.border.color);
            def.hoverBorder.edgeWidth = JsonUtility.FromJson<ZUIEdgeValuesFloat>(JsonUtility.ToJson(def.border.edgeWidth));
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool bdrExp = SectionHeaderWithOverrideCopyPaste("Border", def.hoverBorderOverride, out hoverBdrOvNew,
            () => _clipBorder = DeepCopy(def.hoverBorder),
            () => { if (_clipBorder != null) { DeepPaste(def.hoverBorder, _clipBorder); def.hoverBorder.color.Invalidate(); changed = true; } },
            _clipBorder != null,
            def.hoverBorderOverride ? revertHoverBdr : null, "btn_border");
        if (hoverBdrOvNew != def.hoverBorderOverride) { def.hoverBorderOverride = hoverBdrOvNew; changed = true; }
        if (bdrExp)
        {
            EditorGUI.BeginChangeCheck();
            if (def.hoverBorderOverride)
                DrawHoverBorderRow(def);
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawBorderDefField(def.border, null);
            }
            if (EditorGUI.EndChangeCheck()) changed = true;
        }
        } // showBorder

        ZUI.VerticalSpace("V Section Rows");

        bool hoverTxtOvNew = def.hoverTextOverride;
        if (def.showText)
        {
        Action revertHoverTxt = () => {
            def.hoverText.color = def.text.color; def.hoverText.fontSize = def.text.fontSize;
            def.hoverText.fontStyle = def.text.fontStyle; def.Invalidate();
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool txtExp = SectionHeaderWithOverrideCopyPaste("Text", def.hoverTextOverride, out hoverTxtOvNew,
            () => { _clipText = DeepCopy(def.hoverText); _clipTextStyleId = def.hoverTextStyleId; },
            () => { if (_clipText != null) { DeepPaste(def.hoverText, _clipText); def.hoverTextStyleId = _clipTextStyleId; def.Invalidate(); changed = true; } },
            _clipText != null,
            def.hoverTextOverride ? revertHoverTxt : null, "btn_text");
        if (hoverTxtOvNew != def.hoverTextOverride) { def.hoverTextOverride = hoverTxtOvNew; def.Invalidate(); changed = true; }
        if (txtExp)
        {
            var hoverTxtSource = !string.IsNullOrEmpty(def.hoverTextStyleId) ? (_sheet?.FindText(def.hoverTextStyleId)?.text ?? def.hoverText)
                               : def.hoverTextOverride ? def.hoverText
                               : def.text;
            bool hoverTxtLocked = !def.hoverTextOverride && string.IsNullOrEmpty(def.hoverTextStyleId);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledGroupScope(hoverTxtLocked))
            {
                DrawTextRowWithStyleRef(hoverTxtSource, ref def.hoverTextStyleId, out bool refChg);
                if (refChg) { def.Invalidate(); changed = true; }
                DrawShadowTextRow(hoverTxtSource);
            }
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        } // showText
        } // !hoverHasBox

        DrawButtonSharedSections(def, ref changed, isNormalState: false);

        return changed;
    }

    bool DrawButtonActiveState(ZUIButtonDef def, bool activeHasBox)
    {
        bool changed = false;
        Action invalidate = () => { def.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        if (!activeHasBox)
        {
        var hoverGrad = def.GetHoverColor();
        string bgParent = def.hoverBgOverride ? "Hover" : "Normal";

        bool activeBgOvNew = def.activeBgOverride;
        if (def.showBackground)
        {
        Action revertActiveBg = () => { PasteGrad(def.active, hoverGrad); def.Invalidate(); changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };
        bool bgExp = SectionHeaderWithOverrideCopyPaste("Background", def.activeBgOverride, out activeBgOvNew,
            () => _clipBg = DeepCopy(def.active),
            () => { if (_clipBg != null) { PasteGrad(def.active, _clipBg); def.Invalidate(); changed = true; } },
            _clipBg != null,
            def.activeBgOverride ? revertActiveBg : null, "btn_bg");
        if (activeBgOvNew != def.activeBgOverride) { def.activeBgOverride = activeBgOvNew; def.Invalidate(); changed = true; }
        if (bgExp)
        {
            if (def.activeBgOverride)
            {
                if (DrawFillField(def.active))
                    { def.Invalidate(); changed = true; }
            }
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawFillField(hoverGrad);
            }

            // Show effect toggles row (same layout as Normal) — disabled, inherits from Normal
            ZUI.VerticalSpace("V Section Rows");
            using (new EditorGUI.DisabledGroupScope(true))
            {
                GUILayout.BeginHorizontal();
                ZUI.Toggle(def.glow.enabled, "Glow", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.overlayEnabled, "Overlay", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.pattern.enabled, "Pattern", "Toggle", GUILayout.Height(16f));
                GUILayout.EndHorizontal();
            }
        }
        } // showBackground

        ZUI.VerticalSpace("V Section Rows");

        bool activeBdrOvNew = def.activeBorderOverride;
        if (def.showBorder)
        {
        Action revertActiveBdr = () => {
            var src = def.GetHoverBorder();
            PasteGrad(def.activeBorder.color, src.color);
            def.activeBorder.width = src.width;
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool bdrExp = SectionHeaderWithOverrideCopyPaste("Border", def.activeBorderOverride, out activeBdrOvNew,
            () => _clipBorder = DeepCopy(def.activeBorder),
            () => { if (_clipBorder != null) { DeepPaste(def.activeBorder, _clipBorder); def.activeBorder.color.Invalidate(); changed = true; } },
            _clipBorder != null,
            def.activeBorderOverride ? revertActiveBdr : null, "btn_border");
        if (activeBdrOvNew != def.activeBorderOverride) { def.activeBorderOverride = activeBdrOvNew; changed = true; }
        if (bdrExp)
        {
            EditorGUI.BeginChangeCheck();
            if (def.activeBorderOverride)
                DrawActiveBorderRow(def);
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawBorderDefField(def.GetHoverBorder(), null);
            }
            if (EditorGUI.EndChangeCheck()) changed = true;
        }
        } // showBorder

        ZUI.VerticalSpace("V Section Rows");

        bool activeTxtOvNew = def.activeTextOverride;
        if (def.showText)
        {
        Action revertActiveTxt = () => {
            var ht = def.GetHoverText();
            def.activeText.color = ht.color; def.activeText.fontSize = ht.fontSize;
            def.activeText.fontStyle = ht.fontStyle; def.Invalidate();
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool txtExp = SectionHeaderWithOverrideCopyPaste("Text", def.activeTextOverride, out activeTxtOvNew,
            () => { _clipText = DeepCopy(def.activeText); _clipTextStyleId = def.activeTextStyleId; },
            () => { if (_clipText != null) { DeepPaste(def.activeText, _clipText); def.activeTextStyleId = _clipTextStyleId; def.Invalidate(); changed = true; } },
            _clipText != null,
            def.activeTextOverride ? revertActiveTxt : null, "btn_text");
        if (activeTxtOvNew != def.activeTextOverride) { def.activeTextOverride = activeTxtOvNew; def.Invalidate(); changed = true; }
        if (txtExp)
        {
            var activeTxtSource = !string.IsNullOrEmpty(def.activeTextStyleId) ? (_sheet?.FindText(def.activeTextStyleId)?.text ?? def.activeText)
                                : def.activeTextOverride ? def.activeText
                                : def.GetHoverText();
            bool activeTxtLocked = !def.activeTextOverride && string.IsNullOrEmpty(def.activeTextStyleId);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledGroupScope(activeTxtLocked))
            {
                DrawTextRowWithStyleRef(activeTxtSource, ref def.activeTextStyleId, out bool refChg);
                if (refChg) { def.Invalidate(); changed = true; }
                DrawShadowTextRow(activeTxtSource);
            }
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        } // showText
        } // !activeHasBox

        DrawButtonSharedSections(def, ref changed, isNormalState: false);

        return changed;
    }

    bool DrawButtonToggleOnState(ZUIButtonDef def, bool toggleOnHasBox)
    {
        bool changed = false;
        Action invalidate = () => { def.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        if (!toggleOnHasBox)
        {
        var activeGrad = def.GetActiveColor();
        string bgParent = def.activeBgOverride ? "Active" : (def.hoverBgOverride ? "Hover" : "Normal");

        bool toggleOnBgOvNew = def.toggleOnBgOverride;
        if (def.showBackground)
        {
        Action revertToggleOnBg = () => { PasteGrad(def.toggleOn, activeGrad); def.Invalidate(); changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };
        bool bgExp = SectionHeaderWithOverrideCopyPaste("Background", def.toggleOnBgOverride, out toggleOnBgOvNew,
            () => _clipBg = DeepCopy(def.toggleOn),
            () => { if (_clipBg != null) { PasteGrad(def.toggleOn, _clipBg); def.Invalidate(); changed = true; } },
            _clipBg != null,
            def.toggleOnBgOverride ? revertToggleOnBg : null, "btn_bg");
        if (toggleOnBgOvNew != def.toggleOnBgOverride) { def.toggleOnBgOverride = toggleOnBgOvNew; def.Invalidate(); changed = true; }
        if (bgExp)
        {
            if (def.toggleOnBgOverride)
            {
                if (DrawFillField(def.toggleOn))
                    { def.Invalidate(); changed = true; }
            }
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawFillField(activeGrad);
            }

            // Show effect toggles row (same layout as Normal) — disabled, inherits
            ZUI.VerticalSpace("V Section Rows");
            using (new EditorGUI.DisabledGroupScope(true))
            {
                GUILayout.BeginHorizontal();
                ZUI.Toggle(def.glow.enabled, "Glow", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.overlayEnabled, "Overlay", "Toggle", GUILayout.Height(16f));
                ZUI.Toggle(def.pattern.enabled, "Pattern", "Toggle", GUILayout.Height(16f));
                GUILayout.EndHorizontal();
            }
        }
        } // showBackground

        ZUI.VerticalSpace("V Section Rows");

        bool toggleOnBdrOvNew = def.toggleOnBorderOverride;
        if (def.showBorder)
        {
        Action revertToggleOnBdr = () => {
            var src = def.GetActiveBorder();
            PasteGrad(def.toggleOnBorder.color, src.color);
            def.toggleOnBorder.width = src.width;
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool bdrExp = SectionHeaderWithOverrideCopyPaste("Border", def.toggleOnBorderOverride, out toggleOnBdrOvNew,
            () => _clipBorder = DeepCopy(def.toggleOnBorder),
            () => { if (_clipBorder != null) { DeepPaste(def.toggleOnBorder, _clipBorder); def.toggleOnBorder.color.Invalidate(); changed = true; } },
            _clipBorder != null,
            def.toggleOnBorderOverride ? revertToggleOnBdr : null, "btn_border");
        if (toggleOnBdrOvNew != def.toggleOnBorderOverride) { def.toggleOnBorderOverride = toggleOnBdrOvNew; changed = true; }
        if (bdrExp)
        {
            EditorGUI.BeginChangeCheck();
            if (def.toggleOnBorderOverride)
                DrawToggleOnBorderRow(def);
            else
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawBorderDefField(def.GetActiveBorder(), null);
            }
            if (EditorGUI.EndChangeCheck()) changed = true;
        }
        } // showBorder

        ZUI.VerticalSpace("V Section Rows");

        bool toggleOnTxtOvNew = def.toggleOnTextOverride;
        if (def.showText)
        {
        Action revertToggleOnTxt = () => {
            var at = def.GetActiveText();
            def.toggleOnText.color = at.color; def.toggleOnText.fontSize = at.fontSize;
            def.toggleOnText.fontStyle = at.fontStyle; def.Invalidate();
            changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint();
        };
        bool txtExp = SectionHeaderWithOverrideCopyPaste("Text", def.toggleOnTextOverride, out toggleOnTxtOvNew,
            () => { _clipText = DeepCopy(def.toggleOnText); _clipTextStyleId = def.toggleOnTextStyleId; },
            () => { if (_clipText != null) { DeepPaste(def.toggleOnText, _clipText); def.toggleOnTextStyleId = _clipTextStyleId; def.Invalidate(); changed = true; } },
            _clipText != null,
            def.toggleOnTextOverride ? revertToggleOnTxt : null, "btn_text");
        if (toggleOnTxtOvNew != def.toggleOnTextOverride) { def.toggleOnTextOverride = toggleOnTxtOvNew; def.Invalidate(); changed = true; }
        if (txtExp)
        {
            var toggleOnTxtSource = !string.IsNullOrEmpty(def.toggleOnTextStyleId) ? (_sheet?.FindText(def.toggleOnTextStyleId)?.text ?? def.toggleOnText)
                                  : def.toggleOnTextOverride ? def.toggleOnText
                                  : def.GetActiveText();
            bool toggleOnTxtLocked = !def.toggleOnTextOverride && string.IsNullOrEmpty(def.toggleOnTextStyleId);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledGroupScope(toggleOnTxtLocked))
            {
                DrawTextRowWithStyleRef(toggleOnTxtSource, ref def.toggleOnTextStyleId, out bool refChg);
                if (refChg) { def.Invalidate(); changed = true; }
                DrawShadowTextRow(toggleOnTxtSource);
            }
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        } // showText
        } // !toggleOnHasBox

        DrawButtonSharedSections(def, ref changed, isNormalState: false);

        return changed;
    }

    void DrawHoverBorderRow(ZUIButtonDef def)
    {
        EditorGUI.BeginChangeCheck();
        DrawBorderDefField(def.hoverBorder, () => { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); });
        if (EditorGUI.EndChangeCheck()) { def.hoverBorder.color.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); }
    }

    void DrawActiveBorderRow(ZUIButtonDef def)
    {
        EditorGUI.BeginChangeCheck();
        DrawBorderDefField(def.activeBorder, () => { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); });
        if (EditorGUI.EndChangeCheck()) { def.activeBorder.color.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); }
    }

    void DrawToggleOnBorderRow(ZUIButtonDef def)
    {
        EditorGUI.BeginChangeCheck();
        DrawBorderDefField(def.toggleOnBorder, () => { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); });
        if (EditorGUI.EndChangeCheck()) { def.toggleOnBorder.color.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); }
    }

    static void DrawBorderReadOnlyRow(ZUIBorderDef bDef, ZUIStyleSheetAsset sheet)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Color A", GUILayout.Width(k_LabelWidth - 2f));
        bool dual = bDef.color.isGradient;
        ZUI.Toggle(dual, dual ? "▾" : "▸", "Toggle", GUILayout.Width(20f));
        EditorGUILayout.ColorField(GUIContent.none, bDef.color.GetColorA(sheet), true, true, false);
        if (dual) EditorGUILayout.ColorField(GUIContent.none, bDef.color.GetColorB(sheet), true, true, false);
        { float _lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 14f;
          EditorGUILayout.FloatField("W", bDef.width, GUILayout.Width(50f));
          EditorGUIUtility.labelWidth = _lw; }
        GUILayout.EndHorizontal();
    }

    // ── Box inspector ─────────────────────────────────────────────────────────

    void DrawBoxInspector()
    {
        if (_selectedBox < 0 || _selectedBox >= _sheet.boxes.Count)
        { CenteredLabel("Select a box style."); return; }

        var def = _sheet.boxes[_selectedBox];
        bool changed = false;
        EditorGUIUtility.labelWidth = k_LabelWidth;

        TabTitle("Box Style");

        GUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        def.name = EditorGUILayout.TextField("Name", def.name);
        if (EditorGUI.EndChangeCheck()) { ZUIMissingStyleRegistry.Remove(ZUIMissingStyleRegistry.EntryType.Box, def.name); changed = true; }
        if (ChromeButton(IconFlash, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f))) ZUI.StartFlash(def.name, ZUI.FlashDefType.Box, _sheet);
        if (ChromeButton(IconCopy, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f))) _clipBox = CopyBoxDef(def);
        GUI.enabled = _clipBox != null;
        if (ChromeButton(IconPaste, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f)))
            { PasteBoxDef(def, _clipBox); def.Invalidate(); changed = true; }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        // ── Section visibility toggles ──
        GUILayout.BeginHorizontal();
        def.showPreview      = ZUI.Toggle(def.showPreview, "Prv", "Toggle", GUILayout.Height(16f));
        def.showBackground   = ZUI.Toggle(def.showBackground, "Bg", "Toggle", GUILayout.Height(16f));
        def.showBorder       = ZUI.Toggle(def.showBorder, "Brd", "Toggle", GUILayout.Height(16f));
        def.showTitleText    = ZUI.Toggle(def.showTitleText, "TTxt", "Toggle", GUILayout.Height(16f));
        def.showContentText  = ZUI.Toggle(def.showContentText, "CTxt", "Toggle", GUILayout.Height(16f));
        def.showShape        = ZUI.Toggle(def.showShape, "Shp", "Toggle", GUILayout.Height(16f));
        def.showPadding      = ZUI.Toggle(def.showPadding, "Pad", "Toggle", GUILayout.Height(16f));
        def.showShadow       = ZUI.Toggle(def.showShadow, "Shd", "Toggle", GUILayout.Height(16f));
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");

        if (def.showPreview)
        {
            if (DrawPreviewHeader("box_preview", showRoundingToggle: true))
            {
                DrawBoxPreview(def);
            }
            ZUI.VerticalSpace("V Control Gap");
        }

        // ── Background ───────────────────────────────────────────────────────
        if (def.showBackground)
        {
        bool boxBgGlobalNew;
        if (SectionHeaderWithCopyPasteAndGlobal("Background",
            () => _clipBg = DeepCopy(def.background),
            () => { if (_clipBg != null) { PasteGrad(def.background, _clipBg); def.Invalidate(); changed = true; } },
            _clipBg != null, def.useGlobalBackground, out boxBgGlobalNew))
        {
            var bgSource = def.useGlobalBackground ? (ZUI.ActiveSheet?.globalBox?.background ?? def.background) : def.background;
            Action bgChanged = def.useGlobalBackground ? null : () => { def.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };
            using (new EditorGUI.DisabledGroupScope(def.useGlobalBackground))
            {
                if (DrawFillField(bgSource)) { def.Invalidate(); changed = true; }
            }

            // ── Effect toggles row ───────────────────────────────────
            ZUI.VerticalSpace("V Section Rows");
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            def.glow.enabled    = ZUI.Toggle(def.glow.enabled, "Glow", "Toggle", GUILayout.Height(16f));
            def.overlayEnabled  = ZUI.Toggle(def.overlayEnabled,  "Overlay", "Toggle", GUILayout.Height(16f));
            def.pattern.enabled = ZUI.Toggle(def.pattern.enabled, "Pattern", "Toggle", GUILayout.Height(16f));
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
            GUILayout.EndHorizontal();

            if (def.glow.enabled)
            {
                ZUI.VerticalSpace("V Control Gap");
                SubsectionTitle("Glow");
                DrawGlowFields(def.glow, out bool gc);
                if (gc) { def.Invalidate(); changed = true; }
            }
            if (def.overlayEnabled)
            {
                ZUI.VerticalSpace("V Control Gap");
                SubsectionTitle("Overlay");
                EditorGUI.BeginChangeCheck();
                DrawFillField(def.overlay);
                if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
            }
            if (def.pattern.enabled)
            {
                ZUI.VerticalSpace("V Control Gap");
                SubsectionTitle("Pattern");
                DrawPatternFields(def.pattern, out bool pc);
                if (pc) { def.pattern.Invalidate(); def.Invalidate(); changed = true; }
            }
        }
        if (boxBgGlobalNew != def.useGlobalBackground) { def.useGlobalBackground = boxBgGlobalNew; def.Invalidate(); changed = true; }

        ZUI.VerticalSpace("V Section Rows");
        }

        // ── Border ───────────────────────────────────────────────────────────
        if (def.showBorder)
        {
        bool boxBdrGlobalNew;
        if (SectionHeaderWithCopyPasteAndGlobal("Border",
            () => _clipBorder = DeepCopy(def.border),
            () => { if (_clipBorder != null) { DeepPaste(def.border, _clipBorder); def.border.color.Invalidate(); changed = true; } },
            _clipBorder != null, def.useGlobalBorder, out boxBdrGlobalNew))
        {
            EditorGUI.BeginChangeCheck();
            if (def.useGlobalBorder)
            {
                var gb = ZUI.ActiveSheet?.globalBox;
                using (new EditorGUI.DisabledGroupScope(true))
                    DrawBorderField(gb ?? def, null);
            }
            else
                DrawBorderField(def, () => { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); });
            if (EditorGUI.EndChangeCheck()) changed = true;
        }
        if (boxBdrGlobalNew != def.useGlobalBorder) { def.useGlobalBorder = boxBdrGlobalNew; changed = true; }
        ZUI.VerticalSpace("V Section Rows");
        }

        // ── Title Text ───────────────────────────────────────────────────────
        if (def.showTitleText)
        {
        bool boxTitleGlobalNew;
        var titleForHeader = def.useGlobalTitleText ? (ZUI.ActiveSheet?.globalBox?.titleText ?? def.titleText) : def.titleText;
        if (SectionHeaderText("Title Text",
            () => { _clipText = DeepCopy(def.titleText); _clipTextStyleId = def.titleTextStyleId; },
            () => { if (_clipText != null) {
                        DeepPaste(def.titleText, _clipText); def.titleTextStyleId = _clipTextStyleId;
                        changed = true; } },
            _clipText != null, def.useGlobalTitleText, out boxTitleGlobalNew,
            titleForHeader, out bool titleTogChg))
        {
            var  titleSource = def.useGlobalTitleText         ? (ZUI.ActiveSheet?.globalBox?.titleText ?? def.titleText)
                             : !string.IsNullOrEmpty(def.titleTextStyleId) ? (_sheet?.FindText(def.titleTextStyleId)?.text ?? def.titleText)
                             : def.titleText;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledGroupScope(def.useGlobalTitleText))
            {
                DrawTextRowWithStyleRef(titleSource, ref def.titleTextStyleId, out bool refChg);
                if (refChg) { def.Invalidate(); changed = true; }
            }
            bool titleLocked = def.useGlobalTitleText || !string.IsNullOrEmpty(def.titleTextStyleId);
            using (new EditorGUI.DisabledGroupScope(titleLocked))
                DrawShadowTextRow(titleSource);
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        if (titleTogChg) { def.Invalidate(); changed = true; }
        if (boxTitleGlobalNew != def.useGlobalTitleText) { def.useGlobalTitleText = boxTitleGlobalNew; def.Invalidate(); changed = true; }
        ZUI.VerticalSpace("V Section Rows");
        }

        // ── Content Text ─────────────────────────────────────────────────────
        if (def.showContentText)
        {
        bool boxContentGlobalNew;
        var contentForHeader = def.useGlobalContentText ? (ZUI.ActiveSheet?.globalBox?.contentText ?? def.contentText) : def.contentText;
        if (SectionHeaderText("Content Text",
            () => { _clipText = DeepCopy(def.contentText); _clipTextStyleId = def.contentTextStyleId; },
            () => { if (_clipText != null) {
                        DeepPaste(def.contentText, _clipText); def.contentTextStyleId = _clipTextStyleId;
                        changed = true; } },
            _clipText != null, def.useGlobalContentText, out boxContentGlobalNew,
            contentForHeader, out bool contentTogChg))
        {
            var  contentSource = def.useGlobalContentText         ? (ZUI.ActiveSheet?.globalBox?.contentText ?? def.contentText)
                               : !string.IsNullOrEmpty(def.contentTextStyleId) ? (_sheet?.FindText(def.contentTextStyleId)?.text ?? def.contentText)
                               : def.contentText;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledGroupScope(def.useGlobalContentText))
            {
                DrawTextRowWithStyleRef(contentSource, ref def.contentTextStyleId, out bool refChg);
                if (refChg) { def.Invalidate(); changed = true; }
            }
            bool contentLocked = def.useGlobalContentText || !string.IsNullOrEmpty(def.contentTextStyleId);
            using (new EditorGUI.DisabledGroupScope(contentLocked))
                DrawShadowTextRow(contentSource);
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        if (contentTogChg) { def.Invalidate(); changed = true; }
        if (boxContentGlobalNew != def.useGlobalContentText) { def.useGlobalContentText = boxContentGlobalNew; def.Invalidate(); changed = true; }
        ZUI.VerticalSpace("V Section Rows");
        }

        if (def.showShadow)
        {
            if (SectionHeaderWithToggleCopyPaste("Shadow", "box_shadow", ref def.bgShadow.enabled, out bool boxShadowToggleChanged,
                () => _clipShadow = DeepCopy(def.bgShadow),
                () => { if (_clipShadow != null) { DeepPaste(def.bgShadow, _clipShadow); def.Invalidate(); changed = true; } },
                _clipShadow != null))
            {
                if (def.bgShadow.enabled)
                {
                    DrawBgShadowFields(def.bgShadow, out bool sc);
                    if (sc) { def.Invalidate(); changed = true; }
                }
            }
            if (boxShadowToggleChanged) { def.Invalidate(); changed = true; }
        }

        // ── Padding ──────────────────────────────────────────────────────────
        if (def.showPadding)
        {
        ZUI.VerticalSpace("V Section Rows");
        bool boxPadGlobalNew;
        if (SectionHeaderWithCopyPasteAndGlobal("Padding",
            () => _clipPadding = (DeepCopy(def.padding), def.useGlobalPadding),
            () => { if (_clipPadding.HasValue) {
                        DeepPaste(def.padding, _clipPadding.Value.pad);
                        def.useGlobalPadding = _clipPadding.Value.useGlobal;
                        def.Invalidate(); changed = true; } },
            _clipPadding.HasValue, def.useGlobalPadding, out boxPadGlobalNew))
        {
            EditorGUI.BeginChangeCheck();
            var gp = def.useGlobalPadding ? ZUI.ActiveSheet?.globalBox : null;
            using (new EditorGUI.DisabledGroupScope(def.useGlobalPadding))
                DrawPaddingEditor(gp != null ? gp.padding : def.padding, showMargin: true);
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }
        if (boxPadGlobalNew != def.useGlobalPadding) { def.useGlobalPadding = boxPadGlobalNew; def.Invalidate(); changed = true; }
        }

        // ── Shape ────────────────────────────────────────────────────────────
        if (def.showShape)
        {
        ZUI.VerticalSpace("V Section Rows");
        bool boxShapeGlobalNew;
        if (SectionHeaderWithCopyPasteAndGlobal("Shape",
            () => _clipShape = (def.shape.cornerRadius, def.useGlobalShape),
            () => { if (_clipShape.HasValue)
                    { def.shape.cornerRadius = _clipShape.Value.r;
                      def.useGlobalShape     = _clipShape.Value.useGlobal;
                      changed = true; } },
            _clipShape.HasValue, def.useGlobalShape, out boxShapeGlobalNew))
        {
            EditorGUI.BeginChangeCheck();
            {
                var gs = def.useGlobalShape ? ZUI.ActiveSheet?.globalBox : null;
                using (new EditorGUI.DisabledGroupScope(def.useGlobalShape))
                    DrawShapeEditor(gs != null ? gs.shape : def.shape, 24);
            }
            if (EditorGUI.EndChangeCheck()) changed = true;
        }
        if (boxShapeGlobalNew != def.useGlobalShape) { def.useGlobalShape = boxShapeGlobalNew; changed = true; }
        }

        if (changed) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }

    }

    // ── Text style inspector ──────────────────────────────────────────────────

    void DrawTextStyleInspector()
    {
        if (_selectedText < 0 || _selectedText >= _sheet.textStyles.Count)
        { CenteredLabel("Select a text style."); return; }

        var def = _sheet.textStyles[_selectedText];
        bool changed = false;
        EditorGUIUtility.labelWidth = k_LabelWidth;

        TabTitle("Text Style");

        GUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        def.name = EditorGUILayout.TextField("Name", def.name);
        if (EditorGUI.EndChangeCheck()) { ZUIMissingStyleRegistry.Remove(ZUIMissingStyleRegistry.EntryType.Text, def.name); changed = true; }
        if (ChromeButton(IconFlash, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f)))
            ZUI.StartFlash(def.name, ZUI.FlashDefType.Text, _sheet);
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");

        // ── Section visibility toggles ──
        GUILayout.BeginHorizontal();
        def.showPreview = ZUI.Toggle(def.showPreview, "Prv", "Toggle", GUILayout.Height(16f));
        def.showText    = ZUI.Toggle(def.showText,    "Txt", "Toggle", GUILayout.Height(16f));
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");

        if (def.showPreview && DrawPreviewHeader($"ts_{_selectedText}_preview", showRoundingToggle: false))
        {
            // Text + Background + style picker
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Text", GUILayout.Width(28f));
            _previewTextContent = EditorGUILayout.TextArea(_previewTextContent, GUILayout.MaxWidth(200f), GUILayout.Height(36f));
            ZUI.HorizontalSpace("H Control Gap");
            EditorGUILayout.LabelField("Bg", GUILayout.Width(18f));
            _textPreviewBgMode = ZUIToolbar(_textPreviewBgMode,
                new[] { "None", "Box", "Btn" }, "TabButton", GUILayout.MaxWidth(100f));
            if (_textPreviewBgMode == 1 && _sheet.boxes.Count > 0)
            {
                _textPreviewBoxIndex = Mathf.Clamp(_textPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
                var names = new string[_sheet.boxes.Count];
                for (int i = 0; i < _sheet.boxes.Count; i++) names[i] = _sheet.boxes[i].name;
                _textPreviewBoxIndex = EditorGUILayout.Popup(_textPreviewBoxIndex, names, GUILayout.MaxWidth(100f));
            }
            else if (_textPreviewBgMode == 2 && _sheet.buttons.Count > 0)
            {
                _textPreviewButtonIndex = Mathf.Clamp(_textPreviewButtonIndex, 0, _sheet.buttons.Count - 1);
                var names = new string[_sheet.buttons.Count];
                for (int i = 0; i < _sheet.buttons.Count; i++) names[i] = _sheet.buttons[i].name;
                _textPreviewButtonIndex = EditorGUILayout.Popup(_textPreviewButtonIndex, names, GUILayout.MaxWidth(100f));
            }
            GUILayout.EndHorizontal();

            ZUI.VerticalSpace("V Section Rows");

            if (_textPreviewBgMode == 1 && _sheet.boxes.Count > 0)
            {
                _textPreviewBoxIndex = Mathf.Clamp(_textPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
                using (ZUI.Box(null, _sheet.boxes[_textPreviewBoxIndex]))
                    ZUI.Label(_previewTextContent, def);
            }
            else if (_textPreviewBgMode == 2 && _sheet.buttons.Count > 0)
            {
                _textPreviewButtonIndex = Mathf.Clamp(_textPreviewButtonIndex, 0, _sheet.buttons.Count - 1);
                var btnDef    = _sheet.buttons[_textPreviewButtonIndex];
                var textStyle = def.GetStyle(_sheet);
                var content   = new GUIContent(_previewTextContent);

                // Resolve button padding so the visual is sized the same way a real button would be.
                var _pp = btnDef.useGlobalPadding ? (_sheet?.globalButton?.padding ?? btnDef.padding) : btnDef.padding;
                int pH = _pp.PadH, pV = _pp.PadV;

                var bgRect = GUILayoutUtility.GetRect(1f, 46f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(bgRect, new Color(.13f, .13f, .15f, 1f));
                if (Event.current.type == EventType.Repaint)
                {
                    // Button rect = text content size + button padding on each side.
                    var textSize = textStyle.CalcSize(content);
                    var btnSize  = new Vector2(textSize.x + pH * 2f, textSize.y + pV * 2f);
                    var btnRect  = new Rect(
                        bgRect.x + (bgRect.width  - btnSize.x) * 0.5f,
                        bgRect.y + (bgRect.height - btnSize.y) * 0.5f,
                        btnSize.x, btnSize.y);
                    int r = ZUI.SimulateLegacyCorners ? 0 : btnDef.GetResolvedCornerRadius();
                    btnDef.DrawVisual(btnRect, ZUIButtonDrawState.Normal, r);
                    // Text drawn inside the padded inner area using the text style def.
                    var textRect = new Rect(btnRect.x + pH, btnRect.y + pV, textSize.x, textSize.y);
                    ZUI.DrawLabel(textRect, content, textStyle, def.text);
                }
            }
            else
            {
                ZUI.Label(_previewTextContent, def);
            }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (def.showText)
        {
            bool togChanged = false;
            bool open = SectionHeader(new SectionHeaderConfig {
                title = "Text", key = $"ts_{_selectedText}_text",
                leftToggles = new[] {
                    new HeaderToggle { label = "Shadow",  get = () => def.text.shadow.enabled, set = v => { def.text.shadow.enabled = v; togChanged = true; } },
                    new HeaderToggle { label = "Outline", get = () => def.text.outlineEnabled, set = v => { def.text.outlineEnabled = v; togChanged = true; } },
                },
                onCopy   = () => _clipText = DeepCopy(def.text),
                onPaste  = () => { if (_clipText != null) { DeepPaste(def.text, _clipText); def.Invalidate(); changed = true; } },
                canPaste = _clipText != null,
            });
            if (togChanged) { def.Invalidate(); changed = true; }
            if (open)
            {
                EditorGUI.BeginChangeCheck();
                DrawTextRow(def.text);
                DrawShadowTextRow(def.text);
                if (EditorGUI.EndChangeCheck() || GUI.changed) { def.Invalidate(); changed = true; }
            }
        }

        if (changed) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); }
    }

    // ── Global tab ────────────────────────────────────────────────────────────

    void DrawGlobalInspector()
    {
        _sectionAreaOpen = false;
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Subtab bar
        ZUI.VerticalSpace("V Control Gap");
        _globalSubTab = ZUIToolbar(_globalSubTab, new[] { "Button", "Box", "Layout" }, "TabButton", GUILayout.Height(20f));
        ZUI.VerticalSpace("V Control Gap");

        _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);
        EditorGUIUtility.labelWidth = k_LabelWidth;
        bool changed = false;

        var editorBoxDef = ZUI.EditorSheet?.FindBoxOrNull("StyleDef Editor");
        if (editorBoxDef != null)
        {
            var editorRect = EditorGUILayout.BeginVertical(editorBoxDef.GetLayoutStyle());
            editorBoxDef.DrawBackground(editorRect);
        }
        else
        {
            EditorGUILayout.BeginVertical();
        }

        switch (_globalSubTab)
        {
            case 0: DrawGlobalButtonSubTab(ref changed); break;
            case 1: DrawGlobalBoxSubTab(ref changed);    break;
            case 2: DrawGlobalLayoutSubTab(ref changed); break;
        }

        if (changed) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }

        EndPreviousSectionArea();
        EditorGUILayout.EndVertical(); // end StyleDef Editor box

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    void DrawGlobalButtonSubTab(ref bool changed)
    {
        TabTitle("Global Button Defaults");
        EditorGUILayout.LabelField("Button styles with 'Use Global' inherit these values.", EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Section Rows");

        if (SectionHeaderPlain("Background", "global_btn_bg"))
        {
            if (DrawFillField(_sheet.globalButton.normal))
            { _sheet.globalButton.Invalidate(); foreach (var b in _sheet.buttons) if (b.useGlobalBackground) b.Invalidate(); changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Text", "global_btn_text"))
        {
            EditorGUI.BeginChangeCheck();
            DrawTextRow(_sheet.globalButton.text);
            if (EditorGUI.EndChangeCheck()) { _sheet.globalButton.Invalidate(); foreach (var b in _sheet.buttons) if (b.useGlobalText) b.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Shape", "global_btn_shape"))
        {
            EditorGUI.BeginChangeCheck();
            _sheet.globalButton.shape.cornerRadius = Mathf.RoundToInt(ZUI.Slider(_sheet.globalButton.shape.cornerRadius, 0, 16, "Corner Radius", "SmallSlider"));
            if (EditorGUI.EndChangeCheck()) { _sheet.globalButton.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Border", "global_btn_border"))
        {
            EditorGUI.BeginChangeCheck();
            DrawBorderField(_sheet.globalButton, null);
            if (EditorGUI.EndChangeCheck()) changed = true;
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Padding", "global_btn_size"))
        {
            EditorGUI.BeginChangeCheck();
            DrawPaddingEditor(_sheet.globalButton.padding, showIcon: true);
            if (EditorGUI.EndChangeCheck())
            {
                _sheet.globalButton.Invalidate();
                foreach (var b in _sheet.buttons) if (b.useGlobalPadding) b.Invalidate();
                changed = true;
            }
        }
    }

    void DrawGlobalBoxSubTab(ref bool changed)
    {
        TabTitle("Global Box Defaults");
        EditorGUILayout.LabelField("Box styles with 'Use Global' inherit these values.", EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Section Rows");

        if (SectionHeaderPlain("Background", "global_box_bg"))
        {
            if (DrawFillField(_sheet.globalBox.background))
            { _sheet.globalBox.Invalidate(); foreach (var b in _sheet.boxes) if (b.useGlobalBackground) b.Invalidate(); changed = true; EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Title Text", "global_box_title"))
        {
            EditorGUI.BeginChangeCheck();
            DrawTextRow(_sheet.globalBox.titleText);
            if (EditorGUI.EndChangeCheck()) { _sheet.globalBox.Invalidate(); foreach (var b in _sheet.boxes) if (b.useGlobalTitleText) b.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Content Text", "global_box_content"))
        {
            EditorGUI.BeginChangeCheck();
            DrawTextRow(_sheet.globalBox.contentText);
            if (EditorGUI.EndChangeCheck()) { _sheet.globalBox.Invalidate(); foreach (var b in _sheet.boxes) if (b.useGlobalContentText) b.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Shape", "global_box_shape"))
        {
            EditorGUI.BeginChangeCheck();
            _sheet.globalBox.shape.cornerRadius = Mathf.RoundToInt(ZUI.Slider(_sheet.globalBox.shape.cornerRadius, 0, 24, "Corner Radius", "SmallSlider"));
            if (EditorGUI.EndChangeCheck()) changed = true;
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Border", "global_box_border"))
        {
            EditorGUI.BeginChangeCheck();
            DrawBorderField(_sheet.globalBox, null);
            if (EditorGUI.EndChangeCheck()) changed = true;
        }

        ZUI.VerticalSpace("V Section Rows");
        if (SectionHeaderPlain("Padding", "global_box_padding"))
        {
            EditorGUI.BeginChangeCheck();
            DrawPaddingEditor(_sheet.globalBox.padding);
            if (EditorGUI.EndChangeCheck())
            {
                _sheet.globalBox.Invalidate();
                foreach (var b in _sheet.boxes) if (b.useGlobalPadding) b.Invalidate();
                changed = true;
            }
        }
    }

    void DrawGlobalLayoutSubTab(ref bool changed)
    {
        TabTitle("Spacing");
        ZUI.VerticalSpace("V Section Rows");

        // Vertical base
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Vertical", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        float newVSpacing = ZUI.Slider(_sheet.verticalSpacing, 0f, 24f, "", "SmallSlider");
        if (EditorGUI.EndChangeCheck()) { _sheet.verticalSpacing = newVSpacing; changed = true; }
        if (this.Button(IconFlash, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f)))
            ZUI.StartVerticalSpaceFlash();
        GUILayout.EndHorizontal();

        // Horizontal base
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Horizontal", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        float newHSpacing = ZUI.Slider(_sheet.horizontalSpacing, 0f, 24f, "", "SmallSlider");
        if (EditorGUI.EndChangeCheck()) { _sheet.horizontalSpacing = newHSpacing; changed = true; }
        GUILayout.Space(k_FlashButtonPad);
        GUILayout.EndHorizontal();

        EditorGUILayout.LabelField(
            "Base units for ZUI.VerticalSpace() / HorizontalSpace(). " +
            "Pass a float scale (0.5f, 2f) or a named scale (see below).",
            EditorStyles.wordWrappedMiniLabel);

        ZUI.VerticalSpace("V Control Gap");
        TabTitle("Named Scales");
        EditorGUILayout.LabelField(
            "ZUI.VerticalSpace(\"name\") or HorizontalSpace(\"name\") multiplies the base by this scale.",
            EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Section Rows");

        var scales = _sheet.spacingScales;
        int removeAt = -1;
        for (int i = 0; i < scales.Count; i++)
        {
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            scales[i].name  = EditorGUILayout.TextField(scales[i].name,  GUILayout.Width(110f));
            scales[i].scale = ZUI.Slider(scales[i].scale, 0f, 4f, "", "SmallSlider");
            float resolvedV = _sheet.verticalSpacing   * scales[i].scale;
            float resolvedH = _sheet.horizontalSpacing * scales[i].scale;
            EditorGUILayout.LabelField($"V:{resolvedV:F1}px  H:{resolvedH:F1}px", EditorStyles.miniLabel, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck()) changed = true;
            if (this.Button("−", "TabButton", GUILayout.Width(20f))) removeAt = i;
            GUILayout.EndHorizontal();
        }
        if (removeAt >= 0) { scales.RemoveAt(removeAt); changed = true; }

        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (this.Button("+ Add Scale", "TabButton", GUILayout.Width(80f)))
        {
            scales.Add(new ZUISpacingScale { name = "New Scale", scale = 1f });
            changed = true;
        }
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        TabTitle("Label Widths");
        EditorGUILayout.LabelField("Controls label column widths for all ZUI editor rows.", EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Section Rows");

        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Wide", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        _sheet.labelWidthWide = EditorGUILayout.Slider(_sheet.labelWidthWide, 40f, 160f);
        if (EditorGUI.EndChangeCheck()) changed = true;
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Narrow", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        _sheet.labelWidthNarrow = EditorGUILayout.Slider(_sheet.labelWidthNarrow, 16f, 80f);
        if (EditorGUI.EndChangeCheck()) changed = true;
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Input Min Width", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        _sheet.inputFieldMinWidth = EditorGUILayout.Slider(_sheet.inputFieldMinWidth, 32f, 100f);
        if (EditorGUI.EndChangeCheck()) changed = true;
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        TabTitle("Flash Settings");
        EditorGUILayout.LabelField("Controls speed and duration of all ZUI flash animations.", EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Section Rows");

        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Count", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        int newCount = Mathf.RoundToInt(ZUI.Slider(_sheet.flashCount, 1, 30, "", "SmallSlider"));
        if (EditorGUI.EndChangeCheck()) { _sheet.flashCount = newCount; changed = true; }
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Speed (sec/pulse)", GUILayout.Width(k_LabelWidth));
        EditorGUI.BeginChangeCheck();
        float newInterval = ZUI.Slider(_sheet.flashInterval, 0.02f, 0.5f, "", "SmallSlider");
        if (EditorGUI.EndChangeCheck()) { _sheet.flashInterval = newInterval; changed = true; }
        GUILayout.EndHorizontal();

        float totalSec = _sheet.flashCount * _sheet.flashInterval;
        EditorGUILayout.LabelField($"Total duration: {totalSec:F1}s  ({_sheet.flashCount} pulses × {_sheet.flashInterval:F2}s)", EditorStyles.miniLabel);
    }

    // ── Gradient field ────────────────────────────────────────────────────────

    // ── ZUI.Fill wrapper — opens the gradient stop popup on click ──────────
    Action<Rect> MakeStopEditorCallback(ZUIColor g)
    {
        return barRect =>
        {
            var capturedG = g;
            var popup = new ZUIGradientStopPopup(capturedG, _sheet?.palette, _sheet, () =>
            {
                capturedG.Invalidate();
                EditorUtility.SetDirty(_sheet);
                RepaintShowcase();
                Repaint();
            });
            popup.SetWidth(barRect.width);
            PopupWindow.Show(barRect, popup);
        };
    }

    // Draws a fill editor using ZUI.Fill, with the stop popup wired up.
    bool DrawFillField(ZUIColor g, bool allowGradient = true, bool hidePxEdge = false)
    {
        ZUI.VerticalSpace("V Section Rows");
        return ZUI.ColorEditor(g, MakeStopEditorCallback(g), allowGradient, hidePxEdge, _sheet?.palette);
    }


    // ── Direction picker (pixel mode only) ───────────────────────────────────
    // Each button toggles an edge independently — multiple edges can be active at once.
    // colorA appears at the edge; colorB fills the rest after pixelLength pixels.

    static readonly (string label, ZUIPixelEdges edge, string tooltip)[] k_Edges =
    {
        ("←", ZUIPixelEdges.Left,   "Left edge  — colorA on left"),
        ("→", ZUIPixelEdges.Right,  "Right edge  — colorA on right"),
        ("↑", ZUIPixelEdges.Bottom, "Bottom edge — colorA at bottom"),
        ("↓", ZUIPixelEdges.Top,    "Top edge    — colorA at top"),
    };

    static void DrawDirectionPicker(ZUIColor g)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Edges", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        foreach (var (lbl, edge, tip) in k_Edges)
        {
            bool active = (g.pixelEdges & edge) != 0;
            bool next   = ZUI.Toggle(active, new GUIContent(lbl, tip),
                              "Toggle", GUILayout.Width(28f));
            if (next != active)
                g.pixelEdges = next ? (g.pixelEdges | edge) : (g.pixelEdges & ~edge);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    // ── Text row (compact single-line) ───────────────────────────────────────

    void DrawTextRow(ZUITextDef text)
    {
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Text", GUILayout.Width(32f));
        DrawTextDefFields(text);
        GUILayout.EndHorizontal();
    }

    void DrawTextRowWithStyleRef(ZUITextDef text, ref string styleId, out bool refChanged)
    {
        ZUI.VerticalSpace("V Section Rows");
        refChanged = false;
        GUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        string newRef = DrawTextStyleRefPopupInline(styleId);
        if (EditorGUI.EndChangeCheck()) { styleId = newRef; refChanged = true; }

        bool hasRef = !string.IsNullOrEmpty(styleId);
        using (new EditorGUI.DisabledGroupScope(hasRef))
            DrawTextDefFields(text);
        GUILayout.EndHorizontal();
    }

    /// <summary>Draws the inline text fields: color, gradient toggle, colorB, font size, font style.</summary>
    void DrawTextDefFields(ZUITextDef text)
    {
        float prevLW = EditorGUIUtility.labelWidth;
        ZUIColorPickerInline(ref text.color);
        text.gradientEnabled = ZUI.Toggle(text.gradientEnabled, text.gradientEnabled ? "\u2192" : "\u2022", "Toggle", GUILayout.Width(20f));
        if (text.gradientEnabled)
            ZUIColorPickerInline(ref text.colorB);
        EditorGUIUtility.labelWidth = 28f;
        text.fontSize  = Mathf.Max(0, EditorGUILayout.IntField("Sz", text.fontSize, GUILayout.Width(56f)));
        EditorGUIUtility.labelWidth = prevLW;
        text.fontStyle = (FontStyle)EditorGUILayout.EnumPopup(GUIContent.none, text.fontStyle, GUILayout.Width(70f));
    }

    void DrawShadowTextRow(ZUITextDef text)
    {
        DrawShadowTextDetails(text);
        DrawOutlineTextDetails(text);
    }

    void DrawShadowTextDetails(ZUITextDef text)
    {
        if (!text.shadow.enabled) return;
        var color = ZUI.Control(() => ZUIColorPickerInline(ref text.shadow.color));
        var offX  = ZUI.FloatField(() => text.shadow.offset.x, v => text.shadow.offset.x = v, 48f);
        var offY  = ZUI.FloatField(() => text.shadow.offset.y, v => text.shadow.offset.y = v, 48f);

        var form = ZUI.Form();
        form.Add(ZUI.Row("Shadow").Add(color).Add(offX).Add(offY));
        form.Draw();
    }

    void DrawOutlineTextDetails(ZUITextDef text)
    {
        if (!text.outlineEnabled) return;
        var color  = ZUI.Control(() => ZUIColorPickerInline(ref text.outlineColor));
        var width  = ZUI.IntField(() => text.outlineWidth, v => text.outlineWidth = Mathf.Clamp(v, 1, 3), 40f);
        var passes = ZUI.Control(44f, () =>
            text.outlinePasses = ZUIToolbar(text.outlinePasses >= 8 ? 1 : 0,
                new[] { "4", "8" }, "TabButton", GUILayout.Width(44f)) == 1 ? 8 : 4);

        var form = ZUI.Form();
        form.Add(ZUI.Row("Outline").Add(color).Add(width).Add(passes));
        form.Draw();
    }

    // Draws a "Text Style" popup — returns the selected style's name, or "" for inline.
    // Call without Begin/EndHorizontal — caller handles horizontal layout.
    string DrawTextStyleRefPopupInline(string currentId)
    {
        var styles = _sheet?.textStyles;
        if (styles == null || styles.Count == 0) return currentId;

        int currentIdx = 0;
        var names = new string[styles.Count + 1];
        names[0] = "— Inline —";
        for (int i = 0; i < styles.Count; i++)
        {
            names[i + 1] = styles[i].name;
            if (styles[i].name == currentId) currentIdx = i + 1;
        }

        int newIdx = EditorGUILayout.Popup(currentIdx, names, GUILayout.Width(90f));
        return newIdx == 0 ? "" : names[newIdx];
    }

    void DrawAnimationTimingRow(string label, ref float duration, ref ZUIEaseCurve ease)
    {
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(58f));
        duration = Mathf.Max(0.01f, EditorGUILayout.FloatField(duration, GUILayout.Width(40f)));
        EditorGUILayout.LabelField("s", GUILayout.Width(10f));
        var easeRect = GUILayoutUtility.GetRect(120f, 18f, EditorStyles.popup, GUILayout.Width(120f));
        ease = ZUI.EasePicker(easeRect, ease);
        GUILayout.EndHorizontal();
    }

    void DrawBgShadowFields(ZUIDropShadowDef shadow, out bool shadowChanged)
    {
        const float padSize = 60f;

        EditorGUI.BeginChangeCheck();

        // Color row
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Color", ZUI.LabelNarrow());
        ZUIColorPickerInline(ref shadow.tint);
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        // XY pad (left) + Blur/Passes (right), height-matched via Blocks
        using (var blocks = ZUI.Blocks("shadow_fields"))
        {
            using (blocks.Cell(ZUIAlign.Top))
            {
                shadow.offset = ZUI.Slider2D(shadow.offset,
                    new Vector2(-20f, -20f), new Vector2(20f, 20f),
                    size: padSize, labelX: "X", labelY: "Y",
                    defaultValue: Vector2.zero, flipY: true);
            }
            using (blocks.Cell(ZUIAlign.Even, GUILayout.Width(100f)))
            {
                shadow.blurRadius = ZUI.MicroSlider(shadow.blurRadius, 0f, 20f, "Blur");
                GUILayout.FlexibleSpace();
                shadow.blurPasses = Mathf.RoundToInt(ZUI.MicroSlider(shadow.blurPasses, 1f, 20f, "Passes"));
            }
        }

        shadowChanged = EditorGUI.EndChangeCheck();
    }

    void DrawPatternFields(ZUIPatternDef pattern, out bool patternChanged)
    {
        string typeName = pattern.patternType == ZUIPatternType.Icon && !string.IsNullOrEmpty(pattern.iconId)
            ? pattern.iconId
            : pattern.patternType.ToString();

        var pickerCtrl = ZUI.Control(() => {
            if (GUILayout.Button(typeName, EditorStyles.popup, GUILayout.Width(100f)))
            {
                var btnRect = GUILayoutUtility.GetLastRect();
                var self = this;
                var popup = new ZUIPatternPickerPopup(pattern, _sheet, () =>
                {
                    pattern.Invalidate();
                    EditorUtility.SetDirty(_sheet);
                    self.Repaint();
                    EditorApplication.delayCall += () => self.Repaint();
                });
                PopupWindow.Show(btnRect, popup);
            }
        });
        var tintCtrl  = ZUI.Control(() => ZUIColorPickerInline(ref pattern.tint));
        var scaleCtrl = ZUI.Control(() => {
            EditorGUILayout.LabelField("Scale", GUILayout.Width(ZUI.LabelWidthNarrow));
            pattern.scale = EditorGUILayout.Slider(pattern.scale, 0.1f, 4f);
        });
        var rotCtrl   = ZUI.Control(() => {
            EditorGUILayout.LabelField("Angle", GUILayout.Width(ZUI.LabelWidthNarrow));
            pattern.rotation = EditorGUILayout.Slider(pattern.rotation, 0f, 360f);
        });

        var offXCtrl = ZUI.Control(() => {
            EditorGUILayout.LabelField("X", GUILayout.Width(12f));
            pattern.offsetX = EditorGUILayout.Slider(pattern.offsetX, 0f, 1f);
        });
        var offYCtrl = ZUI.Control(() => {
            EditorGUILayout.LabelField("Y", GUILayout.Width(12f));
            pattern.offsetY = EditorGUILayout.Slider(pattern.offsetY, 0f, 1f);
        });

        var form = ZUI.Form();
        form.Add(ZUI.Row("Type").Add(pickerCtrl).Add(tintCtrl));
        form.Add(ZUI.Row("").Add(scaleCtrl).Add(rotCtrl));
        form.Add(ZUI.Row("Offset").Add(offXCtrl).Add(offYCtrl));
        patternChanged = form.Draw();
    }

    void DrawGlowFields(ZUIGlowDef glow, out bool glowChanged)
    {
        var colorCtrl  = ZUI.Control(() => ZUIColorPickerInline(ref glow.color));
        var radiusCtrl = ZUI.FloatField(() => glow.radius, v => glow.radius = Mathf.Max(0f, v), 72f);
        var passesCtrl = ZUI.IntField(() => glow.passes, v => glow.passes = Mathf.Clamp(v, 1, 16), 76f);

        var spreadCtrl = ZUI.Control(() => {
            string edgeIcon = glow.edgeMode == 0 ? "■" : "⊞";
            string edgeTip  = glow.edgeMode == 0 ? "Uniform — click for per-edge" : "Per-edge — click for uniform";
            if (this.Button(new GUIContent(edgeIcon, edgeTip), "TabButton", GUILayout.Width(18f), GUILayout.Height(16f)))
                glow.edgeMode = glow.edgeMode == 0 ? 1 : 0;
            if (glow.edgeMode == 1)
            {
                float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 12f;
                glow.spreadTop    = ZUI.Slider(glow.spreadTop, 0f, 1f, "T", "SmallSlider", (float?)null, GUILayout.Width(80f));
                glow.spreadRight  = ZUI.Slider(glow.spreadRight, 0f, 1f, "R", "SmallSlider", (float?)null, GUILayout.Width(80f));
                glow.spreadBottom = ZUI.Slider(glow.spreadBottom, 0f, 1f, "B", "SmallSlider", (float?)null, GUILayout.Width(80f));
                glow.spreadLeft   = ZUI.Slider(glow.spreadLeft, 0f, 1f, "L", "SmallSlider", (float?)null, GUILayout.Width(80f));
                EditorGUIUtility.labelWidth = lw;
            }
            else
                EditorGUILayout.LabelField("Uniform", EditorStyles.miniLabel, GUILayout.Width(50f));
        });

        var innerToggle = ZUI.Control(42f, () =>
            glow.innerEnabled = ZUI.Toggle(glow.innerEnabled, "Inner", "Toggle", GUILayout.Width(42f), GUILayout.Height(16f)));
        var innerColorCtrl  = ZUI.Control(() => ZUIColorPickerInline(ref glow.innerColor));
        var innerRadiusCtrl = ZUI.FloatField(() => glow.innerRadius, v => glow.innerRadius = Mathf.Max(0f, v), 72f);
        var innerPassesCtrl = ZUI.IntField(() => glow.innerPasses, v => glow.innerPasses = Mathf.Clamp(v, 1, 16), 76f);

        var form = ZUI.Form();
        form.Add(ZUI.Row("Color").Add(colorCtrl).Add(radiusCtrl).Add(passesCtrl));
        form.Add(ZUI.Row("Spread").Add(spreadCtrl));
        var innerRow = ZUI.Row("Inner").Add(innerToggle);
        if (glow.innerEnabled)
            innerRow.Add(innerColorCtrl).Add(innerRadiusCtrl).Add(innerPassesCtrl);
        form.Add(innerRow);
        glowChanged = form.Draw();
    }

    // ── Border field ──────────────────────────────────────────────────────────

    // ── Shared shape editor ─────────────────────────────────────────────────
    void DrawShapeEditor(ZUIShapeDef shape, int maxRadius = 16)
    {
        const float rowH = 18f;
        const float gapH = 2f;

        GUILayout.BeginHorizontal();

        // Left side: stacked slider (label+value on top, track below)
        EditorGUI.BeginChangeCheck();
        float sliderVal = ZUI.SliderStacked((float)shape.cornerRadius, 0, maxRadius, "Radius", "SmallSlider");
        if (EditorGUI.EndChangeCheck())
            shape.cornerRadius = Mathf.RoundToInt(sliderVal);

        ZUI.HorizontalSpace();

        // Right side: 2x2 corner toggle grid — always visible
        GUILayout.BeginVertical(GUILayout.Width(170f));
        GUILayout.BeginHorizontal(GUILayout.Height(rowH));
        shape.roundTL = ZUI.Toggle(shape.roundTL, "Top Left", "Toggle", GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
        shape.roundTR = ZUI.Toggle(shape.roundTR, "Top Right", "Toggle", GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
        GUILayout.EndHorizontal();
        GUILayout.Space(gapH);
        GUILayout.BeginHorizontal(GUILayout.Height(rowH));
        shape.roundBL = ZUI.Toggle(shape.roundBL, "Bottom Left", "Toggle", GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
        shape.roundBR = ZUI.Toggle(shape.roundBR, "Bottom Right", "Toggle", GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    /// <summary>Draws "Inherited from box: X" + Override toggle. Returns true if the section should show its own controls.</summary>
    bool DrawBoxOverrideToggle(ZUIButtonDef def, string sectionName, ref bool overrideFlag, ref bool changed)
    {
        if (!def.HasBoxStyle) return true; // no box style, always show own controls
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Inherited from box: " + def.boxStyle, EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        overrideFlag = ZUI.Toggle(overrideFlag, "Override", "Toggle", GUILayout.Height(16f));
        if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        GUILayout.EndHorizontal();
        return overrideFlag;
    }

    // ── Shared border editor ─────────────────────────────────────────────────
    void DrawBorderField(ZUIBoxDef def, Action onExternalPaste) => DrawBorderDefField(def.border, onExternalPaste, compact: false);
    void DrawBorderField(ZUIButtonDef def, Action onExternalPaste) => DrawBorderDefField(def.border, onExternalPaste, compact: false);

    void DrawBorderDefField(ZUIBorderDef border, Action onExternalPaste, bool compact = false)
    {
        ZUI.VerticalSpace("V Section Rows");

        if (compact)
        {
            // Compact mode: single color + single width slider via Form
            var widthCtrl = ZUI.Slider(() => border.edgeWidth.all, v => border.edgeWidth.all = v, 0f, 4f, "SmallSlider");
            var colorCtrl = ZUI.Control(() => {
                if (ZUIColorPickerInline(ref border.color.colorA))
                    border.color.Invalidate();
            });

            var form = ZUI.Form();
            form.Add("Border Width", widthCtrl);
            if (border.edgeWidth.all > 0f)
                form.Add("Border Color", colorCtrl);
            form.Draw();
            return;
        }

        var fieldRect = EditorGUILayout.BeginVertical();

        EditorGUI.BeginChangeCheck();
        DrawFillField(border.color, hidePxEdge: true);
        if (EditorGUI.EndChangeCheck()) { border.color.Invalidate(); }

        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        DrawEdgeFieldFloat("Width", border.edgeWidth, 42f);
        GUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        if (Event.current.type == EventType.ContextClick && fieldRect.Contains(Event.current.mousePosition))
        {
            var capturedBorder = border;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Border"), false, () =>
                _clipBorder = DeepCopy(capturedBorder));
            if (_clipBorder != null)
                menu.AddItem(new GUIContent("Paste Border"), false, () =>
                {
                    DeepPaste(capturedBorder, _clipBorder);
                    capturedBorder.color.Invalidate();
                    onExternalPaste?.Invoke();
                    Repaint();
                });
            else
                menu.AddDisabledItem(new GUIContent("Paste Border"));
            menu.ShowAsContext();
            Event.current.Use();
        }
    }

    /// <summary>Draws an edge values field with clickable mode icon (① ② ④).</summary>
    void DrawEdgeField(string label, ZUIEdgeValues edge, float labelW = 28f)
    {
        float _lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 12f;

        // Mode icon — cycles on click
        string modeIcon = edge.mode == 0 ? "■" : edge.mode == 1 ? "⬒" : "⊞";
        string modeTip  = edge.mode == 0 ? "Uniform — click for V|H" : edge.mode == 1 ? "V|H — click for T|R|B|L" : "T|R|B|L — click for uniform";
        if (this.Button(new GUIContent(modeIcon, modeTip), "TabButton", GUILayout.Width(18f), GUILayout.Height(16f)))
        {
            edge.mode = (edge.mode + 1) % 3;
            // Promote values when expanding
            if (edge.mode == 1) { edge.v = edge.all; edge.h = edge.all; }
            if (edge.mode == 2) { edge.top = edge.v; edge.bottom = edge.v; edge.left = edge.h; edge.right = edge.h; }
        }

        EditorGUILayout.LabelField(label, GUILayout.Width(labelW));

        if (edge.mode == 0)
        {
            edge.all = Mathf.Max(0, EditorGUILayout.IntField(edge.all, GUILayout.Width(36f)));
        }
        else if (edge.mode == 1)
        {
            edge.v = Mathf.Max(0, EditorGUILayout.IntField("V", edge.v, GUILayout.Width(42f)));
            edge.h = Mathf.Max(0, EditorGUILayout.IntField("H", edge.h, GUILayout.Width(42f)));
        }
        else
        {
            edge.top    = Mathf.Max(0, EditorGUILayout.IntField("T", edge.top,    GUILayout.Width(42f)));
            edge.right  = Mathf.Max(0, EditorGUILayout.IntField("R", edge.right,  GUILayout.Width(42f)));
            edge.bottom = Mathf.Max(0, EditorGUILayout.IntField("B", edge.bottom, GUILayout.Width(42f)));
            edge.left   = Mathf.Max(0, EditorGUILayout.IntField("L", edge.left,   GUILayout.Width(42f)));
        }

        EditorGUIUtility.labelWidth = _lw;
    }

    /// <summary>Float version of DrawEdgeField — for border width etc.</summary>
    void DrawEdgeFieldFloat(string label, ZUIEdgeValuesFloat edge, float labelW = 28f)
    {
        float _lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 12f;

        string modeIcon = edge.mode == 0 ? "■" : edge.mode == 1 ? "⬒" : "⊞";
        string modeTip  = edge.mode == 0 ? "Uniform — click for V|H" : edge.mode == 1 ? "V|H — click for T|R|B|L" : "T|R|B|L — click for uniform";
        if (this.Button(new GUIContent(modeIcon, modeTip), "TabButton", GUILayout.Width(18f), GUILayout.Height(16f)))
        {
            edge.mode = (edge.mode + 1) % 3;
            if (edge.mode == 1) { edge.v = edge.all; edge.h = edge.all; }
            if (edge.mode == 2) { edge.top = edge.v; edge.bottom = edge.v; edge.left = edge.h; edge.right = edge.h; }
        }

        EditorGUILayout.LabelField(label, GUILayout.Width(labelW));

        if (edge.mode == 0)
        {
            edge.all = Mathf.Max(0f, EditorGUILayout.FloatField(edge.all, GUILayout.Width(42f)));
        }
        else if (edge.mode == 1)
        {
            edge.v = Mathf.Max(0f, EditorGUILayout.FloatField("V", edge.v, GUILayout.Width(48f)));
            edge.h = Mathf.Max(0f, EditorGUILayout.FloatField("H", edge.h, GUILayout.Width(48f)));
        }
        else
        {
            edge.top    = Mathf.Max(0f, EditorGUILayout.FloatField("T", edge.top,    GUILayout.Width(48f)));
            edge.right  = Mathf.Max(0f, EditorGUILayout.FloatField("R", edge.right,  GUILayout.Width(48f)));
            edge.bottom = Mathf.Max(0f, EditorGUILayout.FloatField("B", edge.bottom, GUILayout.Width(48f)));
            edge.left   = Mathf.Max(0f, EditorGUILayout.FloatField("L", edge.left,   GUILayout.Width(48f)));
        }

        EditorGUIUtility.labelWidth = _lw;
    }

    void DrawPaddingEditor(ZUIPaddingDef padding, bool showIcon = false, bool showMargin = false)
    {
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        DrawEdgeField("Pad", padding.pad);
        if (showIcon)
        {
            ZUI.HorizontalSpace("H Control Gap Big");
            DrawEdgeField("Icon", padding.iconPad);
        }
        if (showMargin)
        {
            ZUI.HorizontalSpace("H Control Gap Big");
            DrawEdgeField("Margin", padding.margin, 42f);
        }
        GUILayout.EndHorizontal();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    bool DrawPreviewHeader(string key = null, bool showRoundingToggle = true)
    {
        EndPreviousSectionArea();
        string k = key ?? $"{_activeTab}_Preview";
        bool expanded = GetFoldout(k);
        var rect = GUILayoutUtility.GetRect(1f, 28f, SectionHeaderRectStyle, GUILayout.ExpandWidth(true));
        DrawSectionHeaderBg(rect);
        float cy = rect.y + (rect.height - 14f) * 0.5f;

        EditorGUI.LabelField(new Rect(rect.x + 4f, cy, 14f, 14f), expanded ? "▾" : "▸", SectionHeaderLabelStyle);
        EditorGUI.LabelField(new Rect(rect.x + 18f, cy, 80f, 14f), "Preview", SectionHeaderLabelStyle);

        Rect toggleRect = default;
        if (showRoundingToggle)
        {
            const float tw = 100f;
            toggleRect = new Rect(rect.xMax - tw - 4f, rect.y + (rect.height - 14f) * 0.5f + 1f, tw, 14f);
            EditorGUI.BeginChangeCheck();
            _simulateLegacy = ZUI.Toggle(toggleRect, _simulateLegacy, "Simulate No Rounding", "Toggle");
            if (EditorGUI.EndChangeCheck())
            {
                ZUI.SimulateLegacyCorners = _simulateLegacy;
                RepaintShowcase();
            }
        }

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)
            && (!showRoundingToggle || !toggleRect.Contains(Event.current.mousePosition)))
        {
            expanded = !expanded;
            SetFoldout(k, expanded);
            Event.current.Use();
            Repaint();
        }
        if (expanded) BeginSectionAreaBlock();
        return expanded;
    }

    void DrawButtonPreview(ZUIButtonDef def)
    {
        // Label + Background on one row
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Label", GUILayout.Width(34f));
        _previewButtonText = EditorGUILayout.TextField(_previewButtonText, GUILayout.MaxWidth(120f));
        ZUI.HorizontalSpace("H Control Gap");
        EditorGUILayout.LabelField("Bg", GUILayout.Width(18f));
        _buttonPreviewBgMode = ZUI.MiniRadio(_buttonPreviewBgMode,
            new[] { "None", "Box" }, "Toggle");
        if (_buttonPreviewBgMode == 1 && _sheet.boxes.Count > 0)
        {
            _buttonPreviewBoxIndex = Mathf.Clamp(_buttonPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
            var names = new string[_sheet.boxes.Count];
            for (int i = 0; i < _sheet.boxes.Count; i++) names[i] = _sheet.boxes[i].name;
            _buttonPreviewBoxIndex = EditorGUILayout.Popup(_buttonPreviewBoxIndex, names, GUILayout.MaxWidth(100f));
        }
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        // Scope to edited sheet only for the actual preview rendering, not the settings toolbar above.
        using (ZUI.UseSheet(_sheet))
        {
            var content    = new GUIContent(_previewButtonText);
            var labelStyle = def.GetLabelStyle();
            var btnSize    = labelStyle.CalcSize(content);

            bool useBox = _buttonPreviewBgMode == 1 && _sheet.boxes.Count > 0;
            ZUIBoxDef boxDef = useBox ? _sheet.boxes[_buttonPreviewBoxIndex] : null;

            if (useBox)
            {
                using (ZUI.Box(null, boxDef))
                {
                    DrawButtonPreviewInner(def, content, btnSize, hasBoxBg: true);
                }
            }
            else
            {
                DrawButtonPreviewInner(def, content, btnSize, hasBoxBg: false);
            }
        }
    }

    void DrawButtonPreviewInner(ZUIButtonDef def, GUIContent content, UnityEngine.Vector2 btnSize, bool hasBoxBg = false)
    {
        if (_buttonStateTab == 0)
        {
            var bgRect = GUILayoutUtility.GetRect(1f, 46f, GUILayout.ExpandWidth(true));
            if (!hasBoxBg) EditorGUI.DrawRect(bgRect, new Color(.13f, .13f, .15f, 1f));
            EditorGUI.LabelField(new Rect(bgRect.x + 8f, bgRect.y + 2f, 200f, 14f),
                "Hover and click to test", EditorStyles.miniLabel);
            var btnRect = new Rect(bgRect.x + (bgRect.width  - btnSize.x) * 0.5f,
                                   bgRect.y + (bgRect.height - btnSize.y) * 0.5f + 4f,
                                   btnSize.x, btnSize.y);
            ZUI.Button(btnRect, content, def);
        }
        else
        {
            // In toggle mode: tab 1 = ToggleOn. In normal mode: 1=Hover, 2=Active, 3=ToggleOn.
            ZUIButtonDrawState forcedState;
            string stateLabel;
            if (_previewIsToggleMode)
            {
                forcedState = ZUIButtonDrawState.ToggleOn;
                stateLabel  = "Forced Toggle On state";
            }
            else if (_buttonStateTab == 1) { forcedState = ZUIButtonDrawState.Hover;    stateLabel = "Forced Hover state"; }
            else if (_buttonStateTab == 2) { forcedState = ZUIButtonDrawState.Active;   stateLabel = "Forced Active state"; }
            else                           { forcedState = ZUIButtonDrawState.ToggleOn; stateLabel = "Forced Toggle On state"; }
            var bgRect = GUILayoutUtility.GetRect(1f, 46f, GUILayout.ExpandWidth(true));
            if (!hasBoxBg) EditorGUI.DrawRect(bgRect, new Color(.13f, .13f, .15f, 1f));
            EditorGUI.LabelField(new Rect(bgRect.x + 8f, bgRect.y + 2f, 200f, 14f),
                stateLabel, EditorStyles.miniLabel);

            if (Event.current.type == EventType.Repaint)
            {
                var btnRect = new Rect(bgRect.x + (bgRect.width  - btnSize.x) * 0.5f,
                                       bgRect.y + (bgRect.height - btnSize.y) * 0.5f + 4f,
                                       btnSize.x, btnSize.y);
                int r = ZUI.SimulateLegacyCorners ? 0 : def.GetResolvedCornerRadius();
                def.DrawVisual(btnRect, forcedState, r);
                ZUI.DrawButtonLabel(btnRect, content, def.GetLabelStyle(forcedState), null, ZIconPlacement.LeftOfLabel, def);
            }
        }
    }

    void DrawTogglePreview(ZUIButtonDef def)
    {
        // Label + Background on one row
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Label", GUILayout.Width(34f));
        _previewButtonText = EditorGUILayout.TextField(_previewButtonText, GUILayout.MaxWidth(120f));
        ZUI.HorizontalSpace("H Control Gap");
        EditorGUILayout.LabelField("Bg", GUILayout.Width(18f));
        _buttonPreviewBgMode = ZUI.MiniRadio(_buttonPreviewBgMode,
            new[] { "None", "Box" }, "Toggle");
        if (_buttonPreviewBgMode == 1 && _sheet.boxes.Count > 0)
        {
            _buttonPreviewBoxIndex = Mathf.Clamp(_buttonPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
            var names = new string[_sheet.boxes.Count];
            for (int i = 0; i < _sheet.boxes.Count; i++) names[i] = _sheet.boxes[i].name;
            _buttonPreviewBoxIndex = EditorGUILayout.Popup(_buttonPreviewBoxIndex, names, GUILayout.MaxWidth(100f));
        }
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Control Gap");

        // Scope to edited sheet only for the actual preview rendering, not the settings toolbar above.
        using (ZUI.UseSheet(_sheet))
        {
            bool useBox = _buttonPreviewBgMode == 1 && _sheet.boxes.Count > 0;
            if (useBox)
            {
                using (ZUI.Box(null, _sheet.boxes[_buttonPreviewBoxIndex]))
                    DrawTogglePreviewInner(def, hasBoxBg: true);
            }
            else
            {
                DrawTogglePreviewInner(def, hasBoxBg: false);
            }
        }
    }

    void DrawTogglePreviewInner(ZUIButtonDef def, bool hasBoxBg = false)
    {
        var bgRect = GUILayoutUtility.GetRect(1f, 48f, GUILayout.ExpandWidth(true));
        if (!hasBoxBg) EditorGUI.DrawRect(bgRect, new Color(.13f, .13f, .15f, 1f));
        EditorGUI.LabelField(new Rect(bgRect.x + 8f, bgRect.y + 2f, 200f, 14f),
            "ZToggle preview — click to toggle", EditorStyles.miniLabel);

        var content    = new GUIContent(_previewButtonText);
        var labelStyle = def.GetLabelStyle();
        var btnSize    = labelStyle.CalcSize(content);
        float btnW     = Mathf.Max(btnSize.x, 60f);
        float btnH     = btnSize.y;
        float x        = bgRect.x + (bgRect.width - btnW) * 0.5f;
        float y        = bgRect.y + (bgRect.height - btnH - 14f) * 0.5f + 4f;

        var toggleRect = new Rect(x, y, btnW, btnH);
        _previewToggleValue = ZUI.Toggle(toggleRect, _previewToggleValue, content, def);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.LabelField(new Rect(toggleRect.x, toggleRect.yMax + 2f, toggleRect.width, 12f),
                _previewToggleValue ? "On — click to turn Off" : "Off — click to turn On",
                EditorStyles.centeredGreyMiniLabel);
        }
    }

    void DrawBoxPreview(ZUIBoxDef def)
    {
        // Title + Content
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Title", GUILayout.Width(32f));
        _previewBoxTitle = EditorGUILayout.TextField(_previewBoxTitle, GUILayout.MaxWidth(120f));
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Content", GUILayout.Width(48f));
        _previewBoxContent = EditorGUILayout.TextArea(_previewBoxContent, GUILayout.Height(36f));
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");

        using (ZUI.UseSheet(_sheet))
        {
            using (ZUI.Box(_previewBoxTitle, def))
            {
                if (!string.IsNullOrEmpty(_previewBoxContent))
                    ZUI.Label(_previewBoxContent);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetSheet(ZUIStyleSheetAsset sheet)
    {
        _sheet = sheet; _selectedButton = 0; _selectedBox = 0;
        // Save to prefs so it persists across domain reloads
        if (sheet != null)
            EditorPrefs.SetString("ZUIStyleEditor_LastSheet", UnityEditor.AssetDatabase.GetAssetPath(sheet));
        RepaintShowcase();
    }

    void CreateNewSheet()
    {
        var path = EditorUtility.SaveFilePanelInProject("Create Zheet",
            "ZUIStyleSheet", "asset", "Choose location for new Zheet");
        if (string.IsNullOrEmpty(path)) return;
        var asset = CreateInstance<ZUIStyleSheetAsset>();
        // Empty sheet by design — user adds styles via the editor (or copies from DefaultSheet).
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        SetSheet(asset);
    }

    void RepaintShowcase()
    {
        // Bump version so ZUIWindow subclasses detect the change and refresh cached GUIStyles.
        if (_sheet != null) _sheet.BumpVersion();
        // Repaint all editor windows that may use ZUI styles
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>()) w.Repaint();
    }

    // ── Copy / paste helpers ──────────────────────────────────────────────────
    //
    // All copy/paste is JSON-based via Unity's JsonUtility. This ensures every
    // serialized field is included automatically — no manual field list to
    // maintain when new properties are added to ZUIButtonDef, ZUIBoxDef,
    // ZUIColor, ZUITextDef, etc.

    /// <summary>Deep-copy a [Serializable] object via JSON round-trip.</summary>
    static T DeepCopy<T>(T src) where T : new()
    {
        string json = JsonUtility.ToJson(src);
        var copy = new T();
        JsonUtility.FromJsonOverwrite(json, copy);
        return copy;
    }

    /// <summary>Paste all serialized fields from src onto dst (in-place).</summary>
    static void DeepPaste<T>(T dst, T src)
    {
        string json = JsonUtility.ToJson(src);
        JsonUtility.FromJsonOverwrite(json, dst);
    }

    static void PasteGrad(ZUIColor dst, ZUIColor src)
    {
        DeepPaste(dst, src);
        dst.Invalidate();
    }

    static ZUIButtonDef CopyButtonDef(ZUIButtonDef src) => DeepCopy(src);

    static void PasteButtonDef(ZUIButtonDef dst, ZUIButtonDef src)
    {
        string name = dst.name; // preserve the target's name
        DeepPaste(dst, src);
        dst.name = name;
        dst.Invalidate();
    }

    static ZUIBoxDef CopyBoxDef(ZUIBoxDef src) => DeepCopy(src);

    static void PasteBoxDef(ZUIBoxDef dst, ZUIBoxDef src)
    {
        string name = dst.name;
        DeepPaste(dst, src);
        dst.name = name;
        dst.Invalidate();
    }

    // ── GUI style helpers ─────────────────────────────────────────────────────

    void EnsureStyles()
    {
        if (_listItemStyle != null) return;
        _listItemStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(10, 6, 3, 3), fontSize = 12 };
        _listItemActiveStyle = new GUIStyle(_listItemStyle);
        _listItemActiveStyle.normal.background        = MakeSolidTex(new Color(.25f, .42f, .62f, 1f));
        _listItemActiveStyle.normal.scaledBackgrounds = new[] { _listItemActiveStyle.normal.background };
        _listItemActiveStyle.normal.textColor         = Color.white;
        _tabTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
    }

    // ── Cached icon GUIContent for list buttons ────────────────────────────
    static GUIContent _iconMoveUp, _iconMoveDown, _iconFlash, _iconDuplicate;
    static GUIContent _iconCopy, _iconPaste, _iconDelete, _iconAdd;

    static GUIContent IconMoveUp   => _iconMoveUp   ??= MakeIconContent("move-up",   "▲", "Move up");
    static GUIContent IconMoveDown => _iconMoveDown ??= MakeIconContent("move-down", "▼", "Move down");
    static GUIContent IconFlash    => _iconFlash    ??= MakeIconContent("flash",     "✦", "Flash in preview");
    static GUIContent IconDuplicate=> _iconDuplicate??= MakeIconContent("duplicate", "⧉", "Duplicate");
    static GUIContent IconCopy     => _iconCopy     ??= MakeIconContent("copy",      "C", "Copy style");
    static GUIContent IconPaste    => _iconPaste    ??= MakeIconContent("paste",     "P", "Paste style");
    static GUIContent IconDelete   => _iconDelete   ??= MakeIconContent("delete",    "×", "Delete");
    static GUIContent IconAdd      => _iconAdd      ??= MakeIconContent("add",       "+", "Add");

    // Corner toggle icons
    static GUIContent _iconCornerTL, _iconCornerTR, _iconCornerBL, _iconCornerBR;
    static GUIContent IconCornerTL => _iconCornerTL ??= MakeIconContent("corner-tl", "TL", "Top Left");
    static GUIContent IconCornerTR => _iconCornerTR ??= MakeIconContent("corner-tr", "TR", "Top Right");
    static GUIContent IconCornerBL => _iconCornerBL ??= MakeIconContent("corner-bl", "BL", "Bottom Left");
    static GUIContent IconCornerBR => _iconCornerBR ??= MakeIconContent("corner-br", "BR", "Bottom Right");

    static GUIContent MakeIconContent(string alias, string fallbackText, string tooltip)
    {
        var tex = ZUI.FindIcon(alias);
        // Store text-only GUIContent — icon is drawn manually via IconButton for proper sizing
        if (tex != null) return new GUIContent("", tooltip) { image = tex };
        return new GUIContent(fallbackText, tooltip);
    }

    // ── Chrome button helpers ──────────────────────────────────────────────
    // Chrome buttons (copy, paste, revert, etc.) in section headers must always use the
    // EditorSheet, never the sheet being edited. These helpers temporarily scope to EditorSheet.

    bool ChromeButton(Rect rect, GUIContent content, string style = "IconButton")
    {
        using (ZUI.UseSheet(ZUI.EditorSheet))
            return ZUI.Button(rect, content, style);
    }

    bool ChromeButton(Rect rect, string label, string style = "IconButton")
    {
        using (ZUI.UseSheet(ZUI.EditorSheet))
            return ZUI.Button(rect, label, style);
    }

    bool ChromeButton(GUIContent content, string style = "IconButton", params GUILayoutOption[] options)
    {
        using (ZUI.UseSheet(ZUI.EditorSheet))
            return ZUI.Button(content, style, options);
    }

    /// <summary>ZUI-based toolbar — row of ZUI.Toggle buttons, returns selected index.</summary>
    static int ZUIToolbar(int selected, string[] labels, string style = "TabButton", params GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (ZUI.Toggle(i == selected, labels[i], style, options))
                selected = i;
        }
        GUILayout.EndHorizontal();
        return selected;
    }

    /// <summary>
    /// Draws a miniButton. If the GUIContent has a texture, draws it manually at a good size
    /// instead of relying on IMGUI's default icon-in-button scaling (which renders 256px icons tiny).
    /// </summary>
    static bool IconButton(GUIContent content, float w, float h)
    {
        bool clicked = GUILayout.Button(content.image != null ? new GUIContent("", content.tooltip) : content,
                                         EditorStyles.miniButton, GUILayout.Width(w), GUILayout.Height(h));
        if (content.image != null && Event.current.type == EventType.Repaint)
        {
            var r = GUILayoutUtility.GetLastRect();
            float iconSize = Mathf.Min(r.width - 4f, r.height - 4f);
            var iconRect = new Rect(r.x + (r.width - iconSize) * 0.5f,
                                    r.y + (r.height - iconSize) * 0.5f,
                                    iconSize, iconSize);
            GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit, true);
        }
        return clicked;
    }

    /// <summary>Icon toggle — renders as a miniButton with an icon, toggled on/off with alpha tint.</summary>
    static bool IconToggle(bool value, GUIContent content, float w, float h)
    {
        var style = value ? EditorStyles.miniButton : EditorStyles.miniButton;
        bool clicked;
        using (ZUI.UseSheet(ZUI.EditorSheet))
            clicked = ZUI.Button(GUIContent.none, "TabButton", GUILayout.Width(w), GUILayout.Height(h));
        if (content.image != null && Event.current.type == EventType.Repaint)
        {
            var r = GUILayoutUtility.GetLastRect();
            float iconSize = Mathf.Min(r.width - 4f, r.height - 4f);
            var iconRect = new Rect(r.x + (r.width - iconSize) * 0.5f,
                                    r.y + (r.height - iconSize) * 0.5f,
                                    iconSize, iconSize);
            var tint = value ? Color.white : new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit, true, 0f, tint, Vector4.zero, Vector4.zero);
        }
        else if (content.image == null && Event.current.type == EventType.Repaint)
        {
            // Fallback text
            var r = GUILayoutUtility.GetLastRect();
            var s = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            s.normal.textColor = value ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            GUI.Label(r, content.text, s);
        }
        return clicked ? !value : value;
    }

    /// <summary>Rect-based overload for absolute-positioned icon buttons.</summary>
    static bool IconButton(Rect rect, GUIContent content)
    {
        bool clicked = GUI.Button(rect, content.image != null ? new GUIContent("", content.tooltip) : content, EditorStyles.miniButton);
        if (content.image != null && Event.current.type == EventType.Repaint)
        {
            float iconSize = Mathf.Min(rect.width - 4f, rect.height - 4f);
            var iconRect = new Rect(rect.x + (rect.width - iconSize) * 0.5f,
                                    rect.y + (rect.height - iconSize) * 0.5f,
                                    iconSize, iconSize);
            GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit, true);
        }
        return clicked;
    }

    /// <summary>Draws a mini label with a horizontal line extending to the right, centered vertically.</summary>
    static void SubsectionTitle(string title)
    {
        var rect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            var labelSize = EditorStyles.miniLabel.CalcSize(new GUIContent(title));
            var labelRect = new Rect(rect.x, rect.y, labelSize.x + 2f, rect.height);
            EditorGUI.LabelField(labelRect, title, EditorStyles.miniLabel);

            float lineX = labelRect.xMax + 4f;
            float lineY = rect.y + rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(lineX, lineY, rect.xMax - lineX, 1f), new Color(1f, 1f, 1f, 0.1f));
        }
    }

    void TabTitle(string title)
    {
        EndPreviousSectionArea();
        EditorGUILayout.LabelField(title, _tabTitleStyle);
        ZUI.VerticalSpace("V Control Gap");
    }

    /// <summary>Finds the "Section Header" box def from the ZUI editor sheet (never the consumer sheet).</summary>
    static ZUIBoxDef FindSectionHeaderDef()
        => ZUI.EditorSheet?.FindBoxOrNull("Section Header");

    /// <summary>Draws the background for a section header using the "Section Header" box style if available.</summary>
    static void DrawSectionHeaderBg(Rect rect)
    {
        var def = FindSectionHeaderDef();
        if (def != null)
            def.DrawBackground(rect);
        else
            EditorGUI.DrawRect(rect, new Color(.14f, .14f, .16f, 1f));
    }

    /// <summary>Cached GUIStyle for the section header rect that applies the Section Header box margin.</summary>
    static GUIStyle _sectionHeaderRectStyle;
    static GUIStyle SectionHeaderRectStyle
    {
        get
        {
            var def = FindSectionHeaderDef();
            if (def != null)
            {
                _sectionHeaderRectStyle ??= new GUIStyle();
                var ls = def.GetLayoutStyle();
                _sectionHeaderRectStyle.margin = ls.margin;
                return _sectionHeaderRectStyle;
            }
            return GUIStyle.none;
        }
    }

    /// <summary>Returns a GUIStyle for section header title text, using the Section Header box style if available.</summary>
    static GUIStyle _sectionHeaderLabelStyle;
    static GUIStyle SectionHeaderLabelStyle
    {
        get
        {
            var def = FindSectionHeaderDef();
            if (def != null)
            {
                _sectionHeaderLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
                // Re-apply every frame so live edits are reflected immediately
                _sectionHeaderLabelStyle.fontSize = EditorStyles.miniLabel.fontSize;
                _sectionHeaderLabelStyle.font = EditorStyles.miniLabel.font;
                def.GetResolvedTitleText().Apply(_sectionHeaderLabelStyle, def.ownerSheet);
                return _sectionHeaderLabelStyle;
            }
            return EditorStyles.miniLabel;
        }
    }

    /// <summary>Returns a GUIStyle for the small toggle-row labels in a section header, using the Section Header box's contentText.</summary>
    static GUIStyle _sectionHeaderContentLabelStyle;
    static GUIStyle SectionHeaderContentLabelStyle
    {
        get
        {
            var def = FindSectionHeaderDef();
            if (def != null)
            {
                _sectionHeaderContentLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
                _sectionHeaderContentLabelStyle.fontSize = EditorStyles.miniLabel.fontSize;
                _sectionHeaderContentLabelStyle.font = EditorStyles.miniLabel.font;
                _sectionHeaderContentLabelStyle.alignment = TextAnchor.MiddleRight;
                def.GetResolvedContentText().Apply(_sectionHeaderContentLabelStyle, def.ownerSheet);
                return _sectionHeaderContentLabelStyle;
            }
            _sectionHeaderContentLabelStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            return _sectionHeaderContentLabelStyle;
        }
    }



    // Section area tracking — always Begin/End unconditionally so IMGUI layout stays matched.
    [NonSerialized] bool _sectionAreaOpen;

    void EndPreviousSectionArea()
    {
        if (!_sectionAreaOpen) return;
        EditorGUILayout.EndVertical();
        _sectionAreaOpen = false;
    }

    void BeginSectionAreaBlock()
    {
        var areaDef = ZUI.EditorSheet?.FindBoxOrNull("Section Area");
        if (areaDef != null)
        {
            var rect = EditorGUILayout.BeginVertical(areaDef.GetLayoutStyle());
            areaDef.DrawBackground(rect);
        }
        else
        {
            EditorGUILayout.BeginVertical();
        }
        _sectionAreaOpen = true;
    }

    // ── Unified section header ────────────────────────────────────────────────
    // One method for every section header. Configure via SectionHeaderConfig.
    //
    // Layout: [▸/▾] Title  [left toggles]  ...  [right toggles]  [Copy][Paste]
    // Revert button (↩) appears before right toggles when showRevert && onRevert != null,
    // and right-click on the header opens a "Revert to Parent" menu.

    struct HeaderToggle
    {
        public string label;               // optional small label drawn before the toggle
        public System.Func<bool> get;
        public System.Action<bool> set;
        public bool autoExpandOnEnable;    // expand section when toggle turns on
    }

    struct SectionHeaderConfig
    {
        public string title;
        public string key;
        public HeaderToggle[] leftToggles;
        public HeaderToggle[] rightToggles;
        public Action onCopy;
        public Action onPaste;
        public bool canPaste;
        public Action onRevert;
        public bool showRevert;
    }

    bool SectionHeader(in SectionHeaderConfig cfg)
    {
        EndPreviousSectionArea();
        string k = cfg.key ?? $"{_activeTab}_{cfg.title}";
        bool expanded = GetFoldout(k);

        var rect = GUILayoutUtility.GetRect(1f, 28f, SectionHeaderRectStyle, GUILayout.ExpandWidth(true));
        DrawSectionHeaderBg(rect);
        float cy14 = rect.y + (rect.height - 14f) * 0.5f;
        float cy12 = rect.y + (rect.height - 12f) * 0.5f;
        float cy24 = rect.y + (rect.height - 24f) * 0.5f;

        const float margin = 4f, btnW = 28f, pad = 2f, itemGap = 4f;
        const float tglH = 18f; // SmallToggle button height in the header
        const float tglMinW = 28f;
        const float tglPadX = 10f; // extra width beyond label text
        bool hasCP = cfg.onCopy != null && cfg.onPaste != null;
        bool hasRevert = cfg.onRevert != null && cfg.showRevert;

        var smallToggleDef = ZUI.EditorSheet?.FindButton("SmallToggle");
        float cyTgl = rect.y + (rect.height - tglH) * 0.5f;

        float MeasureToggleW(HeaderToggle t)
        {
            if (string.IsNullOrEmpty(t.label)) return tglMinW;
            var ls = smallToggleDef != null ? smallToggleDef.GetLabelStyle() : EditorStyles.miniLabel;
            float w = ls.CalcSize(new GUIContent(t.label)).x + tglPadX;
            return Mathf.Max(tglMinW, w);
        }

        // Right-side layout (right-to-left): buttons, right toggles, revert
        float rightEdge = rect.xMax - margin;
        float bPx = hasCP ? rightEdge - btnW : rightEdge;
        float bCx = hasCP ? bPx - pad - btnW : bPx;
        float rightZoneStart = hasCP ? bCx : rightEdge;

        float rightX = rightZoneStart;
        var rightToggleRects = cfg.rightToggles != null ? new Rect[cfg.rightToggles.Length] : null;
        if (cfg.rightToggles != null)
        {
            for (int i = cfg.rightToggles.Length - 1; i >= 0; i--)
            {
                float w = MeasureToggleW(cfg.rightToggles[i]);
                rightX -= itemGap + w;
                rightToggleRects[i] = new Rect(rightX, cyTgl, w, tglH);
            }
        }

        float revX = 0f;
        const float revW = 18f;
        if (hasRevert)
        {
            rightX -= itemGap + revW;
            revX = rightX;
        }

        // Left side: arrow, title, left toggles
        float arrowX = rect.x + 4f;
        float titleX = rect.x + 18f;
        bool hasLeft = cfg.leftToggles != null && cfg.leftToggles.Length > 0;

        float titleSlot = hasLeft
            ? SectionHeaderLabelStyle.CalcSize(new GUIContent(cfg.title)).x + 4f
            : 0f;

        float titleW;
        float leftX = titleX;
        if (hasLeft)
        {
            leftX = titleX + titleSlot + itemGap;
            titleW = titleSlot;
        }
        else
        {
            float titleEnd = (cfg.rightToggles != null || hasRevert || hasCP) ? (rightX - itemGap) : rect.xMax - margin;
            titleW = Mathf.Max(1f, titleEnd - titleX);
        }

        var leftToggleRects = hasLeft ? new Rect[cfg.leftToggles.Length] : null;
        if (hasLeft)
        {
            for (int i = 0; i < cfg.leftToggles.Length; i++)
            {
                float w = MeasureToggleW(cfg.leftToggles[i]);
                leftToggleRects[i] = new Rect(leftX, cyTgl, w, tglH);
                leftX += w + itemGap;
            }
        }

        // ── Draw ──
        EditorGUI.LabelField(new Rect(arrowX, cy14, 14f, 14f), expanded ? "▾" : "▸", SectionHeaderLabelStyle);
        EditorGUI.LabelField(new Rect(titleX, cy14, titleW, 14f), cfg.title, SectionHeaderLabelStyle);

        void DrawHeaderToggle(Rect r, HeaderToggle t)
        {
            bool cur = t.get();
            var label = t.label ?? string.Empty;
            bool nxt = smallToggleDef != null
                ? ZUI.Toggle(r, cur, new GUIContent(label), smallToggleDef)
                : GUI.Toggle(r, cur, label);
            if (nxt != cur)
            {
                t.set(nxt);
                if (t.autoExpandOnEnable && nxt && !expanded) { expanded = true; SetFoldout(k, true); }
            }
        }

        if (hasLeft)
            for (int i = 0; i < cfg.leftToggles.Length; i++)
                DrawHeaderToggle(leftToggleRects[i], cfg.leftToggles[i]);

        if (cfg.rightToggles != null)
            for (int i = 0; i < cfg.rightToggles.Length; i++)
                DrawHeaderToggle(rightToggleRects[i], cfg.rightToggles[i]);

        if (hasRevert)
        {
            if (ChromeButton(new Rect(revX, cy12, revW, 12f), "↩"))
            {
                cfg.onRevert();
                GUIUtility.ExitGUI();
            }
        }

        if (hasCP)
        {
            if (ChromeButton(new Rect(bCx, cy24, btnW, 24f), IconCopy)) cfg.onCopy();
            GUI.enabled = cfg.canPaste;
            if (ChromeButton(new Rect(bPx, cy24, btnW, 24f), IconPaste)) cfg.onPaste();
            GUI.enabled = true;
        }

        // Right-click revert menu
        if (hasRevert && Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
        {
            var revertAction = cfg.onRevert;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Revert to Parent"), false, () => revertAction());
            menu.ShowAsContext();
            Event.current.Use();
        }

        // Expand/collapse: exclude interactive zones
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            bool inLeftToggles = false;
            if (hasLeft)
            {
                for (int i = 0; i < leftToggleRects.Length; i++)
                    if (leftToggleRects[i].Contains(Event.current.mousePosition)) { inLeftToggles = true; break; }
            }
            bool inRightZone = Event.current.mousePosition.x >= rightX;
            if (!inLeftToggles && !inRightZone)
            {
                expanded = !expanded;
                SetFoldout(k, expanded);
                Event.current.Use();
                Repaint();
            }
        }

        if (expanded) BeginSectionAreaBlock();
        return expanded;
    }

    bool SectionHeaderPlain(string title, string key = null)
        => SectionHeader(new SectionHeaderConfig { title = title, key = key });

    bool SectionHeaderWithToggleCopyPaste(string title, string key, ref bool toggle, out bool toggleChanged,
        Action onCopy, Action onPaste, bool canPaste)
    {
        bool t0 = toggle;
        bool changed = false;
        bool result = SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            leftToggles = new[] {
                new HeaderToggle { get = () => t0, set = v => { t0 = v; changed = true; }, autoExpandOnEnable = true },
            },
            onCopy = onCopy, onPaste = onPaste, canPaste = canPaste,
        });
        toggle = t0;
        toggleChanged = changed;
        return result;
    }

    bool SectionHeaderWithToggles(string title, string key,
        string label1, ref bool toggle1, string label2, ref bool toggle2,
        out bool togglesChanged)
    {
        bool t1 = toggle1, t2 = toggle2;
        bool changed = false;
        bool result = SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            leftToggles = new[] {
                new HeaderToggle { label = label1, get = () => t1, set = v => { t1 = v; changed = true; } },
                new HeaderToggle { label = label2, get = () => t2, set = v => { t2 = v; changed = true; } },
            },
        });
        toggle1 = t1;
        toggle2 = t2;
        togglesChanged = changed;
        return result;
    }

    bool SectionHeaderWithCopyPaste(string title, Action onCopy, Action onPaste, bool canPaste, string key = null)
        => SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            onCopy = onCopy, onPaste = onPaste, canPaste = canPaste,
        });

    bool SectionHeaderWithOverrideCopyPaste(string title, bool currentOverride, out bool newOverride,
        Action onCopy, Action onPaste, bool canPaste, Action onRevert = null, string key = null)
    {
        bool ov = currentOverride;
        bool result = SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            rightToggles = new[] {
                new HeaderToggle { label = "Override", get = () => ov, set = v => ov = v },
            },
            onCopy = onCopy, onPaste = onPaste, canPaste = canPaste,
            onRevert = onRevert,
            showRevert = currentOverride,
        });
        newOverride = ov;
        return result;
    }

    bool SectionHeaderWithCopyPasteAndGlobal(string title, Action onCopy, Action onPaste, bool canPaste,
                                                   bool currentGlobal, out bool newGlobal, string key = null)
    {
        bool gl = currentGlobal;
        bool result = SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            rightToggles = new[] {
                new HeaderToggle { label = "Global", get = () => gl, set = v => gl = v },
            },
            onCopy = onCopy, onPaste = onPaste, canPaste = canPaste,
        });
        newGlobal = gl;
        return result;
    }

    // Header with Copy/Paste + Global toggle + Shadow/Outline toggles for text sections.
    // Shadow/Outline toggles appear after the title; when enabled, detail fields show in the section area.
    bool SectionHeaderText(string title, Action onCopy, Action onPaste, bool canPaste,
                                                       bool currentGlobal, out bool newGlobal,
                                                       ZUITextDef textDef, out bool textTogglesChanged,
                                                       string key = null)
    {
        bool gl = currentGlobal;
        bool togChanged = false;
        bool result = SectionHeader(new SectionHeaderConfig {
            title = title, key = key,
            leftToggles = new[] {
                new HeaderToggle { label = "Shadow",  get = () => textDef.shadow.enabled, set = v => { textDef.shadow.enabled = v; togChanged = true; } },
                new HeaderToggle { label = "Outline", get = () => textDef.outlineEnabled, set = v => { textDef.outlineEnabled = v; togChanged = true; } },
            },
            rightToggles = new[] {
                new HeaderToggle { label = "Global", get = () => gl, set = v => gl = v },
            },
            onCopy = onCopy, onPaste = onPaste, canPaste = canPaste,
        });
        newGlobal = gl;
        textTogglesChanged = togChanged;
        return result;
    }

    static void DrawDivider()
    {
        var rect = GUILayoutUtility.GetRect(1f, float.MaxValue, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, .4f));
    }

    static void CenteredLabel(string text)
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(text, EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    static Texture2D MakeSolidTex(Color c)
    {
        var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
    }

    static string C(Color c) => $"new Color({c.r:F2}f, {c.g:F2}f, {c.b:F2}f, {c.a:F2}f)";

    static string G(ZUIColor g)
    {
        if (!g.isGradient) return $"new ZUIColor({C(g.colorA.color)})";
        if (g.isRadial)
            return $"new ZUIColor({C(g.colorA.color)}, {C(g.colorB.color)}, 90f) {{ isRadial = true }}";
        return $"new ZUIColor({C(g.colorA.color)}, {C(g.colorB.color)}, {g.angle:F1}f)";
    }

    static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(); bool cap = true;
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(cap ? char.ToUpper(ch) : ch); cap = false; }
            else { cap = true; }
        }
        if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.Length > 0 ? sb.ToString() : "Style";
    }

    static string GetStyleName<T>(T item) where T : class
    {
        if (item is ZUIButtonDef    b) return b.name;
        if (item is ZUIBoxDef       x) return x.name;
        if (item is ZUITextStyleDef t) return t.name;
        if (item is ZUISliderDef    s) return s.name;
        return null;
    }

    // ── ZUIColorPicker ────────────────────────────────────────────────────────
    // Two-control color picker: [swatch or ColorField] [⊞ button]
    // ⊞ opens a popover to toggle between Direct color and Palette color modes.
    //
    // ZUIColorPicker(label, ...)  — draws a full labeled row (BeginHorizontal inside)
    // ZUIColorPickerInline(...)   — draws just the two controls, for embedding in an existing horizontal
    //
    // Because lambdas cannot capture ref parameters, the popover communicates back via a
    // pending-write slot (_cpPendingKey / _cpPendingResult). Each call site passes a unique key
    // string; on the next OnGUI pass the caller reads the pending result and applies it.

    string                                   _cpPendingKey;
    (Color color, string paletteRef, ZUIPaletteSlot slot, string autoColorRef)? _cpPendingResult;
    readonly Dictionary<string, Rect>        _cpBtnRects = new Dictionary<string, Rect>();

    bool ZUIColorPicker(string label, ref Color color, ref string paletteRef, ref ZUIPaletteSlot slot,
        float labelWidth = 82f)
    {
        string autoColorRef = "";
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));
        bool changed = ZUIColorPickerInline(ref color, ref paletteRef, ref slot, ref autoColorRef);
        EditorGUILayout.EndHorizontal();
        return changed;
    }

    // key  — a stable per-layout-position ID so the pending write goes to the right field.
    bool ZUIColorPickerInline(string key, ref Color color, ref string paletteRef, ref ZUIPaletteSlot slot, ref string autoColorRef)
    {
        bool changed = false;

        // Apply any pending write from the popover
        if (_cpPendingKey == key && _cpPendingResult.HasValue)
        {
            color        = _cpPendingResult.Value.color;
            paletteRef   = _cpPendingResult.Value.paletteRef;
            slot         = _cpPendingResult.Value.slot;
            autoColorRef = _cpPendingResult.Value.autoColorRef;
            _cpPendingKey    = null;
            _cpPendingResult = null;
            changed          = true;
        }

        bool hasRef    = !string.IsNullOrEmpty(paletteRef);
        var  pal       = hasRef ? _sheet?.FindPaletteColor(paletteRef) : null;

        // Resolve: autocolor takes priority over slot
        Color resolved;
        string acRef = autoColorRef;  // local copy for lambda access
        if (pal != null && !string.IsNullOrEmpty(acRef) && pal.autoColors != null)
        {
            var ac = pal.autoColors.Find(a => a.name == acRef);
            resolved = ac != null ? ac.Resolve(pal.color) : pal.color;
        }
        else
            resolved = pal != null ? pal.color : color;

        // Left control: read-only swatch (palette mode) or editable ColorField (direct mode)
        if (hasRef)
        {
            var swatchRect = GUILayoutUtility.GetRect(90f, EditorGUIUtility.singleLineHeight, GUILayout.Width(90f));
            EditorGUI.DrawRect(swatchRect, resolved);
            string refLabel = !string.IsNullOrEmpty(acRef) ? $"{paletteRef} · {acRef}" : paletteRef;
            var labelStyle  = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
            EditorGUI.LabelField(swatchRect, $" {refLabel}", labelStyle);
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            color = EditorGUILayout.ColorField(GUIContent.none, color, true, true, false, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck()) changed = true;
        }

        // Right control: ⊞ button opens popover anchored directly to the button rect.
        if (Event.current.type == EventType.Repaint)
        {
            this.Button("⊞", "TabButton", GUILayout.Width(20f));
            _cpBtnRects[key] = GUILayoutUtility.GetLastRect();
        }
        else if (this.Button("⊞", "TabButton", GUILayout.Width(20f)))
        {
            _cpBtnRects.TryGetValue(key, out var btnRect);
            var popup = new ZUIColorPickerPopup(color, paletteRef, slot, autoColorRef, _sheet?.palette,
                (newColor, newRef, newSlot, newAutoRef) =>
                {
                    _cpPendingKey    = key;
                    _cpPendingResult = (newColor, newRef, newSlot, newAutoRef);
                    Repaint();
                });
            PopupWindow.Show(btnRect, popup);
        }

        return changed;
    }

    // Overload without explicit key — derives key from GUIUtility.GetControlID for uniqueness
    bool ZUIColorPickerInline(ref Color color, ref string paletteRef, ref ZUIPaletteSlot slot, ref string autoColorRef)
    {
        int id  = GUIUtility.GetControlID(FocusType.Passive);
        return ZUIColorPickerInline(id.ToString(), ref color, ref paletteRef, ref slot, ref autoColorRef);
    }

    // Overload that takes a ZUIColorRef struct directly
    bool ZUIColorPickerInline(ref ZUIColorRef cr)
    {
        return ZUIColorPickerInline(ref cr.color, ref cr.paletteRef, ref cr.slot, ref cr.autoColorRef);
    }

    // ── Color picker popover content ──────────────────────────────────────────

    // ── Gradient stop editor popover ─────────────────────────────────────────

    class ZUIGradientStopPopup : PopupWindowContent
    {
        ZUIColor _gradient;
        List<ZUIPaletteColor> _palette;
        ZUIStyleSheetAsset _sheet;
        Action _onChanged;
        int _selectedStop = -1;
        int _dragBiasIndex = -1;   // which segment's easing is being dragged (-1 = none)
        bool _draggingStop = false;
        float _measuredHeight;
        float _popW = 340f;        // matches anchor bar width; set via SetWidth before Show

        const float k_PreviewH = 60f;   // 2D gradient preview box
        const float k_BarH     = 24f;
        const float k_BiasBarH = 10f;
        const float k_ThumbW   = 10f;
        const float k_Pad      = 8f;
        const float k_StopRowH = 20f;

        public ZUIGradientStopPopup(ZUIColor gradient, List<ZUIPaletteColor> palette, ZUIStyleSheetAsset sheet, Action onChanged)
        {
            _gradient  = gradient;
            _palette   = palette;
            _sheet     = sheet;
            _onChanged = onChanged;
        }

        public void SetWidth(float width) => _popW = Mathf.Max(200f, width);

        public override Vector2 GetWindowSize()
        {
            // Use measured height from previous frame if available
            if (_measuredHeight > 0f)
                return new Vector2(_popW, _measuredHeight + 4f);

            // Initial estimate — deliberately oversize so content renders on first frame,
            // then _measuredHeight corrects it on the second frame.
            var stops = _gradient.GetEffectiveStops();
            float h = k_Pad * 2f;
            if (_gradient.isRadial) h += k_PreviewH + 40f * 4 + 12f;
            h += k_BarH + k_BiasBarH + 8f;
            h += stops.Count * 40f + 8f;
            h += 40f;
            return new Vector2(_popW, Mathf.Max(h, 200f));
        }

        public override void OnGUI(Rect rect)
        {
            using var _sheetScope = ZUI.UseSheet(ZUI.EditorSheet);
            // Initialize stops from colorA/colorB if not yet created
            if (_gradient.stops == null || _gradient.stops.Count < 2)
            {
                _gradient.stops = new System.Collections.Generic.List<ZUIGradientStop>
                {
                    new ZUIGradientStop(_gradient.colorA, 0f, 0.5f),
                    new ZUIGradientStop(_gradient.colorB, 1f, 0.5f),
                };
            }
            var stops = _gradient.stops;
            bool changed = false;

            GUILayout.Space(k_Pad * 0.5f);

            // ── 2D preview box (shown for radial/2D gradients) ───────────
            if (_gradient.isRadial)
            {
                var previewRect = GUILayoutUtility.GetRect(_popW - k_Pad * 2f, k_PreviewH);
                previewRect.x += k_Pad; previewRect.width -= k_Pad * 2f;
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(previewRect, new Color(0.1f, 0.1f, 0.12f, 1f));
                    var actualTex = _gradient.GetOrBuildTexture(_sheet);
                    GUI.DrawTexture(previewRect, actualTex, ScaleMode.StretchToFill, true);
                    // Border
                    EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, previewRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
                    EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.yMax - 1f, previewRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
                    EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, 1f, previewRect.height), new Color(0f, 0f, 0f, 0.4f));
                    EditorGUI.DrawRect(new Rect(previewRect.xMax - 1f, previewRect.y, 1f, previewRect.height), new Color(0f, 0f, 0f, 0.4f));
                }
                ZUI.VerticalSpace("V Control Gap");
            }

            // ── 2D center + scale controls ───────────────────────────────
            if (_gradient.isRadial)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(k_Pad);
                float prevLW = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 50f;
                _gradient.radialCenterX = ZUI.Slider(_gradient.radialCenterX, 0f, 1f, "Center X", "SmallSlider");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Space(k_Pad);
                _gradient.radialCenterY = ZUI.Slider(_gradient.radialCenterY, 0f, 1f, "Center Y", "SmallSlider");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Space(k_Pad);
                _gradient.scaleX = Mathf.Max(0.01f, ZUI.Slider(_gradient.scaleX, 0.1f, 3f, "Scale X", "SmallSlider"));
                _gradient.radialCircular = ZUI.Toggle(_gradient.radialCircular, new GUIContent("=", "Lock X=Y"), "Toggle", GUILayout.Width(18f));
                GUILayout.EndHorizontal();
                if (!_gradient.radialCircular)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(k_Pad);
                    _gradient.scaleY = Mathf.Max(0.01f, ZUI.Slider(_gradient.scaleY, 0.1f, 3f, "Scale Y", "SmallSlider"));
                    GUILayout.EndHorizontal();
                }
                else
                    _gradient.scaleY = _gradient.scaleX;
                EditorGUIUtility.labelWidth = prevLW;
                if (GUI.changed) { _gradient.Invalidate(); changed = true; }
                ZUI.VerticalSpace("V Control Gap");
            }

            // ── Linear stop bar (always linear, for editing stops) ───────
            var barRect = GUILayoutUtility.GetRect(_popW - k_Pad * 2f, k_BarH);
            barRect.x += k_Pad; barRect.width -= k_Pad * 2f;
            var biasRect = GUILayoutUtility.GetRect(_popW - k_Pad * 2f, k_BiasBarH);
            biasRect.x += k_Pad; biasRect.width -= k_Pad * 2f;

            if (Event.current.type == EventType.Repaint)
            {
                // Gradient preview — always render as a horizontal linear strip
                // synced with the stop handles. Angle and other modes are forced off
                // for the sample so this bar shows only the stop-position gradient.
                EditorGUI.DrawRect(barRect, new Color(0.12f, 0.12f, 0.14f, 1f));
                bool  wasRadial = _gradient.isRadial;
                bool  wasPxLen  = _gradient.usePixelLength;
                float wasAngle  = _gradient.angle;
                _gradient.isRadial       = false;
                _gradient.usePixelLength = false;
                _gradient.angle          = 0f;
                _gradient.Invalidate();
                var tex = _gradient.GetOrBuildTexture(_sheet);
                _gradient.isRadial       = wasRadial;
                _gradient.usePixelLength = wasPxLen;
                _gradient.angle          = wasAngle;
                _gradient.Invalidate();
                GUI.DrawTexture(barRect, tex, ScaleMode.StretchToFill, true);
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width, 1f), new Color(0f, 0f, 0f, 0.5f));
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.yMax - 1f, barRect.width, 1f), new Color(0f, 0f, 0f, 0.5f));

                // Color stop thumbs (above bar)
                for (int i = 0; i < stops.Count; i++)
                {
                    float x = barRect.x + stops[i].position * barRect.width;
                    bool selected = (i == _selectedStop && !_draggingStop || i == _selectedStop && _draggingStop);
                    var thumbR = new Rect(x - k_ThumbW * 0.5f, barRect.y - 3f, k_ThumbW, barRect.height + 3f);
                    EditorGUI.DrawRect(thumbR, selected ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.9f));
                    EditorGUI.DrawRect(new Rect(thumbR.x + 2f, thumbR.y + 2f, thumbR.width - 4f, thumbR.height - 4f), stops[i].color.Resolve(_sheet));
                }

                // Bias thumbs (below bar) — diamond-shaped markers between stops
                EditorGUI.DrawRect(biasRect, new Color(0.1f, 0.1f, 0.12f, 1f));
                for (int i = 0; i < stops.Count - 1; i++)
                {
                    float segStart = stops[i].position;
                    float segEnd   = stops[i + 1].position;
                    float biasPos  = Mathf.Lerp(segStart, segEnd, stops[i + 1].easing);
                    float bx       = biasRect.x + biasPos * biasRect.width;
                    bool  bSel     = (_dragBiasIndex == i);
                    Color bc       = bSel ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.8f);
                    EditorGUI.DrawRect(new Rect(bx - 3f, biasRect.y + 1f, 6f, biasRect.height - 2f), bc);
                    // Segment range indicator
                    float sx = biasRect.x + segStart * biasRect.width;
                    float ex = biasRect.x + segEnd * biasRect.width;
                    EditorGUI.DrawRect(new Rect(sx, biasRect.y + biasRect.height - 1f, ex - sx, 1f), new Color(1f, 1f, 1f, 0.15f));
                }
            }

            // ── Mouse interaction ────────────────────────────────────────
            var fullInteractRect = new Rect(barRect.x, barRect.y, barRect.width, barRect.height + biasRect.height);

            if (Event.current.type == EventType.MouseDown && fullInteractRect.Contains(Event.current.mousePosition))
            {
                float clickX = Event.current.mousePosition.x;
                float clickPos = (clickX - barRect.x) / barRect.width;
                bool inBiasRow = Event.current.mousePosition.y > barRect.yMax;

                if (inBiasRow)
                {
                    // Find nearest bias thumb
                    float bestDist = float.MaxValue;
                    _dragBiasIndex = -1;
                    for (int i = 0; i < stops.Count - 1; i++)
                    {
                        float biasPos = Mathf.Lerp(stops[i].position, stops[i + 1].position, stops[i + 1].easing);
                        float d = Mathf.Abs(biasPos - clickPos);
                        if (d < bestDist) { bestDist = d; _dragBiasIndex = i; }
                    }
                    _draggingStop = false;
                }
                else
                {
                    // Find nearest color stop
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < stops.Count; i++)
                    {
                        float d = Mathf.Abs(stops[i].position - clickPos);
                        if (d < bestDist) { bestDist = d; _selectedStop = i; }
                    }
                    _draggingStop = true;
                    _dragBiasIndex = -1;
                }
                Event.current.Use();
                editorWindow?.Repaint();
            }

            if (Event.current.type == EventType.MouseDrag)
            {
                float dragPos = (Event.current.mousePosition.x - barRect.x) / barRect.width;
                dragPos = Mathf.Clamp01(dragPos);

                if (_draggingStop && _selectedStop > 0 && _selectedStop < stops.Count - 1)
                {
                    // Drag color stop
                    float minP = stops[_selectedStop - 1].position + 0.01f;
                    float maxP = stops[_selectedStop + 1].position - 0.01f;
                    stops[_selectedStop].position = Mathf.Clamp(dragPos, minP, maxP);
                    _gradient.Invalidate(); changed = true;
                    Event.current.Use();
                }
                else if (_dragBiasIndex >= 0 && _dragBiasIndex < stops.Count - 1)
                {
                    // Drag bias thumb — convert screen position to easing 0-1 within segment
                    float segStart = stops[_dragBiasIndex].position;
                    float segEnd   = stops[_dragBiasIndex + 1].position;
                    float segLen   = segEnd - segStart;
                    if (segLen > 0.001f)
                    {
                        float easing = Mathf.Clamp01((dragPos - segStart) / segLen);
                        stops[_dragBiasIndex + 1].easing = easing;
                        _gradient.Invalidate(); changed = true;
                    }
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.MouseUp)
            {
                _draggingStop = false;
                _dragBiasIndex = -1;
            }

            ZUI.VerticalSpace("V Control Gap");

            // ── Per-stop rows ────────────────────────────────────────────
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                bool selected = (i == _selectedStop);
                GUILayout.BeginHorizontal();
                GUILayout.Space(k_Pad);

                // Select button
                if (ZUI.Toggle(selected, (i + 1).ToString(), "Toggle", GUILayout.Width(20f)) && !selected)
                    _selectedStop = i;

                // Color field
                EditorGUI.BeginChangeCheck();
                stop.color.color = EditorGUILayout.ColorField(GUIContent.none, stop.color.Resolve(_sheet), true, true, false, GUILayout.Width(60f));

                // Palette swatch button — opens visual color picker popup
                if (_palette != null && _palette.Count > 0)
                {
                    // Show resolved color as the button swatch
                    Color stopResolved = stop.color.Resolve(_sheet);
                    var palBtnRect = GUILayoutUtility.GetRect(40f, k_StopRowH, GUILayout.Width(40f), GUILayout.Height(k_StopRowH));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(palBtnRect, stopResolved);
                        // Label overlay
                        string lbl = stop.color.IsAutoColorRef ? stop.color.autoColorRef
                                   : stop.color.IsPaletteRef   ? stop.color.paletteRef
                                   : "⊞";
                        float lum = stopResolved.r * 0.299f + stopResolved.g * 0.587f + stopResolved.b * 0.114f;
                        var lblStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = lum > 0.45f ? Color.black : Color.white },
                            alignment = TextAnchor.MiddleCenter, fontSize = 8, clipping = TextClipping.Clip,
                        };
                        GUI.Label(palBtnRect, lbl, lblStyle);
                        // Border
                        EditorGUI.DrawRect(new Rect(palBtnRect.x, palBtnRect.y, palBtnRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
                        EditorGUI.DrawRect(new Rect(palBtnRect.x, palBtnRect.yMax - 1f, palBtnRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
                        EditorGUI.DrawRect(new Rect(palBtnRect.x, palBtnRect.y, 1f, palBtnRect.height), new Color(0f, 0f, 0f, 0.4f));
                        EditorGUI.DrawRect(new Rect(palBtnRect.xMax - 1f, palBtnRect.y, 1f, palBtnRect.height), new Color(0f, 0f, 0f, 0.4f));
                    }
                    if (Event.current.type == EventType.MouseDown && palBtnRect.Contains(Event.current.mousePosition))
                    {
                        int capturedI = i;
                        var colorPopup = new ZUIStopColorPopup(stop.color, _palette, _sheet, (newRef) =>
                        {
                            stops[capturedI].color = newRef;
                            _gradient.Invalidate(); _onChanged?.Invoke();
                            editorWindow?.Repaint();
                        });
                        PopupWindow.Show(palBtnRect, colorPopup);
                        Event.current.Use();
                    }
                }

                // Position
                if (i == 0 || i == stops.Count - 1)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                        EditorGUILayout.FloatField(stop.position, GUILayout.Width(36f));
                }
                else
                {
                    float minP = stops[i - 1].position + 0.01f;
                    float maxP = stops[i + 1].position - 0.01f;
                    stop.position = Mathf.Clamp(EditorGUILayout.FloatField(stop.position, GUILayout.Width(36f)), minP, maxP);
                }

                // Easing (skip first stop)
                if (i > 0)
                    stop.easing = ZUI.Slider(stop.easing, 0f, 1f, "", "SmallSlider", (float?)null, GUILayout.Width(80f));

                if (EditorGUI.EndChangeCheck()) { _gradient.Invalidate(); changed = true; }

                GUILayout.EndHorizontal();
            }

            ZUI.VerticalSpace("V Control Gap");

            // ── Add / Remove buttons ─────────────────────────────────────
            // Chrome inside a nested popup class (not a ZUIWindow) — explicit EditorSheet scope.
            GUILayout.BeginHorizontal();
            GUILayout.Space(k_Pad);
            bool addClicked, removeClicked;
            using (ZUI.UseSheet(ZUI.EditorSheet))
                addClicked = ZUI.Button("+ Add Stop", "TabButton", GUILayout.Width(80f));
            if (addClicked)
            {
                int last = stops.Count - 1;
                float midPos = (stops[last - 1].position + stops[last].position) * 0.5f;
                Color midColor = Color.Lerp(stops[last - 1].color.Resolve(_sheet), stops[last].color.Resolve(_sheet), 0.5f);
                stops.Insert(last, new ZUIGradientStop(new ZUIColorRef(midColor), midPos, 0.5f));
                _gradient.Invalidate(); changed = true;
            }
            using (new EditorGUI.DisabledGroupScope(stops.Count <= 2 || _selectedStop <= 0 || _selectedStop >= stops.Count - 1))
            {
                using (ZUI.UseSheet(ZUI.EditorSheet))
                    removeClicked = ZUI.Button("− Remove", "TabButton", GUILayout.Width(80f));
                if (removeClicked && _selectedStop > 0 && _selectedStop < stops.Count - 1)
                {
                    stops.RemoveAt(_selectedStop);
                    _selectedStop = Mathf.Min(_selectedStop, stops.Count - 1);
                    _gradient.Invalidate(); changed = true;
                }
            }
            GUILayout.EndHorizontal();

            // Sync colorA/colorB
            if (stops.Count >= 2)
            {
                _gradient.colorA = stops[0].color;
                _gradient.colorB = stops[stops.Count - 1].color;
            }

            // Measure total content height for auto-sizing
            GUILayout.Space(1f); // ensure at least one layout entry at the bottom
            if (Event.current.type == EventType.Repaint)
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                _measuredHeight = lastRect.yMax;
            }

            if (changed) _onChanged?.Invoke();
        }
    }


    // ── Pattern Picker Popup ────────────────────────────────────────────────
    // Shows built-in patterns (Stripes, Dots, Grid) as preview tiles,
    // then all available icons as a thumbnail grid.

    class ZUIPatternPickerPopup : PopupWindowContent
    {
        ZUIPatternDef _pattern;
        ZUIStyleSheetAsset _sheet;
        Action _onChanged;
        Vector2 _scroll;
        List<(string name, string path)> _icons;
        Dictionary<string, Texture2D> _iconCache; // cache loaded textures
        float _measuredHeight;

        const float k_Pad      = 8f;
        const float k_TileSize = 36f;
        const float k_Gap      = 3f;
        const float k_PopW     = 260f;

        static readonly ZUIPatternType[] k_BuiltIns = { ZUIPatternType.Stripes, ZUIPatternType.Dots, ZUIPatternType.Grid };

        public ZUIPatternPickerPopup(ZUIPatternDef pattern, ZUIStyleSheetAsset sheet, Action onChanged)
        {
            _pattern   = pattern;
            _sheet     = sheet;
            _onChanged = onChanged;
            _icons     = ZUIAssetLibrary.GetAvailableIcons(sheet?.dataFolderPath);
            // Pre-load all icon textures once
            _iconCache = new Dictionary<string, Texture2D>(_icons.Count);
            foreach (var (name, path) in _icons)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) _iconCache[path] = tex;
            }
        }

        public override Vector2 GetWindowSize()
        {
            if (_measuredHeight > 0f)
                return new Vector2(k_PopW, Mathf.Min(_measuredHeight + 4f, 400f));
            // Estimate: built-ins row + label + icon grid
            int iconCols = Mathf.Max(1, (int)((k_PopW - k_Pad * 2f) / (k_TileSize + k_Gap)));
            int iconRows = Mathf.Max(1, Mathf.CeilToInt((float)_icons.Count / iconCols));
            float h = k_Pad + k_TileSize + 8f + 16f + iconRows * (k_TileSize + k_Gap) + k_Pad;
            return new Vector2(k_PopW, Mathf.Min(h, 400f));
        }

        public override void OnGUI(Rect rect)
        {
            using var _sheetScope = ZUI.UseSheet(ZUI.EditorSheet);

            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Space(k_Pad * 0.5f);

            // ── Built-in patterns ────────────────────────────────────
            EditorGUILayout.LabelField("Patterns", EditorStyles.miniLabel);
            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(k_Pad);
            foreach (var pt in k_BuiltIns)
            {
                bool selected = _pattern.patternType == pt;
                var tileRect = GUILayoutUtility.GetRect(k_TileSize, k_TileSize, GUILayout.Width(k_TileSize));

                if (Event.current.type == EventType.Repaint)
                {
                    // Draw preview: create a tiny pattern texture
                    EditorGUI.DrawRect(tileRect, new Color(0.15f, 0.15f, 0.18f, 1f));
                    var previewDef = new ZUIPatternDef
                    {
                        enabled = true, patternType = pt, opacity = 0.6f,
                        scale = 0.5f, rotation = 0f,
                        tint = new ZUIColorRef(Color.white)
                    };
                    var prevTex = previewDef.GetTextureForRect((int)k_TileSize, (int)k_TileSize, _sheet);
                    if (prevTex != null)
                        GUI.DrawTexture(tileRect, prevTex, ScaleMode.StretchToFill, true);

                    // Selection border
                    if (selected)
                    {
                        float b = 2f;
                        EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.y, tileRect.width, b), Color.white);
                        EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.yMax - b, tileRect.width, b), Color.white);
                        EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.y, b, tileRect.height), Color.white);
                        EditorGUI.DrawRect(new Rect(tileRect.xMax - b, tileRect.y, b, tileRect.height), Color.white);
                    }

                    // Label below
                    var lblRect = new Rect(tileRect.x, tileRect.yMax, tileRect.width, 12f);
                    var lblStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, fontSize = 8 };
                    GUI.Label(lblRect, pt.ToString(), lblStyle);
                }

                if (Event.current.type == EventType.MouseDown && tileRect.Contains(Event.current.mousePosition))
                {
                    _pattern.patternType = pt;
                    _pattern.iconId = "";
                    _pattern.Invalidate();
                    _onChanged?.Invoke();
                    editorWindow?.Repaint();
                    Event.current.Use();
                }
                GUILayout.Space(k_Gap);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(14f); // space for pattern labels below tiles

            // ── Icons ────────────────────────────────────────────────
            if (_icons.Count > 0)
            {
                EditorGUILayout.LabelField("Icons", EditorStyles.miniLabel);
                GUILayout.Space(2f);

                float availW = k_PopW - k_Pad * 2f;
                int cols = Mathf.Max(1, (int)(availW / (k_TileSize + k_Gap)));

                for (int i = 0; i < _icons.Count; i++)
                {
                    if (i % cols == 0)
                    {
                        if (i > 0) GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(k_Pad);
                    }

                    var (iconName, iconPath) = _icons[i];
                    bool selected = _pattern.patternType == ZUIPatternType.Icon && _pattern.iconId == iconName;
                    var tileRect = GUILayoutUtility.GetRect(k_TileSize, k_TileSize, GUILayout.Width(k_TileSize));

                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(tileRect, new Color(0.15f, 0.15f, 0.18f, 1f));
                        _iconCache.TryGetValue(iconPath, out var iconTex);
                        if (iconTex != null)
                        {
                            float iconInset = 4f;
                            var iconRect = new Rect(tileRect.x + iconInset, tileRect.y + iconInset,
                                                     tileRect.width - iconInset * 2f, tileRect.height - iconInset * 2f);
                            GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit, true);
                        }

                        if (selected)
                        {
                            float b = 2f;
                            EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.y, tileRect.width, b), Color.white);
                            EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.yMax - b, tileRect.width, b), Color.white);
                            EditorGUI.DrawRect(new Rect(tileRect.x, tileRect.y, b, tileRect.height), Color.white);
                            EditorGUI.DrawRect(new Rect(tileRect.xMax - b, tileRect.y, b, tileRect.height), Color.white);
                        }
                    }

                    if (Event.current.type == EventType.MouseDown && tileRect.Contains(Event.current.mousePosition))
                    {
                        _pattern.patternType = ZUIPatternType.Icon;
                        _pattern.iconId = iconName;
                        _pattern.Invalidate();
                        _onChanged?.Invoke();
                        editorWindow?.Repaint();
                        Event.current.Use();
                    }
                    GUILayout.Space(k_Gap);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(k_Pad);

            // Measure content height
            GUILayout.Space(1f);
            if (Event.current.type == EventType.Repaint)
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                _measuredHeight = lastRect.yMax;
            }

            GUILayout.EndScrollView();
        }
    }

    // ── Stop color popup — visual swatch picker for gradient stops ──────────
    class ZUIStopColorPopup : PopupWindowContent
    {
        ZUIColorRef _current;
        List<ZUIPaletteColor> _palette;
        ZUIStyleSheetAsset _sheet;
        Action<ZUIColorRef> _onChanged;
        float _measuredHeight;

        const float k_SwW   = 22f;
        const float k_RowH  = 22f;
        const float k_NameW = 70f;
        const float k_Pad   = 6f;
        const float k_PopW  = 280f;

        public ZUIStopColorPopup(ZUIColorRef current, List<ZUIPaletteColor> palette, ZUIStyleSheetAsset sheet, Action<ZUIColorRef> onChanged)
        {
            _current   = current;
            _palette   = palette;
            _sheet     = sheet;
            _onChanged = onChanged;
        }

        public override Vector2 GetWindowSize()
        {
            if (_measuredHeight > 0f) return new Vector2(k_PopW, _measuredHeight + 4f);
            int rows = (_palette != null ? _palette.Count : 0) + 1;
            return new Vector2(k_PopW, rows * (k_RowH + 2f) + 20f);
        }

        public override void OnGUI(Rect rect)
        {
            using var _scope = ZUI.UseSheet(ZUI.EditorSheet);
            bool changed = false;

            GUILayout.Space(k_Pad * 0.5f);

            // "Direct" option — click to clear palette ref
            GUILayout.BeginHorizontal();
            GUILayout.Space(k_Pad);
            bool isDirect = !_current.IsPaletteRef;
            var directRect = GUILayoutUtility.GetRect(k_SwW, k_RowH, GUILayout.Width(k_SwW), GUILayout.Height(k_RowH));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(directRect, _current.color);
                if (isDirect)
                {
                    float b = 2f;
                    EditorGUI.DrawRect(new Rect(directRect.x, directRect.y, directRect.width, b), Color.white);
                    EditorGUI.DrawRect(new Rect(directRect.x, directRect.yMax - b, directRect.width, b), Color.white);
                    EditorGUI.DrawRect(new Rect(directRect.x, directRect.y, b, directRect.height), Color.white);
                    EditorGUI.DrawRect(new Rect(directRect.xMax - b, directRect.y, b, directRect.height), Color.white);
                }
            }
            if (Event.current.type == EventType.MouseDown && directRect.Contains(Event.current.mousePosition))
            {
                _current = new ZUIColorRef(_current.Resolve(_sheet));
                changed = true; Event.current.Use();
            }
            GUILayout.Space(2f);
            EditorGUILayout.LabelField("Direct", EditorStyles.miniLabel, GUILayout.Height(k_RowH));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            // Palette entries with base + autocolors
            if (_palette != null)
            {
                var nameStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
                foreach (var entry in _palette)
                {
                    bool isSelectedEntry = _current.IsPaletteRef && _current.paletteRef == entry.name;

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(k_Pad);

                    // Highlight selected row
                    if (isSelectedEntry)
                    {
                        var hlRect = GUILayoutUtility.GetRect(k_PopW - k_Pad * 2f, k_RowH,
                            GUILayout.Width(k_PopW - k_Pad * 2f), GUILayout.Height(k_RowH));
                        if (Event.current.type == EventType.Repaint)
                            EditorGUI.DrawRect(hlRect, new Color(1f, 1f, 1f, 0.08f));
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(k_Pad);
                    }

                    // Name
                    EditorGUILayout.LabelField(entry.name, nameStyle, GUILayout.Width(k_NameW), GUILayout.Height(k_RowH));

                    // Base color swatch
                    changed |= DrawStopSwatch(entry.color, entry.name, ZUIPaletteSlot.Primary, "",
                        isSelectedEntry && !_current.IsAutoColorRef && _current.slot == ZUIPaletteSlot.Primary);

                    // Autocolor swatches
                    if (entry.autoColors != null)
                    {
                        foreach (var ac in entry.autoColors)
                        {
                            Color acColor = ac.Resolve(entry.color);
                            changed |= DrawStopSwatch(acColor, entry.name, ZUIPaletteSlot.Primary, ac.name,
                                isSelectedEntry && _current.autoColorRef == ac.name);
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(2f);
                }
            }

            // Measure height
            GUILayout.Space(1f);
            if (Event.current.type == EventType.Repaint)
                _measuredHeight = GUILayoutUtility.GetLastRect().yMax;

            if (changed) _onChanged?.Invoke(_current);
        }

        bool DrawStopSwatch(Color color, string paletteName, ZUIPaletteSlot slot, string autoColorName, bool isActive)
        {
            var swRect = GUILayoutUtility.GetRect(k_SwW, k_RowH, GUILayout.Width(k_SwW), GUILayout.Height(k_RowH));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(swRect, color);
                if (isActive)
                {
                    float b = 2f;
                    EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, swRect.width, b), Color.white);
                    EditorGUI.DrawRect(new Rect(swRect.x, swRect.yMax - b, swRect.width, b), Color.white);
                    EditorGUI.DrawRect(new Rect(swRect.x, swRect.y, b, swRect.height), Color.white);
                    EditorGUI.DrawRect(new Rect(swRect.xMax - b, swRect.y, b, swRect.height), Color.white);
                }
            }
            if (Event.current.type == EventType.MouseDown && swRect.Contains(Event.current.mousePosition))
            {
                _current = new ZUIColorRef(color, paletteName, slot, autoColorName);
                Event.current.Use();
                editorWindow?.Repaint();
                return true;
            }
            return false;
        }
    }

    class ZUIColorPickerPopup : PopupWindowContent
    {
        Color  _color;
        string _paletteRef;
        string _autoColorRef;
        List<ZUIPaletteColor> _palette;
        Action<Color, string, ZUIPaletteSlot, string> _onChanged;

        bool _paletteMode;
        float _measuredHeight;

        public ZUIColorPickerPopup(Color color, string paletteRef, ZUIPaletteSlot slot, string autoColorRef,
            List<ZUIPaletteColor> palette, Action<Color, string, ZUIPaletteSlot, string> onChanged)
        {
            _color        = color;
            _paletteRef   = paletteRef;
            _autoColorRef = autoColorRef ?? "";
            _palette      = palette;
            _onChanged    = onChanged;
            _paletteMode  = !string.IsNullOrEmpty(paletteRef);
        }

        const float k_SwatchW  = 22f;
        const float k_RowH     = 18f;
        const float k_Pad      = 6f;
        const float k_NameW    = 90f;
        const float k_PopW     = 280f;

        public override Vector2 GetWindowSize()
        {
            // Use measured height from previous frame if available, otherwise estimate
            if (_measuredHeight > 0f)
                return new Vector2(k_PopW, _measuredHeight + 4f);
            // Initial estimate (generous)
            float h = 34f; // mode radio + gap
            if (_paletteMode)
            {
                int count = (_palette != null ? _palette.Count : 0);
                h += count > 0 ? count * (k_RowH + 2f) + 4f : 20f;
            }
            else
                h += k_RowH + 8f;
            return new Vector2(k_PopW, h);
        }

        public override void OnGUI(Rect rect)
        {
            using var _sheetScope = ZUI.UseSheet(ZUI.EditorSheet);
            bool changed = false;

            ZUI.VerticalSpace("V Control Gap");
            GUILayout.BeginHorizontal();
            GUILayout.Space(k_Pad);

            // Mode radio — detect clicks by comparing return value to previous state
            bool clickedPalette = ZUI.Toggle(_paletteMode,  "Palette", "Toggle",  GUILayout.Width(60f));
            bool clickedDirect  = ZUI.Toggle(!_paletteMode, "Direct", "Toggle", GUILayout.Width(60f));
            // A toggle returns a changed value only when the user clicked it
            bool wantPalette = clickedPalette && !_paletteMode;   // was off, now on
            bool wantDirect  = clickedDirect  && _paletteMode;    // was off, now on
            if (wantPalette || wantDirect)
            {
                _paletteMode = wantPalette;
                if (!_paletteMode) { _paletteRef = ""; _autoColorRef = ""; }
                changed = true;
                editorWindow?.Repaint();
            }
            GUILayout.EndHorizontal();
            ZUI.VerticalSpace("V Control Gap");

            if (_paletteMode)
            {
                if (_palette == null || _palette.Count == 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(k_Pad);
                    EditorGUILayout.LabelField("No palette entries defined.", EditorStyles.miniLabel);
                    GUILayout.EndHorizontal();
                }
                else
                {
                    // One row per palette entry: [Name label][P swatch][H swatch][S swatch]
                    // Clicking any swatch selects that entry + slot in one tap.
                    var nameStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
                    foreach (var entry in _palette)
                    {
                        bool isSelected = entry.name == _paletteRef;
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(k_Pad);

                        // Highlight selected row
                        if (isSelected)
                        {
                            var rowRect = GUILayoutUtility.GetRect(k_PopW - k_Pad * 2f, k_RowH, GUILayout.Width(k_PopW - k_Pad * 2f), GUILayout.Height(k_RowH));
                            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.08f));
                            GUILayout.EndHorizontal();
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(k_Pad);
                        }

                        // Name
                        EditorGUILayout.LabelField(entry.name, nameStyle, GUILayout.Width(k_NameW), GUILayout.Height(k_RowH));

                        // Base color swatch
                        {
                            bool  baseActive = isSelected && string.IsNullOrEmpty(_autoColorRef);
                            var   baseRect   = GUILayoutUtility.GetRect(k_SwatchW, k_RowH, GUILayout.Width(k_SwatchW), GUILayout.Height(k_RowH));
                            DrawPopupSwatch(baseRect, entry.color, baseActive);
                            if (Event.current.type == EventType.MouseDown && baseRect.Contains(Event.current.mousePosition))
                            {
                                _paletteRef   = entry.name;
                                _autoColorRef = "";
                                changed       = true;
                                Event.current.Use();
                                editorWindow?.Repaint();
                            }
                        }

                        // Autocolor swatches
                        if (entry.autoColors != null)
                        {
                            foreach (var ac in entry.autoColors)
                            {
                                Color acColor  = ac.Resolve(entry.color);
                                bool  acActive = isSelected && _autoColorRef == ac.name;
                                var   acRect   = GUILayoutUtility.GetRect(k_SwatchW, k_RowH, GUILayout.Width(k_SwatchW), GUILayout.Height(k_RowH));
                                DrawPopupSwatch(acRect, acColor, acActive);
                                if (Event.current.type == EventType.MouseDown && acRect.Contains(Event.current.mousePosition))
                                {
                                    _paletteRef   = entry.name;
                                    _autoColorRef = ac.name;
                                    changed       = true;
                                    Event.current.Use();
                                    editorWindow?.Repaint();
                                }
                            }
                        }

                        GUILayout.Space(k_Pad);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(2f);
                    }
                }
            }
            else
            {
                // Direct color picker
                GUILayout.BeginHorizontal();
                GUILayout.Space(k_Pad);
                EditorGUI.BeginChangeCheck();
                _color = EditorGUILayout.ColorField(GUIContent.none, _color, true, true, false);
                if (EditorGUI.EndChangeCheck()) changed = true;
                GUILayout.Space(k_Pad);
                GUILayout.EndHorizontal();
            }

            // Measure total content height for auto-sizing
            GUILayout.Space(1f);
            if (Event.current.type == EventType.Repaint)
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                _measuredHeight = lastRect.yMax;
            }

            if (changed) _onChanged?.Invoke(_color, _paletteMode ? _paletteRef : "", ZUIPaletteSlot.Primary, _paletteMode ? _autoColorRef : "");
        }

        static void DrawPopupSwatch(Rect rect, Color color, bool isActive)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, color);
            if (isActive)
            {
                float b = 2f;
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, b), Color.white);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - b, rect.width, b), Color.white);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, b, rect.height), Color.white);
                EditorGUI.DrawRect(new Rect(rect.xMax - b, rect.y, b, rect.height), Color.white);
            }
        }
    }

    // ── Palette tab ───────────────────────────────────────────────────────────

    // ── Missing tab ───────────────────────────────────────────────────────────

    void DrawMissingTab()
    {
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);

        ZUI.VerticalSpace("V Control Gap");
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Missing Style Lookups", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (this.Button("Clear", "TabButton", GUILayout.Width(50f)))
        {
            if (_sheet != null)
                ZUIMissingStyleRegistry.ClearForSheet(_sheet);
            else
                ZUIMissingStyleRegistry.Clear();
            GUIUtility.ExitGUI();
        }
        GUILayout.EndHorizontal();
        EditorGUILayout.LabelField(
            "Any style name that was looked up but not found in the sheet. Resets on domain reload — just repaint your windows to re-populate.",
            EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Control Gap");

        var entries = new List<ZUIMissingStyleRegistry.Entry>(
            _sheet != null ? ZUIMissingStyleRegistry.EntriesForSheet(_sheet) : ZUIMissingStyleRegistry.Entries);
        if (entries.Count == 0)
        {
            var okStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.4f, 0.9f, 0.4f, 1f) } };
            EditorGUILayout.LabelField("No missing styles detected.", okStyle);
        }
        else
        {
            var rowStyle = new GUIStyle(EditorStyles.label)
            {
                normal   = { textColor = new Color(1f, 0.38f, 0.3f, 1f) },
                richText = true,
            };
            var countStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) },
                alignment = TextAnchor.MiddleRight,
            };

            // Sort: Button first, then Box, then Text, alphabetically within each type.
            entries.Sort((a, b) =>
            {
                int tc = a.type.CompareTo(b.type);
                return tc != 0 ? tc : string.Compare(a.requestedName, b.requestedName, System.StringComparison.Ordinal);
            });

            ZUIMissingStyleRegistry.EntryType? lastType = null;
            foreach (var entry in entries)
            {
                if (entry.type != lastType)
                {
                    ZUI.VerticalSpace("V Control Gap");
                    EditorGUILayout.LabelField(entry.type.ToString() + " Styles", EditorStyles.miniLabel);
                    lastType = entry.type;
                }

                GUILayout.BeginHorizontal();
                if (this.Button("+", "IconButton", GUILayout.Width(22f), GUILayout.Height(16f)))
                {
                    CreateMissingStyle(entry);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.LabelField(entry.requestedName, rowStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("x" + entry.hitCount, countStyle, GUILayout.Width(36f));
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    void CreateMissingStyle(ZUIMissingStyleRegistry.Entry entry)
    {
        if (_sheet == null) return;
        Undo.RegisterCompleteObjectUndo(_sheet, "Create Missing Style");

        switch (entry.type)
        {
            case ZUIMissingStyleRegistry.EntryType.Button:
                _sheet.buttons.Add(new ZUIButtonDef { name = entry.requestedName });
                break;
            case ZUIMissingStyleRegistry.EntryType.Box:
                _sheet.boxes.Add(new ZUIBoxDef { name = entry.requestedName });
                break;
            case ZUIMissingStyleRegistry.EntryType.Text:
                _sheet.textStyles.Add(new ZUITextStyleDef { name = entry.requestedName });
                break;
            case ZUIMissingStyleRegistry.EntryType.Slider:
                _sheet.sliders.Add(new ZUISliderDef { name = entry.requestedName });
                break;
            case ZUIMissingStyleRegistry.EntryType.Envelope:
                _sheet.envelopes.Add(new ZUIEnvelopeDef { name = entry.requestedName });
                break;
            case ZUIMissingStyleRegistry.EntryType.SpacingScale:
                _sheet.spacingScales.Add(new ZUISpacingScale { name = entry.requestedName, scale = 1f });
                break;
        }

        ZUIMissingStyleRegistry.Remove(entry.type, entry.requestedName);
        ZUI.InvalidateAllStyles();
        EditorUtility.SetDirty(_sheet);
        Repaint();
    }

    // ── Palette tab ───────────────────────────────────────────────────────────

    // ── Assets tab ─────────────────────────────────────────────────────────

    [NonSerialized] string _assetSearchFilter = "";
    [NonSerialized] Vector2 _assetsScroll;
    [NonSerialized] bool _hideSystemIcons;
    [NonSerialized] List<(string name, string path)> _cachedIcons;
    [NonSerialized] List<(string name, string path)> _cachedFonts;
    [NonSerialized] Dictionary<string, Texture2D> _assetIconTexCache;

    void DrawAssetsTab()
    {
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _assetsScroll = GUILayout.BeginScrollView(_assetsScroll);
        EditorGUIUtility.labelWidth = 100f;

        // ── Data folder ──────────────────────────────────────────────
        TabTitle("Data Folder");
        GUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _sheet.dataFolderPath = EditorGUILayout.TextField("Custom Path", _sheet.dataFolderPath);
        if (this.Button("...", "TabButton", GUILayout.Width(24f)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select ZUI Data Folder", _sheet.dataFolderPath, "");
            if (!string.IsNullOrEmpty(chosen))
            {
                string projectRoot = System.IO.Path.GetFullPath(".");
                if (chosen.StartsWith(projectRoot))
                    _sheet.dataFolderPath = "Assets" + chosen.Substring(projectRoot.Length).Replace('\\', '/');
                else
                    _sheet.dataFolderPath = chosen;
            }
        }
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_sheet);
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Control Gap");

        // ── Default font ─────────────────────────────────────────────
        TabTitle("Default Font");
        EditorGUI.BeginChangeCheck();
        _sheet.defaultFont = (Font)EditorGUILayout.ObjectField("Sheet Default", _sheet.defaultFont, typeof(Font), false);
        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_sheet);
        EditorGUILayout.LabelField($"Resolved: {(ZUI.DefaultFont != null ? ZUI.DefaultFont.name : "Unity Default")}", EditorStyles.miniLabel);
        ZUI.VerticalSpace("V Control Gap");

        // ── Icon aliases ─────────────────────────────────────────────
        TabTitle("Icon Aliases");
        DrawAliasEditor(_sheet.iconAliases, "icon");
        ZUI.VerticalSpace("V Control Gap");

        // ── Font aliases ─────────────────────────────────────────────
        TabTitle("Font Aliases");
        DrawAliasEditor(_sheet.fontAliases, "font");
        ZUI.VerticalSpace("V Section Rows");

        // ── Available icons ──────────────────────────────────────────
        GUILayout.BeginHorizontal();
        TabTitle("Available Icons");
        _hideSystemIcons = ZUI.Toggle(_hideSystemIcons, "Custom Only", "Toggle", GUILayout.Width(80f));
        if (this.Button("Refresh", "TabButton", GUILayout.Width(52f)))
        {
            _cachedIcons = null;
            _cachedFonts = null;
            _assetIconTexCache = null;
            AssetDatabase.Refresh();
        }
        GUILayout.EndHorizontal();

        // ── Search ───────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Search", GUILayout.Width(48f));
        _assetSearchFilter = EditorGUILayout.TextField(_assetSearchFilter);
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Control Gap");

        if (_cachedIcons == null)
        {
            _cachedIcons = ZUIAssetLibrary.GetAvailableIcons(_sheet?.dataFolderPath);
            _assetIconTexCache = null; // invalidate texture cache when icon list changes
        }
        if (_assetIconTexCache == null)
        {
            _assetIconTexCache = new Dictionary<string, Texture2D>(_cachedIcons.Count);
            foreach (var (n, p) in _cachedIcons)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) _assetIconTexCache[p] = t;
            }
        }

        string filter = _assetSearchFilter?.ToLower() ?? "";
        string systemPath = ZUIAssetLibrary.k_SystemIconsPath.Replace('\\', '/').ToLower();
        int iconCount = 0;
        int iconsPerRow = Mathf.Max(1, (int)(EditorGUIUtility.currentViewWidth - 20f) / 52);
        GUILayout.BeginHorizontal();
        foreach (var (name, path) in _cachedIcons)
        {
            if (!string.IsNullOrEmpty(filter) && !name.ToLower().Contains(filter)) continue;
            if (_hideSystemIcons && path.Replace('\\', '/').ToLower().StartsWith(systemPath)) continue;

            _assetIconTexCache.TryGetValue(path, out var tex);
            if (tex == null) continue;

            GUILayout.BeginVertical(GUILayout.Width(48f));
            var iconRect = GUILayoutUtility.GetRect(40f, 40f, GUILayout.Width(40f), GUILayout.Height(40f));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
            EditorGUILayout.LabelField(name, EditorStyles.miniLabel, GUILayout.Width(46f), GUILayout.Height(12f));
            GUILayout.EndVertical();

            // Right-click: edit in texture editor
            if (Event.current.type == EventType.ContextClick && iconRect.Contains(Event.current.mousePosition))
            {
                bool isSystem = path.Replace('\\', '/').ToLower().StartsWith(systemPath);
                string capturedPath = path;
                string capturedName = name;
                var menu = new GenericMenu();
                if (isSystem)
                {
                    menu.AddItem(new GUIContent($"Duplicate \"{capturedName}\" & Edit"), false, () =>
                    {
                        var editor = ZUITextureEditor.GetWindow<ZUITextureEditor>("ZUI Texture Editor");
                        editor.LoadIconForEditing(capturedPath, true);
                    });
                }
                else
                {
                    menu.AddItem(new GUIContent($"Edit \"{capturedName}\""), false, () =>
                    {
                        var editor = ZUITextureEditor.GetWindow<ZUITextureEditor>("ZUI Texture Editor");
                        editor.LoadIconForEditing(capturedPath, false);
                    });
                }
                menu.AddItem(new GUIContent("Show in Project"), false, () =>
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(capturedPath);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Create alias for \"{capturedName}\""), false, () =>
                {
                    _sheet.iconAliases.Add(new ZUIAssetAlias(capturedName, capturedName));
                    EditorUtility.SetDirty(_sheet);
                });
                menu.ShowAsContext();
                Event.current.Use();
            }

            iconCount++;
            if (iconCount % iconsPerRow == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();

        if (iconCount == 0)
        {
            string dataPath = _sheet?.dataFolderPath ?? "(none)";
            EditorGUILayout.LabelField($"No icons found. System: {ZUIAssetLibrary.k_SystemIconsPath}  Data: {dataPath}", EditorStyles.wordWrappedMiniLabel);
        }
        ZUI.VerticalSpace("V Section Rows");

        // ── Available fonts ──────────────────────────────────────────
        TabTitle("Available Fonts");
        if (_cachedFonts == null)
            _cachedFonts = ZUIAssetLibrary.GetAvailableFonts(_sheet?.dataFolderPath);
        foreach (var (name, path) in _cachedFonts)
        {
            if (!string.IsNullOrEmpty(filter) && !name.ToLower().Contains(filter)) continue;
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(150f));
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
        if (_cachedFonts.Count == 0) EditorGUILayout.LabelField("No fonts found.", EditorStyles.miniLabel);

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // Pending icon pick state for alias editor
    [NonSerialized] int _pendingAliasPick = -1;
    [NonSerialized] List<ZUIAssetAlias> _pendingAliasPickList;

    void DrawAliasEditor(List<ZUIAssetAlias> aliases, string type)
    {
        // Apply pending icon pick from popover
        if (_pendingAliasPick >= 0 && _pendingAliasPickList == aliases
            && _pendingAliasPick < aliases.Count && !string.IsNullOrEmpty(aliases[_pendingAliasPick].assetPath))
        {
            EditorUtility.SetDirty(_sheet);
            _pendingAliasPick = -1;
            _pendingAliasPickList = null;
        }

        int removeAt = -1;
        for (int i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            GUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();

            // Icon preview — clickable to open picker
            if (type == "icon")
            {
                var previewTex = !string.IsNullOrEmpty(alias.assetPath) ? ZUI.FindIcon(alias.assetPath) : null;
                if (previewTex == null)
                    previewTex = ZUIAssetLibrary.FindIcon("triangle-dashed");
                var previewRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f));
                if (Event.current.type == EventType.Repaint)
                    ZUI.DrawRotatedTexture(previewRect, previewTex, alias.rotation);
                // Click icon to open picker
                if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
                {
                    int capturedIdx = i;
                    var capturedAliases = aliases;
                    var popup = new ZUIIconPickerPopup(pickedName =>
                    {
                        if (capturedIdx < capturedAliases.Count)
                        {
                            capturedAliases[capturedIdx].assetPath = pickedName;
                            _pendingAliasPick = capturedIdx;
                            _pendingAliasPickList = capturedAliases;
                        }
                    }, _sheet?.dataFolderPath);
                    PopupWindow.Show(previewRect, popup);
                    Event.current.Use();
                }
            }

            // Alias name (editable)
            alias.name = EditorGUILayout.TextField(alias.name, GUILayout.Width(160f));

            // Icon label (read-only, shortened) + rotation controls
            if (type == "icon")
            {
                string displayName = alias.assetPath;
                if (!string.IsNullOrEmpty(displayName))
                {
                    string normalized = displayName.Replace('\\', '/');
                    if (normalized.Contains("/SystemAssets/"))
                        displayName = "sys:" + System.IO.Path.GetFileNameWithoutExtension(normalized);
                    else if (normalized.Contains("/"))
                        displayName = System.IO.Path.GetFileNameWithoutExtension(normalized);
                }
                EditorGUILayout.LabelField(displayName ?? "—", EditorStyles.miniLabel, GUILayout.Width(140f));
                alias.rotation = ZUI.Slider(alias.rotation, 0f, 360f, "", "SmallSlider", (float?)null, GUILayout.Width(60f));
                alias.rotation = EditorGUILayout.FloatField(alias.rotation, GUILayout.Width(32f));
                if (this.Button("↻", "TabButton", GUILayout.Width(18f), GUILayout.Height(16f)))
                {
                    float next = Mathf.Ceil((alias.rotation + 1f) / 45f) * 45f;
                    alias.rotation = next >= 360f ? 0f : next;
                }
            }
            else
            {
                // Font aliases: show name as label
                string displayName = !string.IsNullOrEmpty(alias.assetPath) ? System.IO.Path.GetFileNameWithoutExtension(alias.assetPath) : "—";
                EditorGUILayout.LabelField(displayName, EditorStyles.miniLabel, GUILayout.Width(80f));
            }

            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_sheet);

            if (this.Button("×", "TabButton", GUILayout.Width(18f)))
                removeAt = i;
            GUILayout.EndHorizontal();

        }
        if (removeAt >= 0) { aliases.RemoveAt(removeAt); EditorUtility.SetDirty(_sheet); }

        if (this.Button($"+ Add {type} alias", "TabButton", GUILayout.Width(120f)))
        {
            aliases.Add(new ZUIAssetAlias("New Alias", ""));
            EditorUtility.SetDirty(_sheet);
        }
    }

    void DrawPaletteTab()
    {
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);
        EditorGUIUtility.labelWidth = k_LabelWidth;

        // ── Skin palette override section ─────────────────────────────
        var activeSkin = _sheet.ActiveSkin;
        if (activeSkin != null)
        {
            TabTitle($"Skin: {activeSkin.name}");
            EditorGUILayout.LabelField("Override palette colors for this skin. Base palette shown below (read-only in skin mode).", EditorStyles.wordWrappedMiniLabel);
            ZUI.VerticalSpace("V Control Gap");

            bool skinDirty = false;
            for (int i = 0; i < activeSkin.palette.Count; i++)
            {
                var entry = activeSkin.palette[i];
                var baseEntry = _sheet.palette.Find(p => p.name == entry.name);

                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.name, GUILayout.Width(100f));
                EditorGUI.BeginChangeCheck();
                entry.color = EditorGUILayout.ColorField(GUIContent.none, entry.color, true, true, false, GUILayout.Width(60f));
                if (EditorGUI.EndChangeCheck()) { _paletteDirtyDeferred = true; _paletteChangedThisGUI = true; Repaint(); }

                if (baseEntry != null && this.Button("↺", "TabButton", GUILayout.Width(20f)))
                {
                    entry.color = baseEntry.color;
                    if (baseEntry.autoColors != null)
                    {
                        entry.autoColors = new System.Collections.Generic.List<ZUIAutoColor>();
                        foreach (var ac in baseEntry.autoColors)
                            entry.autoColors.Add(new ZUIAutoColor { name = ac.name, hueMod = ac.hueMod, satMod = ac.satMod, valMod = ac.valMod });
                    }
                    else
                        entry.autoColors = new System.Collections.Generic.List<ZUIAutoColor>();
                    skinDirty = true; ZUI.InvalidateAllStyles();
                }
                GUILayout.EndHorizontal();
            }

            if (skinDirty) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
            ZUI.VerticalSpace("V Section Rows");
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)), new Color(1f, 1f, 1f, 0.1f));
            ZUI.VerticalSpace("V Section Rows");
        }

        TabTitle(IsSkinLocked ? "Color Palette (read-only in skin mode)" : "Color Palette");
        EditorGUILayout.LabelField("Named colors that can be referenced by any style field.", EditorStyles.wordWrappedMiniLabel);
        ZUI.VerticalSpace("V Control Gap");

        using var _basePaletteLock = new EditorGUI.DisabledGroupScope(IsSkinLocked);
        bool dirty = false;
        var palette = _sheet.palette;
        int duplicatePaletteAt = -1;
        int removePaletteAt    = -1;

        for (int i = 0; i < palette.Count; i++)
        {
            var entry = palette[i];
            GUILayout.BeginHorizontal();

            string oldName = entry.name;
            EditorGUI.BeginChangeCheck();
            entry.name = EditorGUILayout.TextField(entry.name, GUILayout.Width(120f));
            if (EditorGUI.EndChangeCheck() && entry.name != oldName)
            {
                foreach (var b in _sheet.buttons)    RenamePaletteRefs(b, oldName, entry.name);
                foreach (var b in _sheet.boxes)      RenamePaletteRefs(b, oldName, entry.name);
                foreach (var t in _sheet.textStyles)  RenamePaletteRefs(t, oldName, entry.name);
                foreach (var s in _sheet.sliders)    RenamePaletteRefs(s, oldName, entry.name);
                _paletteDirtyDeferred = true;
                _paletteChangedThisGUI = true;
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            entry.color = EditorGUILayout.ColorField(GUIContent.none, entry.color, true, true, false, GUILayout.Width(80f));
            if (EditorGUI.EndChangeCheck())
            {
                _paletteDirtyDeferred = true;
                _paletteChangedThisGUI = true;
                Repaint();
            }

            if (this.Button(IconDuplicate, "IconButton", GUILayout.Width(18f), GUILayout.Height(20f)))
                duplicatePaletteAt = i;

            if (this.Button(IconDelete, "IconButton", GUILayout.Width(18f), GUILayout.Height(20f)))
                removePaletteAt = i;

            // Autocolor swatches inline on the same row as name / color / dup / delete.
            int clickedSwatch = ZUIAutoColorEditor.DrawAutoColorSwatchesInline(entry);
            if (clickedSwatch >= 0 && !ZUIAutoColorEditor.IsCreating(entry.name))
            {
                string capturedName = entry.name;
                int capturedIdx = clickedSwatch;
                var capturedAc = entry.autoColors[clickedSwatch];
                EditorApplication.delayCall += () => ZUIAutoColorEditor.BeginEdit(capturedName, capturedIdx, capturedAc);
            }

            // Add-autocolor icon button sits after the last autocolor swatch.
            // Disabled while a creation/edit panel is already open for this entry.
            bool isCreating = ZUIAutoColorEditor.IsCreating(entry.name);
            using (new EditorGUI.DisabledGroupScope(isCreating))
            {
                if (this.Button(IconAdd, "IconButton", GUILayout.Width(18f), GUILayout.Height(20f)) && !isCreating)
                {
                    string capturedName = entry.name;
                    EditorApplication.delayCall += () => ZUIAutoColorEditor.BeginCreate(capturedName);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Inline create/edit panel
            var resultAutoColor = ZUIAutoColorEditor.DrawCreationPanel(entry.name, entry.color, k_PaletteDetailIndent, position.width - 20f, out int editedIndex, entry.autoColors);
            if (resultAutoColor != null)
            {
                if (entry.autoColors == null)
                    entry.autoColors = new System.Collections.Generic.List<ZUIAutoColor>();
                if (editedIndex >= 0 && editedIndex < entry.autoColors.Count)
                {
                    // Editing existing — update in place
                    entry.autoColors[editedIndex] = resultAutoColor;
                }
                else
                {
                    // Creating new
                    entry.autoColors.Add(resultAutoColor);
                }
                dirty = true;
            }

            ZUI.VerticalSpace("V Control Gap");
        }

        if (duplicatePaletteAt >= 0)
        {
            var src   = palette[duplicatePaletteAt];
            var clone = JsonUtility.FromJson<ZUIPaletteColor>(JsonUtility.ToJson(src));
            if (!clone.name.EndsWith(" (Copy)")) clone.name += " (Copy)";
            palette.Insert(duplicatePaletteAt + 1, clone);
            dirty = true;
        }
        if (removePaletteAt >= 0)
        {
            var entry = palette[removePaletteAt];
            var refs = CollectPaletteReferences(entry.name);
            if (refs.Count > 0)
            {
                string refList = string.Join("\n", refs);
                bool confirm = EditorUtility.DisplayDialog(
                    "Palette Color In Use",
                    $"The palette color \"{entry.name}\" is referenced by:\n\n{refList}\n\nDelete anyway? References will be converted to inline colors.",
                    "Delete", "Cancel");
                if (confirm)
                {
                    BakePaletteRefsToInline(entry.name);
                    palette.RemoveAt(removePaletteAt);
                    dirty = true;
                }
            }
            else
            {
                palette.RemoveAt(removePaletteAt);
                dirty = true;
            }
        }

        ZUI.VerticalSpace("V Control Gap");
        if (this.Button("+ Add Color", "TabButton"))
        {
            palette.Add(new ZUIPaletteColor { name = "New Color", color = Color.white });
            dirty = true;
        }

        // Check for deferred autocolor removal (from context menu)
        if (ZUIAutoColorEditor.ConsumePendingRemoval())
            dirty = true;

        if (dirty) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // ── Palette invalidation helpers ──────────────────────────────────────────

    void InvalidatePaletteRefs(string paletteName)
    {
        if (_sheet == null) return;
        foreach (var b in _sheet.buttons)
            if (ReferencesColor(b, paletteName)) b.Invalidate();
        foreach (var b in _sheet.boxes)
            if (ReferencesColor(b, paletteName)) b.Invalidate();
        foreach (var t in _sheet.textStyles)
            if (ReferencesColor(t.text, paletteName)) t.Invalidate();
        foreach (var s in _sheet.sliders)
            if (ReferencesColor(s, paletteName)) s.Invalidate();
    }

    /// <summary>Collects human-readable names of all styles that reference the given palette color.</summary>
    List<string> CollectPaletteReferences(string paletteName)
    {
        var result = new List<string>();
        if (_sheet == null) return result;
        foreach (var b in _sheet.buttons)
            if (ReferencesColor(b, paletteName)) result.Add($"Button: {b.name}");
        foreach (var b in _sheet.boxes)
            if (ReferencesColor(b, paletteName)) result.Add($"Box: {b.name}");
        foreach (var t in _sheet.textStyles)
            if (ReferencesColor(t.text, paletteName)) result.Add($"Text: {t.name}");
        foreach (var s in _sheet.sliders)
            if (ReferencesColor(s, paletteName)) result.Add($"Slider: {s.name}");
        return result;
    }

    /// <summary>
    /// Resolves all palette references to the given color into inline values,
    /// then clears the refs. The visual appearance is preserved.
    /// </summary>
    void BakePaletteRefsToInline(string paletteName)
    {
        if (_sheet == null) return;
        var entry = _sheet.FindPaletteColor(paletteName);
        if (entry == null) return;

        // Bake refs only — invalidation handled by RepaintShowcase/BumpVersion at end of DrawPaletteTab
        foreach (var b in _sheet.buttons)    BakeColorRefsToInline(b, entry, paletteName, _sheet);
        foreach (var b in _sheet.boxes)      BakeColorRefsToInline(b, entry, paletteName, _sheet);
        foreach (var t in _sheet.textStyles) BakeColorRefsToInline(t, entry, paletteName, _sheet);
        foreach (var s in _sheet.sliders)    BakeColorRefsToInline(s, entry, paletteName, _sheet);
    }

    // ── Safe reflection walker for ZUIColorRef fields ──────────────────────
    // Walks all serialized public fields recursively, with cycle protection.
    // Handles List<T> contents (e.g. gradient stops). Skips NonSerialized fields
    // and UnityEngine.Object references to prevent circular walks.

    delegate bool ColorRefVisitor(System.Reflection.FieldInfo field, object owner);

    static bool WalkColorRefs(object obj, ColorRefVisitor visitor)
    {
        var visited = new HashSet<object>(new RefEqComparer());
        return WalkColorRefsInner(obj, visitor, visited);
    }

    static bool WalkColorRefsInner(object obj, ColorRefVisitor visitor, HashSet<object> visited)
    {
        if (obj == null) return false;
        if (!obj.GetType().IsValueType && !visited.Add(obj)) return false;

        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (var field in obj.GetType().GetFields(flags))
        {
            if (field.FieldType == typeof(ZUIColorRef))
            {
                if (visitor(field, obj)) return true;
            }
            else if (IsWalkableField(field))
            {
                var sub = field.GetValue(obj);
                if (sub is System.Collections.IList list)
                {
                    foreach (var item in list)
                        if (item != null && WalkColorRefsInner(item, visitor, visited)) return true;
                }
                else if (sub != null && WalkColorRefsInner(sub, visitor, visited))
                    return true;
            }
        }
        return false;
    }

    static bool IsWalkableField(System.Reflection.FieldInfo field)
    {
        if (field.IsNotSerialized) return false;
        var t = field.FieldType;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Color)
            || t == typeof(Vector2) || t == typeof(Vector4) || t == typeof(Rect))
            return false;
        if (field.Name.StartsWith("_")) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
        return t.IsClass || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>));
    }

    class RefEqComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    // ── Palette ref operations using the safe walker ─────────────────────────

    static bool ReferencesColor(object obj, string paletteName)
    {
        return WalkColorRefs(obj, (field, owner) =>
        {
            var cr = (ZUIColorRef)field.GetValue(owner);
            return cr.paletteRef == paletteName;
        });
    }

    static void BakeColorRefsToInline(object obj, ZUIPaletteColor entry, string paletteName, ZUIStyleSheetAsset sheet)
    {
        WalkColorRefs(obj, (field, owner) =>
        {
            var cr = (ZUIColorRef)field.GetValue(owner);
            if (cr.paletteRef == paletteName)
            {
                cr.color = cr.Resolve(sheet);
                cr.paletteRef = "";
                cr.autoColorRef = "";
                field.SetValue(owner, cr);
            }
            return false;
        });
    }

    static void RenamePaletteRefs(object obj, string oldName, string newName)
    {
        WalkColorRefs(obj, (field, owner) =>
        {
            var cr = (ZUIColorRef)field.GetValue(owner);
            if (cr.paletteRef == oldName)
            {
                cr.paletteRef = newName;
                field.SetValue(owner, cr);
            }
            return false;
        });
    }

    // ── Slider inspector ──────────────────────────────────────────────────────

    [SerializeField] private float _sliderPreviewValue    = 0.6f;
    [SerializeField] private int   _sliderPreviewBgMode   = 0;   // 0=None, 1=Box
    [SerializeField] private int   _sliderPreviewBoxIndex = 0;

    [NonSerialized] private int _lastSyncedSlider = -1;

    void DrawSliderInspector()
    {
        if (_selectedSlider < 0 || _selectedSlider >= _sheet.sliders.Count)
        { CenteredLabel("Select a slider style."); return; }

        var  def     = _sheet.sliders[_selectedSlider];

        // Sync thumb mode tab when switching to a different slider
        if (_selectedSlider != _lastSyncedSlider)
        {
            _lastSyncedSlider = _selectedSlider;
            _sliderThumbModeTab = def.thumbMax != null ? 1 : 0;
        }
        bool changed = false;
        EditorGUIUtility.labelWidth = k_LabelWidth;

        TabTitle("Slider Style");

        GUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        def.name = EditorGUILayout.TextField("Name", def.name);
        if (EditorGUI.EndChangeCheck()) { ZUIMissingStyleRegistry.Remove(ZUIMissingStyleRegistry.EntryType.Slider, def.name); changed = true; }
        if (ChromeButton(IconFlash, "IconButton", GUILayout.Width(24f), GUILayout.Height(18f))) ZUI.StartFlash(def.name, ZUI.FlashDefType.Slider, _sheet);
        GUILayout.EndHorizontal();

        ZUI.VerticalSpace("V Section Rows");

        // ── Section visibility toggles ──
        GUILayout.BeginHorizontal();
        def.showPreview   = ZUI.Toggle(def.showPreview,   "Prv",  "Toggle", GUILayout.Height(16f));
        def.showLayout    = ZUI.Toggle(def.showLayout,    "Lay",  "Toggle", GUILayout.Height(16f));
        def.showTrack     = ZUI.Toggle(def.showTrack,     "Trk",  "Toggle", GUILayout.Height(16f));
        def.showTrackFill = ZUI.Toggle(def.showTrackFill, "Fill", "Toggle", GUILayout.Height(16f));
        def.showThumb     = ZUI.Toggle(def.showThumb,     "Thb",  "Toggle", GUILayout.Height(16f));
        def.showLabelText = ZUI.Toggle(def.showLabelText, "Lbl",  "Toggle", GUILayout.Height(16f));
        def.showValueText = ZUI.Toggle(def.showValueText, "Val",  "Toggle", GUILayout.Height(16f));
        GUILayout.EndHorizontal();
        ZUI.VerticalSpace("V Section Rows");

        if (def.showPreview)
        {
        if (DrawPreviewHeader("slider_preview", showRoundingToggle: true))
        {

        // ── Preview ───────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Background", GUILayout.Width(k_LabelWidth));
        _sliderPreviewBgMode = ZUIToolbar(_sliderPreviewBgMode,
            new[] { "None", "Box" });
        GUILayout.EndHorizontal();

        if (_sliderPreviewBgMode == 1 && _sheet.boxes.Count > 0)
        {
            _sliderPreviewBoxIndex = Mathf.Clamp(_sliderPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
            var names = new string[_sheet.boxes.Count];
            for (int i = 0; i < _sheet.boxes.Count; i++) names[i] = _sheet.boxes[i].name;
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Box Style", GUILayout.Width(k_LabelWidth));
            _sliderPreviewBoxIndex = EditorGUILayout.Popup(_sliderPreviewBoxIndex, names);
            GUILayout.EndHorizontal();
        }

        ZUI.VerticalSpace("V Section Rows");

        float sliderH = Mathf.Max(def.thumbHeight > 0f ? def.thumbHeight : 20f, def.trackHeight);

        if (_sliderPreviewBgMode == 1 && _sheet.boxes.Count > 0)
        {
            _sliderPreviewBoxIndex = Mathf.Clamp(_sliderPreviewBoxIndex, 0, _sheet.boxes.Count - 1);
            using (ZUI.Box(null, _sheet.boxes[_sliderPreviewBoxIndex]))
                _sliderPreviewValue = ZUI.Slider(_sliderPreviewValue, 0f, 1f, "Preview", def, null, GUILayout.ExpandWidth(true));
        }
        else
        {
            var bgRect = GUILayoutUtility.GetRect(1f, sliderH + 16f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, new Color(.13f, .13f, .15f, 1f));
            var sliderRect = new Rect(bgRect.x + 8f, bgRect.y + (bgRect.height - sliderH) * 0.5f,
                                      bgRect.width - 16f, sliderH);
            _sliderPreviewValue = ZUI.Slider(sliderRect, _sliderPreviewValue, 0f, 1f, "Preview", def);
        }

        } // end DrawPreviewHeader
        } // end showPreview

        ZUI.VerticalSpace("V Section Rows");

        // ── Layout ────────────────────────────────────────────────────────────
        if (def.showLayout && SectionHeaderPlain("Layout", "slider_layout"))
        {
            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Track Height", GUILayout.Width(k_LabelWidth));
            def.trackHeight = ZUI.SliderFormatted(def.trackHeight, 2f, 40f, "F1", "", "SmallSlider");
            GUILayout.EndHorizontal();

            ZUI.VerticalSpace("V Section Rows");
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Thumb Width", GUILayout.Width(k_LabelWidth));
            def.thumbWidth = ZUI.SliderFormatted(def.thumbWidth, 0f, 60f, "F1", "", "SmallSlider");
            GUILayout.EndHorizontal();

            ZUI.VerticalSpace("V Section Rows");
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Thumb Height", GUILayout.Width(k_LabelWidth));
            def.thumbHeight = ZUI.SliderFormatted(def.thumbHeight, 0f, 60f, "F1", "", "SmallSlider");
            EditorGUILayout.LabelField("(0 = full height)", EditorStyles.miniLabel, GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            // Pair each toggle with its conditional value on the same row so related controls
            // group visually: Row 1 = Value Field | Value Width, Row 2 = Bipolar | Center.
            // Toggle uses the Zeditor's standard inline-labeled idiom (matches line 750-757).
            const float k_ToggleW   = 110f;
            const float k_AutoBtnW  = 48f;

            // Row 1: Value Field | Value Width
            ZUI.VerticalSpace("V Section Rows");
            GUILayout.BeginHorizontal();
            def.showValueField = ZUI.Toggle(def.showValueField, "Value Field", "Toggle", GUILayout.Width(k_ToggleW), GUILayout.Height(16f));
            ZUI.HorizontalSpace("H Control Gap");
            using (new EditorGUI.DisabledGroupScope(!def.showValueField))
            {
                def.valueWidth = ZUI.SliderFormatted(def.valueWidth, 20f, 120f, "F1", "", "SmallSlider");
            }
            GUILayout.EndHorizontal();

            // Row 2: Bipolar | Center
            ZUI.VerticalSpace("V Section Rows");
            GUILayout.BeginHorizontal();
            def.bipolar = ZUI.Toggle(def.bipolar, "Bipolar", "Toggle", GUILayout.Width(k_ToggleW), GUILayout.Height(16f));
            ZUI.HorizontalSpace("H Control Gap");
            using (new EditorGUI.DisabledGroupScope(!def.bipolar))
            {
                // NaN center = auto midpoint. "Auto" mini-button toggles between NaN and a number.
                bool isAuto = float.IsNaN(def.bipolarCenter);
                bool newAuto = GUILayout.Toggle(isAuto, "Auto", EditorStyles.miniButton, GUILayout.Width(k_AutoBtnW));
                if (newAuto != isAuto)
                {
                    def.bipolarCenter = newAuto ? float.NaN : 0f;
                }
                using (new EditorGUI.DisabledGroupScope(newAuto))
                {
                    float shown = float.IsNaN(def.bipolarCenter) ? 0f : def.bipolarCenter;
                    float edited = EditorGUILayout.FloatField(shown);
                    if (!newAuto) def.bipolarCenter = edited;
                }
            }
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");

        // ── Track (empty / right) ─────────────────────────────────────────────
        if (def.showTrack && SectionHeaderWithCopyPaste("Track (Empty)",
            () => _clipTrack = DeepCopy(def.track),
            () => { if (_clipTrack != null) { DeepPaste(def.track, _clipTrack); def.track.Invalidate(); changed = true; } },
            _clipTrack != null, "slider_track"))
        {
            EditorGUI.BeginChangeCheck();
            ZUI.VerticalSpace("V Section Rows");
            if (def.track == null) def.track = new ZUIBoxDef("Track",
                new Color(.14f, .14f, .18f, 1f), new Color(.88f,.88f,.88f,1f),
                new Color(1f,1f,1f,.08f), 1f, 0, 0);
            DrawInlineBoxDef(def.track, "slider_track");
            if (EditorGUI.EndChangeCheck()) { def.track.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");

        // ── Track fill ────────────────────────────────────────────────────────
        if (def.showTrackFill && SectionHeaderWithCopyPaste("Track Fill",
            () => _clipTrack = DeepCopy(def.trackFill),
            () => { if (_clipTrack != null) { DeepPaste(def.trackFill, _clipTrack); def.trackFill.Invalidate(); changed = true; } },
            _clipTrack != null, "slider_trackfill"))
        {
            EditorGUI.BeginChangeCheck();
            if (def.trackFill == null) def.trackFill = new ZUIBoxDef("TrackFill",
                new Color(.20f,.38f,.55f,1f), new Color(.88f,.88f,.88f,1f),
                new Color(.30f,.60f,1f,.30f), 1f, 0, 0);
            DrawInlineBoxDef(def.trackFill, "slider_trackfill");
            if (EditorGUI.EndChangeCheck()) { def.trackFill.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");

        // ── Thumb ─────────────────────────────────────────────────────────────
        // Outer header is plain (no copy/paste) because the three sub-thumb editors each have
        // their own copy/paste buttons that share _clipThumb — you can copy Min into Max, or
        // Normal into Min, since all three are ZUIButtonDef.
        if (def.showThumb && SectionHeaderPlain("Thumb", "slider_thumb_header"))
        {
            // Normal | MinMax mode selector
            int newMode = ZUIToolbar(_sliderThumbModeTab, new[] { "Normal", "Min / Max" });
            if (newMode != _sliderThumbModeTab)
            {
                _sliderThumbModeTab = newMode;
                // Enabling MinMax: seed thumbMax from thumb
                if (newMode == 1 && def.thumbMax == null)
                {
                    var src = def.thumb;
                    def.thumbMax = new ZUIButtonDef("ThumbMax",
                        src?.normal.colorA.color ?? new Color(.30f,.54f,.78f,1f),
                        src?.hover.colorA.color  ?? new Color(.40f,.64f,.90f,1f),
                        src?.active.colorA.color ?? new Color(.20f,.40f,.62f,1f),
                        src?.textColor           ?? new Color(.92f,.96f,1f,1f));
                    changed = true;
                }
                // Disabling MinMax: clear thumbMax
                if (newMode == 0)
                {
                    def.thumbMax = null;
                    changed = true;
                }
            }

            ZUI.VerticalSpace("V Section Rows");

            if (_sliderThumbModeTab == 0)
            {
                // Single thumb with its own copy/paste header (shares _clipThumb with Min/Max).
                if (SectionHeaderWithCopyPaste("Thumb",
                    () => _clipThumb = DeepCopy(def.thumb),
                    () => { if (_clipThumb != null && def.thumb != null) { DeepPaste(def.thumb, _clipThumb); def.thumb.Invalidate(); changed = true; } },
                    _clipThumb != null, "slider_thumb_normal"))
                {
                    if (def.thumb == null) def.thumb = new ZUIButtonDef("Thumb",
                        new Color(.30f,.54f,.78f,1f), new Color(.40f,.64f,.90f,1f),
                        new Color(.20f,.40f,.62f,1f), new Color(.92f,.96f,1f,1f));
                    EditorGUI.BeginChangeCheck();
                    DrawInlineButtonDefFlat(def.thumb, ref _sliderThumbMinState);
                    if (EditorGUI.EndChangeCheck()) { def.thumb.Invalidate(); changed = true; }
                }
            }
            else
            {
                // Min thumb
                if (SectionHeaderWithCopyPaste("Min Thumb (left)",
                    () => _clipThumb = DeepCopy(def.thumb),
                    () => { if (_clipThumb != null && def.thumb != null) { DeepPaste(def.thumb, _clipThumb); def.thumb.Invalidate(); changed = true; } },
                    _clipThumb != null, "slider_thumb_min"))
                {
                    if (def.thumb == null) def.thumb = new ZUIButtonDef("Thumb",
                        new Color(.30f,.54f,.78f,1f), new Color(.40f,.64f,.90f,1f),
                        new Color(.20f,.40f,.62f,1f), new Color(.92f,.96f,1f,1f));
                    EditorGUI.BeginChangeCheck();
                    DrawInlineButtonDefFlat(def.thumb, ref _sliderThumbMinState);
                    if (EditorGUI.EndChangeCheck()) { def.thumb.Invalidate(); changed = true; }
                }
                ZUI.VerticalSpace("V Section Rows");
                // Max thumb
                if (SectionHeaderWithCopyPaste("Max Thumb (right)",
                    () => _clipThumb = DeepCopy(def.thumbMax),
                    () => { if (_clipThumb != null && def.thumbMax != null) { DeepPaste(def.thumbMax, _clipThumb); def.thumbMax.Invalidate(); changed = true; } },
                    _clipThumb != null, "slider_thumb_max"))
                {
                    if (def.thumbMax == null) def.thumbMax = new ZUIButtonDef("ThumbMax",
                        def.thumb?.normal.colorA.color ?? new Color(.30f,.54f,.78f,1f),
                        def.thumb?.hover.colorA.color  ?? new Color(.40f,.64f,.90f,1f),
                        def.thumb?.active.colorA.color ?? new Color(.20f,.40f,.62f,1f),
                        def.thumb?.textColor           ?? new Color(.92f,.96f,1f,1f));
                    EditorGUI.BeginChangeCheck();
                    DrawInlineButtonDefFlat(def.thumbMax, ref _sliderThumbMaxState);
                    if (EditorGUI.EndChangeCheck()) { def.thumbMax.Invalidate(); changed = true; }
                }
            }

            // Bipolar center thumb (only when Bipolar is enabled in Layout). Shares _clipThumb.
            if (def.bipolar)
            {
                ZUI.VerticalSpace("V Section Rows");
                if (SectionHeaderWithCopyPaste("Center Thumb",
                    () => _clipThumb = DeepCopy(def.thumbCenter ?? def.thumb),
                    () => { if (_clipThumb != null) {
                                if (def.thumbCenter == null) def.thumbCenter = new ZUIButtonDef("ThumbCenter",
                                    def.thumb?.normal.colorA.color ?? new Color(.30f,.54f,.78f,1f),
                                    def.thumb?.hover.colorA.color  ?? new Color(.40f,.64f,.90f,1f),
                                    def.thumb?.active.colorA.color ?? new Color(.20f,.40f,.62f,1f),
                                    def.thumb?.textColor           ?? new Color(.92f,.96f,1f,1f));
                                DeepPaste(def.thumbCenter, _clipThumb); def.thumbCenter.Invalidate(); changed = true; } },
                    _clipThumb != null, "slider_thumb_center"))
                {
                    if (def.thumbCenter == null) def.thumbCenter = new ZUIButtonDef("ThumbCenter",
                        def.thumb?.normal.colorA.color ?? new Color(.30f,.54f,.78f,1f),
                        def.thumb?.hover.colorA.color  ?? new Color(.40f,.64f,.90f,1f),
                        def.thumb?.active.colorA.color ?? new Color(.20f,.40f,.62f,1f),
                        def.thumb?.textColor           ?? new Color(.92f,.96f,1f,1f));
                    EditorGUI.BeginChangeCheck();
                    DrawInlineButtonDefFlat(def.thumbCenter, ref _sliderThumbCenterState);
                    if (EditorGUI.EndChangeCheck()) { def.thumbCenter.Invalidate(); changed = true; }
                }
            }
        }

        ZUI.VerticalSpace("V Section Rows");

        // ── Label text ────────────────────────────────────────────────────────
        if (def.showLabelText && SectionHeaderWithCopyPaste("Label Text",
            () => _clipText = DeepCopy(def.labelText),
            () => { if (_clipText != null) { DeepPaste(def.labelText, _clipText); def.Invalidate(); changed = true; } },
            _clipText != null, "slider_labeltext"))
        {
            EditorGUI.BeginChangeCheck();
            DrawTextRow(def.labelText);
            DrawShadowTextRow(def.labelText);
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }

        ZUI.VerticalSpace("V Section Rows");

        // ── Value text ────────────────────────────────────────────────────────
        if (def.showValueText && SectionHeaderWithCopyPaste("Value Text",
            () => _clipText = DeepCopy(def.valueText),
            () => { if (_clipText != null) { DeepPaste(def.valueText, _clipText); def.Invalidate(); changed = true; } },
            _clipText != null, "slider_valuetext"))
        {
            EditorGUI.BeginChangeCheck();
            DrawTextRow(def.valueText);
            DrawShadowTextRow(def.valueText);
            if (EditorGUI.EndChangeCheck()) { def.Invalidate(); changed = true; }
        }

        if (changed) { EditorUtility.SetDirty(_sheet); RepaintShowcase(); }
    }

    // Draws a ZUIBoxDef inline (background, border, shape — no padding/margin, no title).

    void DrawInlineBoxDef(ZUIBoxDef box, string keyPrefix)
    {
        Action inv = () => { box.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        DrawFillField(box.background);

        ZUI.VerticalSpace("V Section Rows");
        EditorGUI.BeginChangeCheck();
        DrawBorderDefField(box.border, null, compact: true);
        if (EditorGUI.EndChangeCheck()) { box.border.color.Invalidate(); inv(); }

        ZUI.VerticalSpace("V Section Rows");
        DrawShapeEditor(box.shape, 24);
    }

    // Draws a ZUIButtonDef inline for the slider thumb — sub-sections use a visually
    // indented style (lighter bg, smaller height) to differentiate from parent sections.
    void DrawInlineButtonDef(ZUIButtonDef btn, string keyPrefix)
    {
        Action inv = () => { btn.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        if (SubsectionHeader("Normal BG", keyPrefix + "_norm"))
            DrawFillField(btn.normal);

        if (SubsectionHeader("Hover BG", keyPrefix + "_hov"))
        {
            btn.hoverBgOverride = ZUI.Toggle(btn.hoverBgOverride, "Override", "Toggle");
            if (btn.hoverBgOverride)
                DrawFillField(btn.hover);
        }

        if (SubsectionHeader("Active BG", keyPrefix + "_act"))
        {
            btn.activeBgOverride = ZUI.Toggle(btn.activeBgOverride, "Override", "Toggle");
            if (btn.activeBgOverride)
                DrawFillField(btn.active);
        }

        if (SubsectionHeader("Shape", keyPrefix + "_shape"))
        {
            DrawShapeEditor(btn.shape, 24);
        }

        if (SubsectionHeader("Border", keyPrefix + "_bdr"))
        {
            EditorGUI.BeginChangeCheck();
            DrawBorderDefField(btn.border, null, compact: true);
            if (EditorGUI.EndChangeCheck()) { btn.border.color.Invalidate(); inv(); }
        }
    }

    // Flat (no foldout subsections) button def editor used inside slider thumb sections.
    // States shown via a Normal|Hover|Active tab. Shape+Border shown inline.
    void DrawInlineButtonDefFlat(ZUIButtonDef btn, ref int stateTab)
    {
        Action inv = () => { btn.Invalidate(); EditorUtility.SetDirty(_sheet); RepaintShowcase(); Repaint(); };

        // State tab
        stateTab = ZUIToolbar(stateTab, new[] { "Normal", "Hover", "Active" });
        ZUI.VerticalSpace("V Section Rows");

        if (stateTab == 0)
        {
            DrawFillField(btn.normal);
        }
        else if (stateTab == 1)
        {
            btn.hoverBgOverride = ZUI.Toggle(btn.hoverBgOverride, "Override", "Toggle");
            if (btn.hoverBgOverride) DrawFillField(btn.hover);
        }
        else
        {
            btn.activeBgOverride = ZUI.Toggle(btn.activeBgOverride, "Override", "Toggle");
            if (btn.activeBgOverride) DrawFillField(btn.active);
        }

        ZUI.VerticalSpace("V Section Rows");

        // Shape
        DrawShapeEditor(btn.shape, 24);

        // Border — inline
        ZUI.VerticalSpace("V Section Rows");
        EditorGUI.BeginChangeCheck();
        DrawBorderDefField(btn.border, null, compact: true);
        if (EditorGUI.EndChangeCheck()) { btn.border.color.Invalidate(); inv(); }
    }

    // A visually distinct sub-section header — smaller, indented, lighter colour.
    // Used inside parent section header blocks to show hierarchy.
    bool SubsectionHeader(string title, string key)
    {
        bool expanded = GetFoldout(key);
        GUILayout.Space(1f);
        var rect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
        // Slightly lighter and taller indent than a normal section header
        EditorGUI.DrawRect(new Rect(rect.x + 8f, rect.y, rect.width - 8f, rect.height),
                           new Color(.18f, .18f, .22f, 1f));
        float cy = rect.y + (rect.height - 12f) * 0.5f;
        EditorGUI.LabelField(new Rect(rect.x + 16f, cy, 12f,              12f), expanded ? "▾" : "▸", SectionHeaderLabelStyle);
        EditorGUI.LabelField(new Rect(rect.x + 28f, cy, rect.width - 32f, 12f), title,                EditorStyles.miniLabel);
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            expanded = !expanded;
            SetFoldout(key, expanded);
            Event.current.Use();
            Repaint();
        }
        GUILayout.Space(2f);
        return expanded;
    }
}
