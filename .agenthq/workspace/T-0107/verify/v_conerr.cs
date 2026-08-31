var sb = new System.Text.StringBuilder();
var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
if (System.IO.File.Exists(logPath)) {
  string txt; using (var fs = new System.IO.FileStream(logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) using (var sr = new System.IO.StreamReader(fs)) txt = sr.ReadToEnd();
  var lines = txt.Split('\n'); int shown = 0;
  for (int i = lines.Length - 1; i >= 0 && shown < 12; i--)
    if (lines[i].Contains("error CS")) { sb.Insert(0, lines[i].Trim() + "\n"); shown++; }
}
return sb.Length > 0 ? sb.ToString() : "no CS errors found in Editor.log tail";
