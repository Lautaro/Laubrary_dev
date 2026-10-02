var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);

sb.Append("bound before = ").Append((curP.GetValue(win) as UnityEngine.Object) == null ? "<none>" : (curP.GetValue(win) as UnityEngine.Object).name).Append("\n");

// a grid CELL: the ancestor of a thumbnail Image that carries a click handler
UnityEngine.UIElements.VisualElement cell = null;
foreach (var v in all)
    if (v is UnityEngine.UIElements.Image im && im.image != null)
    {
        for (var p = v.parent; p != null && p != win.rootVisualElement; p = p.parent)
        {
            if (!string.IsNullOrEmpty(p.tooltip) && p.tooltip.IndexOf("Open", System.StringComparison.OrdinalIgnoreCase) >= 0) { cell = p; break; }
        }
        if (cell != null) break;
    }
sb.Append("cell by tooltip = ").Append(cell == null ? "<none>" : (cell.GetType().Name + " tip=" + cell.tooltip)).Append("\n");
if (cell == null)
{
    // fall back: the direct parent chain of the SECOND thumbnail, and report what tooltips exist
    int shown = 0;
    foreach (var v in all)
        if (v is UnityEngine.UIElements.Image im2 && im2.image != null && shown < 2)
        {
            shown++;
            for (var p = v.parent; p != null && p != win.rootVisualElement; p = p.parent)
                sb.Append("   ancestor ").Append(p.GetType().Name).Append(" tip='").Append(p.tooltip).Append("' class=").Append(string.Join(",", p.GetClasses())).Append("\n");
            sb.Append("   ---\n");
        }
}
else
{
    using (var d = UnityEngine.UIElements.PointerDownEvent.GetPooled()) { d.target = cell; cell.SendEvent(d); }
    using (var u = UnityEngine.UIElements.PointerUpEvent.GetPooled()) { u.target = cell; cell.SendEvent(u); }
    using (var c = UnityEngine.UIElements.ClickEvent.GetPooled()) { c.target = cell; cell.SendEvent(c); }
    sb.Append("bound after clicking a grid cell = ").Append((curP.GetValue(win) as UnityEngine.Object) == null ? "<none>" : (curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
}
return sb.ToString();
