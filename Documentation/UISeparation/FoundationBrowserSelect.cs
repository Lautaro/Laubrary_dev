int selected = 0;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w is Laubrary.BackSplash.Editor.BackSplashWindow || w is Laubrary.UISeparationConsumerBaseline.FrozenBackSplashWindow) {
    UnityEngine.UIElements.VisualElement found = null;
    foreach (var cell in UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(w.rootVisualElement, className: "zui-cell").ToList()) if (cell.tooltip.StartsWith("FoundationBackSplash —")) found = cell;
    if (found == null) throw new System.InvalidOperationException("Fixture cannot be discovered in browser");
    using (var e = UnityEngine.UIElements.PointerDownEvent.GetPooled(new UnityEngine.Event { type = UnityEngine.EventType.MouseDown, button = 0, clickCount = 2, mousePosition = found.worldBound.center })) { e.target = found; found.SendEvent(e); }
    if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Slider>(w.rootVisualElement) == null) throw new System.InvalidOperationException("Browser selection did not open editor"); selected++;
}
return "Double-clicked fixture through browser event route in " + selected + " consumer windows";
