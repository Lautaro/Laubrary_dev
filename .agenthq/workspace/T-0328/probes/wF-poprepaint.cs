var sb = new System.Text.StringBuilder();
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (x.GetType().Name == "PopupWindow")
  {
      x.Repaint();
      sb.Append("POPUP pos=").Append(x.position).Append(" root=").Append(x.rootVisualElement != null ? ZAll(x.rootVisualElement).Count : -1).Append("\n");
  }
if (sb.Length == 0) sb.Append("popup gone\n");
return sb.ToString();
