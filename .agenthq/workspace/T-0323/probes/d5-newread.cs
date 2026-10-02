var sb=new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T323.auditWin","LatheWindow");
var w = ZWin(wn); if (w==null) return "no "+wn;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te=e as UnityEngine.UIElements.TextElement;
  bool ctrl=ZIsCtrl(e);
  if (!ctrl && te==null) continue;
  string t = ZOwnText(e); string tip=ZTip(e);
  if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(tip)) continue;
  if (e.worldBound.y > 120) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(t).Append("' en=").Append(e.enabledInHierarchy)
    .Append("\n   tip: ").Append((tip??"").Replace("\n"," ")).Append("\n");
}
return sb.ToString();
