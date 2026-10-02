var sb=new System.Text.StringBuilder();
foreach (var n in new string[]{"ChunkWindow","ZoeWindow"}) {
  var w = ZWin(n); string rep = ZAudit(w, n);
  sb.Append(n).Append(" ");
  foreach (var k in new string[]{"elements","captionShort","overflowParentX","overflowWindow","noTooltip"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
  sb.Append("\n");
}
// the cell name label's styling
var cw = ZWin("ChunkWindow");
foreach (var e in ZAll(cw.rootVisualElement)) { if (!(ZCls(e)??"").Contains("zui-cell__name")) continue;
  var l = e as UnityEngine.UIElements.Label;
  sb.Append("CELL '").Append(l.text).Append("' w=").Append(e.worldBound.width).Append(" tip='").Append(ZTip(e)).Append("' overflow=").Append(e.resolvedStyle.textOverflow).Append(" wrap=").Append(e.resolvedStyle.whiteSpace).Append(" pos=").Append(e.resolvedStyle.unityTextOverflowPosition).Append("\n");
}
return sb.ToString();
