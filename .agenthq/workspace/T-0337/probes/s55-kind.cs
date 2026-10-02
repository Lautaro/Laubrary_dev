var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb=new System.Text.StringBuilder();
System.Action<object,string,int> dump=null;
dump=(o,tag,depth)=>{ if(o==null||depth>3) return;
  foreach(var f in o.GetType().GetFields(BFi)){
    var v=f.GetValue(o);
    if(f.FieldType.IsEnum){ sb.Append(tag).Append('.').Append(f.Name).Append('=').Append(v).Append('\n'); continue; }
    if(v is System.Collections.IList il && f.Name!="stops"){ sb.Append(tag).Append('.').Append(f.Name).Append("[]=").Append(il.Count).Append('\n');
      for(int i=0;i<il.Count && i<2;i++) dump(il[i], tag+"."+f.Name+"["+i+"]", depth+1); continue; }
    if(f.Name=="node"||f.Name=="root"||f.Name=="shape") dump(v, tag+"."+f.Name, depth+1);
  } };
foreach (var name in new string[]{"AuditT337B","AuditT337C"}){
  var d=UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/"+name+".asset");
  sb.Append("=== ").Append(name).Append(" ===\n"); dump(d, name, 0); }
return sb.ToString();
