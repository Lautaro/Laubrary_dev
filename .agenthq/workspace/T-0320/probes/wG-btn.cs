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
// Click a button by exact text, but only inside the window body (never the asset toolbar) and never a
// destructive one — the Delete confirm dialog wedges the editor (T-0318 feedback).
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
string want = UnityEditor.EditorPrefs.GetString("T320.btn","");
if (want.Length == 0 || want.Contains("Delete") || want.Contains("Remove")) return "refused '" + want + "'";
UnityEngine.UIElements.VisualElement toolbar = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEditor.UIElements.ObjectField && ZDrawn(e)) { toolbar = e.hierarchy.parent; break; }
UnityEngine.UIElements.Button hit = null;
foreach (var e in ZAll(win.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b) || b.text != want) continue;
  bool inToolbar = false; for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p == toolbar) { inToolbar = true; break; }
  if (inToolbar) continue;
  hit = b; break;
}
if (hit == null) return "no button '" + want + "' in the body";
// scroll it into view first — a Clickable ignores a press outside the panel (measured this session)
UnityEngine.UIElements.ScrollView sv = null;
for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p is UnityEngine.UIElements.ScrollView s) { sv = s; break; }
if (sv != null) { float local = hit.worldBound.y - sv.contentContainer.worldBound.y; sv.scrollOffset = new UnityEngine.Vector2(0, UnityEngine.Mathf.Max(0, local - 100f)); }
UnityEditor.EditorPrefs.SetString("T320.btnPending", want);
return "found '" + want + "' at " + hit.worldBound + (sv != null ? " (scrolled)" : "");
