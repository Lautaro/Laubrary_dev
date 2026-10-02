var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb) || lb.text == null) continue;
    if (!lb.text.StartsWith("Intensity") && !lb.text.StartsWith("Rim") && !lb.text.StartsWith("Spec")) continue;
    sb.Append("'").Append(lb.text).Append("'  len=").Append(lb.text.Length).Append(" w=").Append(lb.resolvedStyle.width.ToString("F1")).Append(" tip=").Append(lb.tooltip == null ? "" : (lb.tooltip.Length>50?lb.tooltip.Substring(0,50):lb.tooltip)).Append("\n");
}
return sb.ToString();
