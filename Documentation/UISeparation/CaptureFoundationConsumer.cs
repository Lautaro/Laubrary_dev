UnityEditor.EditorWindow a = null, b = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationConsumerBaseline.FrozenBackSplashWindow>()) a = w;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.BackSplash.Editor.BackSplashWindow>()) b = w;
if (a == null || b == null || UnityEngine.Vector2.Distance(a.position.position, b.position.position) < 40) throw new System.InvalidOperationException("Independent consumer windows required");
var folder = Laubrary.Zounds.Uitk.ZoundsUitkCompare.OutputFolder;
foreach (var w in new[] { a, b }) { var t = Laubrary.Zounds.Uitk.ZoundsUitkCompare.Capture(w); if (t == null) throw new System.InvalidOperationException("Capture failed"); System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "foundation_consumer_" + (w == a ? "baseline" : "candidate") + ".png"), t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); }
return "Consumer pair captured";
