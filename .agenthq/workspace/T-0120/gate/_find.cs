var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
float ppp = EditorGUIUtility.pixelsPerPoint;
var sb = new System.Text.StringBuilder();
sb.AppendLine("winpos=" + win.position + " ppp=" + ppp);
System.Action<UnityEngine.UIElements.VisualElement> walk = null;
walk = ve =>
{
    var b = ve as UnityEngine.UIElements.Button;
    if (b != null)
    {
        var wb = b.worldBound;
        sb.AppendLine("BTN '" + b.text + "' wb=" + wb.ToString("F1") +
            " screen=" + Mathf.RoundToInt((win.position.x + wb.center.x) * ppp) + " " + Mathf.RoundToInt((win.position.y + wb.center.y) * ppp));
    }
    if (ve.GetType().Name == "MockTimingTracks" || ve.ClassListContains("zui-stage"))
        sb.AppendLine(ve.GetType().Name + " wb=" + ve.worldBound.ToString("F1"));
    foreach (var c in ve.hierarchy.Children()) walk(c);
};
walk(win.rootVisualElement);
return sb.ToString();
