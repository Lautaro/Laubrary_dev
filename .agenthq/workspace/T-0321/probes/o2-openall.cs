var sb=new System.Text.StringBuilder();
string[] wants = {"ChunkWindow","ZoeWindow","MirageWindow","LauminationBuilderWindow"};
foreach (var n in wants) {
  var t = ZType(n);
  var w = ZWin(n);
  if (w == null) {
    var m = UnityEditor.EditorWindow.GetWindow(t, false, n, false);
    sb.Append("opened ").Append(n).Append("\n");
  } else sb.Append("already ").Append(n).Append(" ").Append(w.position).Append("\n");
}
return sb.ToString();
