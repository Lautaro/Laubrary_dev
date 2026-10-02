var w=ZWin("SpriteFxStackWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
for (var t=w.GetType(); t!=null; t=t.BaseType) { var m=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (m!=null) { m.Invoke(w,null); break; } }
w.position=new Rect(20,20,900,880); w.Repaint();
return "rebuilt";
