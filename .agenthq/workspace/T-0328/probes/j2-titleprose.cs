// A box/section TITLE that explains rather than names: it carries a parenthetical, an em-dash clause,
// a colon clause, or is simply long. The rulebook puts explanations in the tooltip, never the title.
var sb = new System.Text.StringBuilder();
string[] skip = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
int total = 0;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue; var tn = w.GetType().Name;
    if (System.Array.IndexOf(skip, tn) >= 0) continue;
    if (ZAll(w.rootVisualElement).Count <= 1) continue;
    foreach (var e in ZAll(w.rootVisualElement))
    {
        if (!ZDrawn(e)) continue;
        var cls = ZCls(e);
        if (!(cls.Contains("zui-box__title") || cls.Contains("zui-section__title") || cls.Contains("zui-text--section"))) continue;
        var te = e as UnityEngine.UIElements.TextElement; if (te == null || string.IsNullOrEmpty(te.text)) continue;
        string t = te.text;
        bool prose = t.Contains("(") || t.Contains("—") || t.Contains(":") || t.Split(' ').Length > 3;
        if (!prose) continue;
        total++;
        sb.Append(tn).Append("  '").Append(t).Append("'  tip=").Append(ZTip(te).Length == 0 ? "<NONE>" : "yes").Append("\n");
    }
}
sb.Append("TOTAL explaining titles = ").Append(total).Append("\n");
return sb.ToString();
