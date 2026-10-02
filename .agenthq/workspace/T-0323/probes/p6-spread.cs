var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || e.GetType().Name!="ZuiMicroSlider") continue;
  UnityEngine.UIElements.Label cap=null;
  foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.Label; if (t!=null && ZCls(t).Contains("zui-microslider__caption")) cap=t; }
  if (cap==null || cap.text!="Spread Progress") continue;
  sb.Append("own=").Append(e.resolvedStyle.width.ToString("F1")).Append(" capHave=").Append(cap.contentRect.width.ToString("F1")).Append(" capNeed=").Append(ZNeed(cap).ToString("F1")).Append("\n");
  for (var p=e.hierarchy.parent; p!=null; p=p.hierarchy.parent) { sb.Append("  ").Append(p.GetType().Name).Append(" cls=").Append(ZCls(p)).Append(" contentW=").Append(p.contentRect.width.ToString("F1")).Append("\n"); if (p.contentRect.width>200) break; }
}
return sb.ToString();
