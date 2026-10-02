var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
string saved = UnityEditor.EditorPrefs.GetString("A24.savedSel", "");
sb.Append("restoring userSel to: '").Append(saved).Append("'\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", saved);
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null); win.Repaint();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var b = new System.Text.StringBuilder();
foreach (var v in all) if (v is UnityEngine.UIElements.Button bb) b.Append('[').Append(bb.text).Append(']');
sb.Append("elements=").Append(all.Count).Append("\nbuttons: ").Append(b.ToString()).Append("\n");
return sb.ToString();
