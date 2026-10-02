var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var sb = new System.Text.StringBuilder();
var winT = System.Type.GetType("Laubrary.Shaper.Editor.ShaperWindow, com.Lautaro-Arino.Laubrary.Shaper.Editor");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll(winT))
{
    ((UnityEditor.EditorWindow)w).Close();
    sb.Append("closed a ShaperWindow\n");
}
bool ok = UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/Audit0271");
sb.Append("deleted Assets/Shaper/Audit0271 = " + ok + "\n");
// Assets/Shaper only existed to hold the audit folders; remove it if this task left it empty.
var left = UnityEditor.AssetDatabase.FindAssets("", new string[] { "Assets/Shaper" });
sb.Append("assets left under Assets/Shaper: " + (left == null ? 0 : left.Length) + "\n");
if (left == null || left.Length == 0)
    sb.Append("deleted Assets/Shaper = " + UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper") + "\n");
UnityEditor.AssetDatabase.Refresh();
sb.Append("scriptCompilationFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed + "\n");
sb.Append("dataPath=" + UnityEngine.Application.dataPath + "\n");
return sb.ToString();
