var sb=new System.Text.StringBuilder();
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/AuditT322Zoe.asset")==null)
  UnityEditor.AssetDatabase.CopyAsset("Assets/Demos/PreviewDemo/PreviewShooterZoe.asset","Assets/Shaper/AuditT322Zoe.asset");
UnityEditor.AssetDatabase.Refresh();
sb.Append(ZBind("ZoeWindow","Assets/Shaper/AuditT322Zoe.asset")).Append("\n");
var mw = ZWin("MirageWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
System.Reflection.MethodInfo sa=null; for (var t=mw.GetType(); t!=null&&sa==null; t=t.BaseType) sa=t.GetMethod("SetAsset",BFi);
sb.Append("mirage SetAsset param=").Append(sa!=null?sa.GetParameters()[0].ParameterType.Name:"none").Append("\n");
if (sa!=null) { var ty=sa.GetParameters()[0].ParameterType; var gs=UnityEditor.AssetDatabase.FindAssets("t:"+ty.Name);
  sb.Append("mirage assets=").Append(gs.Length).Append(" ");
  for(int i=0;i<gs.Length&&i<3;i++) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(gs[i])).Append(" | "); }
return sb.ToString();
