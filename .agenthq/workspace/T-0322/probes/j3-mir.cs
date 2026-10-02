var sb=new System.Text.StringBuilder();
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/AuditT322Mirage.asset")==null)
  UnityEditor.AssetDatabase.CopyAsset("Assets/Mirage/MirageDemo.asset","Assets/Shaper/AuditT322Mirage.asset");
UnityEditor.AssetDatabase.Refresh();
sb.Append(ZBind("MirageWindow","Assets/Shaper/AuditT322Mirage.asset")).Append("\n");
return sb.ToString();
