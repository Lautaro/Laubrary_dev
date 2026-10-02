// Solo the Zoe window on a FRESH Zoe: the clip control and the preview buttons in their empty state.
string keep = "ZoeWindow";
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
var sb = new System.Text.StringBuilder();
var zt = ZType("Zoe");
string path = "Assets/Zoetrope/AuditT325Zoe.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath(path, zt) == null)
{
    var so = UnityEngine.ScriptableObject.CreateInstance(zt);
    UnityEditor.AssetDatabase.CreateAsset(so, path);
    UnityEditor.AssetDatabase.SaveAssets();
    UnityEditor.AssetDatabase.Refresh();
}
var w = ZOpen(keep);
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{ string n = x.GetType().Name; if (n == keep) continue; if (System.Array.IndexOf(unityOwn, n) >= 0) continue; x.Close(); }
w = ZWin(keep);
if (w == null) return "no Zoe window";
w.position = new Rect(40, 40, 900, 900);
w.titleContent = new GUIContent("Zoe");
sb.Append(ZBind(keep, path)).Append("\n");
sb.Append("pos=").Append(w.position).Append("\n");
return sb.ToString();
