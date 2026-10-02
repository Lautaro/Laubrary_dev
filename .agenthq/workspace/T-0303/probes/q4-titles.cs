var sb = new System.Text.StringBuilder();
var t = System.Type.GetType("System.Diagnostics.Process, System");
var procs = System.Diagnostics.Process.GetCurrentProcess();
foreach (var p in System.Diagnostics.Process.GetProcessesByName(procs.ProcessName))
{
    if (p.Id != procs.Id) continue;
    sb.Append("main window title='").Append(p.MainWindowTitle).Append("'\n");
}
return sb.ToString();
