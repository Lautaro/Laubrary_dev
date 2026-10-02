var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<UnityEngine.UIElements.VisualElement, string> Cell = null;

System.Func<string, UnityEngine.UIElements.VisualElement> Find = name => {
    var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l);
    foreach (var v in l) if (v.ClassListContains("zui-cell") && !string.IsNullOrEmpty(v.tooltip) && v.tooltip.StartsWith(name + " ")) return v;
    return null; };

System.Action<UnityEngine.UIElements.VisualElement, int> Poke = (el, clicks) => {
    var t = typeof(UnityEngine.UIElements.PointerDownEvent);
    using (var e = UnityEngine.UIElements.PointerDownEvent.GetPooled())
    {
        e.target = el;
        var bp = t.GetProperty("button", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        for (var bt = t; bt != null && bp == null; bt = bt.BaseType) bp = bt.GetProperty("button", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var cp = t.GetProperty("clickCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        for (var bt = t; bt != null && cp == null; bt = bt.BaseType) cp = bt.GetProperty("clickCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (bp != null && bp.CanWrite) bp.SetValue(e, 0);
        if (cp != null && cp.CanWrite) cp.SetValue(e, clicks);
        sb.Append("   sent button=").Append(e.button).Append(" clickCount=").Append(e.clickCount).Append("\n");
        el.SendEvent(e);
    } };

sb.Append("bound before        = ").Append((curP.GetValue(win) as UnityEngine.Object).name).Append("\n");
var c1 = Find("AuditA24Doc");
sb.Append("cell found=").Append(c1 != null).Append("\n");
if (c1 != null) { Poke(c1, 1); sb.Append("after SINGLE click  = ").Append((curP.GetValue(win) as UnityEngine.Object).name).Append("\n"); }
var c2 = Find("AuditA24b");
if (c2 != null) { Poke(c2, 2); }
var now = curP.GetValue(win) as UnityEngine.Object;
sb.Append("after DOUBLE click  = ").Append(now == null ? "<none>" : now.name).Append("\n");
var brF = WT.GetField("browsing", BFi);
for (var t = WT; t != null && brF == null; t = t.BaseType) brF = t.GetField("browsing", BFi);
sb.Append("browser still open  = ").Append(brF == null ? "?" : brF.GetValue(win).ToString()).Append("\n");
return sb.ToString();
