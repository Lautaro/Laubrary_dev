// Solo the Mirage window, size and place it inside the desktop, and report the stage row's state.
string keep = "MirageWindow";
string[] unityOwn = { "MainToolbarWindow", "InspectorWindow", "ConsoleWindow", "ProjectBrowser",
                      "SceneHierarchyWindow", "SceneView", "GameView", "PopupWindow" };
var closed = new System.Collections.Generic.List<string>();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = w.GetType().Name;
    if (n == keep) continue;
    if (System.Array.IndexOf(unityOwn, n) >= 0) continue;
    closed.Add(n); w.Close();
}
var k = ZWin(keep);
if (k == null) return "no Mirage window";
k.position = new Rect(40, 40, 900, 760);
k.titleContent = new GUIContent("Mirage");
k.Focus(); k.Repaint();
UnityEditor.EditorPrefs.SetString("T320.capWin", keep);

var sb = new System.Text.StringBuilder();
sb.Append("closed: ").Append(string.Join(", ", closed)).Append("\n");
sb.Append("pos=").Append(k.position).Append("\n");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = k.GetType(); t != null; t = t.BaseType) { if (af == null) af = t.GetField("asset", BFi | System.Reflection.BindingFlags.DeclaredOnly); }
sb.Append("boundAsset=").Append(af != null ? (af.GetValue(k) == null ? "<null>" : af.GetValue(k).ToString()) : "<no field>").Append("\n");
var rigT = ZType("MirageRig");
sb.Append("rigInScene=").Append(rigT == null ? "?" : (UnityEngine.Object.FindFirstObjectByType(rigT) == null ? "none" : "yes")).Append("\n");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("MirageStage t:Scene")) sb.Append("stageScene=").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
sb.Append("activeScene=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path).Append("\n");
return sb.ToString();
