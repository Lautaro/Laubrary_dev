var sb=new System.Text.StringBuilder();
var w = ZWin("CartographerWindow");
sb.Append("all=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (e is UnityEngine.UIElements.TextField tf) sb.Append("TF drawn=").Append(ZDrawn(tf)).Append(" tip='").Append(ZTip(tf)).Append("'\n");
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b)) sb.Append("[").Append(b.text).Append("]"); }
return sb.ToString();
