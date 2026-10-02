var sb=new System.Text.StringBuilder();
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name=="TextSplashWindow") { x.Close(); sb.Append("closed\n"); }
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/TextSplash");
sb.Append("reopened=").Append(ZWin("TextSplashWindow")!=null).Append("\n");
return sb.ToString();
