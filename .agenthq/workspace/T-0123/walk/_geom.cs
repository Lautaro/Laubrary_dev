var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
var sb = new System.Text.StringBuilder();
sb.AppendLine("winpos=" + win.position + " ppp=" + EditorGUIUtility.pixelsPerPoint);

// every ROW that is a direct child of a capability card body, with its used vs available width
System.Action<UnityEngine.UIElements.VisualElement, int> walk = null;
walk = (ve, d) =>
{
    if (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) return;
    var n = ve.GetType().Name;
    var cls = string.Join(",", ve.GetClasses());
    var wb = ve.worldBound;
    bool want = n == "ZuiHGroup" || n == "MockTimingTracks" || n == "MockCapabilityStage" || n == "ScrollView"
        || cls.Contains("zui-section") || cls.Contains("zui-box");
    if (want && wb.width > 0.5f && wb.height > 0.5f)
    {
        float used = 0f;
        float right = wb.x;
        foreach (var c in ve.hierarchy.Children())
        {
            if (c.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
            used += c.worldBound.width;
            right = Mathf.Max(right, c.worldBound.xMax);
        }
        sb.AppendLine(new string(' ', d) + n + " [" + cls + "] rect=(" + wb.x.ToString("0.0") + "," + wb.y.ToString("0.0") + "," + wb.width.ToString("0.0") + "," + wb.height.ToString("0.0") + ") childrenW=" + used.ToString("0.0") + " free=" + (wb.xMax - right).ToString("0.0"));
    }
    foreach (var c in ve.hierarchy.Children()) walk(c, d + 1);
};
walk(win.rootVisualElement, 0);
return sb.ToString();
