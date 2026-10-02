// A field label that repeats the title of the box it sits in says nothing twice (UI guide: "Never title a
// box that holds exactly one field ... says nothing twice").  Reported per window, with the box's child
// count so a box that genuinely groups several fields can be told from one that just echoes its own name.
var sb = new System.Text.StringBuilder();
foreach (var wn in new string[]{ "ShaperWindow", "PyreWindow", "ChunkWindow" }) {
  var win = ZWin(wn); if (win == null) { sb.AppendLine(wn + ": not open"); continue; }
  int hits = 0;
  foreach (var e in ZAll(win.rootVisualElement)) {
    if (!ZDrawn(e)) continue;
    if (!e.ClassListContains("zui-field__label")) continue;
    var te = e as UnityEngine.UIElements.TextElement; if (te == null || string.IsNullOrEmpty(te.text)) continue;
    for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) {
      var bx = p as Laubrary.Zui.ZuiBox; if (bx == null) continue;
      if (!string.IsNullOrEmpty(bx.TitleText) && bx.TitleText.Trim() == te.text.Trim()) {
        hits++; sb.AppendLine("  ECHO " + wn + ": field '" + te.text + "' inside box '" + bx.TitleText + "' (box body children=" + bx.childCount + ")");
      }
      break;
    }
  }
  sb.AppendLine(wn + ": fieldLabelsEchoingTheirBox=" + hits);
}
return sb.ToString();
