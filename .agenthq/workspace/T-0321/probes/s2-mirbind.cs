var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var mw = ZWin("MirageWindow"); var mvT = ZType("MirageView");
var g = UnityEditor.AssetDatabase.FindAssets("t:MirageView")[0];
var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g), mvT);
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=mw.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(mw, new object[]{ obj }); if (rb!=null) rb.Invoke(mw,null); mw.Repaint();
sb.Append("mirage -> ").Append(UnityEditor.AssetDatabase.GetAssetPath(obj)).Append("\n");
return sb.ToString();
