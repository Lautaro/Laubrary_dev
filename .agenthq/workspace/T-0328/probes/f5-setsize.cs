int h = UnityEditor.EditorPrefs.GetInt("T328.h", 520);
int wd = UnityEditor.EditorPrefs.GetInt("T328.w", 820);
foreach (var n in new string[]{ "PyreWindow", "ChoreographerWindow", "TilesetBuilderWindow" })
{ var w = ZWin(n); if (w != null) { w.position = new UnityEngine.Rect(30, 30, wd, h); w.Repaint(); } }
return "set " + wd + "x" + h;
