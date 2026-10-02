var sb = new System.Text.StringBuilder();
var w = ZWin("TextSplashWindow");
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e) || !(e is UnityEditor.UIElements.ObjectField of)) continue;
    var lab = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(of, null, "unity-object-field-display__label");
    if (lab == null || lab.text == null || !lab.text.Contains("Border Font")) continue;
    float need = lab.MeasureTextSize(lab.text, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
    sb.Append("before own=").Append(of.resolvedStyle.width.ToString("F1")).Append(" have=").Append(lab.contentRect.width.ToString("F1"))
      .Append(" need=").Append(need.ToString("F1")).Append(" parentContentW=").Append(of.parent.contentRect.width.ToString("F1")).Append("\n");
    of.style.width = 373f;
    sb.Append("set 373; MeasureTextSize label text len=").Append(lab.text.Length).Append("\n");
}
return sb.ToString();
