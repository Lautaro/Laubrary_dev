var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
  var cap = ZCaption(e);
  if (cap != "Alpha" && cap != "Life (frames)") continue;
  sb.AppendLine(e.GetType().Name + " '" + cap + "' " + e.worldBound + " cls=" + ZCls(e));
  for (int i=0;i<e.hierarchy.childCount;i++) { var c = e.hierarchy[i]; var te = c as UnityEngine.UIElements.TextElement;
    sb.AppendLine("   child " + i + " " + c.GetType().Name + " '" + (te!=null?te.text:"") + "' " + c.worldBound + " cls=" + ZCls(c) + (te!=null? " need=" + ZNeed(te).ToString("F1") + " have=" + ZHave(te).ToString("F1") : "")); }
}
// also list what sits in the Shape section body's first flow
foreach (var e in ZAll(win.rootVisualElement)) {
  if (e.GetType().Name != "ZuiMicroMinMax" || !ZDrawn(e)) continue;
  sb.AppendLine("MMX '" + ZCaption(e) + "' " + e.worldBound);
}
return sb.ToString();
