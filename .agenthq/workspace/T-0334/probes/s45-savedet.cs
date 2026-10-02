// Does SAVING a document change the picture it renders? Renderer-only, no preview cache involved.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=win.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(win);
var bakerT = ZType("ShaperBaker");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<string> h4 = () => { var s=new System.Text.StringBuilder();
  foreach (int f in new int[]{0,4,12}) { var px = render.Invoke(null, new object[]{doc, f}) as UnityEngine.Color32[];
    unchecked { uint h=2166136261u; foreach (var p in px){h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u;} s.Append("f").Append(f).Append("=").Append(h.ToString("X8")).Append(" "); } }
  return s.ToString(); };
var sb = new System.Text.StringBuilder();
var o = doc as UnityEngine.Object;
sb.Append("dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
sb.Append("1 before any edit  ").Append(h4()).Append("\n");
// a real, reversible data edit through the window's own control: the layer's enabled flag off and on
var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
var ly = layers[0]; var enF = ly.GetType().GetField("enabled", BFi);
enF.SetValue(ly, false); enF.SetValue(ly, true);
UnityEditor.EditorUtility.SetDirty(o);
sb.Append("2 dirty, unsaved    ").Append(h4()).Append("\n");
UnityEditor.AssetDatabase.SaveAssets();
sb.Append("3 after SaveAssets  ").Append(h4()).Append("\n");
sb.Append("dirtyNow=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
return sb.ToString();
