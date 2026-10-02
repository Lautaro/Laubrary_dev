// ZuiAudit every open Laubrary window as it stands (all bound), one line each.
var sb = new System.Text.StringBuilder();
string[] skip = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (System.Array.IndexOf(skip, tn) >= 0) continue;
    if (ZAll(w.rootVisualElement).Count <= 1) { sb.Append(tn).Append(" | NO UITK (immediate-mode)\n"); continue; }
    ZAudit(w, tn);
    sb.Append(ZSummary(tn));
}
return sb.ToString();
