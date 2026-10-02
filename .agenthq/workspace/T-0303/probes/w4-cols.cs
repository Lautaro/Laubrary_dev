var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
foreach (var v in all) if (v.GetType().Name == "ZuiColumnFlow")
{
    sb.Append("flow world=").Append(v.worldBound.ToString()).Append(" children=").Append(v.childCount).Append("\n");
    for (int i = 0; i < v.childCount; i++)
    {
        var c = v[i];
        sb.Append("  child[").Append(i).Append("] ").Append(c.GetType().Name).Append(" world=").Append(c.worldBound.ToString()).Append(" children=").Append(c.childCount).Append("\n");
        for (int j = 0; j < c.childCount; j++)
            sb.Append("      col[").Append(j).Append("] world=").Append(c[j].worldBound.ToString()).Append(" units=").Append(c[j].childCount).Append("\n");
    }
    break;
}
return sb.ToString();
