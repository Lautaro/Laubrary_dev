// Copy an authored asset to a scratch name under Assets/Shaper and bind the named window to the copy.
string wn  = UnityEditor.EditorPrefs.GetString("T324.win", "");
string src = UnityEditor.EditorPrefs.GetString("T324.src", "");
string dst = UnityEditor.EditorPrefs.GetString("T324.dst", "");
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst) == null)
{
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
    if (!UnityEditor.AssetDatabase.CopyAsset(src, dst)) return "COPY FAILED " + src + " -> " + dst;
    UnityEditor.AssetDatabase.Refresh();
}
string r = ZBind(wn, dst);
var w = ZWin(wn); if (w != null) { w.position = new Rect(40, 20, UnityEditor.EditorPrefs.GetFloat("T324.w",900f), UnityEditor.EditorPrefs.GetFloat("T324.h",880f)); w.Repaint(); }
UnityEditor.EditorPrefs.SetString("T320.capWin", wn);
return r;
