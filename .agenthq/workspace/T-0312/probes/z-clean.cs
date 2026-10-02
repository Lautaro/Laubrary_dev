// Puts the editor back exactly as p0-base.txt found it: closes the tool windows, deletes every scratch
// asset this task created (with its .meta, via AssetDatabase.DeleteAsset), removes every T0312 pref,
// restores the section toggle bar verbatim, drops the two split prefs (neither had a value at session
// start), clears Undo and the console, and re-hashes Assets/Shaper + Assets/Pyre so the report can say
// whether anything moved.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

foreach (var n in new[] { "ShaperWindow", "PyreWindow" })
{
    var t = ZType(n); if (t == null) continue;
    foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
        if (w != null && w.GetType() == t) { w.Close(); sb.Append("closed ").Append(n).Append("\n"); }
}

foreach (var dir in new[] { "Assets/Shaper", "Assets/Pyre" })
{
    if (!System.IO.Directory.Exists(dir)) continue;
    foreach (var e in System.IO.Directory.GetFileSystemEntries(dir))
    {
        var p = e.Replace('\\', '/');
        if (p.EndsWith(".meta")) continue;
        if (p.IndexOf("AuditT0312", System.StringComparison.Ordinal) < 0) continue;
        if (UnityEditor.AssetDatabase.DeleteAsset(p)) sb.Append("deleted ").Append(p).Append("\n");
        else sb.Append("FAILED to delete ").Append(p).Append("\n");
    }
}

foreach (var k in new[] { "T0312.out", "T0312.unit", "T0312.tag", "T0312.doc", "T0312.layer",
                          "T0312.pane", "T0312.winw", "T0312.match", "T0312.depth",
                          "ZUI.Split.shaper.window.split.v1", "ZUI.Split.pyre.window.split.v1" })
    if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("pref deleted ").Append(k).Append("\n"); }

// restored verbatim to the value p0-base.cs recorded at session start
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.DeleteKey("Shaper.lastView");

UnityEditor.Undo.ClearAll();
UnityEditor.AssetDatabase.Refresh();

int dirty = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Object", new[] { "Assets" }))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
    if (o != null && UnityEditor.EditorUtility.IsDirty(o)) dirty++;
}
sb.Append("dirtyCount=").Append(dirty).Append("\n");

foreach (var dir in new[] { "Assets/Shaper", "Assets/Pyre" })
{
    if (!System.IO.Directory.Exists(dir)) { sb.Append(dir).Append(" MISSING\n"); continue; }
    var files = System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories);
    System.Array.Sort(files);
    foreach (var f in files)
    {
        if (f.EndsWith(".meta")) continue;
        var fi = new System.IO.FileInfo(f);
        string sha;
        using (var md = System.Security.Cryptography.SHA256.Create())
        using (var fs = fi.OpenRead())
            sha = System.BitConverter.ToString(md.ComputeHash(fs)).Replace("-", "").Substring(0, 16);
        sb.Append("FILE ").Append(f.Replace('\\', '/')).Append(" ").Append(fi.Length).Append(" ").Append(sha).Append("\n");
    }
}
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
var outp = ZDump("z-clean.txt", sb.ToString());
return outp + "\n" + sb.ToString();
