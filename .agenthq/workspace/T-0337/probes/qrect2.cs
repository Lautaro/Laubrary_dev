var w = ZWin("ShaperWindow"); if(w==null) return "no window";
var sb=new System.Text.StringBuilder();
foreach (var want in new string[]{"Gob count","Gob size","Aim direction","Solidity","Ignition"})
  foreach (var e in ZAll(w.rootVisualElement)) { if(ZCaption(e)!=want||!ZDrawn(e)||e is UnityEngine.UIElements.Label) continue;
    sb.Append(want).Append(' ').Append(e.worldBound).Append(" enabled=").Append(e.enabledInHierarchy).Append('\n'); break; }
return sb.ToString();
