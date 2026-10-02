var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.Undo.ClearAll();
UnityEditor.AssetDatabase.Refresh();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA24a.asset");
sb.Append("AuditA24a on disk loadable=").Append(doc != null).Append("\n");
if (doc != null)
{
    sb.Append("layers=").Append(doc.layers == null ? -1 : doc.layers.Count).Append("\n");
    if (doc.layers != null && doc.layers.Count > 0)
        sb.Append("root kind=").Append(doc.layers[0].root.kind).Append(" children=").Append(doc.layers[0].root.children == null ? 0 : doc.layers[0].root.children.Count).Append("\n");
    foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
    Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc);
    var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
    win.position = new UnityEngine.Rect(50, 50, 2100, 1150); win.Show();
    sb.Append("rebound\n");
}
return sb.ToString();
