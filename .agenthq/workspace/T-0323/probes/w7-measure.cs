var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te=e as UnityEngine.UIElements.TextElement;
  if (te!=null && te.text!=null && (te.text.StartsWith("Nothing to preview")||te.text.StartsWith("Pick a sprite"))) {
    sb.Append("TXT '").Append(te.text.Substring(0,Mathf.Min(30,te.text.Length))).Append("' rect=").Append(te.worldBound).Append(" align=").Append(te.resolvedStyle.unityTextAlign).Append("\n");
    var p=te.hierarchy.parent; for(int i=0;i<3&&p!=null;i++,p=p.hierarchy.parent) sb.Append("   P").Append(i).Append(" ").Append(p.GetType().Name).Append(" cls=").Append(ZCls(p)).Append(" ").Append(p.worldBound).Append("\n");
  }
  if (ZCls(e).Contains("zui-stage")) sb.Append("STAGE ").Append(e.GetType().Name).Append(" ").Append(e.worldBound).Append("\n");
}
// tags section
foreach (var e in ZAll(w.rootVisualElement)) { if (e.GetType().Name=="ZuiSection") { var pi=e.GetType().GetProperty("IsOpen"); sb.Append("SECTION '").Append(ZFirstText(e)).Append("' open=").Append(pi!=null?pi.GetValue(e):null).Append(" h=").Append(e.worldBound.height.ToString("F0")).Append("\n"); } }
// play button
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text=="Play"||b.text=="Reverse")) sb.Append("BTN '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" tip=").Append(ZTip(b)).Append("\n"); }
return sb.ToString();
