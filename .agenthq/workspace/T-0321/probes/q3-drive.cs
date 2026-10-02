// DRIVE every leaf control in a window: change its value through its own control API, then check the bound
// asset's serialized data actually changed.  A control that moves nothing is a suspect.
string winName = UnityEditor.EditorPrefs.GetString("T321.driveWin","ChunkWindow");
var w = ZWin(winName);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
System.Func<string> hash = () => { var so = new UnityEditor.SerializedObject(asset); var it = so.GetIterator(); var sb2=new System.Text.StringBuilder(); int n=0;
  while (it.NextVisible(true) && n<4000) { n++; sb2.Append(it.propertyPath).Append('=');
    switch (it.propertyType) { case UnityEditor.SerializedPropertyType.Float: sb2.Append(it.floatValue); break;
      case UnityEditor.SerializedPropertyType.Integer: sb2.Append(it.intValue); break;
      case UnityEditor.SerializedPropertyType.Boolean: sb2.Append(it.boolValue); break;
      case UnityEditor.SerializedPropertyType.String: sb2.Append(it.stringValue); break;
      case UnityEditor.SerializedPropertyType.Enum: sb2.Append(it.enumValueIndex); break;
      case UnityEditor.SerializedPropertyType.Color: sb2.Append(it.colorValue); break;
      case UnityEditor.SerializedPropertyType.Vector2: sb2.Append(it.vector2Value); break;
      default: break; } sb2.Append(';'); }
  return sb2.ToString(); };
var all = ZAll(w.rootVisualElement);
var leaves = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var e in all) if (ZDrawn(e) && ZIsLeafCtrl(e) && e.enabledInHierarchy) leaves.Add(e);
int driven=0, moved=0, skipped=0; var deadList=new System.Text.StringBuilder();
string before = hash();
foreach (var e in leaves) {
  string h0 = hash(); bool did=false;
  if (e is UnityEngine.UIElements.Toggle tg) { tg.value = !tg.value; did=true; }
  else if (e is UnityEngine.UIElements.FloatField ff) { ff.value = ff.value + 1.37f; did=true; }
  else if (e is UnityEngine.UIElements.IntegerField iff) { iff.value = iff.value + 3; did=true; }
  else if (e is UnityEngine.UIElements.Slider sl) { sl.value = Mathf.Lerp(sl.lowValue, sl.highValue, 0.37f); did=true; }
  else if (e is UnityEngine.UIElements.SliderInt sli) { sli.value = Mathf.Clamp(sli.value+1, sli.lowValue, sli.highValue); did=true; }
  else if (e is UnityEngine.UIElements.MinMaxSlider mm) { mm.minValue = Mathf.Lerp(mm.lowLimit, mm.highLimit, 0.2f); did=true; }
  else if (e is UnityEditor.UIElements.ColorField cf) { cf.value = new Color(0.31f,0.62f,0.17f,1f); did=true; }
  if (!did) { skipped++; continue; }
  driven++;
  if (hash() != h0) moved++; else { if (deadList.Length<1400) deadList.Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' | "); }
}
return "win=" + winName + " leaves=" + leaves.Count + " driven=" + driven + " skipped(no driver)=" + skipped
     + " changedData=" + moved + " noChange=" + (driven-moved) + "\nDEAD: " + deadList.ToString();
