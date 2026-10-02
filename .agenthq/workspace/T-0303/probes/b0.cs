var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
sb.Append("isPlaying=").Append(UnityEngine.Application.isPlaying).Append("\n");

int closed = 0;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (tn == "PyreWindow" || tn == "ShaperWindow" || tn == "ChunksWindow" || tn == "LaunimatorWindow") { sb.Append("closing ").Append(tn).Append("\n"); w.Close(); closed++; }
}
sb.Append("closed=").Append(closed).Append("\n");

// SHA baseline of every authored asset we might touch
System.Func<string,string> sha = p => {
    var full = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, p);
    if (!System.IO.File.Exists(full)) return "MISSING";
    using (var s = System.Security.Cryptography.SHA256.Create())
    using (var f = System.IO.File.OpenRead(full))
        return System.BitConverter.ToString(s.ComputeHash(f)).Replace("-","").Substring(0,16) + " len=" + new System.IO.FileInfo(full).Length;
};
foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Pyre", "*.asset"))
    sb.Append("SHA Assets/Pyre/").Append(System.IO.Path.GetFileName(p)).Append(" ").Append(sha("Assets/Pyre/" + System.IO.Path.GetFileName(p))).Append("\n");
if (System.IO.Directory.Exists(UnityEngine.Application.dataPath + "/Shaper"))
  foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Shaper", "*.asset"))
    sb.Append("SHA Assets/Shaper/").Append(System.IO.Path.GetFileName(p)).Append(" ").Append(sha("Assets/Shaper/" + System.IO.Path.GetFileName(p))).Append("\n");

// EditorPrefs snapshot
string[] keys = { "ZuiSectionToggleBar.ShaperWindow.userSel", "ZuiSectionToggleBar.ShaperWindow.solo", "ZuiSectionToggleBar.ShaperWindow.barMode", "ZUI.Split.shaper.window.split.v1", "Shaper.lastView", "Pyre.leftPaneWidth" };
foreach (var k in keys) sb.Append("PREF ").Append(k).Append(" = ").Append(UnityEditor.EditorPrefs.GetString(k, "<none/nonstring>")).Append("\n");

// dirty assets project-wide
int dirty = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    var path = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(o)) { dirty++; sb.Append("DIRTY ").Append(path).Append("\n"); }
}
sb.Append("dirtyCount=").Append(dirty).Append("\n");
return sb.ToString();
