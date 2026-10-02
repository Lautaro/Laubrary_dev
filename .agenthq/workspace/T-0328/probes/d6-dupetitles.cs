// Sweep every open Laubrary window for two DRAWN box/section titles that read the same.
var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (!tn.EndsWith("Window") || tn.StartsWith("Unity") || tn == "InspectorWindow" || tn == "ConsoleWindow"
        || tn == "SceneHierarchyWindow" || tn == "GameView" || tn == "SceneView" || tn == "MainToolbarWindow") continue;
    var seen = new System.Collections.Generic.Dictionary<string,int>();
    foreach (var e in ZAll(w.rootVisualElement))
    {
        if (!ZDrawn(e)) continue;
        var cls = ZCls(e);
        if (cls.Contains("zui-box__title") || cls.Contains("zui-section__title") || cls.Contains("zui-text--section") || cls.Contains("zui-box__header-title"))
        {
            var te = e as UnityEngine.UIElements.TextElement; if (te == null || string.IsNullOrEmpty(te.text)) continue;
            if (!seen.ContainsKey(te.text)) seen[te.text] = 0;
            seen[te.text]++;
        }
    }
    var dupes = new System.Text.StringBuilder();
    foreach (var kv in seen) if (kv.Value > 1) dupes.Append(" '").Append(kv.Key).Append("'x").Append(kv.Value);
    sb.Append(tn).Append(" titles=").Append(seen.Count).Append(dupes.Length > 0 ? "  DUPES:" + dupes.ToString() : "").Append("\n");
}
return sb.ToString();
