var sb=new System.Text.StringBuilder();
var gs = UnityEditor.AssetDatabase.FindAssets("MirageDemo");
foreach (var g in gs) sb.Append("mirage: ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
string[,] pairs = new string[,]{
 {"Assets/Demos/LarderDemo/Ware_02_Crate.asset","Assets/Shaper/AuditT323Ware.asset"},
 {"Assets/Lathe/New Lathe.asset","Assets/Shaper/AuditT323Lathe.asset"},
 {"Assets/Demos/ProtoGuyDemo/ProtoGuy Fire Flash.asset","Assets/Shaper/AuditT323Fx.asset"},
 {"Assets/Demos/TextSplashDemo/Splash Demo.asset","Assets/Shaper/AuditT323Splash.asset"},
 {"Assets/Demos/PreviewDemo/PreviewShooterZoe.asset","Assets/Shaper/AuditT323Zoe.asset"}
};
for (int i=0;i<pairs.GetLength(0);i++){
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pairs[i,1])!=null) { sb.Append("exists ").Append(pairs[i,1]).Append("\n"); continue; }
  bool ok = UnityEditor.AssetDatabase.CopyAsset(pairs[i,0], pairs[i,1]);
  sb.Append(ok?"copied ":"FAILED ").Append(pairs[i,1]).Append("\n");
}
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
