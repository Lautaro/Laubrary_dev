// ZUIStyleSheetAsset.cs
// Extracted into its own file so Unity can resolve the ScriptableObject type reliably.

using System.Collections.Generic;
using UnityEngine;

// ── Missing-style registry ─────────────────────────────────────────────────────
// Records every lookup that fell back to a default because the named style didn't exist.
// Lives as a static class so it persists across repaints but resets on domain reload.

public static class ZUIMissingStyleRegistry
{
    public enum EntryType { Button, Box, Text, Slider, Envelope, SpacingScale }

    public struct Entry
    {
        public EntryType type;
        public string    requestedName;
        public int       hitCount;
        public ZUIStyleSheetAsset sheet; // which sheet the miss came from
    }

    // key = "SheetID:Button:ZoundBtn" etc.
    private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

    public static IEnumerable<Entry> Entries => _entries.Values;
    public static int Count => _entries.Count;

    /// <summary>Returns only entries for the specified sheet.</summary>
    public static IEnumerable<Entry> EntriesForSheet(ZUIStyleSheetAsset sheet)
    {
        foreach (var e in _entries.Values)
            if (e.sheet == sheet) yield return e;
    }

    public static void Record(EntryType type, string name)
    {
        var sheet = ZUIStyleSheetAsset.Active;
        string sheetId = sheet != null ? sheet.GetInstanceID().ToString() : "null";
        string key = sheetId + ":" + type + ":" + name;
        if (_entries.TryGetValue(key, out var e))
        {
            e.hitCount++;
            _entries[key] = e;
        }
        else
        {
            _entries[key] = new Entry { type = type, requestedName = name, hitCount = 1, sheet = sheet };
        }
    }

    public static void Clear() => _entries.Clear();

    public static void ClearForSheet(ZUIStyleSheetAsset sheet)
    {
        var toRemove = new List<string>();
        foreach (var kvp in _entries)
            if (kvp.Value.sheet == sheet) toRemove.Add(kvp.Key);
        foreach (var k in toRemove) _entries.Remove(k);
    }

    // Called when a style is renamed or added, so stale entries can be removed.
    public static void Remove(EntryType type, string name)
    {
        // Remove from all sheets
        var toRemove = new List<string>();
        foreach (var kvp in _entries)
            if (kvp.Value.type == type && kvp.Value.requestedName == name)
                toRemove.Add(kvp.Key);
        foreach (var k in toRemove) _entries.Remove(k);
    }
}

[CreateAssetMenu(menuName = "ZUI/Zheet", fileName = "ZUIStyleSheet")]
public class ZUIStyleSheetAsset : ScriptableObject
{
    /// <summary>Runtime holder for the "active" sheet. In the editor the ZUI static mirrors
    /// its active sheet here; at runtime callers (e.g. ZUISheet.DrawBox) set it directly.
    /// Used for palette / icon / missing-style resolution when no ownerSheet is available.</summary>
    public static ZUIStyleSheetAsset Active;

    /// <summary>Consumer name for this sheet. Windows reference this name via ConsumerSheetName.
    /// Multiple windows can share the same sheet. Leave empty for sheets not used as consumer sheets.</summary>
    public string consumerName = "";

    // ── Version tracking ────────────────────────────────────────────────────
    // Non-serialized counter that increments whenever the sheet is modified at runtime.
    // ZUIWindow checks this each frame and invalidates cached GUIStyles on mismatch.
    [System.NonSerialized] private int _version;

    /// <summary>Current modification version. Incremented by BumpVersion().</summary>
    public int Version => _version;

    /// <summary>Call after modifying any def on this sheet. Invalidates all cached GUIStyles,
    /// re-wires ownerSheet, and increments the version so ZUIWindow-based windows pick up changes.</summary>
    public void BumpVersion()
    {
        _version++;
        WireOwnerSheet();
        foreach (var b in buttons) b.Invalidate();
        if (globalButton != null) globalButton.Invalidate();
        foreach (var bx in boxes) bx.Invalidate();
        if (globalBox != null) globalBox.Invalidate();
        foreach (var s in sliders) s.Invalidate();
        foreach (var e in envelopes) e.Invalidate();
    }

