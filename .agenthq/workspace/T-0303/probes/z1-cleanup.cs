var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
// unbind and close the tool windows first
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name == n) return t; } return null; };
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var n = w.GetType().Name;
    if (n == "PyreWindow" || n == "ShaperWindow" || n == "ChunkWindow" || n == "LauminationBuilderWindow") { w.Close(); sb.Append("closed ").Append(n).Append("\n"); }
}
string[] targets = {
  "Assets/Shaper/AuditA25a.asset", "Assets/Shaper/AuditA25b.asset",
  "Assets/Shaper/ShaperViews.asset",
  "Assets/Shaper/Audit0271", "Assets/Shaper/Audit0277", "Assets/Shaper/AuditA19",
  "Assets/Pyre/AuditA25" };
foreach (var p in targets)
{
    if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(p) != null || UnityEditor.AssetDatabase.IsValidFolder(p))
        sb.Append(UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ").Append(p).Append("\n");
    else sb.Append("absent  ").Append(p).Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
UnityEditor.Undo.ClearAll();
// prefs written by this pass
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
foreach (var k in new[]{ "A25.paneW","A25.winW","A25.intent","A25.pw","A25.ph","A25.sw","A25.sh","A25.open","A25.shot","A25.type","A25.title","A25.menu","A25.capName","A25.capW","A25.capH","ShaperCap.name","ZUI.Split.shaper.window.split.v1","Shaper.lastView" })
    UnityEditor.EditorPrefs.DeleteKey(k);
foreach (var k in new[]{ "A25.samples","A25.psamples","A25.snap" }) UnityEditor.SessionState.EraseString(k);
sb.Append("prefs cleared; userSel restored\n");
foreach (var p in System.IO.Directory.GetFileSystemEntries(UnityEngine.Application.dataPath + "/Shaper")) sb.Append("  SHAPER ").Append(System.IO.Path.GetFileName(p)).Append("\n");
foreach (var p in System.IO.Directory.GetFileSystemEntries(UnityEngine.Application.dataPath + "/Pyre")) sb.Append("  PYRE ").Append(System.IO.Path.GetFileName(p)).Append("\n");
return sb.ToString();
