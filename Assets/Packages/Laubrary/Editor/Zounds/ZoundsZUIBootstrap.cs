// ZoundsZUIBootstrap.cs
// Registers the Zounds ZUI style sheet on domain reload.
// Uses ZoundsZUIConfig for a serialized asset reference (survives moves/renames).
// Falls back to path-based search if config doesn't exist yet.

using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class ZoundsZUIBootstrap
{
    // Auto-detected install path for Zounds
    static string _zoundsInstallPath;
    internal static string ZoundsInstallPath
    {
        get
        {
            if (_zoundsInstallPath != null) return _zoundsInstallPath;
            var guids = AssetDatabase.FindAssets("t:MonoScript ZoundsZUIBootstrap");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/ZoundsZUIBootstrap.cs"))
                {
                    // path = "Assets/.../Zounds/Scripts/Editor/ZoundsZUIBootstrap.cs"
                    int idx = path.IndexOf("/Scripts/Editor/ZoundsZUIBootstrap.cs");
                    if (idx > 0) { _zoundsInstallPath = path.Substring(0, idx); return _zoundsInstallPath; }
                }
            }
            _zoundsInstallPath = "Assets/Zounds";
            return _zoundsInstallPath;
        }
    }

    internal static string k_SheetPath => ZoundsInstallPath + "/ZUI Assets/ZOUNDS ZUI Style Sheet.asset";
    internal static string k_IconsPath => ZoundsInstallPath + "/ZUI Assets";
    internal const string k_LegacySheetPath = "Assets/ZoundsData/SystemFiles/ZUI Assets/ZOUNDS ZUI Style Sheet.asset";
    internal const string k_LegacyIconsPath = "Assets/ZoundsData/SystemFiles/ZUI Assets";

    static ZoundsZUIBootstrap()
    {
        EditorApplication.delayCall += Register;
    }

    static void Register()
    {
        var sheet = FindOrCreateSheet();
        if (sheet != null)
        {
            ZUI.RegisterConsumerSheet("Zounds", sheet);
            ZUI.SetDefaultActiveSheet(sheet);
        }
    }

    static ZUIStyleSheetAsset FindOrCreateSheet()
    {
        var sheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(k_SheetPath);
        if (sheet != null) return sheet;

        sheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(k_LegacySheetPath);
        if (sheet != null) return sheet;
        return CreateDefaultSheet();
    }

    static ZUIStyleSheetAsset CreateDefaultSheet()
    {
        string dir = Path.GetDirectoryName(k_SheetPath).Replace('\\', '/');
        ZUI.EnsureFolderExists(dir);

        var sheet = ScriptableObject.CreateInstance<ZUIStyleSheetAsset>();
        // Start empty — styles are added by the user in the Style Editor (or copied from DefaultSheet).

        sheet.palette.Add(new ZUIPaletteColor { name = "Bg",     color = new Color(.16f, .16f, .20f, 1f) });
        sheet.palette.Add(new ZUIPaletteColor { name = "Accent", color = new Color(.30f, .55f, .80f, 1f) });
        sheet.palette.Add(new ZUIPaletteColor { name = "Text",   color = new Color(.85f, .85f, .88f, 1f) });

        string icons = ResolveIconsPath();
        if (!string.IsNullOrEmpty(icons))
        {
            sheet.iconAliases = new List<ZUIAssetAlias>
            {
                new ZUIAssetAlias("open-editor",           icons + "/open-editor.png"),
                new ZUIAssetAlias("open-editor-klip",      icons + "/open-editor-klip.png"),
                new ZUIAssetAlias("open-editor-zequence",  icons + "/open-editor-zequence.png"),
                new ZUIAssetAlias("convert",               icons + "/convert.png"),
                new ZUIAssetAlias("convert-zequence",      icons + "/convert-zequence.png"),
                new ZUIAssetAlias("make-shared",           icons + "/make-shared.png"),
                new ZUIAssetAlias("break-to-local",        icons + "/break-to-local.png"),
                new ZUIAssetAlias("reconnect-shared",      icons + "/reconnect-shared.png"),
                new ZUIAssetAlias("klip",                  icons + "/K KLIP.png"),
                new ZUIAssetAlias("zeq",                   icons + "/Z ZEQ.png"),
            };
        }

        sheet.dataFolderPath = icons ?? "Assets/Zounds/SystemAssets/ZUI/Icons";
        sheet.productionMode = true;

        AssetDatabase.CreateAsset(sheet, k_SheetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Zounds] Created default ZUI style sheet at {k_SheetPath} (production mode)");
        return sheet;
    }

    static string ResolveIconsPath()
    {
        if (AssetDatabase.IsValidFolder(k_IconsPath)) return k_IconsPath;
        if (AssetDatabase.IsValidFolder(k_LegacyIconsPath)) return k_LegacyIconsPath;
        return null;
    }
}
