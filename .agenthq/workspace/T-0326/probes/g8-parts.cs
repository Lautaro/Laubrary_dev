// What DO ProtoGuy's parts actually declare? If the parts' animations carry frames/metaLayers/events,
// then the empty pickers are the window's resolution, not missing data.
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
System.Func<object,string,object> fv = (o, n) => {
    if (o == null) return null;
    for (var t = o.GetType(); t != null; t = t.BaseType)
    { var f = t.GetField(n, BFi | System.Reflection.BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(o); }
    return null;
};
var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Demos/ProtoGuyDemo/ProtoGuy.asset", ZType("Zoe"));
var view = fv(zoe, "view");
sb.Append("view=").Append(view.GetType().Name).Append("\n");
var parts = fv(view, "parts") as System.Collections.IEnumerable;
foreach (var p in parts)
{
    var pname = fv(p, "name") as string;
    var pview = fv(p, "view");
    sb.Append(" part '").Append(pname).Append("' view=").Append(pview == null ? "<null>" : pview.GetType().Name).Append("\n");
    var version = fv(pview, "version");
    sb.Append("   version=").Append(version == null ? "<null>" : version.ToString()).Append("\n");
    var anims = version != null ? fv(version, "animations") as System.Collections.IEnumerable : null;
    if (anims == null) continue;
    foreach (var a in anims)
    {
        var an = fv(a, "name") as string;
        var frames = fv(a, "frames") as System.Collections.ICollection;
        var metas = fv(a, "metaLayers") as System.Collections.ICollection;
        var evs = fv(a, "events") as System.Collections.ICollection;
        sb.Append("     anim '").Append(an).Append("' frames=").Append(frames == null ? -1 : frames.Count)
          .Append(" metaLayers=").Append(metas == null ? -1 : metas.Count)
          .Append(" events=").Append(evs == null ? -1 : evs.Count).Append("\n");
        if (metas != null) foreach (var L in metas) sb.Append("        meta id='").Append(fv(L,"id")).Append("' mode=").Append(fv(L,"mode")).Append("\n");
        if (evs != null) foreach (var e in evs) sb.Append("        event '").Append(fv(e,"name")).Append("'\n");
    }
}
return sb.ToString();
