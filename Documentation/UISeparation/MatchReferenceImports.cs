string target="Assets/Editor/UISeparationPilot/Baseline/Skin/";
string source="Assets/Packages/Laubrary/Editor/Zounds/Uitk/Skin/";
int count=0;
foreach (var f in System.IO.Directory.GetFiles(target,"*.png")) {
    var original=UnityEditor.AssetImporter.GetAtPath(source+System.IO.Path.GetFileName(f)) as UnityEditor.TextureImporter;
    var reference=UnityEditor.AssetImporter.GetAtPath(f.Replace('\\','/')) as UnityEditor.TextureImporter;
    if (original==null || reference==null) throw new System.Exception("Missing importer");
    var settings=new UnityEditor.TextureImporterSettings(); original.ReadTextureSettings(settings); reference.SetTextureSettings(settings);
    reference.textureCompression=original.textureCompression;
    reference.SetPlatformTextureSettings(original.GetDefaultPlatformTextureSettings());
    reference.SaveAndReimport(); count++;
}
return "Matched "+count+" frozen texture importer settings; reference GUIDs preserved";
