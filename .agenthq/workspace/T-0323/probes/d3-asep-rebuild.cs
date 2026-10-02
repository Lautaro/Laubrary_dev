var sb=new System.Text.StringBuilder();
var w = ZWin("AnimationAsepriteWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var m in w.GetType().GetMethods(BFi)) if ((m.Name=="Rebuild"||m.Name=="OnZUI"||m.Name=="BuildUI") && m.GetParameters().Length==0) { m.Invoke(w,null); sb.Append("called ").Append(m.Name).Append("\n"); }
w.Repaint();
foreach (var e in ZAll(w.rootVisualElement)) if (ZDrawn(e)) sb.Append(e.GetType().Name).Append("['").Append(ZOwnText(e)).Append("'] ");
return sb.ToString();
