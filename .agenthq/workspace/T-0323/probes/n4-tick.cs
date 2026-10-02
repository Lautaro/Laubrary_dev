var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
foreach (var n in new string[]{"_scrub","_lastTick","_playing"}) { var f=w.GetType().GetField(n,BFi); if (f!=null) sb.Append(n).Append("=").Append(f.GetValue(w)).Append(" "); }
sb.Append(" timeSinceStartup=").Append(UnityEditor.EditorApplication.timeSinceStartup.ToString("F2"));
sb.Append(" delay=").Append(UnityEditor.EditorPrefs.GetString("T323.delay","?"));
return sb.ToString();
