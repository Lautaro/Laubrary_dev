var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  if (!ZCls(e).Contains("zui-microslider")) continue;
  if (e.GetType().Name!="ZuiMicroSlider") continue;
  string cap=""; foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && ZCls(t).Contains("caption")) cap=t.text; }
  UnityEngine.UIElements.TextElement ct=null; foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && ZCls(t).Contains("caption")) ct=t; }
  sb.Append("MS '").Append(cap).Append("' w=").Append(e.worldBound.width.ToString("F0"));
  if (ct!=null) sb.Append(" capNeed=").Append(ZNeed(ct).ToString("F1")).Append(" capHave=").Append(ZHave(ct).ToString("F1")).Append(ct!=null && ZNeed(ct)>ZHave(ct)+1.5f?"  CLIPPED":"");
  sb.Append(" tip=").Append((ZTip(e)??"").Substring(0,Mathf.Min(60,(ZTip(e)??"").Length))).Append("\n");
}
return sb.ToString();
