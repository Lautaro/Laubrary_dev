// resize the Shaper window to T337.w x T337.h (its own separate round trip — a resize and an audit in
// the same eval returns the PRE-resize layout, round 18 §3).
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
float ww = float.Parse(UnityEditor.EditorPrefs.GetString("T337.w", "1500"));
float hh = float.Parse(UnityEditor.EditorPrefs.GetString("T337.h", "900"));
w.position = new UnityEngine.Rect(w.position.x, w.position.y, ww, hh);
w.Repaint();
return "position=" + w.position;
