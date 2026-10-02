var sb=new System.Text.StringBuilder();
// close every SpriteFx stack window
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w!=null && w.GetType().Name=="SpriteFxStackWindow") { w.Close(); sb.Append("closed one\n"); }
sb.Append("remaining=").Append(ZWin("SpriteFxStackWindow")!=null).Append("\n");
return sb.ToString();
