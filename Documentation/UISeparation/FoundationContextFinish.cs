foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>()) if (w.titleContent.text.Contains("Candidate")) {
    var root = w.rootVisualElement; var popup = (Laubrary.Zui.ZuiPopover)root.userData;
    var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.FloatField>(popup.Panel);
    if (UnityEngine.Mathf.Abs(field.resolvedStyle.width - 111) > .5f || !popup.Panel.parent.ClassListContains("lau-tool-context-a") || popup.Panel.parent.styleSheets.count < 9) throw new System.InvalidOperationException("Bare popup did not inherit complete local presentation");
    using (var e = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.Escape, UnityEngine.EventModifiers.None)) { e.target = popup.Panel; popup.Panel.SendEvent(e); }
    if (popup.IsOpen) throw new System.InvalidOperationException("Escape did not dismiss popup");
    root.userData = null; root.RemoveFromClassList("lau-tool-context-a"); root.AddToClassList("zui-root");
}
return "Bare-host popup retained tool scope and all sheets; Escape dismissed it; original root restored";
