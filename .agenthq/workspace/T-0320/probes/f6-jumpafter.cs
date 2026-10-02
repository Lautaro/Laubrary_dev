var win = ZWin("ShaperWindow"); if (win==null) return "no window";
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
var f = win.rootVisualElement.panel.focusController.focusedElement as UnityEngine.UIElements.VisualElement;
foreach (var k in new string[]{ "ZuiPad", "ZuiMicroMinMax", "ZuiValue2DControl" }) {
  UnityEngine.UIElements.VisualElement t = null;
  foreach (var e in all) if (e.GetType().Name == k && ZDrawn(e) && !e.ClassListContains("unity-disabled")) { t = e; break; }
  if (t == null) { sb.AppendLine(k + ": none"); continue; }
  sb.AppendLine(k + " now " + t.worldBound + " bw=" + t.resolvedStyle.borderTopWidth + " wasFocusedTarget=" + object.ReferenceEquals(t,f) + " before=" + UnityEditor.EditorPrefs.GetString("T320.jump."+k,""));
}
sb.AppendLine("focused=" + (f==null?"null":f.GetType().Name));
return sb.ToString();
