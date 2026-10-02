var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","AnimationAsepriteWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  foreach (var e in ZAll(w.rootVisualElement)) {
    var of = e as UnityEditor.UIElements.ObjectField; if (of==null || !ZDrawn(of)) continue;
    UnityEngine.UIElements.Label lbl=null;
    foreach (var d in ZAll(of)) { var l=d as UnityEngine.UIElements.Label; if (l!=null && ZDrawn(l) && !string.IsNullOrEmpty(l.text) && l.text!=of.label) lbl=l; }
    if (lbl==null) continue;
    float need=ZNeed(lbl), have=ZHave(lbl);
    sb.Append(wn).Append(" | '").Append(of.label).Append("' -> '").Append(lbl.text).Append("' fieldW=").Append(of.worldBound.width.ToString("F1"))
      .Append(" lblNeed=").Append(need.ToString("F1")).Append(" lblHave=").Append(have.ToString("F1"))
      .Append(need>have+1.5f?"  ***TRUNCATED***":"").Append("\n");
  }
}
return sb.ToString();
