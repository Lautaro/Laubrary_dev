// Every DRAWN text element in every open Laubrary window whose string is long enough to be PROSE rather
// than a label or a readout — the "never put instructions on screen; that is tooltip content" rule, swept
// mechanically instead of spotted by eye one window at a time.
int min = 80;
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser",
                      "SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
var sb = new System.Text.StringBuilder();
int n = 0;
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string wn = w.GetType().Name;
    if (System.Array.IndexOf(unityOwn, wn) >= 0) continue;
    if (w.rootVisualElement == null || w.rootVisualElement.panel == null) continue;
    foreach (var e in ZAll(w.rootVisualElement))
    {
        var te = e as UnityEngine.UIElements.TextElement;
        if (te == null || string.IsNullOrEmpty(te.text) || te.text.Length < min) continue;
        if (!ZDrawn(te)) continue;
        if (te is UnityEngine.UIElements.Button) continue;
        n++;
        sb.Append(wn).Append("  [").Append(te.text.Length).Append(" chars] cls=").Append(ZCls(te)).Append("\n      \"")
          .Append(te.text.Replace("\n", " ")).Append("\"\n");
    }
}
sb.Append("TOTAL ").Append(n).Append("\n");
return sb.ToString();
