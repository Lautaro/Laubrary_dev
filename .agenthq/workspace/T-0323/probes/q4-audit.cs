var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","AnimationAsepriteWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  string rep=ZAudit(w,wn);
  sb.Append(wn).Append(" ");
  foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k.Replace("overflowParent","ovf").Replace("caption","cap")).Append("=").Append(ZCount.ContainsKey(k)?ZCount[k]:-1).Append(" ");
  sb.Append("\n");
  ZDump("audit2-"+wn, rep);
}
return sb.ToString();
