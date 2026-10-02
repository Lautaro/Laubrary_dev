// Baseline: what the editor looked like before this task touched it. Everything here is restored by z-clean.cs.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed)
  .Append(" isCompiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" playing=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");

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

foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    sb.Append("WIN ").Append(w.GetType().FullName).Append(" | ").Append(w.titleContent.text).Append(" | ").Append(w.position).Append("\n");

foreach (var k in new[] { "ZuiSectionToggleBar.ShaperWindow.userSel", "ZuiSectionToggleBar.ShaperWindow.solo",
                          "ZUI.Split.shaper.window.split.v1", "ZUI.Split.pyre.window.split.v1", "Shaper.lastView" })
    sb.Append("PREF ").Append(k).Append(" = ").Append(UnityEditor.EditorPrefs.GetString(k, "<none-or-nonstring>")).Append("\n");
sb.Append("PREF barMode = ").Append(UnityEditor.EditorPrefs.GetBool("ZuiSectionToggleBar.ShaperWindow.barMode", false)).Append("\n");
sb.Append("splitShaperFloat = ").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.shaper.window.split.v1", -1f)).Append("\n");
sb.Append("splitPyreFloat = ").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.pyre.window.split.v1", -1f)).Append("\n");
return ZDump("p0-base.txt", sb.ToString()) + "\n" + sb.ToString().Substring(0, System.Math.Min(1400, sb.Length));
