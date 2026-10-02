var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
string menu = UnityEditor.EditorPrefs.GetString("A25.menu", "Laubrary/Chunks");
string typeName = UnityEditor.EditorPrefs.GetString("A25.type", "ChunkWindow");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name == n) return t; } return null; };
var wt = FT(typeName);
sb.Append("type found=").Append(wt != null).Append("\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == wt) w.Close();
bool ok = UnityEditor.EditorApplication.ExecuteMenuItem(menu);
sb.Append("menu '").Append(menu).Append("' executed=").Append(ok).Append("\n");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == wt) win = w;
sb.Append("window=").Append(win == null ? "<none>" : win.titleContent.text).Append(" minSize=").Append(win == null ? "" : win.minSize.ToString()).Append("\n");
if (win != null)
{
    UnityEditor.EditorPrefs.SetString("A25.title", win.titleContent.text);
    win.position = new UnityEngine.Rect(50, 50, UnityEditor.EditorPrefs.GetFloat("A25.capW", 1500f), UnityEditor.EditorPrefs.GetFloat("A25.capH", 1100f));
    win.Show(); win.Repaint();
    sb.Append("sized to ").Append(win.position.width).Append("x").Append(win.position.height).Append("\n");
}
return sb.ToString();
