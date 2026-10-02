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
UnityEngine.UIElements.VisualElement cell = null;
foreach (var v in all)
    if (!string.IsNullOrEmpty(v.tooltip) && v.tooltip.StartsWith("AuditA24Doc") && v.tooltip.Contains("double-click")) { cell = v; break; }
sb.Append("target cell = ").Append(cell == null ? "<none>" : cell.tooltip).Append("\n");
sb.Append("bound before = ").Append((curP.GetValue(win) as UnityEngine.Object) == null ? "<none>" : (curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
if (cell != null)
{
    var ccF = typeof(UnityEngine.UIElements.ClickEvent).GetProperty("clickCount",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    using (var c = UnityEngine.UIElements.ClickEvent.GetPooled())
    {
        c.target = cell;
        if (ccF != null && ccF.CanWrite) ccF.SetValue(c, 2);
        else
        {
            var bf = typeof(UnityEngine.UIElements.ClickEvent).BaseType;
            var f = bf.GetProperty("clickCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null && f.CanWrite) f.SetValue(c, 2);
        }
        sb.Append("clickCount sent = ").Append(c.clickCount).Append("\n");
        cell.SendEvent(c);
    }
    sb.Append("bound after double-click = ").Append((curP.GetValue(win) as UnityEngine.Object) == null ? "<none>" : (curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
    sb.Append("Selection.activeObject = ").Append(UnityEditor.Selection.activeObject == null ? "<none>" : UnityEditor.Selection.activeObject.name).Append("\n");
}
return sb.ToString();
