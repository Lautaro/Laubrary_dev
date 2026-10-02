// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
System.Func<UnityEngine.UIElements.VisualElement,bool> ZClick = el => {
  if (el == null) return false;
  var c = el.worldBound.center;
  var pd = UnityEngine.UIElements.PointerDownEvent.GetPooled();
  var pdT = typeof(UnityEngine.UIElements.PointerDownEvent);
  var setPos = pdT.GetProperty("position"); var setLocal = pdT.GetProperty("localPosition"); var setBtn = pdT.GetProperty("button"); var setId = pdT.GetProperty("pointerId");
  if (setPos != null && setPos.GetSetMethod(true) != null) setPos.GetSetMethod(true).Invoke(pd, new object[]{ new UnityEngine.Vector3(c.x, c.y, 0) });
  if (setLocal != null && setLocal.GetSetMethod(true) != null) setLocal.GetSetMethod(true).Invoke(pd, new object[]{ new UnityEngine.Vector3(el.worldBound.width/2f, el.worldBound.height/2f, 0) });
  if (setBtn != null && setBtn.GetSetMethod(true) != null) setBtn.GetSetMethod(true).Invoke(pd, new object[]{ 0 });
  if (setId != null && setId.GetSetMethod(true) != null) setId.GetSetMethod(true).Invoke(pd, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
  pd.target = el; using (pd) el.SendEvent(pd);
  var pu = UnityEngine.UIElements.PointerUpEvent.GetPooled();
  var puT = typeof(UnityEngine.UIElements.PointerUpEvent);
  var sp = puT.GetProperty("position"); var sl = puT.GetProperty("localPosition"); var sb2 = puT.GetProperty("button"); var si = puT.GetProperty("pointerId");
  if (sp != null && sp.GetSetMethod(true) != null) sp.GetSetMethod(true).Invoke(pu, new object[]{ new UnityEngine.Vector3(c.x, c.y, 0) });
  if (sl != null && sl.GetSetMethod(true) != null) sl.GetSetMethod(true).Invoke(pu, new object[]{ new UnityEngine.Vector3(el.worldBound.width/2f, el.worldBound.height/2f, 0) });
  if (sb2 != null && sb2.GetSetMethod(true) != null) sb2.GetSetMethod(true).Invoke(pu, new object[]{ 0 });
  if (si != null && si.GetSetMethod(true) != null) si.GetSetMethod(true).Invoke(pu, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
  pu.target = el; using (pu) el.SendEvent(pu);
  return true;
};
// One step of the shape-family walk: hash what the stage is showing now (the previous pick's result),
// then open the picker and click the entry named by T320.pick.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF = null; for (var t = win.GetType(); t != null && stageF == null; t = t.BaseType) stageF = t.GetField("stage", BFi);
var stage = stageF.GetValue(win);
string hash = "?";
if (stage != null) {
  var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  var tex = texF != null ? texF.GetValue(stage) as UnityEngine.Texture2D : null;
  if (tex != null) {
    var px = tex.GetPixels32();
    unchecked { uint h = 2166136261u; foreach (var p in px) { h = (h ^ p.r) * 16777619u; h = (h ^ p.g) * 16777619u; h = (h ^ p.b) * 16777619u; h = (h ^ p.a) * 16777619u; } hash = h.ToString("X8") + " " + tex.width + "x" + tex.height; }
  } else hash = "no tex";
}
string want = UnityEditor.EditorPrefs.GetString("T320.pick","");
if (want.Length == 0) return "hash=" + hash + " (no pick requested)";
// open the picker
var secT = ZType("ZuiSection"); UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-section__title") && l.text == "Shape") { shapeSec = e; break; } }
  if (shapeSec != null) break; }
if (shapeSec == null) return "hash=" + hash + " | no Shape section";
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(shapeSec)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.hierarchy.childCount > 0) { pick = b; break; } }
if (pick == null) return "hash=" + hash + " | no picker button";
ZClick(pick);
// find the entry row whose label matches, inside the column whose header matches T320.col
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "hash=" + hash + " | menu did not open";
string wantCol = UnityEditor.EditorPrefs.GetString("T320.col","");
UnityEngine.UIElements.VisualElement target = null; string got = "";
foreach (var e in ZAll(scrim)) {
  if (!e.ClassListContains("zui-menu__item")) continue;
  string label = null;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.text != "✓" && !string.IsNullOrEmpty(l.text)) { label = l.text; break; } }
  if (label != want) continue;
  string col = "";
  for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent) {
    foreach (var c in p.hierarchy.Children()) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-menu__section")) { col = l.text; break; } }
    if (col.Length > 0) break; }
  got += col + "|";
  if (wantCol.Length == 0 || col == wantCol) { target = e; break; }
}
if (target == null) return "hash=" + hash + " | entry '" + want + "' not found (cols seen: " + got + ")";
ZClick(target);
return "hash=" + hash + " | picked '" + want + "' from '" + wantCol + "'";
