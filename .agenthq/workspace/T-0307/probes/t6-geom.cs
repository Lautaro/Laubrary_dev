var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb) || lb.text == null || !lb.text.StartsWith("Appearance order")) continue;
    var e = (UnityEngine.UIElements.VisualElement)lb;
    sb.Append("label wb=").Append(lb.worldBound).Append(" enabled=").Append(lb.enabledInHierarchy).Append("\n");
    var p = lb.parent; int depth = 0;
    while (p != null && depth < 8)
    {
        sb.Append("  ancestor[").Append(depth).Append("] ").Append(p.GetType().Name).Append(" wb=").Append(p.worldBound).Append("\n");
        p = p.parent; depth++;
    }
    sb.Append("window root wb=").Append(win.rootVisualElement.worldBound).Append("\n");
    break;
}
return sb.ToString();
