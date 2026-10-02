var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"CartographerWindow","ShaperWindow"}) { var w=ZWin(wn); if (w==null) continue;
  foreach (var e in ZAll(w.rootVisualElement)) if (e is UnityEngine.UIElements.TextField tf && ZDrawn(tf))
    sb.Append(wn).Append(" tip='").Append(ZTip(tf)).Append("'\n"); }
return sb.ToString();
