// Set the by-eye channel's prefs for this session, then report z-order: anything listed BEFORE the Unity
// window is drawn on top of it and makes a desktop read useless.
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
UnityEditor.EditorPrefs.SetString("T320.capWin", wn);
var win = ZWin(wn);
return "capWin=" + wn + " pos=" + (win != null ? win.position.ToString() : "?");
