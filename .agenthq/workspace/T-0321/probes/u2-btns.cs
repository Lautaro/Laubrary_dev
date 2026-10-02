var w = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
sb.Append("pos=").Append(w.position).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null) continue;
  sb.Append(ZDrawn(b)?"* ":"  ").Append("'").Append(b.text).Append("' ").Append(b.worldBound).Append("\n"); }
return sb.ToString();
