var checks = new System.Collections.Generic.List<string>();
void Check(bool ok, string message) { if (!ok) throw new System.InvalidOperationException(message); checks.Add(message); }
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>())
{
    bool reference = (bool)typeof(Laubrary.UISeparationPilot.FoundationWindow).GetField("_reference", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(w);
    if (reference) continue;
    var root = w.rootVisualElement;
    var f = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.FloatField>(root, "factory-float");
    var fixture = (Laubrary.UISeparationPilot.FoundationWindow.Fixture)typeof(Laubrary.UISeparationPilot.FoundationWindow).GetField("_fixture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(w);
    float old = fixture.amount;
    UnityEditor.Undo.IncrementCurrentGroup(); f.value = .71f; UnityEditor.Undo.FlushUndoRecordObjects();
    Check(UnityEngine.Mathf.Abs(fixture.amount - .71f) < .001f, "numeric callback updates fixture");
    Check(UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(root, "foundation-preview").text.Contains("0.71"), "numeric edit reaches visible preview");
    UnityEditor.Undo.PerformUndo(); Check(UnityEngine.Mathf.Abs(fixture.amount - old) < .001f, "numeric Undo restores fixture");
    f.SetValueWithoutNotify(old);
    var box = UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiBox>(root, "foundation-box");
    bool was = box.IsOpen; box.IsOpen = !was; Check(box.IsOpen != was, "card can fold"); box.IsOpen = was;
    var section = UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSection>(root, "foundation-section");
    was = section.IsOpen; section.IsOpen = !was; Check(section.IsOpen != was, "section can fold"); section.IsOpen = was;
    var popup = Laubrary.Zui.ZuiPopover.Show(f, p => p.Add(Laubrary.Zui.Z.Float(.3f, "Popup value", _ => {})));
    Check(popup.IsOpen, "attached popover opens"); popup.Close(); Check(!popup.IsOpen, "attached popover closes");
    var explicitWidth = Laubrary.Zui.Z.Float(0, "Explicit width contract", _ => {}, 83); Check(explicitWidth.style.width.value.value == 83, "explicit legacy width preserved");
}
return string.Join("\n", checks);
