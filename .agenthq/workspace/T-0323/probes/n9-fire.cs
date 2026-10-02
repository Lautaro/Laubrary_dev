var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
foreach (var e in ZAll(w.rootVisualElement)) { if (!ZDrawn(e)) continue; var b=e as UnityEngine.UIElements.Button;
  if (b!=null && b.text!=null && (b.text.Contains("Fire Weapon")||b.text=="Trigger")) sb.Append("BTN '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append("\n   tip=").Append(ZTip(b)).Append("\n"); }
return sb.ToString();
