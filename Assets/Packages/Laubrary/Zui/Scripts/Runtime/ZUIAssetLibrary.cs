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

    // ── Embedded system icons (loaded off disk, not through the AssetDatabase) ──
    // The 1208 system icons live in a Unity-INVISIBLE folder (SystemAssets/Icons~ — the trailing
    // ~ makes Unity ignore it), so they cost nothing to import and never pollute an asset picker.
    // They're read straight off disk with File IO + ImageConversion instead of AssetDatabase.
    // `k_SystemIconsPathAbs` is the ABSOLUTE disk path (File IO can't use a project-relative path).
    // Prefer the hidden Icons~ folder once it exists; before the move it resolves to the plain
    // Icons folder, so the loader works in both states (no broken intermediate).
    public const string EmbeddedIconScheme = "zui://icons/";
    public static string k_SystemIconsPathAbs
    {
        get
        {
            string baseRel = InstallPath + "/SystemAssets/Icons";
            string hidden = Path.GetFullPath(baseRel + "~");
            if (Directory.Exists(hidden)) return hidden;
            return Path.GetFullPath(baseRel);
        }
    }

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
        // Embedded system icon by sentinel pseudo-path (zui://icons/<file>.png) — checked ahead of
        // the Assets/ branch so an alias authored with a sentinel resolves off disk, not via the DB.
        if (name.StartsWith(EmbeddedIconScheme))
        {
            var embTex = LoadEmbeddedIcon(name);
            if (embTex != null) return embTex;
        }

        // Try as direct asset path
        if (name.StartsWith("Assets/"))
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(name);
            if (tex != null) return tex;
        }

        // System icons — read off disk from the Unity-invisible Icons~ folder. LoadEmbeddedIcon
        // reduces whatever it's given to a bare file name, so an OLD alias that stored the full
        // Assets/.../Icons/foo.png path (now stale as an asset) still resolves by its "foo" basename.
        var systemTex = LoadEmbeddedIcon(name);
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

    // ── Embedded-icon loader ──────────────────────────────────────────────────
    // The system icons are NOT AssetDatabase assets — they live in the Unity-invisible Icons~
    // folder and are decoded off disk into transient textures held in a static name→texture
    // cache. A domain reload clears the cache; it refills lazily, which is fine.

    static readonly Dictionary<string, Texture2D> _embeddedIcons =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    // lowercased basename → absolute .png path, built once per domain from the folder listing so
    // lookups stay case-insensitive (the old FindTextureInFolder partial match was too).
    static Dictionary<string, string> _iconFileIndex;

    static Dictionary<string, string> IconFileIndex()
    {
        if (_iconFileIndex != null) return _iconFileIndex;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string dir = k_SystemIconsPathAbs;
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.png"))
                    map[Path.GetFileNameWithoutExtension(f)] = f;
        }
        catch { /* IO failure → empty index → icons resolve null, same as a missing folder */ }
        _iconFileIndex = map;
        return _iconFileIndex;
    }

    /// <summary>The bare, extension-less file name for any icon reference — a plain name ("edit"),
    /// a file name ("edit.png"), a sentinel ("zui://icons/edit.png"), or a full (possibly stale)
    /// "Assets/.../Icons/edit.png" asset path all reduce to "edit".</summary>
    static string IconBaseName(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath)) return null;
        string s = nameOrPath;
        if (s.StartsWith(EmbeddedIconScheme)) s = s.Substring(EmbeddedIconScheme.Length);
        return Path.GetFileNameWithoutExtension(s);
    }

    /// <summary>Decode a system icon off disk (from the invisible Icons~ folder) into a cached,
    /// hidden Texture2D. Accepts any icon reference form (see <see cref="IconBaseName"/>). Returns
    /// null (never throws) when the icon isn't found — callers fall through to the next resolver.</summary>
    public static Texture2D LoadEmbeddedIcon(string nameOrPath)
    {
        string key = IconBaseName(nameOrPath);
        if (string.IsNullOrEmpty(key)) return null;
        if (_embeddedIcons.TryGetValue(key, out var cached) && cached != null) return cached;

        if (!IconFileIndex().TryGetValue(key, out var file) || !File.Exists(file)) return null;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); } catch { return null; }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        if (!ImageConversion.LoadImage(tex, bytes)) { UnityEngine.Object.DestroyImmediate(tex); return null; }
        tex.hideFlags = HideFlags.HideAndDontSave;   // transient — must not leak into a scene on save
        tex.name = key;
        _embeddedIcons[key] = tex;
        return tex;
    }

    /// <summary>True when a path is an embedded system-icon sentinel (zui://icons/…). The clean
    /// system-vs-custom discriminator for the Style/Texture editors, replacing the old folder-path
    /// prefix comparison.</summary>
    public static bool IsSystemIconPath(string path) =>
        !string.IsNullOrEmpty(path) && path.StartsWith(EmbeddedIconScheme);

    /// <summary>Load an icon texture from a path that may be an embedded sentinel (zui://icons/…) OR
    /// a real asset path — the one choke-point every icon picker/editor uses so both kinds resolve.</summary>
    public static Texture2D LoadIconTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith(EmbeddedIconScheme)) return LoadEmbeddedIcon(path);
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
#else
        return null;
#endif
    }

    /// <summary>Drop the embedded-icon caches so the next lookup re-reads the folder (call after the
    /// Icons→Icons~ move, or any on-disk icon change, without waiting for a domain reload).</summary>
    public static void ClearEmbeddedIconCache()
    {
        foreach (var t in _embeddedIcons.Values) if (t != null) UnityEngine.Object.DestroyImmediate(t);
        _embeddedIcons.Clear();
        _iconFileIndex = null;
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
        // System icons: enumerate the Unity-invisible Icons~ folder off disk, emitting sentinel
        // paths (zui://icons/<file>.png). They are not AssetDatabase assets, so ScanFolder — and
        // every Texture2D object picker — can't (and shouldn't) see them; this keeps them browsable.
        foreach (var kv in IconFileIndex())
            result.Add((kv.Key, EmbeddedIconScheme + Path.GetFileName(kv.Value)));
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
