var win = ZWin("ShaperWindow"); var vt = win.rootVisualElement.panel.visualTree;
foreach (var e in ZAll(vt)) if ((ZCls(e)??"").Contains("zui-popover__scrim")) ZClick(e);
// find a ZuiValue control
UnityEngine.UIElements.VisualElement val = null;
foreach (var e in ZAll(win.rootVisualElement)) { if (e.GetType().Name.Contains("ZuiValue") && ZDrawn(e) && !e.GetType().Name.Contains("2D")) { val = e; break; } }
if (val==null) return "no ZuiValue control";
var c = val.worldBound.center;
var pd = UnityEngine.UIElements.PointerDownEvent.GetPooled();
var t = typeof(UnityEngine.UIElements.PointerDownEvent);
t.GetProperty("position").GetSetMethod(true).Invoke(pd, new object[]{ new Vector3(c.x,c.y,0) });
t.GetProperty("localPosition").GetSetMethod(true).Invoke(pd, new object[]{ new Vector3(2,2,0) });
t.GetProperty("button").GetSetMethod(true).Invoke(pd, new object[]{ 1 });
t.GetProperty("pointerId").GetSetMethod(true).Invoke(pd, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
pd.target = val; using(pd) val.SendEvent(pd);
var pu = UnityEngine.UIElements.PointerUpEvent.GetPooled();
var t2 = typeof(UnityEngine.UIElements.PointerUpEvent);
t2.GetProperty("position").GetSetMethod(true).Invoke(pu, new object[]{ new Vector3(c.x,c.y,0) });
t2.GetProperty("localPosition").GetSetMethod(true).Invoke(pu, new object[]{ new Vector3(2,2,0) });
t2.GetProperty("button").GetSetMethod(true).Invoke(pu, new object[]{ 1 });
t2.GetProperty("pointerId").GetSetMethod(true).Invoke(pu, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
pu.target = val; using(pu) val.SendEvent(pu);
return "rightclicked " + val.GetType().Name + " " + val.worldBound;
