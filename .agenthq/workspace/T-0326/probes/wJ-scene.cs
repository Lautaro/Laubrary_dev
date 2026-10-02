var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var sb = new System.Text.StringBuilder();
sb.Append("scene=").Append(s.path).Append(" dirty=").Append(s.isDirty).Append(" loaded=").Append(s.isLoaded).Append("\n");
sb.Append("cameras: ");
foreach (var c in UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsSortMode.None))
  sb.Append(c.name).Append("(depth ").Append(c.depth).Append(", ortho ").Append(c.orthographicSize).Append(") ");
sb.Append("\nroots: ");
foreach (var g in s.GetRootGameObjects()) sb.Append(g.name).Append(" ");
sb.Append("\nchunkSpecs: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ChunkSpec")) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
