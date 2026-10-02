var sw = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(sw.rootVisualElement)) { if (ZPath(e).IndexOf("ViewBar")<0) continue;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b)) sb.Append("btn '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" tip='").Append(b.tooltip).Append("'\n"); }
return sb.ToString();
