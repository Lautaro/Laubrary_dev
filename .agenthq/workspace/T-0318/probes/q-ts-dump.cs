var win = ZWin("LauminaryBrowserWindow"); if (win == null) return "NO BROWSER";
string s = ZTipSample(win, UnityEditor.EditorPrefs.GetString("T318.tsState","A"));
var p = ZDump("twostate-" + UnityEditor.EditorPrefs.GetString("T318.tsState","A") + ".txt", s);
return p + "  lines=" + s.Split('\n').Length;
