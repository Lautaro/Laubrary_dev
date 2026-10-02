UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/out");
var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
return "T0312.out=" + UnityEditor.EditorPrefs.GetString("T0312.out")
     + " | scene=" + s.path + " dirty=" + s.isDirty
     + " | isPlaying=" + UnityEditor.EditorApplication.isPlaying
     + " | compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed
     + " | dataPath=" + UnityEngine.Application.dataPath;
