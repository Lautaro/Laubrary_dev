var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

string src = "Assets/Shaper/New Shaper.asset";
string dup = "Assets/Shaper/AuditA24Doc.asset";
if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup) != null) UnityEditor.AssetDatabase.DeleteAsset(dup);
UnityEditor.AssetDatabase.CopyAsset(src, dup);
UnityEditor.AssetDatabase.Refresh();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(dup);
sb.Append("dup doc loaded=").Append(doc != null).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");

string abs = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, dup);
long t0 = System.IO.File.GetLastWriteTimeUtc(abs).Ticks;
long len0 = new System.IO.FileInfo(abs).Length;

// open the Shaper window exactly as the user does, then bind
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc);
var w = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
w.Show();
sb.Append("AFTER-OPENFOR dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");

// also: bind the REAL demo document and check (never saved)
var real = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(src);
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(real);
sb.Append("REAL New Shaper.asset AFTER-OPENFOR dirty=").Append(UnityEditor.EditorUtility.IsDirty(real)).Append("\n");
var real1 = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/New Shaper 1.asset");
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(real1);
sb.Append("REAL New Shaper 1.asset AFTER-OPENFOR dirty=").Append(UnityEditor.EditorUtility.IsDirty(real1)).Append("\n");

// file untouched?
sb.Append("dup mtimeSame=").Append(System.IO.File.GetLastWriteTimeUtc(abs).Ticks == t0)
  .Append(" lenSame=").Append(new System.IO.FileInfo(abs).Length == len0).Append("\n");

// every dirty SO in the project now
int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null || !UnityEditor.EditorUtility.IsDirty(o)) continue;
    var ap = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(ap)) continue;
    nd++; sb.Append("DIRTY-SO ").Append(ap).Append("\n");
}
sb.Append("dirtyCount=").Append(nd).Append("\n");
return sb.ToString();
