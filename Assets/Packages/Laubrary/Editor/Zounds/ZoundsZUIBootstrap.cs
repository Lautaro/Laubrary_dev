// ZoundsZUIBootstrap.cs
// Registers the Zounds ZUI style sheet on domain reload.
// The sheet ships with the package, next to this script ("ZUI Assets/ZOUNDS ZUI Style Sheet.asset"),
// and is found by its GUID so it resolves wherever the package lives (Assets/Packages/... in the
// dev host, Packages/com.lautaro.arino.laubrary/... in a consumer). Window icons are NOT aliased in
// the sheet: every Zounds call site falls back to Resources/ZoundsWindowIcons, which also ships here.

using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class ZoundsZUIBootstrap
{
    // GUID of the packaged "ZOUNDS ZUI Style Sheet.asset" (see its .meta).
    const string k_PackagedSheetGuid = "e20964114e799464eb4e977adca28957";
    const string k_SheetFileName     = "ZOUNDS ZUI Style Sheet.asset";

    // Project-local locations used by older Zounds installs. Only consulted when the packaged sheet
    // is missing; they used to win over it, which is how an empty placeholder hid the real look.
    const string k_LocalSheetPath  = "Assets/Zounds/ZUI Assets/" + k_SheetFileName;
    const string k_LegacySheetPath = "Assets/ZoundsData/SystemFiles/ZUI Assets/" + k_SheetFileName;

    const int k_MaxRegisterAttempts = 10;
    static int _registerAttempts;

    static ZoundsZUIBootstrap()
    {
        EditorApplication.delayCall += Register;
    }

    // Zounds' windows pick the sheet up by consumer name. It is deliberately NOT made the ambient
    // ZUI.ActiveSheet: that would restyle every unscoped ZUI draw in other tools (e.g. LauAssetBrowser).
    static void Register()
    {
        var sheet = FindOrCreateSheet();
        if (sheet != null)
            ZUI.RegisterConsumerSheet("Zounds", sheet);
        else if (++_registerAttempts < k_MaxRegisterAttempts)
            EditorApplication.delayCall += Register; // packaged sheet still importing
    }

    static ZUIStyleSheetAsset FindOrCreateSheet()
    {
        // 1. The sheet shipped with the package.
        string packagedPath = AssetDatabase.GUIDToAssetPath(k_PackagedSheetGuid);
        var sheet = Load(packagedPath);
        if (sheet != null) return sheet;

        // 2. A copy beside this script that was re-imported under a fresh GUID.
        sheet = Load(ScriptFolder() + "/ZUI Assets/" + k_SheetFileName);
        if (sheet != null) return sheet;

        // 3. Older project-local installs.
        sheet = Load(k_LocalSheetPath) ?? Load(k_LegacySheetPath);
        if (sheet != null) return sheet;

        // The packaged sheet is known to the AssetDatabase but not loadable yet (mid-import):
        // don't strand a placeholder that would outlive the import.
        if (!string.IsNullOrEmpty(packagedPath)) return null;

        return CreateDefaultSheet();
    }

    static ZUIStyleSheetAsset Load(string path)
        => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>(path);

    static string ScriptFolder()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:MonoScript ZoundsZUIBootstrap"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("/ZoundsZUIBootstrap.cs"))
                return path.Substring(0, path.LastIndexOf('/'));
        }
        return null;
    }

    // Last resort only: the packaged sheet is gone. Writes an unstyled sheet into the project
    // (never into the package, which may be read-only) so Zounds still draws.
    static ZUIStyleSheetAsset CreateDefaultSheet()
    {
        ZUI.EnsureFolderExists("Assets/Zounds/ZUI Assets");

        var sheet = ScriptableObject.CreateInstance<ZUIStyleSheetAsset>();
        sheet.consumerName = "Zounds";
        sheet.palette.Add(new ZUIPaletteColor { name = "Bg",     color = new Color(.16f, .16f, .20f, 1f) });
        sheet.palette.Add(new ZUIPaletteColor { name = "Accent", color = new Color(.30f, .55f, .80f, 1f) });
        sheet.palette.Add(new ZUIPaletteColor { name = "Text",   color = new Color(.85f, .85f, .88f, 1f) });
        sheet.productionMode = true;

        AssetDatabase.CreateAsset(sheet, k_LocalSheetPath);
        AssetDatabase.SaveAssets();
        Debug.LogWarning($"[Zounds] Packaged ZUI style sheet not found; created an unstyled fallback at {k_LocalSheetPath}");
        return sheet;
    }
}
