// Every drawn text INPUT in every open Laubrary window, with its caption and tooltip — the mechanical
// half of the "never type a reference string" sweep. Sections are force-expanded first.
var sb = new System.Text.StringBuilder();
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
var auditT = ZType("ZuiAudit");
foreach (var win in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string wn = win.GetType().Name;
    if (System.Array.IndexOf(unityOwn, wn) >= 0) continue;
    if (auditT != null)
    {
        var ex = auditT.GetMethod("ExpandAll", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
        if (ex != null) { try { ex.Invoke(null, new object[]{ win }); } catch { } }
    }
    int n = 0;
    foreach (var e in ZAll(win.rootVisualElement))
    {
        var tf = e as UnityEngine.UIElements.TextField;
        if (tf == null || !ZDrawn(tf)) continue;
        n++;
        string tip = ZTip(tf);
        sb.Append(wn).Append(" | TEXTFIELD label='").Append(tf.label ?? "").Append("' value='")
          .Append(tf.value == null ? "" : (tf.value.Length > 40 ? tf.value.Substring(0,40) : tf.value))
          .Append("' caption='").Append(ZCaption(tf)).Append("'\n    tip=")
          .Append(tip.Length > 150 ? tip.Substring(0,150) : tip).Append("\n    path=").Append(ZPath(tf)).Append("\n");
    }
    sb.Append("== ").Append(wn).Append(": ").Append(n).Append(" drawn text inputs\n");
}
return sb.ToString();
