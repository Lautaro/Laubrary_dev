var sb=new System.Text.StringBuilder();
// empty state: unbind Cartographer, press New, read the name field tooltip
var w = ZWin("CartographerWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(w, new object[]{ null }); rb.Invoke(w,null); w.Repaint();
UnityEngine.UIElements.Button newBtn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") newBtn=b; }
if (newBtn!=null) ZClick(newBtn);
return "unbound + New pressed";
