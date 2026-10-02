int dirty = 0; var names = new System.Collections.Generic.List<string>();
foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
{
    var p = UnityEditor.AssetDatabase.GetAssetPath(so);
    if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets/")) continue;
    if (UnityEditor.EditorUtility.IsDirty(so)) { dirty++; names.Add(p); }
}
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
return "dirty SOs=" + dirty + " : " + string.Join(" | ", names)
  + "\nactive scene=" + sc.path + " dirty=" + sc.isDirty
  + "\nopen scenes=" + UnityEditor.SceneManagement.EditorSceneManager.sceneCount
  + "\nMirageRig in open scenes: " + (UnityEngine.Object.FindFirstObjectByType(ZType("MirageRig")) != null);
