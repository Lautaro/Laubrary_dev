// Drag the splitter fully right using the window's own PointerMove handler.
var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo vsF = null, lpF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (vsF==null) vsF = t.GetField("verticalSplitter", BFi); if (lpF==null) lpF = t.GetField("leftPaneWidth", BFi); }
var splitter = vsF.GetValue(win) as UnityEngine.UIElements.VisualElement;
if (splitter == null) return "no splitter";
var sb = new System.Text.StringBuilder();
sb.AppendLine("splitter " + splitter.worldBound + " leftPaneWidth=" + lpF.GetValue(win));
// synth a pointer-down on the splitter then a long move right, exactly as a mouse drag delivers them
var pd = UnityEngine.UIElements.PointerDownEvent.GetPooled();
pd.target = splitter;
using (pd) splitter.SendEvent(pd);
for (int i=0;i<40;i++) {
  var pm = UnityEngine.UIElements.PointerMoveEvent.GetPooled();
  pm.target = splitter;
  var f = typeof(UnityEngine.UIElements.PointerMoveEvent).GetField("<deltaPosition>k__BackingField", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  if (f == null) { var pi = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerMoveEvent>).GetProperty("deltaPosition"); if (pi != null && pi.CanWrite) pi.SetValue(pm, new UnityEngine.Vector3(100f,0f,0f)); }
  else f.SetValue(pm, new UnityEngine.Vector3(100f,0f,0f));
  using (pm) splitter.SendEvent(pm);
}
sb.AppendLine("after drag leftPaneWidth=" + lpF.GetValue(win));
return sb.ToString();
