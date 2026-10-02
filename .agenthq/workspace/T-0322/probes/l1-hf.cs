string dir = "Assets/Shaper/Audit0277";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets","Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper","Audit0277");
string p = dir + "/rfield0277.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null) {
  var t = new Texture2D(16,16,TextureFormat.RFloat,false); t.name="rfield0277";
  for (int y=0;y<16;y++) for (int x=0;x<16;x++) t.SetPixel(x,y,new Color(Mathf.Clamp01((x+y)/30f),0,0,1));
  t.Apply();
  UnityEditor.AssetDatabase.CreateAsset(t, p);
  UnityEditor.AssetDatabase.SaveAssets();
}
return "hf=" + (UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p)!=null);
