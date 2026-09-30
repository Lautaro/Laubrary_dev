var fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.BackSplash.BackSplash>("Assets/Editor/UISeparationPilot/Fixtures/FoundationBackSplash.asset");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.BackSplash.Editor.BackSplashWindow>()) {
    var field = UnityEngine.UIElements.UQueryExtensions.Q<UnityEditor.UIElements.ColorField>(w.rootVisualElement);
    if (field == null) throw new System.InvalidOperationException("Colour affordance unavailable");
    UnityEditor.Undo.IncrementCurrentGroup(); field.value = new UnityEngine.Color(.6f, .25f, .15f); UnityEditor.Undo.FlushUndoRecordObjects();
    if (fixture.cameraColor != field.value) throw new System.InvalidOperationException("Edit did not reach real asset");
    UnityEditor.AssetDatabase.SaveAssetIfDirty(fixture);
}
return "Colour field updated real isolated fixture and saved preview state";
