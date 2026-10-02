// Record the prefs this session is going to move, then point them at T-0324's own folders.
var sb = new System.Text.StringBuilder();
string[] keys = { "T0312.out", "T320.capWin", "T320.capOut", "T321.src", "T321.dst", "T321.rect", "T324.capOut" };
foreach (var k in keys) sb.AppendLine("BEFORE " + k + " = " + UnityEditor.EditorPrefs.GetString(k, "<unset>"));
sb.AppendLine("BEFORE T321.scale = " + UnityEditor.EditorPrefs.GetFloat("T321.scale", -1f));
UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/out");
sb.AppendLine("dataPath=" + Application.dataPath);
sb.AppendLine("scene=" + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path
    + " dirty=" + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed + " playing=" + UnityEditor.EditorApplication.isPlaying);
var wins = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
sb.AppendLine("open windows (" + wins.Length + "): " + string.Join(", ", System.Linq.Enumerable.Select(wins, w => w.GetType().Name)));
// project-wide dirty scan — nothing of the user's may be dirty while a rig that calls SaveAssets is alive
int dirty = 0; var dnames = new System.Collections.Generic.List<string>();
foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
{
    var p = UnityEditor.AssetDatabase.GetAssetPath(so);
    if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(so)) { dirty++; if (dnames.Count < 12) dnames.Add(p); }
}
sb.AppendLine("dirty project ScriptableObjects: " + dirty + " " + string.Join(" | ", dnames));
return sb.ToString();
