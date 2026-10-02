var sb=new System.Text.StringBuilder();
var w = ZWin("AnimationAsepriteWindow");
var lauT = ZType("Lauminary");
var lau = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/ProtoGuy.asset", lauT);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
foreach (var f in w.GetType().GetFields(BFi)) sb.Append("f:").Append(f.Name).Append(":").Append(f.FieldType.Name).Append(" ");
sb.Append("\n");
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==lauT) { f.SetValue(w, lau); sb.Append("set ").Append(f.Name).Append("\n"); }
foreach (var m in w.GetType().GetMethods(BFi)) if (m.Name=="Rebuild" && m.GetParameters().Length==0) { m.Invoke(w,null); sb.Append("rebuilt\n"); }
w.Repaint();
return sb.ToString();
