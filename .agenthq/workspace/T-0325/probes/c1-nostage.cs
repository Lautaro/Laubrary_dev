// State C of the stage row — "no stage in the project". The only way to reach it by eye is to make the
// stage scene un-findable for a moment: AssetDatabase.MoveAsset keeps the GUID and the .meta, so the
// rename is exactly reversible (c3-restage.cs puts it back and git status is checked afterwards).
var sb = new System.Text.StringBuilder();
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Demos/ShaperDemo/ShaperDemo.unity",
    UnityEditor.SceneManagement.OpenSceneMode.Single);
sb.Append("scene=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path).Append("\n");
string err = UnityEditor.AssetDatabase.MoveAsset("Assets/Mirage/MirageStage.unity", "Assets/Mirage/T325Hidden.unity");
sb.Append("move='").Append(err).Append("'\n");
UnityEditor.AssetDatabase.Refresh();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("MirageStage t:Scene")) sb.Append("still found ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
var w = ZWin("MirageWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
foreach (var m in w.GetType().GetMethods(BFi)) if (m.Name == "RebuildBody" && m.GetParameters().Length == 0) rb = m;
if (rb != null) rb.Invoke(w, null);
w.Repaint();
foreach (var e in ZAll(w.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || b.text != "Open preview stage") continue;
    sb.Append("BUTTON enabled=").Append(b.enabledInHierarchy).Append("\nTIP=").Append(ZTip(b)).Append("\n");
}
return sb.ToString();
