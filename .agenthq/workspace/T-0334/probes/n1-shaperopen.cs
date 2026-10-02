// Restore the scene the session started in, then open Shaper on the shipped demo document.
var sb = new System.Text.StringBuilder();
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = x.GetType().Name;
    if (n == "MainToolbarWindow" || n == "InspectorWindow" || n == "ConsoleWindow" || n == "ProjectBrowser"
        || n == "SceneHierarchyWindow" || n == "SceneView" || n == "GameView" || n == "PopupWindow") continue;
    x.Close();
}
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (sc.path != "Assets/Demos/ShaperDemo/ShaperDemo.unity")
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Demos/ShaperDemo/ShaperDemo.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(sc.path).Append(" dirty=").Append(sc.isDirty).Append("\n");
sb.Append("menu -> ").Append(UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper")).Append("\n");
var w = ZWin("ShaperWindow");
if (w == null) return sb.Append("no ShaperWindow").ToString();
w.position = new Rect(40, 40, 1100, 900);
w.titleContent = new GUIContent("Shaper");
sb.Append(ZBind("ShaperWindow", "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset")).Append("\n");
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("docDirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
return sb.ToString();
