// A box/section whose title repeats its nearest titled ancestor's title says nothing twice (UI guide,
// "Labeling — tooltip, not title; never a redundant title").  Report every such pair, per window.
var sb = new System.Text.StringBuilder();
foreach (var wn in new string[]{ "ShaperWindow", "PyreWindow", "ChunkWindow" }) {
  var win = ZWin(wn); if (win == null) { sb.AppendLine(wn + ": not open"); continue; }
  int dup = 0, titled = 0;
  System.Func<UnityEngine.UIElements.VisualElement,string> titleOf = e => {
    // a ZuiBox/Foldout/ZuiSection title: the .zui-box__title label, the Foldout's text, or the section's header text
    if (e is UnityEngine.UIElements.Foldout f) return f.text;
    foreach (var c in ZAll(e)) {
      if (c == e) continue;
      if (c.ClassListContains("zui-box__title") || c.ClassListContains("zui-section__title")) { var te = c as UnityEngine.UIElements.TextElement; if (te != null) return te.text; }
    }
    return null;
  };
  foreach (var e in ZAll(win.rootVisualElement)) {
    if (!ZDrawn(e)) continue;
    bool isBox = e.ClassListContains("zui-box") || e is UnityEngine.UIElements.Foldout || e.GetType().Name == "ZuiSection" || e.GetType().Name == "ZuiBox";
    if (!isBox) continue;
    string t = titleOf(e); if (string.IsNullOrEmpty(t)) continue; titled++;
    for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) {
      bool pb = p.ClassListContains("zui-box") || p is UnityEngine.UIElements.Foldout || p.GetType().Name == "ZuiSection" || p.GetType().Name == "ZuiBox";
      if (!pb) continue;
      string pt = titleOf(p); if (string.IsNullOrEmpty(pt)) continue;
      if (pt.Trim() == t.Trim()) { dup++; if (dup < 20) sb.AppendLine("  DUP " + wn + ": '" + t + "' inside '" + pt + "' — " + e.GetType().Name + " in " + p.GetType().Name + " rect=" + e.worldBound); }
      break;
    }
  }
  sb.AppendLine(wn + ": titledBoxes=" + titled + " duplicateTitleNestings=" + dup);
}
return sb.ToString();
