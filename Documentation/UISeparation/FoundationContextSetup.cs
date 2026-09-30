Laubrary.UISeparationPilot.FoundationWindow window = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>()) if (w.titleContent.text.Contains("Candidate")) window = w;
var root = window.rootVisualElement;
var holder = new UnityEngine.UIElements.VisualElement { name = "context-probe" }; root.Add(holder);
for (int i = 0; i < 2; i++) {
    var owner = new UnityEngine.UIElements.VisualElement(); owner.AddToClassList(i == 0 ? "lau-tool-context-a" : "lau-tool-context-b"); owner.AddToClassList("do-not-copy-this-state"); holder.Add(owner);
    var anchor = new UnityEngine.UIElements.VisualElement(); owner.Add(anchor);
    owner.AddToClassList("zs-root");
    var context = Laubrary.Zui.ZuiPresentationContext.Capture(anchor, "zs-root");
    var target = new UnityEngine.UIElements.VisualElement { name = "context-target-" + i }; holder.Add(target); context.ApplyTo(target); context.ApplyTo(target);
    target.Add(Laubrary.Zui.Z.Float(0, "Context field", _ => {}));
    if (target.ClassListContains("do-not-copy-this-state")) throw new System.InvalidOperationException("State class leaked");
    if (!target.ClassListContains("zs-root")) throw new System.InvalidOperationException("Explicit skin class omitted");
    var unique = new System.Collections.Generic.HashSet<UnityEngine.UIElements.StyleSheet>(); for (int j = 0; j < target.styleSheets.count; j++) if (!unique.Add(target.styleSheets[j])) throw new System.InvalidOperationException("Duplicate sheet");
    owner.ClearClassList();
}
return "Two independent context snapshots attached; source classes removed; no state-class leak or duplicate sheets";
