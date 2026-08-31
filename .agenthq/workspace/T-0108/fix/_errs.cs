var sb = new System.Text.StringBuilder();
sb.Append("compiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
string log = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Unity/Editor/Editor.log");
if (System.IO.File.Exists(log)) {
  string txt;
  using (var fs = new System.IO.FileStream(log, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
  using (var sr = new System.IO.StreamReader(fs)) txt = sr.ReadToEnd();
  var lines = txt.Split('\n');
  int start = System.Math.Max(0, lines.Length - 400);
  int shown = 0;
  for (int i = start; i < lines.Length; i++)
    if (lines[i].Contains("error CS") && shown < 12) { sb.Append(lines[i].Trim()).Append("\n"); shown++; }
  sb.Append("scanned tail lines=").Append(lines.Length - start);
}
return sb.ToString();
