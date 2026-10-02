var sb=new System.Text.StringBuilder();
var aw = ZWin("AnimationAsepriteWindow");
if (aw!=null) { var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
  foreach (var m in aw.GetType().GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy)) if (m.Name=="Rebuild" && m.GetParameters().Length==0) m.Invoke(aw,null);
  aw.Repaint(); }
return "rebuilt";
