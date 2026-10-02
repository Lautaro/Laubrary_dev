var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
sb.Append("elements=").Append(all.Count).Append("\n");
foreach (var v in all)
    if (v is UnityEngine.UIElements.Button b)
        sb.Append("BTN '").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy).Append("\n");
foreach (var v in all)
{
    var n = v.GetType().Name;
    if (n.Contains("Stage") || n.Contains("Filmstrip") || n.Contains("Preview") || n.Contains("Transport"))
        sb.Append("EL ").Append(n).Append(" name=").Append(v.name).Append("\n");
}
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var f in WT.GetFields(BFi))
    if (f.Name.ToLower().Contains("play") || f.Name.ToLower().Contains("frame") || f.Name.ToLower().Contains("stage"))
        sb.Append("FIELD ").Append(f.Name).Append(" = ").Append(f.GetValue(win)).Append("\n");
return sb.ToString();
