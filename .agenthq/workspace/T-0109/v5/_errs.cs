var sb = new System.Text.StringBuilder();
sb.AppendLine("failed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
string log = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + "/Unity/Editor/Editor.log";
try {
  using (var fs = new System.IO.FileStream(log, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
  using (var sr = new System.IO.StreamReader(fs))
  {
    var all = sr.ReadToEnd();
    var lines = all.Split('\n');
    int start = System.Math.Max(0, lines.Length - 400);
    for (int i = start; i < lines.Length; i++) if (lines[i].Contains("error CS")) sb.AppendLine(lines[i].Trim());
  }
} catch (System.Exception e) { sb.AppendLine("logerr " + e.Message); }
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v5\ERRS.txt", sb.ToString());
return sb.ToString();
