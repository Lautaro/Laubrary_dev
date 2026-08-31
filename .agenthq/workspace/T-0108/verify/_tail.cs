var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
long mark = long.Parse(System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\mark.txt").Trim());
var sb = new System.Text.StringBuilder();
sb.AppendLine("compiling=" + UnityEditor.EditorApplication.isCompiling + " failed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
var seen = new System.Collections.Generic.HashSet<string>();
using (var fs = new System.IO.FileStream(logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) {
  fs.Seek(mark, System.IO.SeekOrigin.Begin);
  using (var sr = new System.IO.StreamReader(fs)) {
    string line;
    while ((line = sr.ReadLine()) != null)
      if (line.Contains("error CS") && seen.Add(line.Trim())) sb.AppendLine(line.Trim());
  }
}
return sb.ToString();
