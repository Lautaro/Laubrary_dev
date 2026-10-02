var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","LauminationBuilderWindow","CartographerWindow"}) {
  var w = ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  var all = ZAll(w.rootVisualElement);
  int total=0, midRow=0, padded=0; string ex="";
  foreach (var e in all) {
    var l = e as UnityEngine.UIElements.Label; if (l==null || !ZDrawn(l)) continue;
    if (!l.ClassListContains("zui-field__label")) continue;
    total++;
    var lb = l.worldBound;
    bool leftOf=false;
    foreach (var o in all) {
      if (o==l || !ZDrawn(o)) continue;
      if (o.Contains(l) || l.Contains(o)) continue;
      var ob=o.worldBound;
      if (ob.xMax <= lb.x + 0.5f && ob.yMax > lb.y + 2f && ob.y < lb.yMax - 2f && ob.width>1f) { leftOf=true; break; } }
    if (!leftOf) continue;
    midRow++;
    float nat = l.MeasureTextSize(l.text ?? "", 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
    if (lb.width > nat + 6f) { padded++; if (padded<=6) ex += "'"+l.text+"' "+lb.width.ToString("F0")+"vs"+nat.ToString("F0")+" ; "; }
  }
  sb.Append(wn).Append(" labels=").Append(total).Append(" midRow=").Append(midRow).Append(" midRowPadded=").Append(padded).Append("  ").Append(ex).Append("\n");
}
return sb.ToString();
