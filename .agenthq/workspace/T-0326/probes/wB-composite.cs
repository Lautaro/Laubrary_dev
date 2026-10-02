// Round 16's composite derivation, re-measured at HEAD: does a composite character's clip resolve to real
// frame counts, point layers and frame events, through the window's own helpers?
var w = ZWin("ZoeWindow"); if (w == null) return "no ZoeWindow";
var t = w.GetType();
var BF = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var mn in new string[]{ "GetClipNameOptions", "GetFrameCount", "GetEventNames", "GetPointLayerIds", "AllMetaLayers", "AllFrameEventNames" })
{
    var ms = t.GetMethods(BF);
    foreach (var m in ms)
    {
        if (m.Name != mn) continue;
        var ps = m.GetParameters();
        sb.Append(m.Name).Append("(").Append(ps.Length).Append(" args: ");
        foreach (var p in ps) sb.Append(p.ParameterType.Name).Append(" ");
        sb.Append(")\n");
    }
}
return sb.ToString();
