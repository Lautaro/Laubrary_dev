// Census of raw/native UITK controls the UI guide bans where a Z.* equivalent exists, in every OPEN
// Laubrary tool window: Slider / SliderInt / MinMaxSlider (should be MicroSlider / MicroMinMax),
// EnumField / PopupField (should be MiniRadio / Segmented), Toggle (should be ZuiToggleButton),
// DropdownField (fine ONLY for a dynamic list, flagged so it can be judged).
var sb = new System.Text.StringBuilder();
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
foreach (var win in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string wn = win.GetType().Name;
    if (System.Array.IndexOf(unityOwn, wn) >= 0) continue;
    int nSlider = 0, nSliderInt = 0, nMinMax = 0, nEnum = 0, nPopup = 0, nToggle = 0, nDrop = 0;
    var det = new System.Text.StringBuilder();
    foreach (var e in ZAll(win.rootVisualElement))
    {
        if (!ZDrawn(e)) continue;
        string tn = e.GetType().Name;
        bool inMicro = false;
        for (var p = e; p != null; p = p.hierarchy.parent)
        { var pn = p.GetType().Name; if (pn == "ZuiMicroSlider" || pn == "ZuiMicroMinMax" || pn == "ZuiValueControl" || pn == "ZuiSegmented" || pn == "ZuiEnvelope" || pn == "ZuiRampControl" || pn == "ZuiGradientControl" || pn == "ZuiTimeline" || pn == "ZuiStepSequencer") { inMicro = true; break; } }
        if (inMicro) continue;
        string cls = ZCls(e);
        if (tn == "Slider") { nSlider++; det.Append("  Slider '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "SliderInt") { nSliderInt++; det.Append("  SliderInt '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "MinMaxSlider") { nMinMax++; det.Append("  MinMaxSlider '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "EnumField") { nEnum++; det.Append("  EnumField '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "PopupField`1") { nPopup++; det.Append("  PopupField '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "DropdownField") { nDrop++; det.Append("  DropdownField '").Append(ZCaption(e)).Append("' | ").Append(ZPath(e)).Append("\n"); }
        else if (tn == "Toggle" && !cls.Contains("zui-audit-allow-toggle")) { nToggle++; det.Append("  Toggle '").Append(ZCaption(e)).Append("' cls=").Append(cls).Append(" | ").Append(ZPath(e)).Append("\n"); }
    }
    sb.Append(wn).Append(": Slider=").Append(nSlider).Append(" SliderInt=").Append(nSliderInt).Append(" MinMax=").Append(nMinMax)
      .Append(" Enum=").Append(nEnum).Append(" Popup=").Append(nPopup).Append(" Dropdown=").Append(nDrop).Append(" Toggle=").Append(nToggle).Append("\n").Append(det);
}
return sb.ToString();
