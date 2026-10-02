// T-0271 step 0 - what does Unity actually WRITE for a null plain-[Serializable] class field?
// Build a bag with two members, every fill and border left null in memory, save, read the YAML back.
string dir = "Assets/Shaper/Audit0271";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "Audit0271");
string path = dir + "/Phantom.asset";
UnityEditor.AssetDatabase.DeleteAsset(path);
var d0 = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d0.canvasWidth = 96; d0.canvasHeight = 64; d0.frameCount = 4; d0.seed = 3u;
var lay = new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, id = 1,
    root = new Laubrary.Shaper.ShaperNode { name = "bag", kind = Laubrary.Shaper.ShaperNodeKind.Bag } };
var m1 = new Laubrary.Shaper.ShaperNode { name = "m1", kind = Laubrary.Shaper.ShaperNodeKind.Primitive };
lay.root.children.Add(m1);
d0.layers.Add(lay);
var sb = new System.Text.StringBuilder();
sb.Append("IN MEMORY: root.fill=" + (lay.root.fill == null ? "null" : "obj")
        + " root.border=" + (lay.root.border == null ? "null" : "obj")
        + " m1.fill=" + (m1.fill == null ? "null" : "obj") + "\n");
UnityEditor.AssetDatabase.CreateAsset(d0, path);
UnityEditor.AssetDatabase.SaveAssetIfDirty(d0);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
string yaml = System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, path));
sb.Append("YAML BYTES: " + yaml.Length + "\n");
sb.Append(yaml);
var re = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
var rn = re.layers[0].root;
sb.Append("\nAFTER RELOAD: root.fill=" + (rn.fill == null ? "null" : ("obj kind=" + rn.fill.kind + " color=" + rn.fill.solidColor + " composite=" + rn.fill.composite))
        + " root.border=" + (rn.border == null ? "null" : ("obj enabled=" + rn.border.enabled))
        + " m1.fill=" + (rn.children[0].fill == null ? "null" : ("obj kind=" + rn.children[0].fill.kind + " color=" + rn.children[0].fill.solidColor)) + "\n");
sb.Append("dataPath=" + UnityEngine.Application.dataPath + "\n");
return sb.ToString();
