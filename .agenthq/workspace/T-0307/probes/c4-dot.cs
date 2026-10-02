var sb = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
foreach (var v in all)
{
    if (!(v is UnityEngine.UIElements.Label lb) || lb.text != "●") continue;
    var r = lb.resolvedStyle;
    sb.Append("dot width=").Append(r.width.ToString("F2"))
      .Append(" content=").Append(lb.contentRect.width.ToString("F2"))
      .Append(" padL=").Append(r.paddingLeft.ToString("F2")).Append(" padR=").Append(r.paddingRight.ToString("F2"))
      .Append(" bL=").Append(r.borderLeftWidth.ToString("F2")).Append(" bR=").Append(r.borderRightWidth.ToString("F2"))
      .Append(" font=").Append(r.fontSize.ToString("F1"))
      .Append(" need=").Append(lb.MeasureTextSize("●", 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x.ToString("F2"))
      .Append("\n");
    break;
}
return sb.ToString();
