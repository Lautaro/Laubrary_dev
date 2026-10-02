var w = ZWin("ShaperWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(w);
var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
var root = layers[0].GetType().GetField("root", BFi).GetValue(layers[0]);
var sb = new System.Text.StringBuilder();
foreach (var nm in new string[]{"primitive","border","fill","swarm"}) {
  var o = root.GetType().GetField(nm, BFi).GetValue(root);
  sb.Append("== ").Append(nm).Append(" (").Append(o==null?"null":o.GetType().Name).Append(")\n");
  if (o == null) continue;
  foreach (var f in o.GetType().GetFields(BFi)) {
    var v = f.GetValue(o);
    string s = v == null ? "<null>" : (v.GetType().IsPrimitive || v is string || v.GetType().IsEnum) ? v.ToString() : v.GetType().Name;
    sb.Append("   ").Append(f.Name).Append(" = ").Append(s).Append("\n");
  }
}
return sb.ToString();
