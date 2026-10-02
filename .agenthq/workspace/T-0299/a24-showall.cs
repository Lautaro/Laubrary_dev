var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("prevUserSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<none>")).Append("\n");
if (!UnityEditor.EditorPrefs.HasKey("A24.savedSel"))
    UnityEditor.EditorPrefs.SetString("A24.savedSel", UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", ""));
string[] all = { "Views","Canvas","Layers","Shape","Transform","Fill","Border","SpriteFX","Global SpriteFX","Swarm","Lights","Lighting","Tags" };
var s = new System.Text.StringBuilder();
foreach (var l in all) { if (s.Length > 0) s.Append(';'); s.Append(l).Append("=1"); }
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", s.ToString());
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.preSolo");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", false);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null);
win.Repaint();
sb.Append("rebuilt\n");
return sb.ToString();
