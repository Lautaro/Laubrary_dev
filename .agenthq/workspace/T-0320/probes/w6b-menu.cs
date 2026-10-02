var sb = new System.Text.StringBuilder();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) {
  var n = w.GetType().Name;
  if (n.Contains("Popup") || n.Contains("Menu") || n.Contains("Zui")) sb.AppendLine("WIN " + n + " " + w.position);
}
var win = ZWin("ShaperWindow");
int buttons = 0; var names = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) { if (e.GetType().Name.Contains("Menu")) sb.AppendLine("MENU EL " + e.GetType().Name + " " + e.worldBound + " children=" + e.hierarchy.childCount); }
return sb.Length == 0 ? "no menu element found" : sb.ToString();
