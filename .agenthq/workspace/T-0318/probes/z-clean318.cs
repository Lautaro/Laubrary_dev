// T-0313 — put the editor back exactly as out/p0-base.txt found it: close every tool window this task
// opened (none was open at session start — the baseline's WIN list is the seven stock editor windows),
// delete every scratch asset (with its .meta, through AssetDatabase.DeleteAsset), remove every T313./T0312.
// pref, restore the section toggle bar verbatim, drop the two split prefs (neither had a value at session
// start), clear Undo and the console, and re-hash Assets/Shaper + Assets/Pyre so the report can say whether
// anything moved.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

foreach (var n in new[] { "ShaperWindow", "PyreWindow", "ChunkWindow", "LauminationBuilderWindow",
                          "LauminaryBrowserWindow", "SpriteCatalogWindow", "AnimationAsepriteWindow", "ZoeWindow", "MirageWindow" })
{
    var t = ZType(n); if (t == null) continue;
    foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
        if (w != null && w.GetType() == t) { w.Close(); sb.Append("closed ").Append(n).Append("\n"); }
}

foreach (var p in new[] { "Assets/Pyre/AuditT0313.asset", "Assets/Pyre/AuditT0312.asset",
                          "Assets/Shaper/AuditT0313", "Assets/Shaper/AuditT0318" })
{
    if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(p) == null && !UnityEditor.AssetDatabase.IsValidFolder(p))
    { sb.Append("already gone ").Append(p).Append("\n"); continue; }
    if (UnityEditor.AssetDatabase.DeleteAsset(p)) sb.Append("deleted ").Append(p).Append("\n");
    else sb.Append("FAILED to delete ").Append(p).Append("\n");
}

foreach (var k in new[] { "T313.form", "T313.shape", "T313.pane", "T313.winw", "T313.width", "T313.win",
                          "T313.menu", "T313.bind", "T313.step", "T313.t", "T313.card", "T313.doc",
                          "T313.lauminary", "T318.lpw", "T318.winw", "T318.ctl", "T318.capWin", "T318.capTag", "T318.capW", "T318.capH", "T318.capName", "T318.capRestore", "T318.sampleA", "T318.sampleB", "T318.tsState",
                          "T0312.out", "T0312.unit", "T0312.tag", "T0312.doc", "T0312.layer",
                          "T0312.pane", "T0312.winw", "T0312.match", "T0312.depth",
                          "ZUI.Split.shaper.window.split.v1", "ZUI.Split.pyre.window.split.v1" })
    if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("pref deleted ").Append(k).Append("\n"); }

// restored verbatim to the value out/p0-base.txt recorded at this task's start
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
    if (o != null && UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(p).Append("\n"); }
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
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed)
  .Append(" playing=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return ZDump("z-clean318.txt", sb.ToString()) + "\n" + sb.ToString().Substring(0, System.Math.Min(2000, sb.Length));
