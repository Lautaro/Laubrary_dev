// Audit any open window by type name (T320.auditWin), at width T320.auditW, every section open.
string wn = UnityEditor.EditorPrefs.GetString("T320.auditWin","ChunkWindow");
float w = float.Parse(UnityEditor.EditorPrefs.GetString("T320.auditW","820"));
var win = ZWin(wn); if (win == null) return wn + ": not open";
win.position = new UnityEngine.Rect(0, 20, w, 900);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var e in ZAll(win.rootVisualElement)) { if (secT.IsInstanceOfType(e)) isOpen.SetValue(e, true); }
win.Repaint();
return wn + " sized " + win.position;
