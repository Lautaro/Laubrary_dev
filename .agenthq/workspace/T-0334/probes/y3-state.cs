var sb = new System.Text.StringBuilder();
int dirty = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject", new string[]{"Assets"}))
{
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
  if (o != null && UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(p).Append("\n"); }
}
sb.Append("dirtyScriptableObjects=").Append(dirty).Append("\n");
var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(s.path).Append(" dirty=").Append(s.isDirty).Append("\n");
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying)
  .Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
return sb.ToString();
