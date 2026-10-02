// T-0313 — a scratch Shaper document in a realistically NESTED folder, so the Bake box's Destination
// readout can be measured against a path longer than the demo document's 23 characters.  Everything it
// creates lives under Assets/Shaper/AuditT0313 and is deleted (with its .meta) at the end of the task.
string root = "Assets/Shaper/AuditT0313";
string[] parts = { root, root + "/Characters", root + "/Characters/Bosses", root + "/Characters/Bosses/Final Boss" };
var sb = new System.Text.StringBuilder();
foreach (var p in parts)
{
    if (UnityEditor.AssetDatabase.IsValidFolder(p)) continue;
    int i = p.LastIndexOf('/');
    UnityEditor.AssetDatabase.CreateFolder(p.Substring(0, i), p.Substring(i + 1));
    sb.Append("created folder ").Append(p).Append("\n");
}
string docPath = parts[3] + "/AuditT0313Doc.asset";
var t = ZType("ShaperDocument");
if (t == null) return "NO ShaperDocument TYPE";
var existing = UnityEditor.AssetDatabase.LoadMainAssetAtPath(docPath);
if (existing == null)
{
    var so = UnityEngine.ScriptableObject.CreateInstance(t);
    UnityEditor.AssetDatabase.CreateAsset(so, docPath);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(so);
    sb.Append("created ").Append(docPath).Append("\n");
}
UnityEditor.EditorPrefs.SetString("T313.doc", docPath);
sb.Append("folder=").Append(parts[3]).Append(" (").Append(parts[3].Length).Append(" chars)\n");
return sb.ToString();
