// Open every fold in the window (ZuiBox toggles + Foldouts), so a walk sees the Advanced groups too.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
int opened = 0, seen = 0;
foreach (var e in ZAll(w.rootVisualElement)) {
  var t = e as UnityEngine.UIElements.Toggle; if (t == null) continue;
  if (!ZCls(t).Contains("zui-box__toggle") && !ZCls(t).Contains("unity-foldout__toggle")) continue;
  seen++;
  if (!t.value) { t.value = true; opened++; }
}
var f = ZType("ZuiAudit");
return "foldToggles=" + seen + " opened=" + opened;
