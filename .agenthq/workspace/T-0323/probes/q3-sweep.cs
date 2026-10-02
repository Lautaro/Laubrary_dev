var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","AnimationAsepriteWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  var secT=ZType("ZuiSection"); var sOpen=secT.GetProperty("IsOpen");
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) sOpen.SetValue(e,true);
  var boxT=ZType("ZuiBox"); var bOpen=boxT.GetProperty("IsOpen");
  foreach (var e in ZAll(w.rootVisualElement)) if (boxT.IsInstanceOfType(e)) bOpen.SetValue(e,true);
  w.Repaint();
}
return "opened all";
