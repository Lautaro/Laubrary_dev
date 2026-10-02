var sb=new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w!=null && w.GetType().Name=="MirageWindow") { w.Close(); sb.Append("closed\n"); }
sb.Append("gone=").Append(ZWin("MirageWindow")==null).Append("\n");
sb.Append("scene=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path).Append("\n");
return sb.ToString();
