var sb=new System.Text.StringBuilder();
var lauT = ZType("Lauminary");
var lb = ZType("LauminationBuilderWindow");
var lau = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/ProtoGuy.asset", lauT);
// find the laumination names
var svc = ZType("LauminaryService") ?? ZType("LauminaryStore") ?? ZType("LaunimatorService");
sb.Append("svc=").Append(svc!=null?svc.FullName:"-").Append("\n");
// look in the folder for Laumination assets
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Laumination", new string[]{"Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd"})) sb.Append("L:").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
var ofe = lb.GetMethod("OpenForEdit", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
foreach (var p in ofe.GetParameters()) sb.Append("param ").Append(p.Name).Append(":").Append(p.ParameterType.Name).Append(" ");
return sb.ToString();
