var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(w);
var sb = new System.Text.StringBuilder();
var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
foreach (var ly in layers) {
  var root = ly.GetType().GetField("root", BFi).GetValue(ly);
  sb.Append("layer '").Append(ly.GetType().GetField("name",BFi).GetValue(ly)).Append("' root=").Append(root==null?"<null>":root.GetType().Name).Append("\n");
  if (root != null) foreach (var f in root.GetType().GetFields(BFi)) {
    var v = f.GetValue(root);
    string s = v == null ? "<null>" : v.GetType().IsPrimitive || v is string || v.GetType().IsEnum ? v.ToString() : v.GetType().Name;
    sb.Append("   ").Append(f.Name).Append(" = ").Append(s).Append("\n");
  }
}
return sb.ToString();
