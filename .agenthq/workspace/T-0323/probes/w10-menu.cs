var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover")) {
  sb.Append("POPOVER ").Append(e.worldBound).Append(" vis=").Append(e.resolvedStyle.visibility).Append("\n");
  if (e.worldBound.width>600) continue;
  int n=0; foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && !string.IsNullOrEmpty(t.text)) { sb.Append("  '").Append(t.text).Append("'"); if(++n%6==0) sb.Append("\n"); } }
}
sb.Append("\nwin=").Append(w.position).Append("\n");
return sb.ToString();
