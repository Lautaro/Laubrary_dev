var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
int lights = 0; bool removeEdge = false, edgeWidth = false, addEdge = false;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var b = e as UnityEngine.UIElements.Button;
  if (b != null) { if (b.text == "Remove edge") removeEdge = true; if (b.text == "Add edge") addEdge = true; if (b.text == "×" ) {} }
  var l = e as UnityEngine.UIElements.Label;
  if (l != null && l.ClassListContains("zui-box__title") && (l.text == "Key" || l.text.StartsWith("Light"))) lights++;
  if (l != null && l.text == "Width") edgeWidth = true;
}
// count light cards by their Directional/Point segmented pairs instead
int dirBtns = 0; foreach (var e in ZAll(win.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Directional") dirBtns++; }
sb.AppendLine("light cards (Directional buttons) = " + dirBtns);
sb.AppendLine("edge: AddEdge=" + addEdge + " RemoveEdge=" + removeEdge + " WidthField=" + edgeWidth);
return sb.ToString();
