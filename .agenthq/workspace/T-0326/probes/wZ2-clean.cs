var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{ "Assets/Screenshots" }))
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    sb.Append(UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ").Append(p).Append("\n");
}
if (UnityEditor.AssetDatabase.IsValidFolder("Assets/Screenshots"))
    sb.Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Screenshots") ? "rmdir   " : "RMFAIL  ").Append("Assets/Screenshots\n");
UnityEditor.AssetDatabase.Refresh();
sb.Append("remaining AuditT326 = ").Append(UnityEditor.AssetDatabase.FindAssets("AuditT326").Length).Append("\n");
// project-wide dirty scan
int dirty = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject", new string[]{ "Assets" }))
{
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
    if (o != null && UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(UnityEditor.AssetDatabase.GetAssetPath(o)).Append("\n"); }
}
sb.Append("dirty ScriptableObjects = ").Append(dirty).Append("\n");
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
return sb.ToString();
