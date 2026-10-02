var sb=new System.Text.StringBuilder();
foreach (var n in new string[]{"LauminaryBrowserWindow","SpriteCatalogWindow"}) {
  var t = ZType(n); var w = ZWin(n);
  if (w==null) { w = UnityEditor.EditorWindow.GetWindow(t, false, n, false); }
  w.position = new Rect(20,20,1100,880); w.Repaint();
  sb.Append(n).Append(" ").Append(w.position).Append("\n");
}
return sb.ToString();
