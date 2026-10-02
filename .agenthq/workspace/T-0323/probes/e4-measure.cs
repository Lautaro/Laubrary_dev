var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow"); sb.Append("pos=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var of = e as UnityEditor.UIElements.ObjectField; if (of==null || !ZDrawn(of)) continue;
  foreach (var d in ZAll(of)) { var l=d as UnityEngine.UIElements.Label; if (l!=null && ZDrawn(l) && l.text!=null && l.text.Contains("Border Font")) {
    sb.Append("fieldW=").Append(of.worldBound.width.ToString("F1")).Append(" parentContentW=").Append(ZContentWorld(of.hierarchy.parent).width.ToString("F1"))
      .Append(" lblNeed=").Append(ZNeed(l).ToString("F1")).Append(" lblHave=").Append(ZHave(l).ToString("F1"))
      .Append(" overflow=").Append(l.resolvedStyle.textOverflow).Append(" ws=").Append(l.resolvedStyle.whiteSpace).Append("\n"); } }
}
return sb.ToString();
