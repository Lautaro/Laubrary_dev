var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow");
var boxT=ZType("ZuiBox"); var bOpen=boxT.GetProperty("IsOpen"); int nb=0;
foreach (var e in ZAll(w.rootVisualElement)) if (boxT.IsInstanceOfType(e)) { bOpen.SetValue(e,true); nb++; }
var secT=ZType("ZuiSection"); var sOpen=secT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) sOpen.SetValue(e,true);
w.Repaint();
sb.Append("boxes=").Append(nb).Append("\n");
return sb.ToString();
