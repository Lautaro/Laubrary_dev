// The plain authoring round trip on a pristine bag: Save through the toolbar, force it back off disk,
// rebind through the window's own SetAsset — the three steps an author actually performs — checking the
// authored flags and what the RENDERER paints at each one.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; System.Reflection.MethodInfo setAsset=null;
for(var t=win.GetType();t!=null;t=t.BaseType){ if(assetF==null) assetF=t.GetField("asset",BFi); if(setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
var bakerT = ZType("ShaperBaker");
var renderM = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
var sb=new System.Text.StringBuilder();
System.Action<string> report = tag => {
  var d = assetF.GetValue(win) as UnityEngine.ScriptableObject;
  sb.Append(tag).Append("  ");
  var l = d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var root = l[0].GetType().GetField("root",BFi).GetValue(l[0]);
  var ch = root.GetType().GetField("children",BFi).GetValue(root) as System.Collections.IList;
  System.Action<object,string> one=(nd,t2)=>{ foreach(var g in nd.GetType().GetFields(BFi)){ if(g.Name!="fill"&&g.Name!="border") continue;
    var v=g.GetValue(nd); if(v==null){ sb.Append(t2).Append('.').Append(g.Name.Substring(0,1)).Append("=NULL "); continue; }
    var af=v.GetType().GetField("authored",BFi); if(af!=null) sb.Append(t2).Append('.').Append(g.Name.Substring(0,1)).Append(".auth=").Append(af.GetValue(v)).Append(' '); } };
  one(root,"root"); if(ch!=null) for(int i=0;i<ch.Count;i++) one(ch[i],"c"+i);
  sb.Append("\n     render: ");
  foreach(int f in new int[]{0,1,2,4,8,11,12,15}){ var px=renderM.Invoke(null,new object[]{d,f}) as UnityEngine.Color32[];
    unchecked{uint x=2166136261u; foreach(var p in px){x=(x^p.r)*16777619u;x=(x^p.g)*16777619u;x=(x^p.b)*16777619u;x=(x^p.a)*16777619u;} sb.Append(x.ToString("X8")).Append(' ');} }
  sb.Append('\n');
};
report("1 as authored, never saved   :");
var doc0 = assetF.GetValue(win) as UnityEngine.ScriptableObject;
string path = UnityEditor.AssetDatabase.GetAssetPath(doc0);
UnityEngine.UIElements.Button save=null;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Save") save=b; }
if (save==null) sb.Append("   (no Save button; dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc0)).Append(")\n");
else { ZPress(win, save); sb.Append("   pressed the toolbar Save; dirty now=").Append(UnityEditor.EditorUtility.IsDirty(doc0)).Append('\n'); }
report("2 right after the toolbar Save:");
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
report("3 after a forced reimport     :");
var back = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
setAsset.Invoke(win, new object[]{ back });
report("4 after reopening it in Shaper:");
return sb.ToString();
