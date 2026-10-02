// Opens a floating, uniquely-titled Lazor window so PrintWindow can find it, and loads a shape into it.
string ASSET = "Assets/Lazor/New Lazor Shape.asset";   // "" = leave empty (browser state)

var wt = System.Type.GetType("Laubrary.Lazor.Editor.LazorWindow, com.Lautaro-Arino.Laubrary.Lazor.Editor");
foreach (var old in UnityEngine.Resources.FindObjectsOfTypeAll(wt))
    if (old is UnityEditor.EditorWindow ew && ew.titleContent.text.Contains("LazorWalk")) ew.Close();

var w = (UnityEditor.EditorWindow)UnityEngine.ScriptableObject.CreateInstance(wt);
w.titleContent = new UnityEngine.GUIContent("LazorWalk");
w.ShowUtility();
w.position = new UnityEngine.Rect(80, 80, 1180, 740);

if (!string.IsNullOrEmpty(ASSET)) {
    var so = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(ASSET);
    var setA = wt.GetMethod("SetAsset", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
    setA.Invoke(w, new object[]{ so });
}
w.Focus();
w.Repaint();
return "opened LazorWalk at " + w.position + "  asset=" + (ASSET == "" ? "<none>" : ASSET);
