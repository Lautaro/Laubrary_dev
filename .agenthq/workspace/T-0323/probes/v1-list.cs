var sb=new System.Text.StringBuilder();
foreach (var d in new string[]{"Assets/Shaper","Assets/SpriteFx","Assets/Mirage","Assets/Lathe"}) {
  sb.Append("== ").Append(d).Append("\n");
  if (System.IO.Directory.Exists(d)) foreach (var f in System.IO.Directory.GetFileSystemEntries(d)) sb.Append("  ").Append(f.Replace("\\","/")).Append("\n");
}
if (System.IO.Directory.Exists("Assets/Shaper/Variations")) sb.Append("variations=").Append(System.IO.Directory.GetFiles("Assets/Shaper/Variations").Length).Append("\n");
return sb.ToString();
