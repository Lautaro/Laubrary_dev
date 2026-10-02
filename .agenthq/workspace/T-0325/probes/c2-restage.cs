var sb = new System.Text.StringBuilder();
string err = UnityEditor.AssetDatabase.MoveAsset("Assets/Mirage/T325Hidden.unity", "Assets/Mirage/MirageStage.unity");
sb.Append("move back='").Append(err).Append("'\n");
UnityEditor.AssetDatabase.Refresh();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("MirageStage t:Scene")) sb.Append("found ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
var w = ZWin("MirageWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var m in w.GetType().GetMethods(BFi)) if (m.Name == "RebuildBody" && m.GetParameters().Length == 0) m.Invoke(w, null);
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && b.text == "Open preview stage") sb.Append("BUTTON enabled=").Append(b.enabledInHierarchy).Append("\n"); }
sb.Append("scene=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path)
  .Append(" dirty=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty).Append("\n");
return sb.ToString();
