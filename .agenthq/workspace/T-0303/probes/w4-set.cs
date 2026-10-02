var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
float paneW = UnityEditor.EditorPrefs.GetFloat("A25.paneW", 400f);
float winW  = UnityEditor.EditorPrefs.GetFloat("A25.winW", 900f);
var doc0 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA25a.asset");
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc0);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
// show every section so every card is on screen at once
var sel = new System.Text.StringBuilder();
foreach (var l in new[]{"Views","Canvas","Layers","Shape","Fill","Swarm","SpriteFX","Lights","Tags"}) { if (sel.Length>0) sel.Append(';'); sel.Append(l).Append("=1"); }
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", sel.ToString());
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.SetFloat("ZUI.Split.shaper.window.split.v1", paneW);
win.position = new UnityEngine.Rect(30, 30, winW, 1300);
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null);
win.Repaint();
return sb.Append("set win=").Append(winW).Append(" paneW=").Append(paneW).Append("\n").ToString();
