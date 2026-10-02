var sb = new System.Text.StringBuilder();
foreach (var f in System.IO.Directory.GetFiles(@"D:\UNITY\Laubrary Dev - Shaper\Assets\Shaper")) {
  var fi = new System.IO.FileInfo(f); if (fi.Name.EndsWith(".meta")) continue;
  sb.AppendLine(fi.Name + "  " + fi.Length + " bytes");
}
return sb.ToString();
