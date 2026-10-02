var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=1;Layers=1;Shape=1;Fill=1;Swarm=1;SpriteFX=1;Lights=1;Tags=1");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null); win.Repaint();
return sb.Append("rebuilt with all 9 sections on\n").ToString();
