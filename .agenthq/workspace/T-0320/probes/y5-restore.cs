var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  if (b.text == "✔" || b.text == "Height" || b.text == "Mask" || b.text == "Lighting") sb.AppendLine(b.text + " on=" + b.ClassListContains("zui-togglebutton--on")); }
return sb.ToString();
