var sb = new System.Text.StringBuilder();
// close the Shaper window
int closed = 0;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ShaperWindow") { w.Close(); closed++; }
sb.Append("closed Shaper windows=").Append(closed).Append("\n");
// prefs: restore the session-start values, delete every T334.*
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
  "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
foreach (var k in new string[]{ "T334.tag","T334.walkWin","T334.newName","T334.pick","T334.col","T334.step",
  "T334.pressText","T334.pressNth","T334.w","T334.h","T334.preSave","T334.savePath","T334.phase",
  "T334.bakeFolder","T334.reach","T334.splitMode","T334.win","T334.find","T334.findNth","Shaper.lastView" })
  UnityEditor.EditorPrefs.DeleteKey(k);
sb.Append("userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel")).Append("\n");
sb.Append("T320.capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut")).Append("\n");
sb.Append("T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>")).Append("\n");
// nothing dirty anywhere
int dirty = 0; var names = new System.Text.StringBuilder();
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>()) {
  if (o == null) continue; var p = UnityEditor.AssetDatabase.GetAssetPath(o);
  if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
  if (UnityEditor.EditorUtility.IsDirty(o)) { dirty++; names.Append(p).Append(" "); } }
sb.Append("dirty ScriptableObjects under Assets/=").Append(dirty).Append(" ").Append(names).Append("\n");
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
sb.Append("AuditT334 matches=").Append(UnityEditor.AssetDatabase.FindAssets("AuditT334").Length).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed)
  .Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
UnityEditor.Undo.ClearAll();
sb.Append("open windows: ");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append(w.GetType().Name).Append(" ");
return sb.ToString();
