var sb=new System.Text.StringBuilder();
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets","Shaper");
string dst="Assets/Shaper/AuditT322Pyre.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst)==null)
  sb.Append("copy=").Append(UnityEditor.AssetDatabase.CopyAsset("Assets/Pyre/Imported/Proper Blast.asset", dst)).Append("\n");
UnityEditor.AssetDatabase.Refresh();
var w = ZWin("PyreWindow");
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var af = w.GetType().GetField("asset", BF2);
var a = UnityEditor.AssetDatabase.LoadAssetAtPath(dst, af.FieldType);
foreach (var m in w.GetType().GetMethods(BF2)) if (m.Name=="Bind" && m.GetParameters().Length==1) { m.Invoke(w, new object[]{a}); sb.Append("bound via Bind\n"); break; }
sb.Append("asset=").Append(af.GetValue(w)==null?"null":((UnityEngine.Object)af.GetValue(w)).name).Append("\n");
return sb.ToString();
