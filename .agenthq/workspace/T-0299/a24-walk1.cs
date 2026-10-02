var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

// COLD: every instance closed, then opened from its own menu item
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 60, 1600, 1150);
win.Show(); win.Repaint();

var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("EMPTY STATE: boundDocument=").Append(doc == null ? "<none>" : doc.name).Append(" title=").Append(win.titleContent.text).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
int buttons = 0, noTip = 0, live = 0;
var names = new System.Text.StringBuilder();
foreach (var v in all)
{
    if (v is UnityEngine.UIElements.Button b)
    {
        buttons++;
        if (string.IsNullOrEmpty(b.tooltip)) { noTip++; names.Append("[NOTIP ").Append(b.text).Append("] "); }
        if (b.enabledInHierarchy) live++;
        names.Append(b.text).Append(b.enabledInHierarchy ? "(live) " : "(GREY) ");
    }
}
sb.Append("elements=").Append(all.Count).Append(" buttons=").Append(buttons).Append(" live=").Append(live).Append(" withoutTooltip=").Append(noTip).Append("\n");
sb.Append("buttons: ").Append(names.ToString()).Append("\n");
foreach (var v in all)
    if (v is UnityEngine.UIElements.Button b2 && b2.text == "Save")
        sb.Append("Save tooltip: ").Append(b2.tooltip).Append(" enabled=").Append(b2.enabledInHierarchy).Append("\n");
return sb.ToString();
