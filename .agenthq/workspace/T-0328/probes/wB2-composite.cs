var w = ZWin("ZoeWindow"); if (w == null) return "no ZoeWindow";
var t = w.GetType();
var BF = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy;
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var x = t; x != null; x = x.BaseType) if (af == null) af = x.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var zoe = af.GetValue(w);
// find the Zoe's view (the object the helpers take)
object view = null;
foreach (var f in zoe.GetType().GetFields(BFi))
    if (f.Name.ToLower().Contains("view")) { view = f.GetValue(zoe); }
var sb = new System.Text.StringBuilder();
sb.Append("zoe=").Append(((UnityEngine.Object)zoe).name).Append(" view=").Append(view == null ? "<null>" : view.GetType().Name).Append("\n");
var clips = t.GetMethod("GetClipNameOptions", BF).Invoke(w, new object[]{ view }) as System.Collections.IEnumerable;
var names = new System.Collections.Generic.List<string>();
if (clips != null) foreach (var c in clips) names.Add(c.ToString());
sb.Append("clips(").Append(names.Count).Append(") = ").Append(string.Join(", ", names.ToArray())).Append("\n");
foreach (var n in names)
{
    if (n.StartsWith("(")) continue;
    int fc = (int)t.GetMethod("GetFrameCount", BF).Invoke(w, new object[]{ view, n });
    var pl = t.GetMethod("GetPointLayerIds", BF).Invoke(w, new object[]{ view, n }) as System.Collections.IEnumerable;
    var ev = t.GetMethod("GetEventNames", BF).Invoke(w, new object[]{ view, n }) as System.Collections.IEnumerable;
    var pls = new System.Collections.Generic.List<string>(); if (pl != null) foreach (var p in pl) pls.Add(p.ToString());
    var evs = new System.Collections.Generic.List<string>(); if (ev != null) foreach (var p in ev) evs.Add(p.ToString());
    sb.Append("  ").Append(n).Append(": frames=").Append(fc).Append(" pointLayers=[").Append(string.Join(",", pls.ToArray()))
      .Append("] events=[").Append(string.Join(",", evs.ToArray())).Append("]\n");
}
var ml = t.GetMethod("AllMetaLayers", BF).Invoke(w, new object[]{ view }) as System.Collections.IEnumerable;
var mls = new System.Collections.Generic.List<string>(); if (ml != null) foreach (var p in ml) mls.Add(p.ToString());
sb.Append("AllMetaLayers = [").Append(string.Join(", ", mls.ToArray())).Append("]\n");
return sb.ToString();
