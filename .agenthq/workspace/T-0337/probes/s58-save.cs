// The one step B had and C did not: Shaper's OWN toolbar Save. Does pressing it change what the document paints?
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for(var t=win.GetType();t!=null;t=t.BaseType) if(assetF==null) assetF=t.GetField("asset",BFi);
var d = assetF.GetValue(win) as UnityEngine.ScriptableObject;
var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<object,int,string> h=(o,f)=>{ var px=renderM.Invoke(null,new object[]{o,f}) as UnityEngine.Color32[]; if(px==null) return "null";
  unchecked{uint x=2166136261u; foreach(var p in px){x=(x^p.r)*16777619u;x=(x^p.g)*16777619u;x=(x^p.b)*16777619u;x=(x^p.a)*16777619u;} return x.ToString("X8");} };
System.Func<string> strip=()=>{ var s=""; foreach(int f in new int[]{0,1,2,4,8,11,12,15}) s+=h(d,f)+" "; return s; };
System.Func<string> flags=()=>{ var sb2=new System.Text.StringBuilder();
  var l = d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var root = l[0].GetType().GetField("root",BFi).GetValue(l[0]);
  System.Action<object,string> one=(nd,tag)=>{ if(nd==null) return;
    foreach(var g in nd.GetType().GetFields(BFi)){ if(g.Name!="fill"&&g.Name!="border") continue; var v=g.GetValue(nd);
      if(v==null){ sb2.Append(tag).Append('.').Append(g.Name).Append("=<null> "); continue; }
      var af=v.GetType().GetField("authored",BFi);
      if(af!=null) sb2.Append(tag).Append('.').Append(g.Name).Append(".authored=").Append(af.GetValue(v)).Append(' '); } };
  one(root,"root");
  var ch = root.GetType().GetField("children",BFi).GetValue(root) as System.Collections.IList;
  if(ch!=null&&ch.Count>0) one(ch[0],"child0");
  return sb2.ToString(); };
var sb=new System.Text.StringBuilder();
sb.Append("doc=").Append(d.name).Append('\n');
sb.Append("BEFORE Save  render: ").Append(strip()).Append('\n').Append("             flags : ").Append(flags()).Append('\n');
UnityEngine.UIElements.Button save=null;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Save") save=b; }
if (save==null) return sb.Append("no Save button (nothing dirty?) dirty=").Append(UnityEditor.EditorUtility.IsDirty(d)).ToString();
sb.Append("Save enabled=").Append(save.enabledInHierarchy).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(d)).Append('\n');
UnityEditor.EditorUtility.SetDirty(d);
ZPress(win, save);
sb.Append("AFTER  Save  render: ").Append(strip()).Append('\n').Append("             flags : ").Append(flags()).Append('\n');
return sb.ToString();
