var sb=new System.Text.StringBuilder();
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>()) {
  if (o==null) continue; var p = UnityEditor.AssetDatabase.GetAssetPath(o);
  if (string.IsNullOrEmpty(p) || !p.StartsWith("Assets")) continue;
  if (UnityEditor.EditorUtility.IsDirty(o)) sb.Append("DIRTY ").Append(p).Append("\n"); }
return sb.Length==0 ? "no dirty assets" : sb.ToString();
