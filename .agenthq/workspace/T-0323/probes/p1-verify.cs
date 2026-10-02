var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow"); if (w==null) return "no fx window";
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(w,new object[]{null}); rb.Invoke(w,null);
w.position=new Rect(20,20,900,500); w.titleContent=new GUIContent("SpriteFxStackWindow");
return "unbound";
