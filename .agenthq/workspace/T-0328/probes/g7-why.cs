var sb = new System.Text.StringBuilder();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>("Assets/Demos/ShaperDemo/AuditT328ShaperA.asset");
var ppuF = doc.GetType().GetField("pixelsPerUnit");
sb.Append("doc.pixelsPerUnit=").Append(ppuF == null ? "<no field>" : ppuF.GetValue(doc).ToString()).Append("\n");
var w = ZWin("ShaperWindow");
foreach (var fn in new string[]{"bakeAnimationClip","bakeShaperClip","bakeGif"})
{
  var f = w.GetType().GetField(fn, System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
  sb.Append(fn).Append("=").Append(f == null ? "<no field>" : f.GetValue(w).ToString()).Append("\n");
}
// invoke DoBake directly, by reflection, and see what happens
var m = w.GetType().GetMethod("DoBake", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
sb.Append("DoBake found=").Append(m != null).Append("\n");
try { m.Invoke(w, null); sb.Append("DoBake invoked OK\n"); }
catch (System.Exception ex) { sb.Append("DoBake THREW ").Append((ex.InnerException ?? ex).GetType().Name).Append(": ").Append((ex.InnerException ?? ex).Message).Append("\n"); }
return sb.ToString();
