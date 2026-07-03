// ZUIAssetLibrary.cs
// Central registry for ZUI icons and fonts. Resolves assets through a layered chain:
//   Icons:  alias → system folder → custom folder → Resources → null
//   Fonts:  skin override → alias → system folder → custom folder → sheet default → ZUI default → Unity default
//
// System assets ship with ZUI (Assets/ZUI/SystemAssets/).
// Custom assets live in a user-configured data folder per style sheet.
// Aliases are per-sheet name→path mappings that let users swap assets without changing code.
//
// Asset discovery relies on AssetDatabase, which only exists in the editor. Those paths are
// guarded with #if UNITY_EDITOR; at runtime the resolvers fall back to Resources / null so
// this compiles and runs (as a no-op discovery) in players.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class ZUIAssetAlias
{
    public string name;        // lookup key (e.g. "Edit", "Title Font")
    public string assetPath;   // icon name or path (resolved through the library)
    public float  rotation;    // degrees (0, 90, 180, 270) — applied when drawing

    public ZUIAssetAlias() { }
    public ZUIAssetAlias(string name, string assetPath, float rotation = 0f)
    {
        this.name = name; this.assetPath = assetPath; this.rotation = rotation;
    }
}

[Serializable]
public class ZUIFontOverride
{
    public string aliasName;   // which font alias to override in this skin
    public string assetPath;   // skin's replacement font path
}

public static class ZUIAssetLibrary
{
    // ── Install path (auto-detected, editor only) ─────────────────────────────
    // Derived from this script's own asset location. Falls back to "Assets/ZUI"
    // at runtime (or if the script can't be located).
    static string _installPath;
    public static string InstallPath
    {
        get
        {
            if (!string.IsNullOrEmpty(_installPath)) return _installPath;
#if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets("t:MonoScript ZUIAssetLibrary");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/ZUIAssetLibrary.cs"))
                {
                    int idx = path.IndexOf("/Scripts/");
                    if (idx > 0) { _installPath = path.Substring(0, idx); return _installPath; }
                }
            }
#endif
            _installPath = "Assets/ZUI";
            return _installPath;
        }
    }

    // ── System asset paths ────────────────────────────────────────────────────
    // Public so editor windows (Style Editor, Texture Editor) can reference them
    // across the assembly boundary now that this type lives in the Runtime assembly.
    public static string k_SystemIconsPath => InstallPath + "/SystemAssets/Icons";
    public static string k_SystemFontsPath => InstallPath + "/SystemAssets/Fonts";

    // ZUI's internal default font (hardcoded fallback)
    static Font _zuiDefaultFont;
    public static Font ZUIDefaultFont
    {
        get
        {
            if (_zuiDefaultFont == null)
            {
                // Try loading from system fonts folder
                _zuiDefaultFont = LoadFirstFontInFolder(k_SystemFontsPath);
                // Ultimate fallback: Unity's built-in font
                if (_zuiDefaultFont == null)
                    _zuiDefaultFont = GUI.skin?.font;
            }
            return _zuiDefaultFont;
        }
    }

    // ── Icon resolution ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an icon by name, also returning any rotation from the alias.
    /// </summary>
    public static Texture2D FindIcon(string name, out float rotation)
    {
        rotation = 0f;
        if (string.IsNullOrEmpty(name)) return null;

        var sheet = ZUIStyleSheetAsset.Active;
        if (sheet != null)
        {
            var alias = sheet.iconAliases?.Find(a => a.name == name);
            if (alias != null)
            {
                rotation = alias.rotation;
                if (!string.IsNullOrEmpty(alias.assetPath))
                {
                    // Resolve the alias target (which may itself be an icon name)
                    var tex = FindIconDirect(alias.assetPath, sheet);
                    if (tex != null) return tex;
                }
            }
        }

        return FindIconDirect(name, sheet);
    }

    /// <summary>
    /// Resolves an icon by name. Chain: alias → system icons → custom icons → Resources → null.
    /// </summary>
    public static Texture2D FindIcon(string name)
    {
        return FindIcon(name, out _);
    }

    static Texture2D FindIconDirect(string name, ZUIStyleSheetAsset sheet)
    {
        if (string.IsNullOrEmpty(name)) return null;

#if UNITY_EDITOR
        // Try as direct asset path
        if (name.StartsWith("Assets/"))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(name);
            if (tex != null) return tex;
        }

        // System icons folder
        var systemTex = FindTextureInFolder(k_SystemIconsPath, name);
        if (systemTex != null) return systemTex;

        // Custom data folder (root + Icons/ subfolder)
        if (sheet != null && !string.IsNullOrEmpty(sheet.dataFolderPath))
        {
            var customTex = FindTextureInFolder(sheet.dataFolderPath, name);
            if (customTex != null) return customTex;
            string customIconsPath = Path.Combine(sheet.dataFolderPath, "Icons");
            customTex = FindTextureInFolder(customIconsPath, name);
            if (customTex != null) return customTex;
        }
