// Rebind the Zoe window to ProtoGuy with every section on, then report every drawn text input and every
// drawn dropdown — the after-picture for the reference sweep.
string keep = "ZoeWindow";
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
ZOpen(keep);
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{ string n = x.GetType().Name; if (n == keep) continue; if (System.Array.IndexOf(unityOwn, n) >= 0) continue; x.Close(); }
var w = ZWin(keep); if (w == null) return "no window";
w.position = new Rect(40, 40, 900, 900);
w.titleContent = new GUIContent("Zoe");
return ZBind(keep, "Assets/Demos/ProtoGuyDemo/ProtoGuy.asset");
