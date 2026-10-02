var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
var so=new UnityEditor.SerializedObject(asset); var it=so.GetIterator(); int n=0;
while (it.NextVisible(true) && n<3000) { n++; if (it.propertyType==UnityEditor.SerializedPropertyType.Float && Mathf.Abs(it.floatValue-7.25f)<0.001f) sb.Append("FOUND ").Append(it.propertyPath).Append("\n"); }
sb.Append("scanned=").Append(n).Append("\n");
// what does the control read now?
foreach (var e in ZAll(w.rootVisualElement)) { var f=e as UnityEngine.UIElements.FloatField; if (f!=null&&ZDisplayed(f)&&(ZTip(f)??"").StartsWith("Ring only: distance")) sb.Append("control now=").Append(f.value).Append(" drawn=").Append(ZDrawn(f)).Append("\n"); }
return sb.ToString();
