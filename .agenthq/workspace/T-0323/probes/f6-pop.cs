var sb=new System.Text.StringBuilder();
var w=ZWin("ZoeWindow");
if (w.rootVisualElement.panel==null) return "no panel";
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover")) {
  sb.Append("POPOVER ").Append(e.worldBound).Append(" vis=").Append(e.resolvedStyle.visibility).Append(" disp=").Append(e.resolvedStyle.display).Append(" kids=").Append(e.hierarchy.childCount).Append("\n");
  int n=0; foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && !string.IsNullOrEmpty(t.text)) { sb.Append("   '").Append(t.text).Append("' drawn=").Append(ZDrawn(t)).Append("\n"); if(++n>30) break; } }
}
sb.Append("winRect=").Append(w.position).Append("\n");
return sb.ToString();
