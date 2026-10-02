// Round 12/13/14 all hit this: the fill sweep LOADS an RFloat height field it never creates, and reports a
// false regression on Pyramid/Solid/Fill/heightFieldScale without it.
string dir = "Assets/Shaper/Audit0277";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0277");
string p = dir + "/rfield0277.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null)
{
    var t = new Texture2D(16, 16, TextureFormat.RFloat, false);
    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) t.SetPixel(x, y, new Color((x + y) / 30f, 0, 0, 1));
    t.Apply();
    UnityEditor.AssetDatabase.CreateAsset(t, p);
    UnityEditor.AssetDatabase.SaveAssets();
}
return "rfield ready: " + (UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
