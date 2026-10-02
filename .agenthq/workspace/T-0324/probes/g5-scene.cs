var sb = new System.Text.StringBuilder();
var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.AppendLine("scene=" + sc.path + " dirty=" + sc.isDirty + " roots=" + sc.rootCount);
foreach (var go in sc.GetRootGameObjects())
    sb.AppendLine("  " + go.name + " hideFlags=" + go.hideFlags + " children=" + go.transform.childCount);
var t = ZType("ZoePalettePreview");
if (t != null) foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public))
    sb.AppendLine("  ZoePalettePreview." + f.Name + " = " + f.GetValue(null));
return sb.ToString();
