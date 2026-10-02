// Round-trip determinism, straight from the RENDERER (no preview cache): hash 4 frames, reimport the
// saved asset off disk, rebind, hash again. Any difference is the document not reproducing itself.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; System.Reflection.MethodInfo setAsset=null;
for (var t=win.GetType(); t!=null; t=t.BaseType) { if (assetF==null) assetF=t.GetField("asset",BFi); if (setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
var bakerT = ZType("ShaperBaker");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<object,string> hash4 = doc => { var sb2=new System.Text.StringBuilder();
  foreach (int f in new int[]{0,4,8,12}) { var px = render.Invoke(null, new object[]{doc, f}) as UnityEngine.Color32[];
    unchecked { uint h=2166136261u; int lit=0; foreach (var p in px){ if(p.a>8) lit++; h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u; }
      sb2.Append("f").Append(f).Append("=").Append(h.ToString("X8")).Append("/").Append(lit).Append(" "); } }
  return sb2.ToString(); };
var sb = new System.Text.StringBuilder();
var doc0 = assetF.GetValue(win);
string path = UnityEditor.AssetDatabase.GetAssetPath(doc0 as UnityEngine.Object);
sb.Append("path=").Append(path).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc0 as UnityEngine.Object)).Append("\n");
sb.Append("A (in memory)   ").Append(hash4(doc0)).Append("\n");
sb.Append("B (same object) ").Append(hash4(doc0)).Append("\n");
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate|UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var doc1 = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
sb.Append("sameInstance=").Append(object.ReferenceEquals(doc0, doc1)).Append("\n");
sb.Append("C (off disk)    ").Append(hash4(doc1)).Append("\n");
sb.Append("D (off disk 2)  ").Append(hash4(doc1)).Append("\n");
setAsset.Invoke(win, new object[]{ doc1 });
return sb.ToString();
