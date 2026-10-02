// For every MicroSlider/MicroMinMax caption: the string, its length, and whether it FITS its own box.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder(); int over=0, clipped=0, n=0;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var tn = e.GetType().Name; if (tn != "ZuiMicroSlider" && tn != "ZuiMicroMinMax") continue;
  foreach (var c in ZAll(e)) { if (!ZCls(c).Contains("__caption")) continue;
    var l = c as UnityEngine.UIElements.Label; if (l == null || string.IsNullOrEmpty(l.text)) continue;
    n++;
    float need = l.MeasureTextSize(l.text, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
    float have = l.contentRect.width;
    bool cut = need > have + 1.5f; if (cut) clipped++;
    if (l.text.Length > 13) { over++;
      sb.Append(cut ? "CLIPPED " : "fits    ").Append("(").Append(l.text.Length).Append(" ch) '").Append(l.text)
        .Append("' need=").Append(need.ToString("F1")).Append(" have=").Append(have.ToString("F1"))
        .Append(" sliderW=").Append(e.worldBound.width.ToString("F1")).Append("\n"); }
    break; } }
return "microCaptions=" + n + " over13=" + over + " clipped=" + clipped + "\n" + sb.ToString();
