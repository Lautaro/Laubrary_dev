var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
var sb = new System.Text.StringBuilder();
System.Action<UnityEngine.UIElements.VisualElement,int> walk = null;
walk = (ve, d) =>
{
    var te = ve as UnityEngine.UIElements.TextElement;
    string txt = te != null ? te.text : null;
    string cls = string.Join(".", ve.GetClasses());
    var wb = ve.worldBound;
    bool interesting = !string.IsNullOrEmpty(txt) || ve.GetType().Name.Contains("Mock") || cls.Contains("zui-section") || cls.Contains("zui-box") || cls.Contains("zui-stage");
    if (interesting)
        sb.AppendLine(new string(' ', d) + ve.GetType().Name + " [" + cls + "] '" + (txt ?? "") + "' " +
            "x=" + wb.x.ToString("0.0") + " y=" + wb.y.ToString("0.0") + " w=" + wb.width.ToString("0.0") + " h=" + wb.height.ToString("0.0") +
            (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None ? " DISPLAY-NONE" : ""));
    foreach (var c in ve.Children()) walk(c, d + 1);
};
walk(win.rootVisualElement, 0);
return sb.ToString();
