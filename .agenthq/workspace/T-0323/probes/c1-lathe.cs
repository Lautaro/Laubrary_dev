var sb=new System.Text.StringBuilder();
sb.Append(ZBind("LatheWindow","Assets/Shaper/AuditT323Lathe.asset")).Append("\n");
var w=ZWin("LatheWindow"); w.position=new Rect(20,20,820,880); w.titleContent=new GUIContent("LatheWindow");
var secT=ZType("ZuiSection"); var isOpen=secT.GetProperty("IsOpen"); int n=0;
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e,true); n++; }
w.Repaint();
string rep = ZAudit(w,"LatheWindow");
sb.Append("sections=").Append(n).Append(" ");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount.ContainsKey(k)?ZCount[k]:-1).Append(" ");
sb.Append("\n");
// find the X/Y/Z label rows
foreach (var e in ZAll(w.rootVisualElement)) {
  var te = e as UnityEngine.UIElements.TextElement; if (te==null) continue;
  if (te.text=="X"||te.text=="Y"||te.text=="Z"||te.text=="Position"||te.text=="Rotation"||te.text=="Scale") {
    if (!ZDrawn(te)) continue;
    var b=te.worldBound;
    sb.Append("LBL '").Append(te.text).Append("' at ").Append(b.x.ToString("F1")).Append(",").Append(b.y.ToString("F1"))
      .Append(" ").Append(b.width.ToString("F1")).Append("x").Append(b.height.ToString("F1"))
      .Append(" need=").Append(ZNeed(te).ToString("F1")).Append("\n");
  }
}
sb.Append(ZDump("audit-LatheWindow", rep)).Append("\n");
return sb.ToString();
