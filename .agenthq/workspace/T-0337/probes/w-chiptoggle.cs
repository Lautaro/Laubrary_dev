// press the section chip named by T337.chip (whatever state it is in) — one per eval.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
string want = UnityEditor.EditorPrefs.GetString("T337.chip","Fill");
foreach (var e in ZAll(w.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b) || b.text != want) continue;
  bool inBar = false; for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p.GetType().Name == "ZuiSectionToggleBar") { inBar = true; break; }
  if (!inBar) continue;
  return "pressed '" + want + "' on=" + ZCls(b).Contains("zui-segmented__on") + " -> " + ZPress(w, b);
}
return "chip '" + want + "' not found";
