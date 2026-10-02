var sb=new System.Text.StringBuilder();
var w = ZWin("LauminationBuilderWindow");
sb.Append("win=").Append(w!=null?w.position.ToString():"NULL").Append("\n");
if (w!=null) { int n=0; foreach (var e in ZAll(w.rootVisualElement)) { var l=e as UnityEngine.UIElements.Label; if (l==null||string.IsNullOrEmpty(l.text)) continue; n++; if (l.text.Contains("Canvas")||l.text.Contains("·")) sb.Append("L '").Append(l.text).Append("' tip='").Append(l.tooltip).Append("'\n"); } sb.Append("labels=").Append(n).Append("\n"); }
return sb.ToString();
