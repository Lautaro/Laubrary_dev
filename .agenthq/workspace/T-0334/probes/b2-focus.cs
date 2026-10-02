var sb = new System.Text.StringBuilder();
string want = UnityEditor.EditorPrefs.GetString("T334.focus", "LazorWindow");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == want) { w.Focus(); w.Repaint(); sb.Append("focused ").Append(want).Append(" pos=").Append(w.position).Append("\n"); }
return sb.ToString();
