var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
return "scene=" + s.path + " dirty=" + s.isDirty + " objs=" + s.GetRootGameObjects().Length;
