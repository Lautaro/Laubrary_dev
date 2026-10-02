// A19 — find every laid-out element whose content is taller than the box it was given, i.e. every place the
// window draws one control on top of another.
var SB = new System.Text.StringBuilder();
UnityEditor.EditorWindow win = null; foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == "PyreWindow") { foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == t) win = w0; }
var list = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
System.Action<UnityEngine.UIElements.VisualElement> W = null;
W = e => { list.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) W(e.hierarchy[i]); }; W(win.rootVisualElement);
System.Func<UnityEngine.UIElements.VisualElement, string> T = v =>
{ var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
  if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v); } catch { } } return null; };
System.Func<UnityEngine.UIElements.VisualElement, string> DeepText = null;
DeepText = v => { var t = T(v); if (!string.IsNullOrEmpty(t)) return t; for (int i = 0; i < v.childCount; i++) { var s = DeepText(v[i]); if (!string.IsNullOrEmpty(s)) return s; } return ""; };

int hits = 0;
foreach (var v in list)
{
    if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    var r = v.worldBound;
    if (r.height <= 0) continue;
    float childBottom = r.yMax;
    for (int i = 0; i < v.hierarchy.childCount; i++)
    {
        var c = v.hierarchy[i];
        if (c.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
        if (c.worldBound.yMax > childBottom) childBottom = c.worldBound.yMax;
    }
    float over = childBottom - r.yMax;
    if (over < 4f) continue;
    hits++;
    if (hits <= 25)
        SB.Append("OVERFLOW ").Append((int)over).Append("px  ").Append(v.GetType().Name)
          .Append(" [").Append(string.Join(",", v.GetClasses())).Append("]  @")
          .Append((int)r.x).Append(',').Append((int)r.y).Append(' ').Append((int)r.width).Append('x').Append((int)r.height)
          .Append("  first text: \"").Append(DeepText(v)).Append("\"\n");
}
SB.Append("elements overflowing their own box: ").Append(hits).Append(" of ").Append(list.Count).Append('\n');
return SB.ToString();
