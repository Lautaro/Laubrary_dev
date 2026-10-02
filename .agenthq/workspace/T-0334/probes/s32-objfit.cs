// Measure every ObjectField in Shaper: does the drawn asset-name label fit its box?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
sb.Append("window=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var f = e as UnityEditor.UIElements.ObjectField; if (f == null || !ZDrawn(f)) continue;
  UnityEngine.UIElements.Label lab = null;
  foreach (var d in ZAll(f)) { if (d.ClassListContains("unity-object-field-display__label")) { lab = d as UnityEngine.UIElements.Label; break; } }
  string txt = lab == null ? "<no label>" : lab.text;
  float need = lab == null ? 0f : lab.MeasureTextSize(txt ?? "", 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
  float have = lab == null ? 0f : lab.contentRect.width;
  float room = f.parent == null ? -1f : f.parent.contentRect.width;
  sb.Append(need > have + 1.5f ? "CLIPPED " : "ok      ")
    .Append("'").Append(txt).Append("' need=").Append(need.ToString("F1")).Append(" have=").Append(have.ToString("F1"))
    .Append(" fieldW=").Append(f.resolvedStyle.width.ToString("F1")).Append(" parentRoom=").Append(room.ToString("F1"))
    .Append(" | ").Append(ZPath(f)).Append("\n");
}
return sb.ToString();
