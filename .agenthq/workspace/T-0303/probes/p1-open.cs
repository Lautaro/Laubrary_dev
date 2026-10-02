var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) w.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre");
var win = UnityEditor.EditorWindow.GetWindow(pyreT);
win.position = new UnityEngine.Rect(60, 60, 1500, 1150);
win.Show(); win.Repaint();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var cur = curP == null ? null : curP.GetValue(win) as UnityEngine.Object;
sb.Append("EMPTY STATE bound=").Append(cur == null ? "<none>" : cur.name).Append(" title=").Append(win.titleContent.text).Append(" minSize=").Append(win.minSize.ToString()).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int b = 0, noTip = 0; var names = new System.Text.StringBuilder();
foreach (var v in all) if (v is UnityEngine.UIElements.Button bt) { b++; if (string.IsNullOrEmpty(bt.tooltip)) { noTip++; names.Append("[NOTIP ").Append(bt.text).Append("] "); } names.Append(bt.text).Append(bt.enabledInHierarchy ? "(live) " : "(GREY) "); }
sb.Append("elements=").Append(all.Count).Append(" buttons=").Append(b).Append(" withoutTooltip=").Append(noTip).Append("\n").Append("buttons: ").Append(names.ToString()).Append("\n");
return sb.ToString();
