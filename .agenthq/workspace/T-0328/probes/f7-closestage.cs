var sc = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath("Assets/Mirage/MirageStage.unity");
if (!sc.IsValid()) return "MirageStage not open; scenes=" + UnityEditor.SceneManagement.EditorSceneManager.sceneCount;
bool stageDirty = sc.isDirty;
UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
var demo = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
return "closed MirageStage (was dirty=" + stageDirty + ", NOT saved); scenes=" + UnityEditor.SceneManagement.EditorSceneManager.sceneCount
  + " active=" + demo.path + " dirty=" + demo.isDirty
  + " rig=" + (UnityEngine.Object.FindFirstObjectByType(ZType("MirageRig")) != null);
