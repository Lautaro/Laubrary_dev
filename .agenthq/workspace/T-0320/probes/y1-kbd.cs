// Keyboard-only operation of the Layers card: focus each leaf control in Tab order and press the key that
// operates it (Enter/Space on a button, Right on a slider/min-max/pad), recording whether anything changed.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement card = null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-section__title") && l.text == "Layers") { card = e; break; } } if (card != null) break; }
if (card == null) return "no Layers section";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=win.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(win) as UnityEngine.Object;
System.Func<string> snap = () => { var so = new UnityEditor.SerializedObject(doc); var it = so.GetIterator(); var sb2 = new System.Text.StringBuilder(); int n = 0;
  while (it.NextVisible(true) && n < 3000) { n++; sb2.Append(it.propertyPath).Append('='); 
    switch (it.propertyType) { case UnityEditor.SerializedPropertyType.Float: sb2.Append(it.floatValue); break; case UnityEditor.SerializedPropertyType.Integer: sb2.Append(it.intValue); break; case UnityEditor.SerializedPropertyType.Boolean: sb2.Append(it.boolValue); break; case UnityEditor.SerializedPropertyType.String: sb2.Append(it.stringValue); break; case UnityEditor.SerializedPropertyType.Enum: sb2.Append(it.enumValueIndex); break; default: break; } sb2.Append(';'); }
  unchecked { uint h = 2166136261u; foreach (var ch in sb2.ToString()) { h = (h ^ (byte)ch) * 16777619u; } return h.ToString("X8"); } };
var results = new System.Text.StringBuilder();
int focusable = 0, moved = 0, notFocusable = 0;
foreach (var e in ZAll(card)) {
  if (!ZDrawn(e) || !ZIsLeafCtrl(e)) continue;
  string cap = ZCaption(e);
  if (cap != null && (cap.Contains("×") || cap.Contains("Delete") || cap.Contains("Dup"))) { results.AppendLine("SKIPPED (destructive/duplicating) '" + cap + "'"); continue; }
  if (!e.focusable) { notFocusable++; results.AppendLine("NOT FOCUSABLE '" + cap + "' " + e.GetType().Name); continue; }
  focusable++;
  string before = snap();
  e.Focus();
  var key = (e is UnityEngine.UIElements.Button) ? UnityEngine.KeyCode.Return : UnityEngine.KeyCode.RightArrow;
  for (int i=0;i<2;i++) {
    var kd = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', key, UnityEngine.EventModifiers.None);
    kd.target = e; using (kd) e.SendEvent(kd);
    var ku = UnityEngine.UIElements.KeyUpEvent.GetPooled('\0', key, UnityEngine.EventModifiers.None);
    ku.target = e; using (ku) e.SendEvent(ku);
  }
  string after = snap();
  bool ch = before != after;
  if (ch) moved++;
  results.AppendLine((ch ? "CHANGED " : "no-op   ") + "'" + cap + "' " + e.GetType().Name + " key=" + key);
}
return "Layers card: leafControls focusable=" + focusable + " notFocusable=" + notFocusable + " changedByKeyboard=" + moved + "\n" + results;
