// Create a scratch sprite with a deliberately long name, to stress every Z.Object in Shaper.
string dir = "Assets/Shaper/AuditT337Long";
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditT337Long");
string name = "AuditT337 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping";
string path = dir + "/" + name + ".png";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path) == null) {
  var t = new UnityEngine.Texture2D(8, 8, UnityEngine.TextureFormat.RGBA32, false);
  var px = new UnityEngine.Color32[64]; for (int i=0;i<64;i++) px[i] = new UnityEngine.Color32(200,120,40,255);
  t.SetPixels32(px); t.Apply();
  System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(t));
  UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  var imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
  if (imp != null) { imp.textureType = UnityEditor.TextureImporterType.Sprite; imp.spriteImportMode = UnityEditor.SpriteImportMode.Single; imp.SaveAndReimport(); }
}
var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
return "sprite=" + (sp==null?"<null>":sp.name) + " len=" + (sp==null?0:sp.name.Length) + " path=" + path;
