var sb = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>()) {
    var root=w.rootVisualElement;
    bool candidate=w.titleContent.text.Contains("Candidate"), scoped=root.ClassListContains("lau-tool-pilot");
    var slider=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root, className:"zui-microslider");
    var bands=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root,className:"zui-skinband");
    var range=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root,className:"zui-skinrange");
    var env=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root,className:"zui-skinenvelope");
    sb.AppendLine(w.titleContent.text+" override="+scoped+" width="+slider.resolvedStyle.width);
    if (UnityEngine.Mathf.Abs(slider.resolvedStyle.width-(scoped?190f:150f))>.5f) throw new System.Exception("CSS width override/reset mismatch");
    if (candidate) {
        float gap=(float)bands.GetType().GetField("_gap",flags).GetValue(bands);
        sb.AppendLine("band gap="+gap);
        if (gap!=(scoped?8f:2f)) throw new System.Exception("Band metric override/reset failed");
        float thumbWidth=(float)range.GetType().GetField("_thumbWidth",flags).GetValue(range);
        sb.AppendLine("range thumb width="+thumbWidth);
        if (thumbWidth!=(scoped?18f:12f)) throw new System.Exception("Range metric override/reset failed");
        var p=env.GetType().GetProperty("Presentation",flags).GetValue(env);
        float padding=(float)p.GetType().GetField("paddingLeft",flags).GetValue(p);
        sb.AppendLine("envelope left padding="+padding);
        if (padding!=(scoped?18f:6f)) throw new System.Exception("Envelope padding override/reset failed");
        var palette=(UnityEngine.Color)slider.GetType().GetField("_resolvedFillLeft",flags).GetValue(slider);
        sb.AppendLine("slider fill="+palette);
        if (scoped && palette.r<.5f) throw new System.Exception("Painter ignored scoped style");
    }
    if (env.worldBound.height<100 || range.worldBound.height<20 || bands.worldBound.height<40) throw new System.Exception("Fixture collapsed");
}
return sb.ToString();
