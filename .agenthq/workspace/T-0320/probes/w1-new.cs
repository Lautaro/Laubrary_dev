// Click the toolbar's own New button (scoped to the row that holds the asset ObjectField, so no other
// "New" anywhere in the tree can be hit by accident).
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
UnityEngine.UIElements.VisualElement toolbar = null;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (e is UnityEditor.UIElements.ObjectField && ZDrawn(e)) { toolbar = e.hierarchy.parent; break; }
}
if (toolbar == null) return "no toolbar row";
UnityEngine.UIElements.Button newBtn = null;
for (int i=0;i<toolbar.hierarchy.childCount;i++) { var b = toolbar.hierarchy[i] as UnityEngine.UIElements.Button; if (b != null && b.text == "New") newBtn = b; }
if (newBtn == null) return "no New button in the toolbar";
using (var ev = UnityEngine.UIElements.ClickEvent.GetPooled()) { }
var m = typeof(UnityEngine.UIElements.Button).GetProperty("clicked");
newBtn.SendEvent(new UnityEngine.UIElements.NavigationSubmitEvent());
// Buttons respond to their clickable; invoke it the way a real press does
var clickable = newBtn.clickable;
var inv = typeof(UnityEngine.UIElements.Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
if (inv != null) inv.Invoke(clickable, new object[]{ (UnityEngine.UIElements.EventBase)null });
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF = null;
for (var t = win.GetType(); t != null && assetF == null; t = t.BaseType) assetF = t.GetField("asset", BFi);
var a = assetF.GetValue(win) as UnityEngine.Object;
return "after New: bound=" + (a != null ? a.name : "null") + " path=" + (a != null ? UnityEditor.AssetDatabase.GetAssetPath(a) : "-");
