// DRIVE, re-querying after every change (the window rebuilds and detaches handles).
string winName = UnityEditor.EditorPrefs.GetString("T321.driveWin","ChunkWindow");
var w = ZWin(winName);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
System.Func<string> hash = () => { var so = new UnityEditor.SerializedObject(asset); var it = so.GetIterator(); var s=new System.Text.StringBuilder(); int n=0;
  while (it.NextVisible(true) && n<4000) { n++; s.Append(it.propertyPath).Append('=');
    switch (it.propertyType) { case UnityEditor.SerializedPropertyType.Float: s.Append(it.floatValue); break;
      case UnityEditor.SerializedPropertyType.Integer: s.Append(it.intValue); break;
      case UnityEditor.SerializedPropertyType.Boolean: s.Append(it.boolValue); break;
      case UnityEditor.SerializedPropertyType.String: s.Append(it.stringValue); break;
      case UnityEditor.SerializedPropertyType.Enum: s.Append(it.enumValueIndex); break;
      case UnityEditor.SerializedPropertyType.Color: s.Append(it.colorValue); break;
      case UnityEditor.SerializedPropertyType.Vector2: s.Append(it.vector2Value); break;
      case UnityEditor.SerializedPropertyType.ObjectReference: s.Append(it.objectReferenceInstanceIDValue); break;
      default: break; } s.Append(';'); }
  return s.ToString(); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> drivables = () => {
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
  foreach (var e in ZAll(w.rootVisualElement)) {
    if (!ZDisplayed(e) || !e.enabledInHierarchy) continue;
    if (e is UnityEngine.UIElements.Toggle || e is UnityEngine.UIElements.FloatField || e is UnityEngine.UIElements.IntegerField
     || e is UnityEngine.UIElements.Slider || e is UnityEngine.UIElements.SliderInt || e is UnityEngine.UIElements.MinMaxSlider
     || e is UnityEditor.UIElements.ColorField) l.Add(e);
  }
  return l; };
int total = drivables().Count, moved=0; var dead=new System.Text.StringBuilder();
for (int i=0;i<total;i++) {
  var list = drivables(); if (i>=list.Count) { dead.Append("SHRANK at i=").Append(i).Append(" to ").Append(list.Count).Append(" | "); break; } var e = list[i];
  string h0 = hash();
  if (e is UnityEngine.UIElements.Toggle tg) tg.value = !tg.value;
  else if (e is UnityEngine.UIElements.IntegerField iff) iff.value = iff.value + 3;
  else if (e is UnityEngine.UIElements.FloatField ff) ff.value = ff.value + 1.37f;
  else if (e is UnityEngine.UIElements.SliderInt sli) sli.value = Mathf.Clamp(sli.value+1, sli.lowValue, sli.highValue);
  else if (e is UnityEngine.UIElements.Slider sl) sl.value = Mathf.Lerp(sl.lowValue, sl.highValue, 0.37f);
  else if (e is UnityEngine.UIElements.MinMaxSlider mm) mm.minValue = Mathf.Lerp(mm.lowLimit, mm.highLimit, 0.2f);
  else if (e is UnityEditor.UIElements.ColorField cf) cf.value = new Color(0.31f,0.62f,0.17f,1f);
  if (hash() != h0) moved++;
  else if (dead.Length<1200) dead.Append("[").Append(i).Append("] ").Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' | ");
}
return "win=" + winName + " drivable=" + total + " changedData=" + moved + " noChange=" + (total-moved) + "\nDEAD: " + dead;
