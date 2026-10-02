var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow"); if (w==null) return "NO WINDOW";
sb.Append("pos=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te=e as UnityEngine.UIElements.TextElement;
  if (te!=null && !string.IsNullOrEmpty(te.text)) sb.Append("TXT '").Append(te.text).Append("' ").Append(ZIsCtrl(e.hierarchy.parent??e)?"(in ctrl)":"").Append(" y=").Append(te.worldBound.y.ToString("F0")).Append("\n");
  else if (ZIsLeafCtrl(e)) sb.Append("CTL ").Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' en=").Append(e.enabledInHierarchy).Append(" y=").Append(e.worldBound.y.ToString("F0")).Append("\n");
}
return sb.ToString();
