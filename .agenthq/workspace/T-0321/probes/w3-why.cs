var w = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var boxT = ZType("ZuiBox"); var titleP = boxT.GetProperty("TitleText");
foreach (var e in ZAll(w.rootVisualElement)) { if (!boxT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  string t = titleP.GetValue(e) as string;
  if (t!="Fracture 2" && t!="Source frame" && t!="Fracture") continue;
  sb.Append("BOX '").Append(t).Append("' w=").Append(e.worldBound.width.ToString("F1")).Append(" cls=").Append(ZCls(e))
    .Append(" flexGrow=").Append(e.resolvedStyle.flexGrow).Append(" alignSelf=").Append(e.resolvedStyle.alignSelf)
    .Append(" width=").Append(e.resolvedStyle.width).Append("\n");
  var p = e.hierarchy.parent;
  sb.Append("   parent ").Append(p.GetType().Name).Append(" cls=").Append(ZCls(p)).Append(" w=").Append(p.worldBound.width.ToString("F1"))
    .Append(" flexDir=").Append(p.resolvedStyle.flexDirection).Append(" alignItems=").Append(p.resolvedStyle.alignItems).Append(" children=").Append(p.hierarchy.childCount).Append("\n");
  for (int i=0;i<p.hierarchy.childCount && i<6;i++) sb.Append("     sib ").Append(p.hierarchy[i].GetType().Name).Append(" cls=").Append(ZCls(p.hierarchy[i])).Append(" w=").Append(p.hierarchy[i].worldBound.width.ToString("F1")).Append("\n");
}
return sb.ToString();
