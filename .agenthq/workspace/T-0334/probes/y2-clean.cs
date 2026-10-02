var sb = new System.Text.StringBuilder();
// unbind every window first so nothing holds a deleted asset
foreach (var n in new string[]{"ShaperWindow","PyreWindow","PropWindow","TilesetBuilderWindow","ZoePreviewWindow"})
{
    var w = ZWin(n); if (w == null) continue;
    for (var t = w.GetType(); t != null; t = t.BaseType)
    { var m = t.GetMethod("SetAsset", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly);
      if (m != null) { try { m.Invoke(w, new object[]{ null }); } catch { } break; } }
}
var zp = ZWin("ZoePreviewWindow");
if (zp != null) { var f = zp.GetType().GetField("_zoe", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance); if (f != null) f.SetValue(zp, null); }
int n2 = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT334"))
{ var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g); if (UnityEditor.AssetDatabase.DeleteAsset(p)) { n2++; sb.Append("deleted ").Append(p).Append("\n"); } }
foreach (var g in UnityEditor.AssetDatabase.FindAssets("rfield0277"))
{ var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g); if (UnityEditor.AssetDatabase.DeleteAsset(p)) { n2++; sb.Append("deleted ").Append(p).Append("\n"); } }
foreach (var d in new string[]{ "Assets/Shaper/Audit0277", "Assets/Cartographer/Props", "Assets/Cartographer/Tilesets" })
  if (UnityEditor.AssetDatabase.IsValidFolder(d)) { UnityEditor.AssetDatabase.DeleteAsset(d); sb.Append("deleted folder ").Append(d).Append("\n"); }
UnityEditor.AssetDatabase.Refresh();
sb.Append("deleted=").Append(n2).Append("\n");
sb.Append("remaining AuditT334 matches=").Append(UnityEditor.AssetDatabase.FindAssets("AuditT334").Length).Append("\n");
return sb.ToString();
