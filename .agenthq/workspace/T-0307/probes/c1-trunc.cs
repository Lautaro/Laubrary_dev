// Truncation sweep: every laid-out TextElement whose text needs more width than its content box has.
// Unit is chosen by EditorPrefs "T0307.unit" = "shaper" | "pyre".
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
string unit = UnityEditor.EditorPrefs.GetString("T0307.unit", "shaper");
var wt = FT(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == wt) win = w0;
if (win == null) return "NO WINDOW " + unit;

var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" unit=").Append(unit)
  .Append(" pos=").Append(win.position).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

int textEls = 0, trunc = 0;
foreach (var v in all)
{
    var te = v as UnityEngine.UIElements.TextElement;
    if (te == null || string.IsNullOrEmpty(te.text)) continue;
    if (float.IsNaN(te.contentRect.width) || te.contentRect.width <= 0f) continue;
    if (te.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    if (te.resolvedStyle.whiteSpace == UnityEngine.UIElements.WhiteSpace.Normal) continue; // wrapping labels are not truncations
    textEls++;
    float need = te.MeasureTextSize(te.text, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined,
                                    0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
    float have = te.contentRect.width;
    if (need > have + 0.5f)
    {
        trunc++;
        sb.Append("TRUNC '").Append(te.text.Length > 40 ? te.text.Substring(0, 40) : te.text)
          .Append("' need=").Append(need.ToString("F1")).Append(" have=").Append(have.ToString("F1"))
          .Append(" short=").Append((need - have).ToString("F1"))
          .Append(" cls=").Append(te.GetType().Name)
          .Append(" parent=").Append(te.parent == null ? "<none>" : te.parent.GetType().Name)
          .Append("/").Append(te.parent == null || te.parent.parent == null ? "" : te.parent.parent.GetType().Name)
          .Append("\n");
    }
}
sb.Append("measured text elements=").Append(textEls).Append("  truncated=").Append(trunc).Append("\n");
return sb.ToString();
