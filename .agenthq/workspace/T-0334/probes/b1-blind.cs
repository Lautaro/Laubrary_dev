// Open every ZuiAudit-blind window, one at a time, at a known rect; report its UITK element count.
var sb = new System.Text.StringBuilder();
string[] menus = {
  "Laubrary/Lazor/Lazor", "Laubrary/Zoetrope/Zoe Preview", "Laubrary/Zounds",
  "Laubrary/ZUI/Playground", "Laubrary/Dashboard", "Laubrary/Notifyer Log",
  "Tools/ZUI/Zhowcase", "Tools/ZUI/Zeditor", "Tools/ZUI/Texture Editor"
};
foreach (var m in menus)
{
    bool ok = false;
    try { ok = UnityEditor.EditorApplication.ExecuteMenuItem(m); } catch (System.Exception ex) { sb.Append(m).Append(" THREW ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append("\n"); continue; }
    sb.Append(m).Append(" exec=").Append(ok).Append("\n");
}
sb.Append("--- windows now alive ---\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (tn.StartsWith("Unity") || tn == "InspectorWindow" || tn == "ConsoleWindow" || tn == "ProjectBrowser"
        || tn == "SceneHierarchyWindow" || tn == "SceneView" || tn == "GameView" || tn == "MainToolbarWindow") continue;
    int n = 0; System.Action<UnityEngine.UIElements.VisualElement> w2 = null;
    w2 = e => { n++; for (int i=0;i<e.hierarchy.childCount;i++) w2(e.hierarchy[i]); };
    try { w2(w.rootVisualElement); } catch { }
    sb.Append("  ").Append(tn).Append(" '").Append(w.titleContent.text).Append("' elements=").Append(n)
      .Append(" pos=").Append(w.position).Append(" min=").Append(w.minSize).Append(" base=").Append(w.GetType().BaseType.Name).Append("\n");
}
return sb.ToString();
