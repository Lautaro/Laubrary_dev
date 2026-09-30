var windows=UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>();
if(windows.Length!=2) throw new System.Exception("Expected exactly two restored windows");
int baseline=0,candidate=0;
foreach(var w in windows) {
    if(UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiMicroSlider>(w.rootVisualElement)!=null) candidate++;
    if(UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.PilotBaseline.ZuiMicroSlider>(w.rootVisualElement)!=null) baseline++;
}
if(baseline!=1 || candidate!=1) throw new System.Exception("Domain reload did not restore independent trees");
return "PASS domain reload restored one frozen and one candidate tree; "+Laubrary.UISeparationPilot.PilotWindow.State();
