var sb = new System.Text.StringBuilder();
foreach (var wn in new string[]{ "ShaperWindow", "PyreWindow", "ChunkWindow" }) {
  var win = ZWin(wn); if (win == null) { sb.AppendLine(wn + ": not open"); continue; }
  int raw = 0, allowed = 0;
  foreach (var e in ZAll(win.rootVisualElement)) {
    if (!ZDrawn(e)) continue;
    if (!(e is UnityEngine.UIElements.Toggle)) continue;
    if (e.ClassListContains("zui-audit-allow-toggle")) { allowed++; continue; }
    raw++;
    if (raw < 12) sb.AppendLine("  RAW " + wn + " '" + ZCaption(e) + "' cls=" + ZCls(e) + " path=" + ZPath(e) + " rect=" + e.worldBound);
  }
  sb.AppendLine(wn + ": rawToggles=" + raw + " allowed=" + allowed);
}
return sb.ToString();
