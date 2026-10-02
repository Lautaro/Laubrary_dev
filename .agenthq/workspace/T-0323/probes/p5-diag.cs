var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || e.GetType().Name!="ZuiMicroSlider") continue;
  UnityEngine.UIElements.Label cap=null;
  foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.Label; if (t!=null && ZCls(t).Contains("zui-microslider__caption")) cap=t; }
  if (cap==null || cap.text==null || !cap.text.Contains("Curve")) continue;
  sb.Append("'").Append(cap.text).Append("' own=").Append(e.resolvedStyle.width.ToString("F1"))
    .Append(" capHave=").Append(cap.contentRect.width.ToString("F1")).Append(" capNeed=").Append(ZNeed(cap).ToString("F1"))
    .Append("\n  parent=").Append(e.hierarchy.parent.GetType().Name).Append(" cls=").Append(ZCls(e.hierarchy.parent))
    .Append(" contentW=").Append(ZContentWorld(e.hierarchy.parent).width.ToString("F1"))
    .Append(" dir=").Append(e.hierarchy.parent.resolvedStyle.flexDirection)
    .Append("\n  gp=").Append(e.hierarchy.parent.hierarchy.parent.GetType().Name).Append(" cls=").Append(ZCls(e.hierarchy.parent.hierarchy.parent)).Append(" contentW=").Append(ZContentWorld(e.hierarchy.parent.hierarchy.parent).width.ToString("F1"))
    .Append("\n");
  break;
}
return sb.ToString();
