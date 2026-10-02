// Every reachable scalar of both documents, side by side. The two asset FILES are identical bar the name
// and the SerializeReference rid renumbering, so anything that differs here is unserialized runtime state.
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var lines = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,string>>();
foreach (var name in new string[]{"AuditT337B","AuditT337C"}){
  var d=UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/"+name+".asset");
  var map=new System.Collections.Generic.Dictionary<string,string>();
  var seen=new System.Collections.Generic.HashSet<object>();
  System.Action<object,string,int> walk=null;
  walk=(o,tag,depth)=>{ if(o==null||depth>6) return;
    if(!o.GetType().IsValueType){ if(seen.Contains(o)) return; seen.Add(o); }
    foreach(var f in o.GetType().GetFields(BFi)){ if(f.Name.StartsWith("m_")) continue;
      object v=null; try{ v=f.GetValue(o);}catch{continue;}
      string k=tag+"."+f.Name;
      if(v==null){ map[k]="<null>"; continue; }
      var t=v.GetType();
      if(t.IsPrimitive||t.IsEnum||v is string||v is UnityEngine.Color||v is UnityEngine.Vector2||v is UnityEngine.Vector3){ map[k]=v.ToString(); continue; }
      if(v is UnityEngine.AnimationCurve ac){ map[k]="curve:"+ac.length; for(int i=0;i<ac.length;i++) map[k+".k"+i]=ac[i].time+"/"+ac[i].value; continue; }
      if(v is UnityEngine.Object uo){ map[k]= uo==null?"obj:<destroyed>":("obj:"+uo.name); continue; }
      if(v is System.Collections.IList il){ map[k]="[]"+il.Count; for(int i=0;i<il.Count&&i<4;i++) walk(il[i],k+"["+i+"]",depth+1); continue; }
      walk(v,k,depth+1);
    } };
  walk(d,"",0);
  lines.Add(map);
}
var a=lines[0]; var b=lines[1];
var sb=new System.Text.StringBuilder(); int same=0,diff=0,only=0;
var keys=new System.Collections.Generic.List<string>(a.Keys); keys.Sort();
foreach(var k in keys){ if(!b.ContainsKey(k)){ only++; sb.Append("ONLY-B ").Append(k).Append('=').Append(a[k]).Append('\n'); continue; }
  if(a[k]==b[k]) same++; else { diff++; sb.Append("DIFF ").Append(k).Append("  B=").Append(a[k]).Append("  C=").Append(b[k]).Append('\n'); } }
foreach(var k in b.Keys) if(!a.ContainsKey(k)){ only++; sb.Append("ONLY-C ").Append(k).Append('=').Append(b[k]).Append('\n'); }
return "same="+same+" differing="+diff+" onlyOneSide="+only+"\n"+sb.ToString();
