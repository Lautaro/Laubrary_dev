var sb=new System.Text.StringBuilder();
var lvT = ZType("LauminaryVersion");
var lv = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/ProtoGuy_draft.asset", lvT);
var so = new UnityEditor.SerializedObject(lv);
var anims = so.FindProperty("animations");
for (int i=0;i<anims.arraySize;i++) {
  var a = anims.GetArrayElementAtIndex(i);
  var nm = a.FindPropertyRelative("name").stringValue;
  var top = a.FindPropertyRelative("sourceTextureGuid");
  var rec = a.FindPropertyRelative("recipe");
  string r0 = rec!=null && rec.arraySize>0 ? rec.GetArrayElementAtIndex(0).FindPropertyRelative("sourceTextureGuid").stringValue : "-";
  sb.Append(nm).Append(" topGuid='").Append(top!=null?top.stringValue:"<no field>").Append("' recipe0Guid='").Append(r0).Append("' frames=").Append(rec!=null?rec.arraySize:0).Append("\n");
}
// what the window holds now
var w = ZWin("LauminationBuilderWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach (var n in new string[]{"_sheet","_sheetPath","_sequence","_regions","_status","_animName"}) {
  var f = w.GetType().GetField(n, BFi); if (f==null) continue;
  var v = f.GetValue(w);
  sb.Append(n).Append("=").Append(v==null?"NULL":(v is System.Collections.ICollection c ? "count "+c.Count : v.ToString())).Append("\n");
}
return sb.ToString();
