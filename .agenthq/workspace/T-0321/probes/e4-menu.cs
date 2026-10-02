var win = ZWin("ShaperWindow"); var root = win.rootVisualElement.panel.visualTree;
var sb = new System.Text.StringBuilder();
float ww = win.position.width, wh = win.position.height;
sb.Append("win=").Append(ww).Append("x").Append(wh).Append("\n");
foreach (var e in ZAll(root)) {
  var tn = e.GetType().Name;
  if (tn.Contains("Popover") || (ZCls(e)??"").Contains("popover")) {
    sb.Append(tn).Append(" cls=").Append(ZCls(e)).Append(" wb=").Append(e.worldBound)
      .Append(" vis=").Append(e.resolvedStyle.visibility).Append(" disp=").Append(e.resolvedStyle.display).Append("\n");
  }
}
// column headers
foreach (var e in ZAll(root)) { var l = e as UnityEngine.UIElements.Label; if (l==null||!ZDrawn(l)) continue;
  if (l.worldBound.y > 100 && (ZCls(l)??"").Contains("header")) sb.Append("HDR '").Append(l.text).Append("' ").Append(l.worldBound).Append("\n"); }
return sb.ToString();
