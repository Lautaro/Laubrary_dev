var sb = new System.Text.StringBuilder();
System.Type FindT(string n) { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { try { foreach (var t in a.GetTypes()) if (t.Name==n) return t; } catch {} } return null; }
var win = UnityEditor.EditorWindow.GetWindow(FindT("PyreWindow"));
System.Action<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> W = null;
W = (e,l) => { l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) W(e.hierarchy[i], l); };
System.Func<UnityEngine.UIElements.VisualElement,string> TextOf = v => { var p=v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public); if (p!=null && p.PropertyType==typeof(string)) { try { return (string)p.GetValue(v); } catch {} } return null; };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W(win.rootVisualElement, all);
foreach (var e in all) {
  if (!e.ClassListContains("zui-field")) continue;
  bool m=false; for (int i=0;i<e.hierarchy.childCount;i++) if (TextOf(e.hierarchy[i])=="Colour ramp") m=true;
  if (!m) continue;
  sb.Append("zui-field 'Colour ramp' wb=").Append(e.worldBound).Append(" style.height=").Append(e.style.height.ToString()).Append('\n');
  for (int i=0;i<e.hierarchy.childCount;i++) { var c=e.hierarchy[i]; sb.Append("  child ").Append(c.GetType().Name).Append(" \"").Append(TextOf(c)).Append("\" wb=").Append(c.worldBound).Append(" flexWrap=").Append(c.resolvedStyle.flexWrap).Append('\n');
    for (int j=0;j<c.hierarchy.childCount;j++) { var g=c.hierarchy[j]; sb.Append("      gc ").Append(g.GetType().Name).Append(" \"").Append(TextOf(g)).Append("\" wb=").Append(g.worldBound).Append('\n'); } }
  break;
}
return sb.ToString();
