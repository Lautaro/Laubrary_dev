// Click one NOT-currently-selected option in every radio group, re-querying after each.
string winName = UnityEditor.EditorPrefs.GetString("T322.driveWin","ZoeWindow");
var w = ZWin(winName);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
System.Func<string> hash = () => { var so = new UnityEditor.SerializedObject(asset); var it = so.GetIterator(); var s=new System.Text.StringBuilder(); int n=0;
  while (it.NextVisible(true) && n<6000) { n++; s.Append(it.propertyPath).Append('=');
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
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> radios = () => {
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
  foreach (var e in ZAll(w.rootVisualElement)) if (ZDisplayed(e) && e.enabledInHierarchy && ZCls(e).Contains("zui-radio")) l.Add(e);
  return l; };
int n0 = radios().Count, moved=0, tried=0; var dead=new System.Text.StringBuilder();
for (int i=0;i<n0;i++) {
  var list = radios(); if (i>=list.Count) break; var g = list[i];
  var kids=new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); ZWalk(g,kids);
  UnityEngine.UIElements.Button pick=null; string caps="";
  foreach (var k in kids) { var b=k as UnityEngine.UIElements.Button; if (b==null||!ZDisplayed(b)||!b.enabledInHierarchy) continue;
    caps += b.text+","; if (pick==null && !ZCls(b).Contains("--on") && !ZCls(b).Contains("selected") && !ZCls(b).Contains("active")) pick=b; }
  if (pick==null) continue;
  tried++; string h0 = hash(); ZClick(pick);
  if (hash()!=h0) moved++; else if (dead.Length<1200) dead.Append("[").Append(i).Append("] '").Append(pick.text).Append("' of {").Append(caps).Append("} | ");
}
return "win="+winName+" radioGroups="+n0+" clicked="+tried+" changedData="+moved+"\nNOCHANGE: "+dead;
