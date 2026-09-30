var result = new System.Collections.Generic.List<string>();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>()) if (w.titleContent.text.Contains("Candidate")) {
    var holder = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(w.rootVisualElement, "context-probe");
    for (int i = 0; i < 2; i++) { var target = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(holder, "context-target-" + i); var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.FloatField>(target); float width = field.resolvedStyle.width; if (UnityEngine.Mathf.Abs(width - (i == 0 ? 111 : 222)) > .5f) throw new System.InvalidOperationException("Context scope failed " + width); result.Add("Independent context " + i + " width " + width); }
    holder.RemoveFromHierarchy();
    var root = w.rootVisualElement; root.RemoveFromClassList("zui-root"); root.AddToClassList("lau-tool-context-a");
    var anchor = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.FloatField>(root, "factory-float");
    var popup = Laubrary.Zui.ZuiPopover.Show(anchor, panel => panel.Add(Laubrary.Zui.Z.Float(0, "Bare host popup", _ => {})));
    root.userData = popup;
}
return string.Join("\n", result) + "\nBare-host popup opened";
