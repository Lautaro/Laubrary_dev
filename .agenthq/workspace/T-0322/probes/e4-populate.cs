var sb=new System.Text.StringBuilder();
System.Func<string,string,string> cp = (src,dst) => {
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst)==null) UnityEditor.AssetDatabase.CopyAsset(src,dst);
  return dst; };
cp("Assets/Demos/LarderDemo/Ware_02_Crate.asset","Assets/Shaper/AuditT322Ware.asset");
cp("Assets/Lathe/New Lathe.asset","Assets/Shaper/AuditT322Lathe.asset");
cp("Assets/Lathe/Molds/New Lathe Mold.asset","Assets/Shaper/AuditT322Mold.asset");
cp("Assets/Demos/ProtoGuyDemo/ProtoGuy Fire Flash.asset","Assets/Shaper/AuditT322Fx.asset");
cp("Assets/Demos/TextSplashDemo/Splash Demo.asset","Assets/Shaper/AuditT322Splash.asset");
UnityEditor.AssetDatabase.Refresh();
sb.Append(ZBind("LarderWindow","Assets/Shaper/AuditT322Ware.asset")).Append("\n");
sb.Append(ZBind("LatheWindow","Assets/Shaper/AuditT322Lathe.asset")).Append("\n");
sb.Append(ZBind("SpriteFxStackWindow","Assets/Shaper/AuditT322Fx.asset")).Append("\n");
sb.Append(ZBind("TextSplashWindow","Assets/Shaper/AuditT322Splash.asset")).Append("\n");
return sb.ToString();
