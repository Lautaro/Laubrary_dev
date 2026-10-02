// COLD WALK step 1 — close every Shaper window, open it from its own menu item, report the empty state.
var sb = new System.Text.StringBuilder();
int closed = 0;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType().Name == "ShaperWindow") { w0.Close(); closed++; }
sb.Append("closed=").Append(closed).Append("\n");
bool ok = UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
sb.Append("menu Laubrary/Shaper -> ").Append(ok).Append("\n");
var w = ZWin("ShaperWindow");
if (w == null) return sb.Append("NO WINDOW").ToString();
sb.Append("minSize=").Append(w.minSize).Append("\n");
w.position = new UnityEngine.Rect(30, 40, 1500, 900);
w.Repaint();
sb.Append("position=").Append(w.position).Append("\n");
// what is bound?
object cur = null;
for (var t = w.GetType(); t != null; t = t.BaseType)
{
    var p = t.GetProperty("Current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
                                   | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
    if (p != null) { cur = p.GetValue(w); break; }
}
var obj = cur as UnityEngine.Object;
sb.Append("Current=").Append(obj == null ? "<null>" : UnityEditor.AssetDatabase.GetAssetPath(obj)).Append("\n");
return sb.ToString();
