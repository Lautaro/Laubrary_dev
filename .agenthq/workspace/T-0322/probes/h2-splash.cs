var sb=new System.Text.StringBuilder();
var w = ZWin("TextSplashWindow");
// the 't' transport field
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  if (e.worldBound.y>140 || e.worldBound.y<115) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cls=").Append(ZCls(e)).Append(" wb=").Append(e.worldBound).Append(" tip='").Append((ZTip(e)??"").Substring(0, Mathf.Min(60,(ZTip(e)??"").Length))).Append("'\n"); }
sb.Append("--- ease rows\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e) || !ZCls(e).Contains("zui-radio")) continue;
  var kids=new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); ZWalk(e,kids);
  string names=""; float maxX=0; int rows=1; float y0=-1;
  foreach (var k in kids) { var b=k as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
    if (y0<0) y0=b.worldBound.y; else if (Mathf.Abs(b.worldBound.y-y0)>3f) { rows=2; }
    names += b.text+"("+b.worldBound.xMax.ToString("F0")+") "; maxX=Mathf.Max(maxX,b.worldBound.xMax); }
  if (names.Length<2) continue;
  sb.Append("radio rows=").Append(rows).Append(" parentW=").Append(ZContentWorld(e.parent).width.ToString("F0")).Append(" own=").Append(e.worldBound.width.ToString("F0")).Append(" :: ").Append(names).Append("\n");
}
return sb.ToString();
