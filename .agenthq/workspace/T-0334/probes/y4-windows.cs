var sb = new System.Text.StringBuilder();
string[] keep = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView" };
var toClose = new System.Collections.Generic.List<UnityEditor.EditorWindow>();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue; var tn = w.GetType().Name;
    if (System.Array.IndexOf(keep, tn) >= 0) continue;
    toClose.Add(w);
}
foreach (var w in toClose) { sb.Append("closing ").Append(w.GetType().Name).Append("\n"); try { w.Close(); } catch { } }
return sb.ToString();
