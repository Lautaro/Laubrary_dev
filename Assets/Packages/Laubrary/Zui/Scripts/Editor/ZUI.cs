// ZUI.cs

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== Active Style Sheet =================================================

    static ZUIStyleSheetAsset _activeSheet;

    // Fallback path used when EditorPrefs has no entry (e.g. first launch or branch switch).
    internal static string k_DefaultSheetPath => ZUIInstallPath + "/ZUIStyleSheet.asset";

    // ===== ZUI Internal Editor Sheet ==========================================
    // Used by ZUI's own editor windows (style editor, asset browser, etc.)
    // Separate from ActiveSheet so editing a consumer sheet doesn't break the editor.
    internal static string k_EditorSheetPath => ZUIInstallPath + "/SystemAssets/ZeditorZheet.asset";
    static ZUIStyleSheetAsset _editorSheet;

    // ===== ZUI Default Sheet ==================================================
    // The fallback sheet used when a ZUIWindow doesn't set its Sheet property,
    // or when a static ZUI.X(...) call is made without a sheet parameter.
    // Ships with ZUI; users can override ZUI.DefaultSheet at init to point at
    // their own sheet.
    internal static string k_DefaultSheetAssetPath => ZUIInstallPath + "/SystemAssets/ZUIDefaultSheet.asset";
    static ZUIStyleSheetAsset _defaultSheet;

    /// <summary>The fallback sheet for windows that don't declare one and for static
    /// draw calls that don't pass a sheet parameter. Lazy-loaded from a built-in asset
    /// the first time it's needed. Assign at init to override.</summary>
    public static ZUIStyleSheetAsset DefaultSheet
    {
        get
        {
            if (_defaultSheet != null) return _defaultSheet;
            _defaultSheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(k_DefaultSheetAssetPath);
            if (_defaultSheet == null)
            {
                // Asset missing — create an empty in-memory default so rendering still works.
                // Not persisted; the user should create the asset or assign DefaultSheet explicitly.
                _defaultSheet = ScriptableObject.CreateInstance<ZUIStyleSheetAsset>();
                _defaultSheet.name = "ZUIDefaultSheet (in-memory)";
            }
            return _defaultSheet;
        }
        set => _defaultSheet = value;
    }

    // ===== ZUI Install Path (auto-detected) ==================================
    static string _zuiInstallPath;
    /// <summary>The Assets-relative path where ZUI is installed (e.g. "Assets/ZUI" or "Assets/Plugins/ZUI").</summary>
    public static string ZUIInstallPath
    {
        get
        {
            if (_zuiInstallPath != null) return _zuiInstallPath;
            // Find this script's own path and derive the ZUI root from it
            var guids = AssetDatabase.FindAssets("t:MonoScript ZUI");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Look for the ZUI.cs that's in a Scripts/Editor folder
                if (path.EndsWith("/ZUI.cs") && path.Contains("/Scripts/Editor/"))
                {
                    // path = "Assets/.../ZUI/Scripts/Editor/ZUI.cs"
                    // We want "Assets/.../ZUI"
                    int idx = path.IndexOf("/Scripts/Editor/ZUI.cs");
                    if (idx > 0)
                    {
                        _zuiInstallPath = path.Substring(0, idx);
                        return _zuiInstallPath;
                    }
                }
            }
            // Fallback
            _zuiInstallPath = "Assets/ZUI";
            return _zuiInstallPath;
        }
    }

    // ===== Consumer Sheet Registry ============================================
    // Consumer sheets are auto-discovered on domain reload by scanning all ZUIStyleSheetAsset
    // assets for a non-empty consumerName. Windows reference sheets by name via ConsumerSheetName.
    static readonly Dictionary<string, ZUIStyleSheetAsset> _consumerSheets = new();

    /// <summary>The consumer name used by ZUI's own editor windows (Style Editor, Texture Editor, etc.).</summary>
    public const string EditorSheetConsumerName = "ZUIEditor";

    [UnityEditor.InitializeOnLoadMethod]
    static void AutoDiscoverConsumerSheets()
    {
        _consumerSheets.Clear();

        // Always register the ZUI editor sheet
        var editorSheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(k_EditorSheetPath);
        if (editorSheet != null)
            _consumerSheets[EditorSheetConsumerName] = editorSheet;

        // Auto-discover all sheets with a consumer name
        var guids = AssetDatabase.FindAssets("t:ZUIStyleSheetAsset");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var sheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(path);
            if (sheet != null && !string.IsNullOrEmpty(sheet.consumerName))
                _consumerSheets[sheet.consumerName] = sheet;
        }
    }

    /// <summary>Register a named consumer sheet. Call from [InitializeOnLoad] or OnEnable.</summary>
    public static void RegisterConsumerSheet(string name, ZUIStyleSheetAsset sheet)
    {
        if (sheet == null || string.IsNullOrEmpty(name)) return;
        _consumerSheets[name] = sheet;
    }

    /// <summary>Unregister a consumer sheet.</summary>
    public static void UnregisterConsumerSheet(string name)
    {
        if (!string.IsNullOrEmpty(name)) _consumerSheets.Remove(name);
    }

    /// <summary>Returns the names of all registered consumer sheets.</summary>
    public static string[] GetRegisteredConsumerNames()
    {
        var names = new string[_consumerSheets.Count];
        _consumerSheets.Keys.CopyTo(names, 0);
        return names;
    }

    /// <summary>Get a registered consumer sheet by name. Returns null if not found.</summary>
    public static ZUIStyleSheetAsset GetConsumerSheet(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        _consumerSheets.TryGetValue(name, out var sheet);
        return sheet;
    }

    /// <summary>
    /// Activates a named consumer sheet for the duration of the returned scope.
    /// Usage: using (ZUI.UseSheet("Zounds")) { /* draw */ }
    /// </summary>
    public static SheetScope UseSheet(string consumerName)
    {
        var sheet = GetConsumerSheet(consumerName);
        return new SheetScope(sheet ?? ActiveSheet);
    }

    /// <summary>
    /// Activates a specific sheet for the duration of the returned scope.
    /// </summary>
    public static SheetScope UseSheet(ZUIStyleSheetAsset sheet)
    {
        return new SheetScope(sheet ?? ActiveSheet);
    }

    public readonly struct SheetScope : IDisposable
    {
        private readonly ZUIStyleSheetAsset _previous;
        public SheetScope(ZUIStyleSheetAsset sheet)
        {
            _previous = _activeSheet;
            _activeSheet = sheet;
            ZUIStyleSheetAsset.Active = sheet;
        }
        public void Dispose() { _activeSheet = _previous; ZUIStyleSheetAsset.Active = _previous; }
    }

    /// <summary>
    /// The internal style sheet used by ZUI's own editor windows.
    /// Auto-creates with defaults if the asset doesn't exist.
    /// </summary>
    public static ZUIStyleSheetAsset EditorSheet
    {
        get
        {
            if (_editorSheet != null) return _editorSheet;
            _editorSheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(k_EditorSheetPath);
            if (_editorSheet == null)
                _editorSheet = CreateEditorSheet();
            return _editorSheet;
        }
    }

    static ZUIStyleSheetAsset CreateEditorSheet()
    {
        // Ensure directory exists (recursive)
        string dir = System.IO.Path.GetDirectoryName(k_EditorSheetPath).Replace('\\', '/');
        EnsureFolderExists(dir);

        var sheet = ScriptableObject.CreateInstance<ZUIStyleSheetAsset>();
        // Start with an empty sheet — styles are added by the user in the Style Editor.

        // Editor icon aliases — map semantic names to Phosphor icons in SystemAssets. Targets are
        // embedded-icon sentinels (zui://icons/foo.png), resolved off disk by ZUIAssetLibrary rather
        // than through the AssetDatabase (the icons are no longer Unity assets).
        string sysIcons = ZUIAssetLibrary.EmbeddedIconScheme.TrimEnd('/');
        sheet.iconAliases = new System.Collections.Generic.List<ZUIAssetAlias>
        {
            // Style list controls
            new ZUIAssetAlias("move-up",       sysIcons + "/arrow-up.png"),
            new ZUIAssetAlias("move-down",     sysIcons + "/arrow-down.png"),
            new ZUIAssetAlias("flash",         sysIcons + "/star.png"),
            new ZUIAssetAlias("duplicate",     sysIcons + "/copy.png"),
            new ZUIAssetAlias("copy",          sysIcons + "/copy-simple.png"),
            new ZUIAssetAlias("paste",         sysIcons + "/clipboard-text.png"),
            new ZUIAssetAlias("delete",        sysIcons + "/trash.png"),
            // Toolbar
            new ZUIAssetAlias("add",           sysIcons + "/plus-circle.png"),
            new ZUIAssetAlias("remove",        sysIcons + "/minus-circle.png"),
            // Section toggles
            new ZUIAssetAlias("expand",        sysIcons + "/caret-down.png"),
            new ZUIAssetAlias("collapse",      sysIcons + "/caret-right.png"),
            // Preview
            new ZUIAssetAlias("eye",           sysIcons + "/eye.png"),
            new ZUIAssetAlias("eye-closed",    sysIcons + "/eye-closed.png"),
            // Settings
            new ZUIAssetAlias("settings",      sysIcons + "/sliders-horizontal.png"),
            new ZUIAssetAlias("palette",       sysIcons + "/palette.png"),
            new ZUIAssetAlias("folder",        sysIcons + "/folder-simple.png"),
            // Misc
            new ZUIAssetAlias("sort-asc",      sysIcons + "/sort-ascending.png"),
            new ZUIAssetAlias("sort-desc",     sysIcons + "/sort-descending.png"),
            new ZUIAssetAlias("search",        sysIcons + "/list-magnifying-glass.png"),
            new ZUIAssetAlias("warning",       sysIcons + "/warning.png"),
            // Corner toggles (angle icon rotated per corner)
            new ZUIAssetAlias("corner-tl",     sysIcons + "/angle.png",   0f),
            new ZUIAssetAlias("corner-tr",     sysIcons + "/angle.png",  90f),
            new ZUIAssetAlias("corner-br",     sysIcons + "/angle.png", 180f),
            new ZUIAssetAlias("corner-bl",     sysIcons + "/angle.png", 270f),
        };

        AssetDatabase.CreateAsset(sheet, k_EditorSheetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZUI] Created editor style sheet at {k_EditorSheetPath} with {sheet.iconAliases.Count} icon aliases");
        return sheet;
    }

    /// <summary>Recursively creates asset folders if they don't exist.</summary>
    public static void EnsureFolderExists(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    /// <summary>Sets the default active sheet. Use from bootstrap code to establish the fallback sheet.</summary>
    public static void SetDefaultActiveSheet(ZUIStyleSheetAsset sheet)
    {
        if (sheet != null) { _activeSheet = sheet; ZUIStyleSheetAsset.Active = sheet; }
    }

    public static ZUIStyleSheetAsset ActiveSheet
    {
        get
        {
            if (_activeSheet != null) return _activeSheet;
            var path = EditorPrefs.GetString("ZUIStyleEditor_LastSheet", "");
            if (string.IsNullOrEmpty(path)) path = k_DefaultSheetPath;
            _activeSheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(path);
            ZUIStyleSheetAsset.Active = _activeSheet;
            return _activeSheet;
        }
        internal set { _activeSheet = value; ZUIStyleSheetAsset.Active = value; }
    }

    // ===== Skin API ==========================================================

    /// <summary>Returns the names of all skins on the active sheet.</summary>
    public static string[] GetSkinNames() => ActiveSheet?.GetSkinNames() ?? new string[0];

    /// <summary>Returns the name of the active skin, or null if no skin is active.</summary>
    public static string ActiveSkinName => ActiveSheet?.ActiveSkin?.name;

    /// <summary>Sets the active skin by name. Pass null or "" to clear (use base palette).</summary>
    public static void SetActiveSkin(string skinName)
    {
        var sheet = ActiveSheet;
        if (sheet == null) return;
        sheet.SetActiveSkin(skinName);
        InvalidateAllStyles();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(sheet);
#endif
    }

    /// <summary>Invalidates all cached styles so palette color changes are reflected.</summary>
    public static void InvalidateAllStyles()
    {
        var sheet = ActiveSheet;
        if (sheet == null) return;
        foreach (var b in sheet.buttons) b.Invalidate();
        foreach (var b in sheet.boxes)   b.Invalidate();
        foreach (var s in sheet.sliders) s.Invalidate();
        foreach (var t in sheet.textStyles) t.Invalidate();
        if (sheet.globalButton != null) sheet.globalButton.Invalidate();
        if (sheet.globalBox != null)    sheet.globalBox.Invalidate();
    }

    // ===== Icon library ======================================================

    /// <summary>Resolves an icon by alias name, system path, or filename.</summary>
    /// <summary>Resolves an icon by alias name, system path, or filename.</summary>
    public static Texture2D FindIcon(string id) => ZUIAssetLibrary.FindIcon(id);

    /// <summary>Resolves an icon by name, also returning alias rotation.</summary>
    public static Texture2D FindIcon(string id, out float rotation) => ZUIAssetLibrary.FindIcon(id, out rotation);

    /// <summary>Resolves a font by alias name, system path, or filename. Respects skin overrides.</summary>
    public static Font FindFont(string name) => ZUIAssetLibrary.FindFont(name);

    /// <summary>Returns the resolved default font (sheet default → ZUI default → Unity default).</summary>
    public static Font DefaultFont => ZUIAssetLibrary.ResolveDefaultFont();

    // ===== Palette color lookup ==============================================

    /// <summary>
    /// Returns a palette color by name. The sheet's FindPaletteColor handles
    /// skin-first resolution internally.
    /// </summary>
    public static ZUIPaletteColor FindPaletteColor(string name)
        => ActiveSheet?.FindPaletteColor(name);

    /// <summary>
    /// Returns a palette color from the active sheet by name.
    /// Falls back to <paramref name="fallback"/> when no sheet is loaded or the entry is not found.
    /// </summary>
    public static Color PaletteColor(string name, Color fallback)
    {
        var entry = FindPaletteColor(name);
        return entry != null ? entry.color : fallback;
    }

    /// <summary>
    /// Paints a solid rectangle using a named palette color — the cheap
    /// alternative to opening a Box when you just need a background fill.
    /// Layout-free: safe inside tight render loops (e.g. per-row tracker
    /// backgrounds). Silently does nothing when not in a Repaint event.
    /// </summary>
    public static void FillRect(Rect rect, string paletteColorName)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        var entry = FindPaletteColor(paletteColorName);
        if (entry == null) return;
        EditorGUI.DrawRect(rect, entry.color);
    }

    /// <summary>
    /// Paints a solid rectangle using a named palette color, falling back to
    /// <paramref name="fallback"/> when the palette entry is missing.
    /// </summary>
    public static void FillRect(Rect rect, string paletteColorName, Color fallback)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(rect, PaletteColor(paletteColorName, fallback));
    }

    // ===== Style flash =======================================================
    // Flashes an overlay border on every control that uses a named style def.
    // Call StartFlash(name) to begin; controls pick it up each Repaint automatically.

    public enum FlashDefType { Button, Box, Slider, Text }

    private static string              _flashStyleName;
    private static FlashDefType        _flashDefType;
    private static double              _flashEndTime;
    private static ZUIStyleSheetAsset  _flashSheet; // which sheet the flash belongs to

    /// <summary>When true, flash overlay drawing is suppressed.
    /// Used to mark the style editor's inspector/preview scope — controls there are
    /// editing instances, not real consumers of the style.</summary>
    public static bool SuppressFlash { get; set; }

    // Flash params read from the active sheet; these are the compile-time fallbacks.
    private const int   k_FallbackFlashCount    = 8;
    private const float k_FallbackFlashInterval = 0.12f;

    private static float ActiveFlashInterval => ActiveSheet?.flashInterval ?? k_FallbackFlashInterval;
    private static int   ActiveFlashCount    => ActiveSheet?.flashCount    ?? k_FallbackFlashCount;

    public static void StartFlash(string styleName, FlashDefType type)
    {
        _flashStyleName = styleName;
        _flashDefType   = type;
        _flashSheet     = ActiveSheet;
        _flashEndTime   = EditorApplication.timeSinceStartup + ActiveFlashCount * ActiveFlashInterval;
        EnsureAnimUpdateRunning();
        ForwardToRuntimeFlash(styleName, type, ActiveSheet);
    }

    public static void StartFlash(string styleName, FlashDefType type, ZUIStyleSheetAsset sheet)
    {
        _flashStyleName = styleName;
        _flashDefType   = type;
        _flashSheet     = sheet;
        _flashEndTime   = EditorApplication.timeSinceStartup + ActiveFlashCount * ActiveFlashInterval;
        EnsureAnimUpdateRunning();
        ForwardToRuntimeFlash(styleName, type, sheet);
    }

    // So a flash triggered here also highlights a Play-mode HUD rendered via ZUISheet.DrawBox/Button
    // (see ZUIFlash.cs) — Slider/Text have no runtime rendering counterpart today, so those no-op.
    static void ForwardToRuntimeFlash(string styleName, FlashDefType type, ZUIStyleSheetAsset sheet)
    {
        ZUIFlashKind? kind = type == FlashDefType.Button ? ZUIFlashKind.Button
                           : type == FlashDefType.Box    ? ZUIFlashKind.Box
                           : (ZUIFlashKind?)null;
        if (kind.HasValue) ZUIFlash.Start(styleName, kind.Value, sheet, ActiveFlashInterval, ActiveFlashCount);
    }

    internal static void DrawFlashOverlayIfNeeded(Rect rect, string defName, int cornerRadius, FlashDefType type)
    {
        if (SuppressFlash) return;
        if (string.IsNullOrEmpty(_flashStyleName) || defName != _flashStyleName || type != _flashDefType) return;
        // Sheet guard: only flash controls belonging to the same sheet as the flash target.
        if (_flashSheet != null && _activeSheet != null && _flashSheet != _activeSheet) return;
        double t = EditorApplication.timeSinceStartup;
        if (t > _flashEndTime) { _flashStyleName = null; return; }

        float interval = ActiveFlashInterval;
        int phase = (int)((t % (interval * 2)) / interval);
        var color = phase == 0 ? Color.white : Color.black;

        var prev = GUI.color;
        GUI.color = color;
        float bw = 2f;
        GUI.DrawTexture(new Rect(rect.x,              rect.y,               rect.width, bw),          EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x,              rect.yMax - bw,       rect.width, bw),          EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x,              rect.y,               bw,         rect.height), EditorGUIUtility.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - bw,      rect.y,               bw,         rect.height), EditorGUIUtility.whiteTexture);
        GUI.color = prev;
    }

    // ===== Spacing ===========================================================

    private const float k_FallbackVerticalSpacing   = 6f;
    private const float k_FallbackHorizontalSpacing = 8f;

    private static bool   _spaceFlashActive;
    private static double _spaceFlashEndTime;

    /// <summary>Triggers the animated spacing overlay on every ZUI.VerticalSpace() / HorizontalSpace() call.</summary>
    public static void StartVerticalSpaceFlash()
    {
        _spaceFlashActive  = true;
        _spaceFlashEndTime = EditorApplication.timeSinceStartup + ActiveFlashCount * ActiveFlashInterval;
        EnsureAnimUpdateRunning();
    }

    // Called after GUILayout.Space() — uses GetLastRect() to draw on top of the
    // already-consumed space, so zero extra layout height is added.
    private static void DrawSpaceOverlay(float scale, string scaleName, bool isHorizontal)
    {
        if (!_spaceFlashActive) return;
        double t = EditorApplication.timeSinceStartup;
        if (t > _spaceFlashEndTime) { _spaceFlashActive = false; return; }
        if (Event.current.type != EventType.Repaint) return;

        var spaceRect  = GUILayoutUtility.GetLastRect();
        float interval = ActiveFlashInterval;
        int   phase    = (int)((t % (interval * 2)) / interval);
        var lineColor  = phase == 0 ? Color.white : Color.black;
        var labelColor = phase == 0 ? Color.black  : Color.white;

        float px = isHorizontal ? spaceRect.width : spaceRect.height;
        string label = scaleName != null
            ? $"\"{scaleName}\" ×{scale:F2}  ({px:F0}px)"
            : scale == 1f
                ? $"×1  ({px:F0}px)"
                : $"×{scale:F2}  ({px:F0}px)";

        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal    = { textColor = labelColor },
            fontStyle = FontStyle.Bold,
            fontSize  = 9,
            padding   = new RectOffset(2, 2, 0, 0),
        };
        Vector2 labelSize = labelStyle.CalcSize(new GUIContent(label));

        var prev = GUI.color;
        if (isHorizontal)
        {
            float lineW = 2f;
            float lineX = spaceRect.x + (spaceRect.width - lineW) * 0.5f;
            GUI.color = lineColor;
            GUI.DrawTexture(new Rect(lineX, spaceRect.y, lineW, spaceRect.height), EditorGUIUtility.whiteTexture);
            float labelX = lineX - labelSize.x * 0.5f;
            float labelY = spaceRect.y + (spaceRect.height - labelSize.y) * 0.5f;
            GUI.DrawTexture(new Rect(labelX - 2f, labelY, labelSize.x + 4f, labelSize.y), EditorGUIUtility.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(labelX, labelY, labelSize.x, labelSize.y), label, labelStyle);
        }
        else
        {
            float lineH = 2f;
            float lineY = spaceRect.y + (spaceRect.height - lineH) * 0.5f;
            GUI.color = lineColor;
            GUI.DrawTexture(new Rect(spaceRect.x, lineY, spaceRect.width, lineH), EditorGUIUtility.whiteTexture);
            float labelX = spaceRect.x + spaceRect.width * 0.5f - labelSize.x * 0.5f;
            float labelY = lineY - labelSize.y;
            GUI.DrawTexture(new Rect(labelX - 2f, labelY, labelSize.x + 4f, labelSize.y), EditorGUIUtility.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(labelX, labelY, labelSize.x, labelSize.y), label, labelStyle);
        }
        GUI.color = prev;
    }

    // ── Vertical ─────────────────────────────────────────────────────────────

    /// <summary>Inserts the standard vertical gap, as configured in the active ZUI Style Sheet.</summary>
    public static void VerticalSpace()
    {
        GUILayout.Space(ActiveSheet?.verticalSpacing ?? k_FallbackVerticalSpacing);
        DrawSpaceOverlay(1f, null, false);
    }

    /// <summary>Inserts a scaled multiple of the standard vertical spacing.</summary>
    public static void VerticalSpace(float scale)
    {
        GUILayout.Space((ActiveSheet?.verticalSpacing ?? k_FallbackVerticalSpacing) * scale);
        DrawSpaceOverlay(scale, null, false);
    }

    /// <summary>Inserts vertical spacing using a named scale from the active style sheet.</summary>
    public static void VerticalSpace(string scaleName)
    {
        float scale = ActiveSheet?.FindSpacingScale(scaleName) ?? 1f;
        GUILayout.Space((ActiveSheet?.verticalSpacing ?? k_FallbackVerticalSpacing) * scale);
        DrawSpaceOverlay(scale, scaleName, false);
    }

    // ── Horizontal ───────────────────────────────────────────────────────────

    /// <summary>Inserts the standard horizontal gap, as configured in the active ZUI Style Sheet.</summary>
    public static void HorizontalSpace()
    {
        GUILayout.Space(ActiveSheet?.horizontalSpacing ?? k_FallbackHorizontalSpacing);
        DrawSpaceOverlay(1f, null, true);
    }

    /// <summary>Inserts a scaled multiple of the standard horizontal spacing.</summary>
    public static void HorizontalSpace(float scale)
    {
        GUILayout.Space((ActiveSheet?.horizontalSpacing ?? k_FallbackHorizontalSpacing) * scale);
        DrawSpaceOverlay(scale, null, true);
    }

    /// <summary>Inserts horizontal spacing using a named scale from the active style sheet.</summary>
    public static void HorizontalSpace(string scaleName)
    {
        float scale = ActiveSheet?.FindSpacingScale(scaleName) ?? 1f;
        GUILayout.Space((ActiveSheet?.horizontalSpacing ?? k_FallbackHorizontalSpacing) * scale);
        DrawSpaceOverlay(scale, scaleName, true);
    }

    // ── Label width helpers ─────────────────────────────────────────────────
    const float k_FallbackLabelWide   = 82f;
    const float k_FallbackLabelNarrow = 36f;
    const float k_FallbackInputMin    = 56f;
    const float k_FallbackControlH    = 18f;

    public static float LabelWidthWide   => ActiveSheet?.labelWidthWide   ?? k_FallbackLabelWide;
    public static float LabelWidthNarrow => ActiveSheet?.labelWidthNarrow ?? k_FallbackLabelNarrow;
    public static float InputFieldMinWidth => ActiveSheet?.inputFieldMinWidth ?? k_FallbackInputMin;

    /// <summary>Standard control height. All inline controls should use this for visual consistency.</summary>
    public static float ControlHeight => ActiveSheet?.controlHeight ?? k_FallbackControlH;

    /// <summary>GUILayoutOption for standard control height.</summary>
    public static GUILayoutOption ControlH() => GUILayout.Height(ControlHeight);

    // ── Label style for horizontal rows ──────────────────────────────────────
    // Vertically centered so labels align with taller controls in the same row.
    static GUIStyle _rowLabelStyle;

    /// <summary>Label style with MiddleLeft alignment for use in horizontal rows.
    /// Ensures labels vertically center-align with adjacent controls.</summary>
    public static GUIStyle RowLabelStyle
    {
        get
        {
            if (_rowLabelStyle == null)
            {
                _rowLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                };
            }
            return _rowLabelStyle;
        }
    }

    public static GUILayoutOption LabelWide()   => GUILayout.Width(LabelWidthWide);
    public static GUILayoutOption LabelNarrow() => GUILayout.Width(LabelWidthNarrow);
    public static GUILayoutOption InputMin()    => GUILayout.MinWidth(InputFieldMinWidth);

    // ── Shaped toggle row ──────────────────────────────────────────────────────
    // A row of toggles with cohesive rounded corners: first=Left, last=Right, middle=Square.
    // Returns a bitmask of which toggles are on.

    /// <summary>
    /// Draws a horizontal row of toggle buttons with shaped corners (rounded edges, square middle).
    /// Each toggle is independent (multi-select, not radio). Returns updated values array.
    /// </summary>
    public static bool[] ToggleRow(bool[] values, string[] labels, string style = Style.Default,
                                    params GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < values.Length && i < labels.Length; i++)
        {
            var mask = values.Length == 1 ? ZUICornerMask.All
                     : i == 0            ? ZUICornerMask.Left
                     : i == values.Length - 1 ? ZUICornerMask.Right
                     :                     ZUICornerMask.Square;
            values[i] = Toggle(values[i], labels[i], style, mask, options);
        }
        GUILayout.EndHorizontal();
        return values;
    }

    /// <summary>
    /// Draws a horizontal row of toggle buttons with shaped corners, using GUIContent (for icons/tooltips).
    /// </summary>
    public static bool[] ToggleRow(bool[] values, GUIContent[] labels, string style = Style.Default,
                                    params GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < values.Length && i < labels.Length; i++)
        {
            var mask = values.Length == 1 ? ZUICornerMask.All
                     : i == 0            ? ZUICornerMask.Left
                     : i == values.Length - 1 ? ZUICornerMask.Right
                     :                     ZUICornerMask.Square;
            values[i] = Toggle(values[i], labels[i], style, mask, options);
        }
        GUILayout.EndHorizontal();
        return values;
    }

    // ── Backwards-compatible aliases ──────────────────────────────────────────
    /// <inheritdoc cref="VerticalSpace()"/>
    public static void RowSpace() => VerticalSpace();
    /// <inheritdoc cref="VerticalSpace(float)"/>
    public static void RowSpace(float scale) => VerticalSpace(scale);

    // ===== Box API — named string style =======================================
    // Looks up a ZUIBoxDef by name from the active sheet. Falls back to the
    // sheet's "Default" entry, then to SectionStyleRegistry's hardcoded default.

    /// <summary>
    /// <paramref name="tooltip"/> (optional): when set, draws a <see cref="HelpIcon"/> sharing the SAME row
    /// as the title text (right-aligned via FlexibleSpace), not a row of its own below it — see
    /// ui-layout-rules.md's "Labeling" section for why a hover icon explaining an area must live on that
    /// area's own title row. Preserves the sheet-resolved title text style either way (unlike hand-rolling a
    /// plain <c>GUILayout.Label</c> title alongside the icon at the call site).
    /// </summary>
    public static BoxScope Box(string title, string styleName = ZUIStyle.Default, string tooltip = null)
    {
        var sheet = ActiveSheet;
        if (sheet != null)
        {
            var def = sheet.FindBox(styleName);
            if (def != null) { _pendingBoxStyle = styleName; _pendingBoxStyleSet = true; return new BoxScope(title, def, tooltip); }
        }
        return new BoxScope(title, SectionStyleRegistry.Default, tooltip);
    }

    public static BoxScope Box() => Box(null, ZUIStyle.Default);

    // ===== Box API — ZUIBoxDef (named style def) ==============================

    public static BoxScope Box(ZUIBoxDef def)           => new BoxScope(null,  def);
    /// <inheritdoc cref="Box(string, string, string)"/>
    public static BoxScope Box(string title, ZUIBoxDef def, string tooltip = null) => new BoxScope(title, def, tooltip);

    /// <inheritdoc cref="Box(string, string)"/>
    public static BoxScope BoxNamed(string styleName)
    {
        var def = ActiveSheet?.FindBox(styleName);
        if (def != null) return new BoxScope(null, def);
        return new BoxScope(null, SectionStyleRegistry.Default);
    }

    // ===== AreaBox ============================================================

    public static IDisposable AreaBox(Rect rect, string title = null, string styleName = ZUIStyle.Default)
    {
        GUILayout.BeginArea(rect);
        return new AreaBoxScope(Box(title, styleName));
    }

    public static IDisposable AreaBox(Rect rect, ZUIBoxDef def)
    {
        GUILayout.BeginArea(rect);
        return new AreaBoxScope(Box(def));
    }

    public static IDisposable AreaBox(Rect rect, string title, ZUIBoxDef def)
    {
        GUILayout.BeginArea(rect);
        return new AreaBoxScope(Box(title, def));
    }

    // ===== BoxScope ===========================================================

    public readonly struct BoxScope : IDisposable
    {
        // ZUIStyle path — existing SectionStyle
        public readonly GUIStyle ContentStyle;
        private readonly bool    _hasContext;

        public BoxScope(string title, SectionStyle style, string tooltip = null)
        {
            ContentStyle = null;
            _hasContext  = false;
            EditorGUILayout.BeginVertical(style.GuiStyle);

            if (!string.IsNullOrEmpty(title))
            {
                DrawTitleRow(title, style.LabelStyle, tooltip);
                GUILayout.Space(2);
            }
        }

        // ZUIBoxDef path — DrawRect background, no texture

        public BoxScope(string title, ZUIBoxDef def, string tooltip = null)
        {
            ContentStyle = def.GetContentStyle();
            _hasContext  = true;
            ZUI.PushBoxContext(def);
            var rect = EditorGUILayout.BeginVertical(def.GetLayoutStyle());
            def.DrawBackground(rect);

            if (!string.IsNullOrEmpty(title))
            {
                var ls = new GUIStyle(EditorStyles.boldLabel);
                def.GetResolvedTitleText().Apply(ls, def.ownerSheet);
                DrawTitleRow(title, ls, tooltip);
                GUILayout.Space(2);
            }
        }

        // Shared by both ctors: plain title label when there's no tooltip (unchanged layout — a single
        // full-width LabelField, not a row), or title + a right-aligned HelpIcon sharing ONE row when there
        // is. Never a separate row below the title for the icon — that's the anti-pattern this exists to fix.
        static void DrawTitleRow(string title, GUIStyle labelStyle, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip)) { EditorGUILayout.LabelField(title, labelStyle); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, labelStyle, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                ZUI.HelpIcon(tooltip);
            }
        }

        public void Dispose()
        {
            EditorGUILayout.EndVertical();
            if (_hasContext) ZUI.PopBoxContext();
        }
    }

    // ===== FoldoutBox API =====================================================
    //
    // A ZUI.Box whose content area animates in/out via AnimatedFoldout.
    // The box header (title + background) always draws; the body folds.
    //
    // Usage:
    //   using (var box = ZUI.FoldoutBox("Title", "StyleName", isOpen))
    //   {
    //       if (box.visible)
    //       {
    //           // draw content
    //       }
    //   }
    //
    // No field declaration or unique key needed — managed internally.

    private static readonly Dictionary<string, AnimatedFoldout> _foldoutBoxCache
        = new Dictionary<string, AnimatedFoldout>();

    /// <summary>
    /// Opens a ZUI box with animated foldout content, using a named style from the active sheet.
    /// The header always draws; the content animates in/out based on <paramref name="open"/>.
    /// </summary>
    public static FoldoutBoxScope FoldoutBox(string title, string styleName, bool open)
    {
        var def = ActiveSheet?.FindBox(styleName);
        if (def == null) def = ActiveSheet?.FindBox("Default");
        return new FoldoutBoxScope(title, def, open, styleName);
    }

    /// <summary>
    /// Opens a ZUI box with animated foldout content, using a ZUIBoxDef directly.
    /// </summary>
    public static FoldoutBoxScope FoldoutBox(string title, ZUIBoxDef def, bool open, string key = null)
    {
        return new FoldoutBoxScope(title, def, open, key ?? title ?? "FoldoutBox");
    }


    public struct FoldoutBoxScope : IDisposable
    {
        /// <summary>True when the content area should be drawn.</summary>
        public readonly bool visible;

        private readonly bool _hasBoxDef;
        private readonly bool _boxOpened;
        private readonly FoldoutScope _foldoutScope;

        public FoldoutBoxScope(string title, ZUIBoxDef def, bool open, string key)
        {
            // Get or create the cached AnimatedFoldout for this key.
            string foldoutKey = "FoldoutBox_" + key;
            if (!_foldoutBoxCache.TryGetValue(foldoutKey, out var foldout))
            {
                foldout = new AnimatedFoldout(foldoutKey);
                _foldoutBoxCache[foldoutKey] = foldout;
            }

            _hasBoxDef = def != null;

            // Foldout wraps the box — the entire box animates in/out.
            _foldoutScope = foldout.Begin(open);
            visible = _foldoutScope.visible;

            // Only open the box shell when the foldout has content to show.
            _boxOpened = _foldoutScope.visible;
            if (_boxOpened)
            {
                if (_hasBoxDef)
                {
                    PushBoxContext(def);
                    var rect = EditorGUILayout.BeginVertical(def.GetLayoutStyle());
                    def.DrawBackground(rect);

                    if (!string.IsNullOrEmpty(title))
                    {
                        var ls = new GUIStyle(EditorStyles.boldLabel);
                        def.GetResolvedTitleText().Apply(ls, def.ownerSheet);
                        EditorGUILayout.LabelField(title, ls);
                        GUILayout.Space(2);
                    }
                }
                else
                {
                    EditorGUILayout.BeginVertical();
                    if (!string.IsNullOrEmpty(title))
                    {
                        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                        GUILayout.Space(2);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_boxOpened)
            {
                EditorGUILayout.EndVertical();
                if (_hasBoxDef) PopBoxContext();
            }
            _foldoutScope.Dispose();
        }
    }

    // ===== AreaBoxScope =======================================================

    private readonly struct AreaBoxScope : IDisposable
    {
        private readonly IDisposable _box;

        public AreaBoxScope(IDisposable box) { _box = box; }

        public void Dispose()
        {
            _box.Dispose();
            GUILayout.EndArea();
        }
    }

    // ===== ZUIStyle style-name constants ======================================
    // Matches the const-string pattern used by Style (buttons) and SliderStyle.
    // Consumers pass raw strings; this class exists for discoverability of the
    // single built-in default. Custom box styles use raw string names.

    public static class ZUIStyle
    {
        public const string Default = "Default";
    }

    // Translates a ZUICornerMask into (roundTL, roundTR, roundBL, roundBR).
    // When mask is None, falls back to the def's own per-corner flags.
    // Single implementation lives in ZUICore (Runtime) so moved defs can share it.
    internal static (bool tl, bool tr, bool bl, bool br) ResolveCornerMask(ZUIButtonDef def, ZUICornerMask mask)
        => ZUICore.ResolveCornerMask(def, mask);

    // Converts resolved corner booleans + radius into a Vector4 for GPU draw.
    // Unity GUI.DrawTexture borderRadius order: x=TL, y=TR, z=BR, w=BL
    internal static Vector4 CornerMaskToVector(bool tl, bool tr, bool bl, bool br, float r)
        => new Vector4(tl ? r : 0f, tr ? r : 0f, br ? r : 0f, bl ? r : 0f);

    // ===== SectionStyle =======================================================

    public class SectionStyle
    {
        public GUIStyle GuiStyle;
        public GUIStyle LabelStyle;
    }

    // ===== SectionStyleRegistry ===============================================
    // Hardcoded fallback used when no sheet is loaded or the requested name is
    // missing from the sheet. Only holds a single "Default" style — consumers
    // must define any other styles they want in a sheet.

    static class SectionStyleRegistry
    {
        static SectionStyle _default;

        public static SectionStyle Default
        {
            get
            {
                if (_default == null || _default.GuiStyle.normal.background == null)
                    _default = Create(new Color(.8f, 0.8f, 1f, 0.12f));
                return _default;
            }
        }

        static SectionStyle Create(Color bg)
        {
            var tex = MakeTex(bg);

            var gui = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin  = new RectOffset(4, 4, 4, 4),
            };
            gui.normal.background = tex;

            return new SectionStyle
            {
                GuiStyle   = gui,
                LabelStyle = EditorStyles.boldLabel,
            };
        }

        internal static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
}

// ZUICornerMask moved to ZUICore.cs (Runtime assembly) so style defs can reference it there.
