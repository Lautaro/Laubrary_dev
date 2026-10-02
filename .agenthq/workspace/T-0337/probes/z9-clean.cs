var sb = new System.Text.StringBuilder();
// 1. unbind and close the Shaper window (none was open at session start)
int closed=0;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w0!=null && w0.GetType().Name=="ShaperWindow") { w0.Close(); closed++; }
sb.Append("shaper windows closed=").Append(closed).Append('\n');
// 2. delete every scratch asset this task made (DeleteAsset removes the .meta too)
string[] paths = {
 "Assets/Shaper/AuditT337B.asset","Assets/Shaper/AuditT337C.asset","Assets/Shaper/AuditT337D.asset",
 "Assets/Shaper/AuditT337P.asset","Assets/Shaper/AuditT337F.asset",
 "Assets/Shaper/Audit0277/rfield0277.asset","Assets/Shaper/Audit0277",
};
foreach (var p in paths) {
  if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(p)==null && !UnityEditor.AssetDatabase.IsValidFolder(p)) { sb.Append("absent  ").Append(p).Append('\n'); continue; }
  bool ok = UnityEditor.AssetDatabase.DeleteAsset(p);
  sb.Append(ok?"deleted ":"FAILED  ").Append(p).Append('\n');
}
UnityEditor.AssetDatabase.Refresh();
// 3. restore / clear every pref this task touched
foreach (var k in new string[]{"T337.form","T337.formNext","T337.paths","T337.newName","T337.pick","T337.col",
  "T337.pressText","T337.pressNth","T337.scrollTo","T337.reach","T337.want","T337.t0","T337.spec","T337.tag",
  "T337.w","T337.h","T337.raise","T337.watch","T337.chip","T337.stageHash","T337.preSave","T337.savePath","T337.bakeFolder"})
  UnityEditor.EditorPrefs.DeleteKey(k);
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
  "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
UnityEditor.EditorPrefs.SetString("T320.capWin", "ShaperWindow");
UnityEditor.EditorPrefs.DeleteKey("Shaper.lastView");
UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/out");
sb.Append("prefs restored\n");
UnityEditor.Undo.ClearAll();
// 4. a project-wide dirty scan, and what is left under Assets/Shaper
int dirty=0;
foreach (var so in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
  if (so!=null && UnityEditor.EditorUtility.IsDirty(so) && !string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(so))) { dirty++; if(dirty<6) sb.Append("  DIRTY ").Append(UnityEditor.AssetDatabase.GetAssetPath(so)).Append('\n'); }
sb.Append("dirty ScriptableObjects=").Append(dirty).Append('\n');
sb.Append("AuditT337 matches=").Append(UnityEditor.AssetDatabase.FindAssets("AuditT337").Length).Append('\n');
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) sb.Append("  left: ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append('\n');
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append('\n');
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append('\n');
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append("win=").Append(w0.GetType().Name).Append('\n');
return sb.ToString();
