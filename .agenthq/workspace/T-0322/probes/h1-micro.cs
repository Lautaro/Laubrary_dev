var sb=new System.Text.StringBuilder();
var msT = ZType("ZuiMicroSlider"); var mmT = ZType("ZuiMicroMinMax"); var padT = ZType("ZuiValue2DControl");
int over=0, tot=0;
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","LauminationBuilderWindow","ChunkWindow","ZoeWindow","MirageWindow","CartographerWindow"}) {
  var w = ZWin(wn); if (w==null) continue;
  foreach (var e in ZAll(w.rootVisualElement)) {
    if (!ZDrawn(e)) continue;
    if (msT.IsInstanceOfType(e) || (mmT!=null && mmT.IsInstanceOfType(e))) {
      string lbl=null;
      foreach (var k in ZAll(e)) { var L=k as UnityEngine.UIElements.Label; if (L!=null && L.ClassListContains("zui-microslider__label")) { lbl=L.text; break; } }
      if (lbl==null) foreach (var k in ZAll(e)) { var L=k as UnityEngine.UIElements.Label; if (L!=null && !string.IsNullOrEmpty(L.text)) { lbl=L.text; break; } }
      tot++;
      if (lbl!=null && lbl.Length>13) { over++; sb.Append(wn).Append(" '").Append(lbl).Append("' len=").Append(lbl.Length).Append("\n"); } }
    if (padT!=null && padT.IsInstanceOfType(e)) sb.Append(wn).Append(" PAD ").Append(e.worldBound).Append(" cap='").Append(ZCaption(e)).Append("'\n");
  } }
sb.Append("microSliders=").Append(tot).Append(" labelsOver13=").Append(over);
return sb.ToString();
