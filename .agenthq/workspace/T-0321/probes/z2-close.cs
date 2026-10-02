var sb=new System.Text.StringBuilder();
// demo/real assets must be clean
foreach (var p in new string[]{"Assets/Demos/ShaperDemo/ShaperDemoDoc.asset","Assets/Demos/ChunksDemo/WallDebris.asset","Assets/Demos/PreviewDemo/PreviewShooterZoe.asset","Assets/Mirage/MirageDemo.asset","Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/draft/ProtoGuy_draft.asset","Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/ProtoGuy.asset"}) {
  var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
  sb.Append(o!=null?("dirty("+System.IO.Path.GetFileName(p)+")="+UnityEditor.EditorUtility.IsDirty(o)):("MISSING "+p)).Append("\n"); }
foreach (var n in new string[]{"ChunkWindow","ZoeWindow","MirageWindow","LauminationBuilderWindow","LauminaryBrowserWindow","SpriteCatalogWindow","PyreWindow"}) {
  var w = ZWin(n); if (w!=null) { w.Close(); sb.Append("closed ").Append(n).Append("\n"); } }
UnityEditor.Undo.ClearAll();
var lt = ZType("LogEntries") ?? ZType("UnityEditor.LogEntries");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
return sb.ToString();
