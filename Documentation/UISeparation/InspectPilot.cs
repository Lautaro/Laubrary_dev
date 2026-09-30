var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>()) {
    sb.AppendLine(w.titleContent.text + " " + w.position + " dpi=" + UnityEditor.EditorGUIUtility.pixelsPerPoint);
    var root = w.rootVisualElement;
    for (int i=0; i<root.styleSheets.count; i++) sb.AppendLine("sheet=" + root.styleSheets[i].name);
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(root).ForEach(e => {
        if (e.GetType().Namespace != null && (e.GetType().Namespace.StartsWith("Laubrary.Zui") || e.ClassListContains("pilot-wrap")))
            sb.AppendLine(e.GetType().Name + " " + e.worldBound + " classes=" + string.Join(",", e.GetClasses()));
    });
}
return sb.ToString();
