UnityEditor.Undo.PerformUndo();
var fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.BackSplash.BackSplash>("Assets/Editor/UISeparationPilot/Fixtures/FoundationBackSplash.asset");
if (UnityEngine.Vector4.Distance(fixture.cameraColor, new UnityEngine.Color(.2f, .3f, .4f)) > .01f) throw new System.InvalidOperationException("Consumer Undo did not restore original backdrop");
UnityEditor.AssetDatabase.SaveAssetIfDirty(fixture);
return "Consumer Undo restored original saved backdrop";
