foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.BackSplash.Editor.BackSplashWindow>()) w.Close();
var reopened = UnityEngine.ScriptableObject.CreateInstance<Laubrary.BackSplash.Editor.BackSplashWindow>(); reopened.Show(); reopened.position = new UnityEngine.Rect(825, 95, 720, 760); reopened.titleContent = new UnityEngine.GUIContent("BackSplash — Candidate");
var current = (Laubrary.BackSplash.BackSplash)typeof(Laubrary.BackSplash.Editor.BackSplashWindow).GetProperty("Current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(reopened);
if (current == null || UnityEditor.AssetDatabase.GetAssetPath(current) != "Assets/Editor/UISeparationPilot/Fixtures/FoundationBackSplash.asset") throw new System.InvalidOperationException("Reopen lost selected fixture");
if (UnityEngine.Vector4.Distance(current.cameraColor, new UnityEngine.Color(.2f, .3f, .4f)) > .01f) throw new System.InvalidOperationException("Saved colour not restored");
return "Cold reopened consumer restored fixture selection and saved colour without SetAsset";
