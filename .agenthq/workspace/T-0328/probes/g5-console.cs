var sb = new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
// what does the document actually hold?
object cur = null;
for (var t = w.GetType(); t != null; t = t.BaseType)
{ var p = t.GetProperty("Current", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly);
  if (p != null) { cur = p.GetValue(w); break; } }
var doc = cur as UnityEngine.ScriptableObject;
var dt = doc.GetType();
foreach (var fn in new string[]{"frameCount","frameRate","canvasWidth","canvasHeight","layers","cherries"})
{
    var f = dt.GetField(fn);
    if (f == null) { sb.Append(fn).Append("=<no field>\n"); continue; }
    var v = f.GetValue(doc);
    var col = v as System.Collections.ICollection;
    sb.Append(fn).Append("=").Append(col != null ? col.Count.ToString() + " items" : (v == null ? "null" : v.ToString())).Append("\n");
}
return sb.ToString();
