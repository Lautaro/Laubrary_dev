// Does binding a saved document into the window CHANGE it? C is currently unpromoted (authored=False) and
// animates. Bind it through the window's own SetAsset, exactly as s43 does, and look again.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset=null; System.Reflection.FieldInfo assetF=null;
for (var t=win.GetType(); t!=null; t=t.BaseType){ if(setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if(assetF==null) assetF=t.GetField("asset",BFi); }
var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<object,int,string> h=(d,f)=>{ var px=renderM.Invoke(null,new object[]{d,f}) as UnityEngine.Color32[]; if(px==null) return "null";
  unchecked{uint x=2166136261u; foreach(var p in px){x=(x^p.r)*16777619u;x=(x^p.g)*16777619u;x=(x^p.b)*16777619u;x=(x^p.a)*16777619u;} return x.ToString("X8");} };
System.Func<object,string> strip=d=>{ var s=""; foreach(int f in new int[]{0,1,2,4,8,11,12,15}) s+=h(d,f)+" "; return s; };
System.Func<object,string> flags=d=>{ var sb2=new System.Text.StringBuilder();
  foreach(var f in d.GetType().GetFields(BFi)) if(f.Name=="layers"){ var l=f.GetValue(d) as System.Collections.IList; if(l==null||l.Count==0) return "no layers";
    var root=l[0].GetType().GetField("root",BFi).GetValue(l[0]);
    System.Action<object,string> one=(nd,tag)=>{ if(nd==null) return;
      foreach(var g in nd.GetType().GetFields(BFi)){ if(g.Name!="fill"&&g.Name!="border") continue; var v=g.GetValue(nd);
        if(v==null){ sb2.Append(tag).Append('.').Append(g.Name).Append("=<null> "); continue; }
        var af=v.GetType().GetField("authored",BFi); if(af==null){ var ff=v.GetType().GetField("fill",BFi); if(ff!=null){ var inner=ff.GetValue(v); af= inner==null?null:inner.GetType().GetField("authored",BFi); if(af!=null){ sb2.Append(tag).Append(".border.fill.authored=").Append(af.GetValue(inner)).Append(' '); continue; } } continue; }
        sb2.Append(tag).Append('.').Append(g.Name).Append(".authored=").Append(af.GetValue(v)).Append(' '); } };
    one(root,"root");
    var ch=root.GetType().GetField("children",BFi).GetValue(root) as System.Collections.IList;
    if(ch!=null&&ch.Count>0) one(ch[0],"child0"); }
  return sb2.ToString(); };
var sb=new System.Text.StringBuilder();
var c = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/AuditT337C.asset");
sb.Append("C BEFORE bind: ").Append(strip(c)).Append('\n').Append("   flags: ").Append(flags(c)).Append('\n');
sb.Append("   dirty=").Append(UnityEditor.EditorUtility.IsDirty(c)).Append('\n');
setAsset.Invoke(win, new object[]{ c });
sb.Append("C AFTER  bind: ").Append(strip(c)).Append('\n').Append("   flags: ").Append(flags(c)).Append('\n');
sb.Append("   dirty=").Append(UnityEditor.EditorUtility.IsDirty(c)).Append('\n');
sb.Append("window bound to ").Append((assetF.GetValue(win) as UnityEngine.Object).name).Append('\n');
return sb.ToString();
