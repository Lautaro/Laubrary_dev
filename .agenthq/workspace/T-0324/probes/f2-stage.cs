// Open the Mirage stage ADDITIVELY: the button's own path is Single + a save prompt, and the demo scene
// in this session is dirty, so a prompt would wedge the editor. Additive gets the rig into the open scene
// set, which is all the rig actually needs, and can be closed again without touching the demo scene.
string path = null;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("MirageStage t:Scene"))
{ var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g); if (!string.IsNullOrEmpty(p)) { path = p; break; } }
if (path == null) return "no MirageStage scene";
var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
var rig = UnityEngine.Object.FindFirstObjectByType(ZType("MirageRig"));
return "opened " + path + " additively; scenes=" + UnityEditor.SceneManagement.EditorSceneManager.sceneCount
  + " rig=" + (rig != null ? rig.name : "NONE")
  + " demoDirty=" + UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath("Assets/Demos/ShaperDemo/ShaperDemo.unity").isDirty;
