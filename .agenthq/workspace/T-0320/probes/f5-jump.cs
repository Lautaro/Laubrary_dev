// measure the focus-induced geometry jump for each .zui-kbd-focus control kind
var win = ZWin("ShaperWindow"); if (win==null) return "no window";
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
var kinds = new string[]{ "ZuiPad", "ZuiMicroMinMax", "ZuiValue2DControl" };
foreach (var k in kinds) {
  UnityEngine.UIElements.VisualElement t = null;
  foreach (var e in all) if (e.GetType().Name == k && ZDrawn(e) && !e.ClassListContains("unity-disabled")) { t = e; break; }
  if (t == null) { sb.AppendLine(k + ": none drawn"); continue; }
  var parent = t.hierarchy.parent;
  var before = t.worldBound; var pb = parent.worldBound;
  var sibBefore = new System.Collections.Generic.List<UnityEngine.Rect>();
  for (int i=0;i<parent.hierarchy.childCount;i++) sibBefore.Add(parent.hierarchy[i].worldBound);
  t.Focus(); t.panel.visualTree.Q<UnityEngine.UIElements.VisualElement>(); // force
  // layout is deferred; record and compare on the next probe instead — store ids
  UnityEditor.EditorPrefs.SetString("T320.jump." + k, before.ToString());
  sb.AppendLine(k + " before " + before + " bw=" + t.resolvedStyle.borderTopWidth);
}
return sb.ToString();
