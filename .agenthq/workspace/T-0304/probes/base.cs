var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
sb.Append("isPlaying=").Append(UnityEngine.Application.isPlaying).Append("\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (tn.Contains("Pyre") || tn.Contains("Shaper") || tn.Contains("Chunk") || tn.Contains("Laumination"))
        sb.Append("OPEN ").Append(tn).Append(" ").Append(w.position.ToString()).Append("\n");
}
string[] keys = { "ZuiSectionToggleBar.ShaperWindow.userSel", "ZuiSectionToggleBar.PyreWindow.userSel", "ZuiSectionToggleBar.PyreWindow.solo", "ZuiSectionToggleBar.PyreWindow.barMode" };
foreach (var k in keys) sb.Append("PREF ").Append(k).Append(" = ").Append(UnityEditor.EditorPrefs.GetString(k, "<none>")).Append("\n");
int dirty = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    var path = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(path).Append("\n"); }
}
sb.Append("dirtyAssets=").Append(dirty).Append("\n");
return sb.ToString();
