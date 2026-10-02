var sb=new System.Text.StringBuilder();
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/AuditT323Pyre.asset")==null)
  sb.Append(UnityEditor.AssetDatabase.CopyAsset("Assets/Pyre/Imported/Proper Blast.asset","Assets/Shaper/AuditT323Pyre.asset")?"copied\n":"FAILED\n");
UnityEditor.AssetDatabase.Refresh();
sb.Append(ZBind("PyreWindow","Assets/Shaper/AuditT323Pyre.asset")).Append("\n");
var w=ZWin("PyreWindow"); w.position=new Rect(20,20,900,880);
// set zoom high to reproduce the round-13 case
return sb.ToString();
