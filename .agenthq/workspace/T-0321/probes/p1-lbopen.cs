var sb=new System.Text.StringBuilder();
var lauT = ZType("Lauminary"); var lvT = ZType("LauminaryVersion");
var lv = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/ProtoGuy_draft.asset", lvT);
var so = new UnityEditor.SerializedObject(lv);
var it = so.GetIterator(); int n=0; string firstAnim=null;
while (it.NextVisible(true) && n<400) { n++;
  if (it.propertyPath.EndsWith(".name") && it.propertyType==UnityEditor.SerializedPropertyType.String && !string.IsNullOrEmpty(it.stringValue) && firstAnim==null && it.propertyPath.Contains("anim")) firstAnim=it.stringValue;
  if (n<40) sb.Append(it.propertyPath).Append("=").Append(it.propertyType==UnityEditor.SerializedPropertyType.String?it.stringValue:it.propertyType.ToString()).Append("\n");
}
sb.Append("firstAnim=").Append(firstAnim).Append("\n");
return sb.ToString();
