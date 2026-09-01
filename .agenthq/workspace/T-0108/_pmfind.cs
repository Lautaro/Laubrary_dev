var sb = new System.Text.StringBuilder();
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
  System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (t.Name.EndsWith("Audit") && t.FullName.Contains("Shaper")) sb.AppendLine(t.FullName + "  [" + a.GetName().Name + "]");
}
return sb.ToString();
