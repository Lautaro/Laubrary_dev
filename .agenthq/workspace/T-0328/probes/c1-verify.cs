var sb = new System.Text.StringBuilder();
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed)
  .Append(" isCompiling=").Append(UnityEditor.EditorApplication.isCompiling).Append("\n");
System.Type zp = null, dash = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) { if (t.Name == "ZoePreviewWindow") zp = t; if (t.Name == "DashboardEditorWindow") dash = t; } }
sb.Append("ZoePreviewWindow base=").Append(zp == null ? "<none>" : zp.BaseType.Name).Append("\n");
sb.Append("Dashboard has GroupBoxStyle=").Append(dash != null && dash.GetProperty("GroupBoxStyle", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance) != null).Append("\n");
return sb.ToString();
