// Is `authored` what makes B static? Flip B's two child flags in memory (never saved) and re-render;
// then flip C's the other way. Both restored before the probe returns.
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<object,int,string> h=(o,f)=>{ var px=renderM.Invoke(null,new object[]{o,f}) as UnityEngine.Color32[]; if(px==null) return "null";
  unchecked{uint x=2166136261u; foreach(var p in px){x=(x^p.r)*16777619u;x=(x^p.g)*16777619u;x=(x^p.b)*16777619u;x=(x^p.a)*16777619u;} return x.ToString("X8");} };
System.Func<object,string> strip=o=>{ var s=""; foreach(int f in new int[]{0,1,2,4,8,11,12,15}) s+=h(o,f)+" "; return s; };
var sb=new System.Text.StringBuilder();
foreach (var name in new string[]{"AuditT337B","AuditT337C"})
{
  var d = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/"+name+".asset");
  var l = d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var root = l[0].GetType().GetField("root",BFi).GetValue(l[0]);
  var ch = root.GetType().GetField("children",BFi).GetValue(root) as System.Collections.IList;
  var child = ch[0];
  var fillF = child.GetType().GetField("fill",BFi); var bordF = child.GetType().GetField("border",BFi);
  var fill = fillF.GetValue(child); var bord = bordF.GetValue(child);
  var fa = fill==null?null:fill.GetType().GetField("authored",BFi);
  var ba = bord==null?null:bord.GetType().GetField("authored",BFi);
  bool of = fa==null?false:(bool)fa.GetValue(fill);
  bool ob = ba==null?false:(bool)ba.GetValue(bord);
  sb.Append(name).Append("  child0 fill.authored=").Append(of).Append(" border.authored=").Append(ob).Append('\n');
  sb.Append("   as it stands : ").Append(strip(d)).Append('\n');
  if (fa!=null) fa.SetValue(fill, !of);
  sb.Append("   fill flipped : ").Append(strip(d)).Append('\n');
  if (ba!=null) ba.SetValue(bord, !ob);
  sb.Append("   both flipped : ").Append(strip(d)).Append('\n');
  if (fa!=null) fa.SetValue(fill, of);
  if (ba!=null) ba.SetValue(bord, ob);
  sb.Append("   restored     : ").Append(strip(d)).Append("  (dirty=").Append(UnityEditor.EditorUtility.IsDirty(d)).Append(")\n");
}
return sb.ToString();
