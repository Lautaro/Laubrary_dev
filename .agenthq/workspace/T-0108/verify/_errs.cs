var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
var sb = new System.Text.StringBuilder();
try {
  using (var fs = new System.IO.FileStream(logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
  using (var sr = new System.IO.StreamReader(fs)) {
    var all = sr.ReadToEnd();
    var lines = all.Split('\n');
    int shown = 0;
    for (int i = lines.Length - 1; i >= 0 && shown < 40; i--) {
      if (lines[i].Contains("error CS")) { sb.Insert(0, lines[i].Trim() + "\n"); shown++; }
    }
  }
} catch (System.Exception e) { sb.Append("ERR " + e.Message); }
return sb.ToString();
