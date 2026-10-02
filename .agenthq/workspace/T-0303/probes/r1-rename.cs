var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach (var t in ts) if (t.Name == n) return t; } return null; };
var pyreT = FT("PyreWindow");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) w.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre");
var win = UnityEditor.EditorWindow.GetWindow(pyreT);
win.position = new UnityEngine.Rect(60, 60, 1500, 1150);
win.Repaint();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int plus = 0;
foreach (var v in all)
{
    string t = v is UnityEngine.UIElements.Label lb ? lb.text : (v is UnityEngine.UIElements.Button b ? b.text : "");
    if (t != null && t.Contains("Pyre Plus")) { plus++; sb.Append("STILL SAYS (text): '").Append(t).Append("'\n"); }
    if (v.tooltip != null && v.tooltip.Contains("Pyre Plus")) { plus++; sb.Append("STILL SAYS (tooltip): '").Append(v.tooltip.Length > 90 ? v.tooltip.Substring(0,90) : v.tooltip).Append("'\n"); }
}
sb.Append("elements saying 'Pyre Plus' = ").Append(plus).Append("\n");
foreach (var v in all) if (v is UnityEngine.UIElements.Button b2 && b2.text == "Save") sb.Append("Save tooltip: ").Append(b2.tooltip).Append("\n");
foreach (var v in all) if (v is UnityEngine.UIElements.Button b3 && b3.text == "New") sb.Append("New tooltip: ").Append(b3.tooltip).Append("\n");
foreach (var v in all) if (v is UnityEngine.UIElements.Label l2 && l2.text != null && l2.text.Contains("library")) sb.Append("browser header: '").Append(l2.text).Append("'\n");
return sb.ToString();
