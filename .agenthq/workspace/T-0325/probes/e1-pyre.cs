// Pyre's shipped "Proper Blast" has Size 1.366666 — the value T-0324 grew the MicroSlider input for.
// Work on a COPY so nothing shipped is dirtied by the preview re-render.
string src = "Assets/Pyre/Imported/Proper Blast.asset";
string dst = "Assets/Pyre/AuditT325Pyre.asset";
var sb = new System.Text.StringBuilder();
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(src) == null) return "no source at " + src;
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst) == null)
    sb.Append("copy=").Append(UnityEditor.AssetDatabase.CopyAsset(src, dst)).Append("\n");
string keep = "PyreWindow";
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
ZOpen(keep);
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{ string n = x.GetType().Name; if (n == keep) continue; if (System.Array.IndexOf(unityOwn, n) >= 0) continue; x.Close(); }
var w = ZWin(keep); if (w == null) return sb.Append("no Pyre window").ToString();
w.position = new Rect(40, 40, 1000, 900);
w.titleContent = new GUIContent("Pyre");
sb.Append(ZBind(keep, dst)).Append("\n");
sb.Append("pos=").Append(w.position).Append("\n");
return sb.ToString();
