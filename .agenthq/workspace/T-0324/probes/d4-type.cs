// Type into the New row's name field the way the field's own value change does, then press Create.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "");
string nm = UnityEditor.EditorPrefs.GetString("T324.newName", "AuditT324");
var w = ZWin(wn); if (w == null) return "no window " + wn;
UnityEngine.UIElements.TextField tf = null;
foreach (var e in ZAll(w.rootVisualElement)) { var t = e as UnityEngine.UIElements.TextField; if (t != null && ZDrawn(t)) { tf = t; break; } }
if (tf == null) return "no TextField in " + wn;
tf.value = nm;
w.Repaint();
return "typed '" + nm + "' into " + ZCls(tf);
