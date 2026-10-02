var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

string src = "Assets/Pyre/New Pyre Plus.asset";
string dup = "Assets/Shaper/AuditA24Pyre.asset";
if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup) != null) UnityEditor.AssetDatabase.DeleteAsset(dup);
sb.Append("copy=").Append(UnityEditor.AssetDatabase.CopyAsset(src, dup)).Append("\n");
UnityEditor.AssetDatabase.Refresh();

var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup);
sb.Append("dup loaded=").Append(o != null).Append(" dirty=").Append(o != null && UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
var f = o.GetType().GetField("previewLayerSel");
sb.Append("dup previewLayerSel(inmem)=").Append(f == null ? "NOFIELD" : f.GetValue(o).ToString()).Append("\n");
var layersF = o.GetType().GetField("layers");
var layers = layersF.GetValue(o) as System.Collections.ICollection;
sb.Append("dup layers=").Append(layers == null ? -1 : layers.Count).Append("\n");

// raw file text right after copy
string abs = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, dup);
foreach (var line in System.IO.File.ReadAllLines(abs)) if (line.Contains("previewLayerSel")) { sb.Append("dup file: ").Append(line.Trim()).Append("\n"); break; }
sb.Append("dup mtime=").Append(System.IO.File.GetLastWriteTimeUtc(abs).Ticks).Append("\n");
return sb.ToString();
