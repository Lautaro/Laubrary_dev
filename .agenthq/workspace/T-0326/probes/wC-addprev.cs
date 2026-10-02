// Press Mirage's own "Add Previewable" — the LauAssetBrowser popup this programme has never photographed.
var w = ZWin("MirageWindow"); if (w == null) return "no Mirage window";
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement))
{ var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text.Contains("Add Previewable")) { btn = b; break; } }
if (btn == null) return "no Add Previewable button";
w.Focus();
bool ok = ZClick(btn);
return "pressed Add Previewable -> " + ok + " at " + btn.worldBound;
