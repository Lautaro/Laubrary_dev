var sb=new System.Text.StringBuilder();
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var n in new string[]{"ShaperWindow","PyreWindow","ChunkWindow","ZoeWindow","MirageWindow","LauminationBuilderWindow"}) {
  var w = ZWin(n); if (w==null) { sb.Append(n).Append(" (closed)\n"); continue; }
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  string rep = ZAudit(w, n+"-final");
  sb.Append(n).Append(" ");
  foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
  sb.Append("\n"); ZDump("audit-final-"+n, rep);
}
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
return sb.ToString();
