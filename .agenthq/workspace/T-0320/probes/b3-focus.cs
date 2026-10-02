var win = ZWin(UnityEditor.EditorPrefs.GetString("T320.capWin","ShaperWindow"));
if (win == null) return "no window";
win.Focus(); win.Repaint();
UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
return "focused " + win.titleContent.text + " " + win.position;
