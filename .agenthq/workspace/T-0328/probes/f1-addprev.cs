// Reopen Mirage on the scratch view (the popup is pressed in the NEXT eval, once this is laid out —
// LauAssetBrowser.Show reads the button's worldBound to place itself).
string keep = "MirageWindow";
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
ZOpen(keep);
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{ string n = x.GetType().Name; if (n == keep) continue; if (System.Array.IndexOf(unityOwn, n) >= 0) continue; x.Close(); }
var w = ZWin(keep); if (w == null) return "no Mirage window";
w.position = new Rect(40, 40, 900, 760);
w.titleContent = new GUIContent("Mirage");
return ZBind(keep, "Assets/Mirage/AuditT328View.asset") + " pos=" + w.position;
