// The fill sweep LOADS Assets/Shaper/Audit0277/rfield0277.asset and never creates it; round 12 and round 14
// both reported a false regression on Pyramid/Solid/Fill/heightFieldScale because of that. Create it first.
string dir = "Assets/Shaper/Audit0277";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0277");
string p = dir + "/rfield0277.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(p) == null)
{
    var t = new UnityEngine.Texture2D(16, 16, UnityEngine.TextureFormat.RFloat, false);
    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        t.SetPixel(x, y, new UnityEngine.Color((x + y) / 30f, 0f, 0f, 1f));
    t.Apply();
    UnityEditor.AssetDatabase.CreateAsset(t, p);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(t);
}
var got = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(p);
return "rfield=" + (got == null ? "<null>" : got.format + " " + got.width + "x" + got.height);
