var sb=new System.Text.StringBuilder();
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name=="PopupWindow") { x.Close(); sb.Append("closed popup\n"); }
return sb.ToString()+"done";
