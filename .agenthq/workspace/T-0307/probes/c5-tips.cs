// Reads back the edited tooltips out of the live window, and re-checks that no user-visible tooltip in the
// Shaper window still carries an internal design-doc code or a source file:line.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

var rx = new System.Text.RegularExpressions.Regex(@"\b(LR-\d|HS-\d|BC-\d|B9'|W\d\.\d)|\.cs:\d");
int tips = 0, bad = 0;
foreach (var v in all)
{
    if (string.IsNullOrEmpty(v.tooltip)) continue;
    tips++;
    if (rx.IsMatch(v.tooltip))
    {
        bad++;
        sb.Append("CODE IN TOOLTIP: ").Append(v.GetType().Name).Append(" | ")
          .Append(v.tooltip.Length > 130 ? v.tooltip.Substring(0, 130) + "…" : v.tooltip).Append("\n");
    }
}
sb.Append("tooltips=").Append(tips).Append(" carrying an internal code or file:line=").Append(bad).Append("\n\n");

foreach (var v in all)
{
    var t = v.tooltip;
    if (string.IsNullOrEmpty(t)) continue;
    if (t.StartsWith("Canvas pixels between consecutive") || t.StartsWith("The seed every deterministic")
        || t.StartsWith("The document's layers") || t.StartsWith("Canvas width in samples")
        || t.StartsWith("Canvas units per sample"))
        sb.Append(">> ").Append(t.Replace("\n", " / ")).Append("\n\n");
}
return sb.ToString();
