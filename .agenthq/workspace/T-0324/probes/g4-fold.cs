// Open every Foldout / ZuiBox / ZuiSection in the window and report what appeared.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "");
var w = ZWin(wn); if (w == null) return "no " + wn;
int before = ZAll(w.rootVisualElement).Count, opened = 0;
foreach (var e in ZAll(w.rootVisualElement))
{ var f = e as UnityEngine.UIElements.Foldout; if (f != null && !f.value) { f.value = true; opened++; } }
var zaT = ZType("ZuiAudit");
opened += (int)zaT.GetMethod("ExpandAll").Invoke(null, new object[]{ w });
w.Repaint();
return "opened " + opened + "; elements " + before + " -> " + ZAll(w.rootVisualElement).Count;