    public List<ZUIButtonDef>     buttons    = new List<ZUIButtonDef>();
    public List<ZUIBoxDef>        boxes      = new List<ZUIBoxDef>();
    public List<ZUITextStyleDef>  textStyles = new List<ZUITextStyleDef>();
    public List<ZUISliderDef>     sliders    = new List<ZUISliderDef>();
    public List<ZUIEnvelopeDef>   envelopes  = new List<ZUIEnvelopeDef>();
    public List<ZUINineSliceDef>  nineSlices = new List<ZUINineSliceDef>();
    [HideInInspector] public ZUIIconLibraryAsset iconLibrary; // legacy — kept for serialization
    public List<ZUIPaletteColor>  palette    = new List<ZUIPaletteColor>();

    // Global defaults — per-def useGlobal* flags pull values from here.
    public ZUIButtonDef globalButton;
    public ZUIBoxDef    globalBox;

    /// <summary>
    /// When true, the style editor only allows skin editing (palette overrides).
    /// Style structure, defs, icons, fonts, and spacing are locked.
    /// Use this for shipped/production style sheets.
    /// </summary>
    public bool productionMode;

    // ── Asset library ────────────────────────────────────────────────────────
    /// <summary>Path to the custom data folder for this sheet's icons and fonts.</summary>
    public string dataFolderPath = "Assets/ZUIData";

    /// <summary>Default font for this sheet. Used when no alias or system font matches.</summary>
    public Font defaultFont;

    /// <summary>Icon aliases: name → asset path.</summary>
    public List<ZUIAssetAlias> iconAliases = new List<ZUIAssetAlias>();

    /// <summary>Font aliases: name → asset path.</summary>
    public List<ZUIAssetAlias> fontAliases = new List<ZUIAssetAlias>();

    /// <summary>
    /// Default vertical space between rows. Use via ZUI.VerticalSpace().
    /// </summary>
    [Min(0f)]
    public float verticalSpacing = 6f;

    /// <summary>
    /// Default horizontal space between columns/controls. Use via ZUI.HorizontalSpace().
    /// </summary>
    [Min(0f)]
    public float horizontalSpacing = 8f;

    /// <summary>
    /// Default inset around a whole content area (e.g. a window pane) from its container's edge, on all four
    /// sides. Distinct from `horizontalSpacing`/`verticalSpacing`, which space controls apart from EACH OTHER
    /// — this is the gap between the content and the OUTER boundary it's drawn inside. Use via
    /// ZUI.PaddedArea().
    /// </summary>
    [Min(0f)]
    public float contentPadding = 8f;

    /// <summary>
    /// Named spacing scales. Each is a multiplier applied on top of the base vertical or horizontal
    /// spacing. E.g. scale 0.25 named "EqToolbar" → ZUI.VerticalSpace("EqToolbar") = verticalSpacing × 0.25.
    /// </summary>
    public List<ZUISpacingScale> spacingScales = new List<ZUISpacingScale>();

