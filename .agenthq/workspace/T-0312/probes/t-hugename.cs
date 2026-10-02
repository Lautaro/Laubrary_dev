// The pathological case T-0309 warned about: a name far wider than the row's whole spare width. flex-grow
// distributes only FREE space, so the field must stop at the row's edge and never push Delete off screen.
const string path = "Assets/Shaper/AuditT0312 An Absurdly Long Shaper Document Name That No One Would Ever Type But Which Must Not Break The Toolbar.asset";
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
if (doc == null)
{
    doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    UnityEditor.AssetDatabase.CreateAsset(doc, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(doc);
}
UnityEditor.EditorPrefs.SetString("T0312.doc", path);
return "seeded " + doc.name + " (" + doc.name.Length + " chars)";
