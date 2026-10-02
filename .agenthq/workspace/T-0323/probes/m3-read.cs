var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow"); if (w==null) return "NO WINDOW";
w.position=new Rect(20,20,900,700); w.titleContent=new GUIContent("MirageWindow");
sb.Append("pos=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te=e as UnityEngine.UIElements.TextElement;
  if (te!=null && !string.IsNullOrEmpty(te.text)) sb.Append("TXT '").Append(te.text).Append("' y=").Append(te.worldBound.y.ToString("F0")).Append("\n");
}
return sb.ToString();
