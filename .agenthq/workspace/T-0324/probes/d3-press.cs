// Press a button by its caption, through the window's own path, with a before/after snapshot.
var sb = new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "");
string bt = UnityEditor.EditorPrefs.GetString("T324.btn", "");
int nth   = UnityEditor.EditorPrefs.GetInt("T324.btnNth", 0);
string ap = UnityEditor.EditorPrefs.GetString("T324.dst", "");
var w = ZWin(wn); if (w == null) return "no window " + wn;
sb.Append("BEFORE ").Append(ZState(w, ap)).Append("\n");
var b = ZFindBtn(w, bt, nth);
if (b == null) return sb.Append("NO BUTTON '").Append(bt).Append("' #").Append(nth).ToString();
UnityEngine.UIElements.ScrollView sv = null;
for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null && b.worldBound.yMax > w.position.height) { sv.ScrollTo(b); sb.Append("scrolled\n"); }
sb.Append("button rect=").Append(b.worldBound).Append(" enabled=").Append(b.enabledInHierarchy)
  .Append(" onscreen=").Append(b.worldBound.yMax <= w.position.height).Append("\n");
ZClick(b);
sb.Append("AFTER  ").Append(ZState(w, ap)).Append("\n");
return sb.ToString();
