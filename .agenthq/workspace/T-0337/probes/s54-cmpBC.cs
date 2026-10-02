var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Func<object,int,string> h = (d,f)=>{ var px=renderM.Invoke(null,new object[]{d,f}) as UnityEngine.Color32[]; if(px==null) return "null";
  unchecked{uint x=2166136261u; foreach(var p in px){x=(x^p.r)*16777619u;x=(x^p.g)*16777619u;x=(x^p.b)*16777619u;x=(x^p.a)*16777619u;} return x.ToString("X8");} };
var sb=new System.Text.StringBuilder();
foreach (var name in new string[]{"AuditT337B","AuditT337C"})
{
  string p = "Assets/Shaper/" + name + ".asset";
  var d = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
  if (d==null){ sb.Append(name).Append(": NOT ON DISK\n"); continue; }
  sb.Append(name).Append(" id=").Append(d.GetInstanceID());
  foreach (var f in d.GetType().GetFields(BFi)) if (f.Name=="frameCount"||f.Name=="frameRate"||f.Name=="canvasWidth"||f.Name=="canvasHeight") sb.Append(' ').Append(f.Name).Append('=').Append(f.GetValue(d));
  foreach (var f in d.GetType().GetFields(BFi)) if (f.Name=="layers") { var l=f.GetValue(d) as System.Collections.IList; sb.Append(" layers=").Append(l==null?0:l.Count);
    if (l!=null&&l.Count>0) foreach (var g in l[0].GetType().GetFields(BFi)) if (g.Name=="node"){ var nd=g.GetValue(l[0]);
      if(nd!=null) foreach (var k in nd.GetType().GetFields(BFi)) if(k.Name=="kind") sb.Append(" node.kind=").Append(k.GetValue(nd)); } }
  sb.Append("\n   frames 0,1,2,4,8,11,12,15: ");
  foreach (int f in new int[]{0,1,2,4,8,11,12,15}) sb.Append(h(d,f)).Append(' ');
  sb.Append('\n');
}
var win = ZWin("ShaperWindow");
if (win!=null){ System.Reflection.FieldInfo aF=null; for(var t=win.GetType();t!=null;t=t.BaseType) if(aF==null) aF=t.GetField("asset",BFi);
  var a=aF.GetValue(win) as UnityEngine.Object; sb.Append("window is bound to ").Append(a==null?"<null>":a.name).Append(" id=").Append(a==null?0:a.GetInstanceID()).Append('\n'); }
return sb.ToString();
