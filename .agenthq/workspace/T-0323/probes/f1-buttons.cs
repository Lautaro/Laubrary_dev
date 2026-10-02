var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ZoeWindow","MirageWindow","LarderWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  var secT=ZType("ZuiSection"); var isOpen=secT.GetProperty("IsOpen");
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  w.Repaint();
  sb.Append("=== ").Append(wn).Append(" pos=").Append(w.position).Append("\n");
}
return sb.ToString();
