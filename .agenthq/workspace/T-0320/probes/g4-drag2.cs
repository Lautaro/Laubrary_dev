var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo vsF = null, lpF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (vsF==null) vsF = t.GetField("verticalSplitter", BFi); if (lpF==null) lpF = t.GetField("leftPaneWidth", BFi); }
var splitter = vsF.GetValue(win) as UnityEngine.UIElements.VisualElement;
var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.PointerCaptureHelper.CapturePointer(splitter, UnityEngine.UIElements.PointerId.mousePointerId);
sb.AppendLine("captured=" + UnityEngine.UIElements.PointerCaptureHelper.HasPointerCapture(splitter, UnityEngine.UIElements.PointerId.mousePointerId));
var pmT = typeof(UnityEngine.UIElements.PointerMoveEvent);
var dpProp = pmT.GetProperty("deltaPosition");
var dpSet = dpProp != null ? dpProp.GetSetMethod(true) : null;
var pidProp = pmT.GetProperty("pointerId");
var pidSet = pidProp != null ? pidProp.GetSetMethod(true) : null;
sb.AppendLine("dpSet=" + (dpSet!=null) + " pidSet=" + (pidSet!=null));
for (int i=0;i<40;i++) {
  var pm = UnityEngine.UIElements.PointerMoveEvent.GetPooled();
  if (pidSet != null) pidSet.Invoke(pm, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
  if (dpSet != null) dpSet.Invoke(pm, new object[]{ new UnityEngine.Vector3(100f,0f,0f) });
  pm.target = splitter;
  using (pm) splitter.SendEvent(pm);
}
UnityEngine.UIElements.PointerCaptureHelper.ReleasePointer(splitter, UnityEngine.UIElements.PointerId.mousePointerId);
sb.AppendLine("after drag leftPaneWidth=" + lpF.GetValue(win));
return sb.ToString();
