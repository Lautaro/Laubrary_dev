var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
var secT = ZType("ZuiSection");
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!secT.IsInstanceOfType(e)) continue;
  string title=null; foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && !string.IsNullOrEmpty(l.text) && l.text!="?") { title=l.text; break; } }
  sb.Append("SECTION '").Append(title).Append("' drawn=").Append(ZDrawn(e)).Append(" wb=").Append(e.worldBound).Append("\n");
  // header row
  var hdr = e.hierarchy.childCount>0 ? e.hierarchy[0] : null;
  if (hdr!=null) sb.Append("   hdr ").Append(hdr.GetType().Name).Append(" wb=").Append(hdr.worldBound).Append(" children=").Append(hdr.hierarchy.childCount).Append("\n");
}
return sb.ToString();
