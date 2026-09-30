const string folder = "Assets/Editor/UISeparationPilot/Fixtures";
if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Editor/UISeparationPilot", "Fixtures");
var fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.BackSplash.BackSplash>(folder + "/FoundationBackSplash.asset");
if (fixture == null) { fixture = UnityEngine.ScriptableObject.CreateInstance<Laubrary.BackSplash.BackSplash>(); fixture.cameraColor = new UnityEngine.Color(.2f, .3f, .4f); UnityEditor.AssetDatabase.CreateAsset(fixture, folder + "/FoundationBackSplash.asset"); }
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationConsumerBaseline.FrozenBackSplashWindow>()) w.Close();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.BackSplash.Editor.BackSplashWindow>()) w.Close();
UnityEditor.EditorWindow[] windows = { UnityEngine.ScriptableObject.CreateInstance<Laubrary.UISeparationConsumerBaseline.FrozenBackSplashWindow>(), UnityEngine.ScriptableObject.CreateInstance<Laubrary.BackSplash.Editor.BackSplashWindow>() };
for (int i = 0; i < windows.Length; i++) { var w = windows[i]; w.Show(); w.position = new UnityEngine.Rect(90 + i * 735, 95, 720, 760); w.titleContent = new UnityEngine.GUIContent(i == 0 ? "BackSplash — Frozen" : "BackSplash — Candidate"); w.GetType().GetMethod("SetAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(w, new object[] { fixture }); }
return "Opened independent real BackSplash consumer/reference with isolated fixture";