    public float FindSpacingScale(string name)
    {
        var entry = spacingScales?.Find(s => s.name == name);
        if (entry != null) return entry.scale;
        if (!string.IsNullOrEmpty(name))
            ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.SpacingScale, name);
        return 1f;
    }

    // ── Label widths ────────────────────────────────────────────────────────
    /// <summary>Wide label width for descriptive labels (Corner Radius, Track Height, etc.)</summary>
    public float labelWidthWide   = 82f;
    /// <summary>Narrow label width for short labels (Ang, Crv, X, Y, etc.)</summary>
    public float labelWidthNarrow = 36f;
    /// <summary>Minimum width for float/int input fields to avoid value cutoff.</summary>
    public float inputFieldMinWidth = 56f;

    // ── Control height ──────────────────────────────────────────────────────
    /// <summary>Standard height for inline controls (buttons, toggles, sliders, color pickers).
    /// All ZUI controls should use this as their default height for visual consistency.</summary>
    [Min(14f)]
    public float controlHeight = 18f;

    /// <summary>
    /// Number of flash pulses when flashing a style or spacing marker.
    /// </summary>
    [Min(1)]
    public int flashCount = 8;

    /// <summary>
    /// Duration of each flash pulse in seconds.
    /// </summary>
    [Min(0.02f)]
    public float flashInterval = 0.12f;

    // ── Skins ─────────────────────────────────────────────────────────────────
    // Skins are palette overrides embedded in the sheet. The active skin index
    // determines which palette is used at resolve time. -1 = base palette (no skin).

    public List<ZUISkin> skins = new List<ZUISkin>();
    public int activeSkinIndex = -1;

    /// <summary>The currently active skin, or null if using the base palette.</summary>
    public ZUISkin ActiveSkin => activeSkinIndex >= 0 && activeSkinIndex < skins.Count ? skins[activeSkinIndex] : null;

    /// <summary>True when a skin is active (editor should lock structural edits).</summary>
    public bool IsSkinActive => activeSkinIndex >= 0;

    /// <summary>Returns the names of all available skins.</summary>
    public string[] GetSkinNames()
    {
        var names = new string[skins.Count];
        for (int i = 0; i < skins.Count; i++) names[i] = skins[i].name;
        return names;
    }

    /// <summary>Sets the active skin by name. Pass null or "" to clear.</summary>
    public void SetActiveSkin(string skinName)
    {
        if (string.IsNullOrEmpty(skinName)) { activeSkinIndex = -1; return; }
        activeSkinIndex = skins.FindIndex(s => s.name == skinName);
    }

    /// <summary>Creates a new skin from the current base palette.</summary>
    public ZUISkin CreateSkin(string skinName)
    {
        var skin = new ZUISkin { name = skinName };
        foreach (var p in palette)
        {
            var clone = new ZUIPaletteColor { name = p.name, color = p.color };
            if (p.autoColors != null)
            {
                clone.autoColors = new System.Collections.Generic.List<ZUIAutoColor>();
                foreach (var ac in p.autoColors)
                    clone.autoColors.Add(new ZUIAutoColor { name = ac.name, hueMod = ac.hueMod, satMod = ac.satMod, valMod = ac.valMod });
            }
            skin.palette.Add(clone);
        }
        skins.Add(skin);
        return skin;
    }

    public ZUIPaletteColor FindPaletteColor(string id)
    {
        // Active skin palette takes priority
        var skin = ActiveSkin;
        if (skin != null)
        {
            var skinEntry = skin.palette?.Find(p => p.name == id);
            if (skinEntry != null) return skinEntry;
        }
        return palette?.Find(p => p.name == id);
    }

    void OnEnable()
    {
        // Only ensure list containers exist, never re-add deleted styles
        if (buttons == null) buttons = new List<ZUIButtonDef>();
        if (boxes   == null) boxes   = new List<ZUIBoxDef>();
        if (textStyles == null) textStyles = new List<ZUITextStyleDef>();
        if (sliders == null) sliders = new List<ZUISliderDef>();
        if (envelopes == null) envelopes = new List<ZUIEnvelopeDef>();

        // Wire ownerSheet on all defs so resolution always targets this sheet.
        WireOwnerSheet();
    }

    /// <summary>Sets ownerSheet on all defs (buttons, boxes, slider sub-defs, globals).
    /// Called on enable and can be called manually after adding defs at runtime.</summary>
    public void WireOwnerSheet()
    {
        foreach (var b in buttons) b.ownerSheet = this;
        if (globalButton != null) globalButton.ownerSheet = this;
        foreach (var bx in boxes) bx.ownerSheet = this;
        if (globalBox != null) globalBox.ownerSheet = this;
        foreach (var s in sliders)
        {
            if (s.track     != null) s.track.ownerSheet     = this;
            if (s.trackFill != null) s.trackFill.ownerSheet = this;
            if (s.thumb     != null) s.thumb.ownerSheet     = this;
            if (s.thumbMax  != null) s.thumbMax.ownerSheet  = this;
        }
        foreach (var e in envelopes) e.ownerSheet = this;
    }

    static readonly ZUIButtonDef k_EmptyButton = new ZUIButtonDef { name = "__fallback__" };

    public ZUIButtonDef FindButton(string name)
    {
        var found = buttons.Find(b => b.name == name);
        if (found != null) { found.ownerSheet = this; return found; }
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Button, name);
        var fallback = buttons.Find(b => b.name == "Default") ?? (buttons.Count > 0 ? buttons[0] : k_EmptyButton);
        fallback.ownerSheet = this;
        return fallback;
    }

    static readonly ZUIBoxDef k_EmptyBox = new ZUIBoxDef { name = "__fallback__" };
    static readonly ZUISliderDef k_EmptySlider = new ZUISliderDef { name = "__fallback__" };
    static readonly ZUIEnvelopeDef k_EmptyEnvelope = new ZUIEnvelopeDef { name = "__fallback__" };

    public ZUIBoxDef FindBox(string name)
    {
        var found = boxes.Find(b => b.name == name);
        if (found != null) { found.ownerSheet = this; return found; }
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Box, name);
        var fallback = boxes.Find(b => b.name == "Default") ?? (boxes.Count > 0 ? boxes[0] : k_EmptyBox);
        fallback.ownerSheet = this;
        return fallback;
    }

    /// <summary>
    /// Finds a box def by name, returning null if not found. Records the miss in the
    /// missing-style registry like <see cref="FindBox"/>, but does not fall back to
    /// Default. Use when the caller wants to skip drawing entirely if the def is absent.
    /// </summary>
    public ZUIBoxDef FindBoxOrNull(string name)
    {
        var found = boxes.Find(b => b.name == name);
        if (found != null) { found.ownerSheet = this; return found; }
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Box, name);
        return null;
    }

    public ZUITextStyleDef FindText(string name)
    {
        var found = textStyles.Find(t => t.name == name);
        if (found != null) return found;
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Text, name);
        return textStyles.Find(t => t.name == "Default") ?? (textStyles.Count > 0 ? textStyles[0] : null);
    }

    public ZUISliderDef FindSlider(string name)
    {
        if (sliders == null) return k_EmptySlider;
        var found = sliders.Find(s => s.name == name);
        if (found != null) return found;
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Slider, name);
        return sliders.Find(s => s.name == "Default") ?? (sliders.Count > 0 ? sliders[0] : k_EmptySlider);
    }

    public ZUIEnvelopeDef FindEnvelope(string name)
    {
        if (envelopes == null) return k_EmptyEnvelope;
        var found = envelopes.Find(e => e.name == name);
        if (found != null) { found.ownerSheet = this; return found; }
        ZUIMissingStyleRegistry.Record(ZUIMissingStyleRegistry.EntryType.Envelope, name);
        var fallback = envelopes.Find(e => e.name == "Default") ?? (envelopes.Count > 0 ? envelopes[0] : k_EmptyEnvelope);
        fallback.ownerSheet = this;
        return fallback;
    }

    /// <summary>A named 9-slice frame, or null if the name isn't defined (no fallback — the caller
    /// falls back to the procedural background/border).</summary>
    public ZUINineSliceDef FindNineSlice(string name)
    {
        if (string.IsNullOrEmpty(name) || nineSlices == null) return null;
        return nineSlices.Find(n => n.name == name);
    }

}
