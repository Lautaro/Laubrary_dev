var sb = new System.Text.StringBuilder();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w.GetType().Name.ToLower().Contains("popup") || w.GetType().Name.ToLower().Contains("drop"))
    sb.Append(w.GetType().FullName).Append(" pos=").Append(w.position).Append(" hasRoot=").Append(w.rootVisualElement != null).Append("\n");
if (sb.Length == 0) sb.Append("no popup window\n");
return sb.ToString();
