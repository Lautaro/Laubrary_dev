var sb=new System.Text.StringBuilder();
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name=="PopupWindow") { x.Close(); sb.Append("closed stray popup\n"); }
var w=ZWin("MirageWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var zoeT=ZType("Zoe");
var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Demos/PreviewDemo/PreviewShooterZoe.asset", zoeT);
System.Reflection.MethodInfo add=null, rb=null; System.Reflection.FieldInfo af=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (add==null) add=t.GetMethod("AddEntry", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("RebuildBody", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (af==null) af=t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sb.Append("add=").Append(add!=null).Append(" rb=").Append(rb!=null).Append(" asset=").Append(af!=null).Append("\n");
var view = af!=null? af.GetValue(w) : null;
if (add!=null && view!=null && zoe!=null) { add.Invoke(w, new object[]{ view, zoe }); if (rb!=null) rb.Invoke(w,null); sb.Append("added ").Append(zoe.name).Append("\n"); }
return sb.ToString();
