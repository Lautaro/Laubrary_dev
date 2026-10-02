var sb=new System.Text.StringBuilder();
var w=ZWin("LatheWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  var te = e as UnityEngine.UIElements.TextElement; if (te==null) continue;
  if (!ZDrawn(te)) continue;
  if (te.text=="X"||te.text=="Y"||te.text=="Z"||te.text=="Position"||te.text=="Rotation"||te.text=="Scale") {
    var b=te.worldBound;
    sb.Append("LBL '").Append(te.text).Append("' x=").Append(b.x.ToString("F1")).Append(" y=").Append(b.y.ToString("F1"))
      .Append(" w=").Append(b.width.ToString("F1")).Append(" need=").Append(ZNeed(te).ToString("F1")).Append("\n");
  }
}
return sb.ToString();
