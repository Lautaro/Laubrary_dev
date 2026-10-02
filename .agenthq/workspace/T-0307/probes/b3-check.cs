var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
int shown = 0, hidden = 0;
foreach (var v in all)
{
    if (v.GetType().Name != "ZuiSection") continue;
    bool vis = v.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.None;
    string title = "";
    var sub = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    Walk(v, sub);
    foreach (var c in sub) if (c is UnityEngine.UIElements.Label l2 && !string.IsNullOrEmpty(l2.text)) { title = l2.text; break; }
    if (vis) shown++; else hidden++;
    sb.Append(vis ? "SHOWN  " : "hidden ").Append(title).Append("  h=").Append(v.resolvedStyle.height.ToString("F0")).Append("\n");
}
sb.Append("sections shown=").Append(shown).Append(" hidden=").Append(hidden).Append(" elements=").Append(all.Count).Append("\n");
sb.Append("PREF=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<none>")).Append("\n");
return sb.ToString();
