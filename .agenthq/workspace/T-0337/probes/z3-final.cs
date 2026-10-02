var sb = new System.Text.StringBuilder();
// close every Shaper window (none was open at session start)
int closed = 0;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w0 != null && w0.GetType().Name == "ShaperWindow") { w0.Close(); closed++; }
sb.Append("shaperWindowsClosed=").Append(closed).Append("\n");
// restore prefs to their session-start values
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
UnityEditor.EditorPrefs.SetString("T320.capWin", "ShaperWindow");
foreach (var k in new string[]{"T337.w","T337.h","T337.tag","T337.pick","T337.col","T337.pressText","T337.pressNth",
                               "T337.newName","T337.scrollTo","T337.reach","T337.want","T337.t0","T337.spec",
                               "T337.preSave","T337.savePath","T337.bakeFolder","T337.walkWin","Shaper.lastView"})
  UnityEditor.EditorPrefs.DeleteKey(k);
// project-wide dirty scan
int dirty = 0; var names = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
  if (so != null && UnityEditor.EditorUtility.IsDirty(so) && !string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(so)))
  { dirty++; if (names.Length < 400) names.Append(UnityEditor.AssetDatabase.GetAssetPath(so)).Append(" | "); }
sb.Append("dirtyScriptableObjects=").Append(dirty).Append(" ").Append(names).Append("\n");
sb.Append("AuditT337 matches=").Append(UnityEditor.AssetDatabase.FindAssets("AuditT337").Length).Append("\n");
UnityEditor.Undo.ClearAll();
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
sb.Append("compFail=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
sb.Append("windows: "); foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append(w.GetType().Name).Append(" ");
sb.Append("\nprefs now: userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<unset>"))
  .Append(" capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut","<unset>"))
  .Append(" capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>"));
return sb.ToString();
