var w = ZWin("ShaperWindow"); if (w==null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null; t=t.BaseType) if (assetF==null) assetF=t.GetField("asset",BFi);
var a = assetF.GetValue(w) as UnityEngine.ScriptableObject;
if (a==null) return "no asset bound";
var sb=new System.Text.StringBuilder();
sb.Append("asset=").Append(a.name).Append(" path=").Append(UnityEditor.AssetDatabase.GetAssetPath(a)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(a)).Append('\n');
var fc=a.GetType().GetField("frameCount",BFi); if(fc!=null) sb.Append("frameCount=").Append(fc.GetValue(a)).Append('\n');
foreach (var f in a.GetType().GetFields(BFi)) if (f.Name=="layers") {
  var l=f.GetValue(a) as System.Collections.IList; sb.Append("layers=").Append(l==null?0:l.Count).Append('\n');
  if (l!=null && l.Count>0) { var n0=l[0];
    foreach (var g in n0.GetType().GetFields(BFi)) if (g.Name=="node"||g.Name=="root") { var nd=g.GetValue(n0);
      if (nd!=null) foreach (var h in nd.GetType().GetFields(BFi)) if (h.Name=="kind"||h.Name=="children") {
        var v=h.GetValue(nd); sb.Append("  node.").Append(h.Name).Append('=').Append(v is System.Collections.IList il ? il.Count.ToString()+" children" : v).Append('\n'); } } }
}
return sb.ToString();
