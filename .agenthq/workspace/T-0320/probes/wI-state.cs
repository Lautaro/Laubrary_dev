var win = ZWin("ShaperWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=win.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(win) as UnityEngine.Object; if (doc == null) return "nothing bound";
var so = new UnityEditor.SerializedObject(doc);
var sb = new System.Text.StringBuilder("doc=" + doc.name + " dirty=" + UnityEditor.EditorUtility.IsDirty(doc) + "\n");
var it = so.GetIterator(); bool go = it.NextVisible(true); int n = 0;
while (go && n < 400) { n++;
  string p = it.propertyPath;
  if (p.Split('.').Length <= 2 && (p.ToLower().Contains("light") || p.ToLower().Contains("layer") || p.ToLower().Contains("frame") || p.ToLower().Contains("border") || p.ToLower().Contains("swarm") || p.ToLower().Contains("height")))
    sb.AppendLine("  " + p + " : " + it.propertyType + (it.isArray ? " count=" + it.arraySize : (it.propertyType == UnityEditor.SerializedPropertyType.Boolean ? " = " + it.boolValue : (it.propertyType == UnityEditor.SerializedPropertyType.Integer ? " = " + it.intValue : ""))));
  go = it.NextVisible(true); }
return sb.ToString();
