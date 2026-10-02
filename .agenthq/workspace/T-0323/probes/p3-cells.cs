var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","PyreWindow"}) {
  var w=ZWin(wn); if (w==null) continue;
  int cells=0, thumbs=0, nothumb=0;
  foreach (var e in ZAll(w.rootVisualElement)) { if (!ZDrawn(e)) continue; var c=ZCls(e);
    if (c.Contains("zui-cell__thumb")) thumbs++;
    else if (c.Contains("zui-cell--nothumb")) { cells++; nothumb++; }
    else if (c.Contains("zui-cell")) cells++; }
  if (cells+thumbs>0) sb.Append(wn).Append(" cells=").Append(cells).Append(" thumbBoxes=").Append(thumbs).Append(" nameChips=").Append(nothumb).Append("\n");
}
return sb.ToString();
