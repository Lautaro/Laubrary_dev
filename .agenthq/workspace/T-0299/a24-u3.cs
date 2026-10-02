var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
System.Func<string> Sig = () => {
    var d = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
    if (d == null) return "windowDoc=NULL";
    if (d.layers == null || d.layers.Count == 0) return "windowDoc=alive <no layers>";
    return "windowDoc=alive layerName=" + d.layers[0].name; };
sb.Append("before undo : ").Append(Sig()).Append("\n");
UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
sb.Append("after undo  : ").Append(Sig()).Append(" loadable=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA24u.asset") != null).Append("\n");
UnityEditor.Undo.PerformRedo(); rb.Invoke(win, null);
sb.Append("after redo  : ").Append(Sig()).Append("\n");
return sb.ToString();
