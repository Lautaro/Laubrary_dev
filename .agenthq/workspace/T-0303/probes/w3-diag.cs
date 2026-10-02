var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA25a.asset");
sb.Append("target doc=").Append(doc == null ? "<missing>" : doc.name).Append("\n");
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 60, 1600, 1150);
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// what does the toggle bar think?
sb.Append("PREF userSel = ").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<none>")).Append("\n");
sb.Append("PREF solo = ").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.solo","<none>")).Append("\n");

// force every section on
var sel = new System.Text.StringBuilder();
foreach (var l in new[]{"Views","Canvas","Layers","Shape","Fill","Swarm","SpriteFX","Lights","Tags"}) { if (sel.Length>0) sel.Append(';'); sel.Append(l).Append("=1"); }
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", sel.ToString());
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null);

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
sb.Append("elements=").Append(all.Count).Append("\n");
foreach (var v in all)
{
    var n = v.GetType().Name;
    if (n == "ZuiSection" || n == "ZuiBox")
    {
        string title = "?";
        foreach (var f in v.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public))
            if (f.FieldType == typeof(string) && (f.Name.ToLower().Contains("title") || f.Name.ToLower().Contains("label"))) { var s0 = f.GetValue(v) as string; if (!string.IsNullOrEmpty(s0)) title = f.Name + "=" + s0; }
        sb.Append(n).Append(" [").Append(title).Append("] children=").Append(v.childCount).Append(" displayed=").Append(v.resolvedStyle.display).Append("\n");
    }
}
var swF = WT.GetField("swarmSection", BFi);
sb.Append("swarmSection field = ").Append(swF == null ? "<nofield>" : (swF.GetValue(win) == null ? "null" : "present")).Append("\n");
return sb.ToString();
