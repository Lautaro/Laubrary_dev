var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
System.Func<object,string,object> fv = (o, n) => {
    if (o == null) return null;
    for (var t = o.GetType(); t != null; t = t.BaseType)
    { var f = t.GetField(n, BFi | System.Reflection.BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(o); }
    return null;
};
var zoeWinT = ZType("ZoeWindow");
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var mAllMeta = zoeWinT.GetMethod("AllMetaLayers", BFs);
sb.Append("AllMetaLayers found=").Append(mAllMeta != null).Append("\n");
var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Demos/ProtoGuyDemo/ProtoGuy.asset", ZType("Zoe"));
var view = fv(zoe, "view");
var parts = fv(view, "parts") as System.Collections.IEnumerable;
foreach (var p in parts)
{
    sb.Append("part '").Append(fv(p, "name")).Append("' parent='").Append(fv(p, "parentPartName")).Append("'\n");
    foreach (var side in new string[]{ "parentAnchor", "childAnchor" })
    {
        var a = fv(p, side);
        sb.Append("   ").Append(side).Append(" mode=").Append(fv(a, "mode")).Append(" metaLayerId='").Append(fv(a, "metaLayerId")).Append("'\n");
    }
    if (mAllMeta != null)
    {
        var res = mAllMeta.Invoke(null, new object[]{ fv(p, "view") }) as System.Array;
        sb.Append("   AllMetaLayers(this part view) = ").Append(res == null ? -1 : res.Length);
        if (res != null) foreach (var r in res) sb.Append(" ").Append(r);
        sb.Append("\n");
    }
}
return sb.ToString();
