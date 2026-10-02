string wn = UnityEditor.EditorPrefs.GetString("T318.capWin","ChunkWindow");
var win = ZWin(wn); if (win == null) return "NO WINDOW " + wn;
win.Show(); win.Focus(); win.Repaint();
return "focused " + wn + " at " + win.position;
