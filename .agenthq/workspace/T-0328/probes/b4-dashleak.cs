// Does DashboardEditorWindow.OnGUI allocate a Texture2D per repaint? Push one ownerless log into the
// static list the window paints from, then count Texture2D objects across forced repaints.
var sb = new System.Text.StringBuilder();
System.Type dash = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (t.FullName == "Laubrary.Dashboards.Dashboard") { dash = t; break; } if (dash != null) break; }
if (dash == null) return "no Dashboard type";
var logsF = dash.GetField("logs", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                                | System.Reflection.BindingFlags.NonPublic);
sb.Append("logs field=").Append(logsF == null ? "<null>" : logsF.FieldType.ToString()).Append("\n");
var logs = logsF.GetValue(null) as System.Collections.IList;
var elemT = logsF.FieldType.GetGenericArguments()[0];
var ctor = elemT.GetConstructors()[0];
var entry = ctor.Invoke(new object[] { "probe", "T-0328 leak probe", 12, UnityEngine.Color.white, null });
logs.Add(entry);
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "DashboardEditorWindow") win = w;
win.Focus();
System.Func<int> texCount = () => UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Texture2D>().Length;
int t0 = texCount();
var repaint = win.GetType().GetMethod("OnGUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
// drive real repaints through EditorApplication.update, which does tick between MCP calls
int ticks = 0; UnityEditor.EditorApplication.CallbackFunction cb = null;
cb = () => { ticks++; win.Repaint(); if (ticks >= 60) UnityEditor.EditorApplication.update -= cb; };
UnityEditor.EditorApplication.update += cb;
UnityEditor.EditorPrefs.SetInt("T328.tex0", t0);
sb.Append("logs now=").Append(logs.Count).Append(" texturesBefore=").Append(t0).Append(" (60 repaints armed)\n");
return sb.ToString();
