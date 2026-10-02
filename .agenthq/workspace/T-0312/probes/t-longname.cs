// T-0309 growth check: bind a Shaper document whose file name is far longer than the old 200px field
// could ever show, at the window width in T0312.winw, and read back what the name label needs vs has.
const string path = "Assets/Shaper/AuditT0312 A Deliberately Very Long Document Name.asset";
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
if (doc == null)
{
    doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
    UnityEditor.AssetDatabase.CreateAsset(doc, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
}
UnityEditor.EditorPrefs.SetString("T0312.doc", path);
return "seeded " + doc.name + " (" + doc.name.Length + " chars)";
