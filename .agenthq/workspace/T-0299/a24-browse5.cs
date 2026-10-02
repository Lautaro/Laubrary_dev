var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Reflection.FieldInfo brF = null;
for (var t = WT; t != null && brF == null; t = t.BaseType) brF = t.GetField("browsing", BFi);
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<string, UnityEngine.UIElements.VisualElement> Find = name => {
    var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l);
    foreach (var v in l) if (v.ClassListContains("zui-cell") && !string.IsNullOrEmpty(v.tooltip) && v.tooltip.StartsWith(name + " ")) return v;
    return null; };
System.Action<UnityEngine.UIElements.VisualElement, int> Poke = (el, clicks) => {
    var t = typeof(UnityEngine.UIElements.PointerDownEvent);
    using (var e = UnityEngine.UIElements.PointerDownEvent.GetPooled())
    {
        e.target = el;
        System.Reflection.PropertyInfo bp = null, cp = null;
        for (var bt = t; bt != null; bt = bt.BaseType) { if (bp == null) bp = bt.GetProperty("button", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); if (cp == null) cp = bt.GetProperty("clickCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); }
        if (bp != null && bp.CanWrite) bp.SetValue(e, 0);
        if (cp != null && cp.CanWrite) cp.SetValue(e, clicks);
        el.SendEvent(e);
    } };
sb.Append("browsing=").Append(brF.GetValue(win)).Append(" bound=").Append((curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
var c = Find("New Shaper");
sb.Append("cell found=").Append(c != null).Append("\n");
if (c != null)
{
    Poke(c, 2);
    var now = curP.GetValue(win) as UnityEngine.Object;
    sb.Append("after DOUBLE click: bound=").Append(now == null ? "<none>" : now.name).Append(" browsing=").Append(brF.GetValue(win)).Append("\n");
}
sb.Append("cell tooltip text: ").Append(Find("New Shaper") == null ? "?" : Find("New Shaper").tooltip).Append("\n");
return sb.ToString();
