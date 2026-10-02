var sb=new System.Text.StringBuilder();
var vT=ZType("MirageView");
var v=UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Mirage/AuditT323Walk2.asset", vT);
sb.Append("view=").Append(v!=null).Append("\n");
if (v!=null) { var f=vT.GetField("previewables"); var l=f.GetValue(v) as System.Collections.IList; sb.Append("previewables=").Append(l==null?-1:l.Count).Append("\n");
  var m=vT.GetMethod("AddEntry"); sb.Append("AddEntry=").Append(m!=null?m.ToString():"null").Append("\n"); }
var w=ZWin("MirageWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null; t=t.BaseType) if (af==null) af=t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var bound = af.GetValue(w) as UnityEngine.Object;
sb.Append("bound=").Append(bound!=null?UnityEditor.AssetDatabase.GetAssetPath(bound):"null").Append("\n");
return sb.ToString();
