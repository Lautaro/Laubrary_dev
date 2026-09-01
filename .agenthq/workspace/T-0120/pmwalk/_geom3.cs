var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
var sb = new System.Text.StringBuilder();
sb.AppendLine("winpos=" + win.position + " ppp=" + EditorGUIUtility.pixelsPerPoint);
System.Action<UnityEngine.UIElements.VisualElement,int> walk = null;
walk = (ve, d) =>
{
    if (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) return;
    var cls = string.Join(",", ve.GetClasses());
    var wb = ve.worldBound;
    var n = ve.GetType().Name;
    bool want = cls.Contains("timeline") || cls.Contains("split") || cls.Contains("stage") || cls.Contains("drag") || cls.Contains("handle") || n.Contains("Timeline") || n.Contains("Split") || n.Contains("Stage");
    if (want && wb.width > 0.5f && wb.height > 0.5f)
        sb.AppendLine(new string(' ', d) + n + " [" + cls + "] rect=(" + wb.x.ToString("0.0") + "," + wb.y.ToString("0.0") + "," + wb.width.ToString("0.0") + "," + wb.height.ToString("0.0") + ")");
    foreach (var c in ve.hierarchy.Children()) walk(c, d + 1);
};
walk(win.rootVisualElement, 0);
return sb.ToString();
