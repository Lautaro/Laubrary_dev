UnityEditor.EditorPrefs.SetString("T323.delay","pending");
UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorPrefs.SetString("T323.delay","FIRED");
return "registered";
