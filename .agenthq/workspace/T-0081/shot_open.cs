System.Func<string, System.Type> findType = full =>
{
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = a.GetType(full);
        if (t != null) return t;
    }
    return null;
};

var specType = findType("Laubrary.Chunks.ChunkSpec");
if (specType == null) return "ChunkSpec type not found";
var winType = findType("Laubrary.Chunks.Editor.ChunkWindow");
if (winType == null) return "ChunkWindow type not found";

string path = "Assets/Chunks/_ScratchEmptyState.asset";
var existing = UnityEditor.AssetDatabase.LoadAssetAtPath(path, specType);
if (existing == null)
{
    var so = UnityEngine.ScriptableObject.CreateInstance(specType);
    UnityEditor.AssetDatabase.CreateAsset(so, path);
    UnityEditor.AssetDatabase.SaveAssets();
    existing = UnityEditor.AssetDatabase.LoadAssetAtPath(path, specType);
}

var getWindow = typeof(UnityEditor.EditorWindow).GetMethod("GetWindow",
    new System.Type[] { typeof(System.Type), typeof(bool), typeof(string), typeof(bool) });
var win = (UnityEditor.EditorWindow)getWindow.Invoke(null, new object[] { winType, true, "ChunksEmptyProbe", true });
win.titleContent = new UnityEngine.GUIContent("ChunksEmptyProbe");
win.position = new UnityEngine.Rect(80, 40, 900, 1500);

var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var setAsset = winType.GetMethod("SetAsset", flags);
if (setAsset == null) return "SetAsset not found on ChunkWindow";
setAsset.Invoke(win, new object[] { existing });
win.Repaint();

return "opened ChunksEmptyProbe on " + path;
