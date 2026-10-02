System.Type dash = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (t.FullName == "Laubrary.Dashboards.Dashboard") { dash = t; break; } if (dash != null) break; }
var logs = dash.GetField("logs").GetValue(null) as System.Collections.IList;
logs.Clear();
UnityEditor.EditorPrefs.DeleteKey("T334.tex0");
return "logs cleared, count=" + logs.Count;
