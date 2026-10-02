if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper/Audit0277")) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper","Audit0277");
var t = new UnityEngine.Texture2D(16,16,UnityEngine.TextureFormat.RFloat,false);
for (int y=0;y<16;y++) for (int x=0;x<16;x++) t.SetPixel(x,y,new UnityEngine.Color((x+y)/30f,0,0,1));
t.Apply();
UnityEditor.AssetDatabase.CreateAsset(t, "Assets/Shaper/Audit0277/rfield0277.asset");
UnityEditor.AssetDatabase.SaveAssets(); UnityEditor.AssetDatabase.Refresh();
return "created rfield0277 " + (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/Shaper/Audit0277/rfield0277.asset")!=null);
