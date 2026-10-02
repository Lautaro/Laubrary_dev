// Copy the shipped Proper Blast (never edit or save the original) and open Pyre on the copy.
string src = "Assets/Pyre/Imported/Proper Blast.asset";
string dst = "Assets/Shaper/AuditT324Pyre.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst) == null)
{
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
    if (!UnityEditor.AssetDatabase.CopyAsset(src, dst)) return "COPY FAILED " + src;
    UnityEditor.AssetDatabase.Refresh();
}
var w = ZOpen("PyreWindow");
if (w == null) return "no PyreWindow type";
string bound = ZBind("PyreWindow", dst);
w.position = new Rect(40, 40, 900, 880);
w.titleContent = new GUIContent("Pyre");
return bound + " | pos=" + w.position + " | srcDirty=" + UnityEditor.EditorUtility.IsDirty(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(src));
