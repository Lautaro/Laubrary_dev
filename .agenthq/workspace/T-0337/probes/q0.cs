var sb=new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append(" isCompiling=").Append(UnityEditor.EditorApplication.isCompiling).Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
var sc=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append("win=").Append(w.GetType().Name).Append(" ").Append(w.position.ToString()).Append("\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Object", new string[]{"Assets/Shaper"})) sb.Append("shaperAsset=").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
sb.Append("prefs: T0312.out=").Append(UnityEditor.EditorPrefs.GetString("T0312.out","<unset>"))
  .Append(" T320.capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut","<unset>"))
  .Append(" T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>"))
  .Append("\n userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<unset>"))
  .Append("\n Shaper.lastView=").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView","<unset>"));
return sb.ToString();