#endif

        // Legacy: try Resources.Load (backward compat with existing Zounds icons; runtime-safe)
        var resTex = Resources.Load<Texture2D>(name);
        if (resTex != null) return resTex;

        return null;
    }

    // ── Font resolution ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a font by name. Chain: skin override → alias → system → custom → sheet default → ZUI default.
    /// </summary>
    public static Font FindFont(string name)
    {
        if (string.IsNullOrEmpty(name)) return ResolveDefaultFont();

        var sheet = ZUIStyleSheetAsset.Active;

#if UNITY_EDITOR
        // 1. Check skin font overrides
        if (sheet != null)
        {
            var skin = sheet.ActiveSkin;
            if (skin != null)
            {
                var ov = skin.fontOverrides?.Find(f => f.aliasName == name);
                if (ov != null && !string.IsNullOrEmpty(ov.assetPath))
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>(ov.assetPath);
                    if (font != null) return font;
                }
            }
        }

        // 2. Check aliases
        if (sheet != null)
        {
            var alias = sheet.fontAliases?.Find(a => a.name == name);
            if (alias != null && !string.IsNullOrEmpty(alias.assetPath))
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(alias.assetPath);
                if (font != null) return font;
            }
        }

        // 3. System fonts folder
        var systemFont = FindFontInFolder(k_SystemFontsPath, name);
        if (systemFont != null) return systemFont;

        // 4. Custom data folder (root + Fonts/ subfolder)
        if (sheet != null && !string.IsNullOrEmpty(sheet.dataFolderPath))
        {
            var customFont = FindFontInFolder(sheet.dataFolderPath, name);
            if (customFont != null) return customFont;
            string customFontsPath = Path.Combine(sheet.dataFolderPath, "Fonts");
            customFont = FindFontInFolder(customFontsPath, name);
            if (customFont != null) return customFont;
        }
#endif

        return ResolveDefaultFont();
    }

    /// <summary>Returns the sheet's default font, or ZUI's default, or Unity's default.</summary>
    public static Font ResolveDefaultFont()
    {
        var sheet = ZUIStyleSheetAsset.Active;
        if (sheet != null && sheet.defaultFont != null)
            return sheet.defaultFont;
        return ZUIDefaultFont;
    }

    // ── Scanning helpers ─────────────────────────────────────────────────────

    /// <summary>Returns all available icons (system + custom) as (name, path) pairs.</summary>
    public static List<(string name, string path)> GetAvailableIcons(string dataFolderOverride = null)
    {
        var result = new List<(string, string)>();
        ScanFolder(k_SystemIconsPath, "t:Texture2D", result);
        string dataFolder = dataFolderOverride ?? ZUIStyleSheetAsset.Active?.dataFolderPath;
        if (!string.IsNullOrEmpty(dataFolder))
        {
            ScanFolder(dataFolder, "t:Texture2D", result);
            ScanFolder(Path.Combine(dataFolder, "Icons"), "t:Texture2D", result);
        }
        return result;
    }

    /// <summary>Returns all available fonts (system + custom) as (name, path) pairs.</summary>
    public static List<(string name, string path)> GetAvailableFonts(string dataFolderOverride = null)
    {
        var result = new List<(string, string)>();
        ScanFolder(k_SystemFontsPath, "t:Font", result);
        string dataFolder = dataFolderOverride ?? ZUIStyleSheetAsset.Active?.dataFolderPath;
        if (!string.IsNullOrEmpty(dataFolder))
        {
            ScanFolder(dataFolder, "t:Font", result);
            ScanFolder(Path.Combine(dataFolder, "Fonts"), "t:Font", result);
        }
        return result;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    static string NormalizePath(string p)
    {
        if (p == null) return null;
        p = p.Replace('\\', '/');
        // Convert absolute paths to relative Assets/ paths
        if (!p.StartsWith("Assets/") && !p.StartsWith("Assets\\"))
        {
            int idx = p.IndexOf("/Assets/");
            if (idx >= 0) p = p.Substring(idx + 1);
        }
        return p;
    }

    static Texture2D FindTextureInFolder(string folderPath, string name)
    {
#if UNITY_EDITOR
        folderPath = NormalizePath(folderPath);
        if (!AssetDatabase.IsValidFolder(folderPath)) return null;
        // Try exact filename match (with common extensions)
        foreach (var ext in new[] { ".png", ".psd", ".jpg", ".tga", "" })
        {
            string path = NormalizePath(Path.Combine(folderPath, name + ext));
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
        }
        // Try partial match (name contained in filename)
        var guids = AssetDatabase.FindAssets($"t:Texture2D {name}", new[] { folderPath });
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
#endif
        return null;
    }

    static Font FindFontInFolder(string folderPath, string name)
    {
#if UNITY_EDITOR
        folderPath = NormalizePath(folderPath);
        if (!AssetDatabase.IsValidFolder(folderPath)) return null;
        foreach (var ext in new[] { ".ttf", ".otf", "" })
        {
            string path = NormalizePath(Path.Combine(folderPath, name + ext));
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font != null) return font;
        }
        var guids = AssetDatabase.FindAssets($"t:Font {name}", new[] { folderPath });
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        return null;
    }

    static Font LoadFirstFontInFolder(string folderPath)
    {
#if UNITY_EDITOR
        folderPath = NormalizePath(folderPath);
        if (!AssetDatabase.IsValidFolder(folderPath)) return null;
        var guids = AssetDatabase.FindAssets("t:Font", new[] { folderPath });
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        return null;
    }

    static void ScanFolder(string folderPath, string filter, List<(string name, string path)> results)
    {
#if UNITY_EDITOR
        folderPath = NormalizePath(folderPath);
        if (!AssetDatabase.IsValidFolder(folderPath)) return;
        var guids = AssetDatabase.FindAssets(filter, new[] { folderPath });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            results.Add((name, path));
        }
#endif
    }
}
