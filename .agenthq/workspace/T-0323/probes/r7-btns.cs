var sb=new System.Text.StringBuilder();
var w=ZWin("ShaperWindow");
sb.Append("elements=").Append(ZAll(w.rootVisualElement).Count).Append(" pos=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b)) sb.Append("'").Append(b.text).Append("' "); }
sb.Append("\ntexts: ");
foreach (var e in ZAll(w.rootVisualElement)) { var t=e as UnityEngine.UIElements.TextElement; if (t!=null && ZDrawn(t) && !string.IsNullOrEmpty(t.text) && t.text.Length<26) sb.Append("'").Append(t.text).Append("' "); }
return sb.ToString();
