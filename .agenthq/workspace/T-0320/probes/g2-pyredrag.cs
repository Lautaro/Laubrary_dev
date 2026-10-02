// Open Pyre, bind a committed asset (read only), size the window narrow, then drag the divider fully
// right THROUGH THE WINDOW'S OWN DRAG PATH (real PointerDown/Move/Up on the splitter element).
var pyreT = ZType("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var win = ZWin("PyreWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre"); win = ZWin("PyreWindow"); }
if (win == null) return "NO PYRE WINDOW";
float w = float.Parse(UnityEditor.EditorPrefs.GetString("T320.pyrew","1000"));
win.position = new UnityEngine.Rect(0, 20, w, 900);
win.titleContent = new GUIContent("T320Pyre");
win.Show();
var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Pyre/New Pyre Plus.asset");
System.Reflection.MethodInfo setAsset = null;
for (var t = pyreT; t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[]{ asset });
System.Reflection.FieldInfo lpw = null;
for (var t = pyreT; t != null && lpw == null; t = t.BaseType) lpw = t.GetField("leftPaneWidth", BFi);
return "pyre bound=" + (asset!=null?asset.name:"NULL") + " win=" + win.position + " leftPaneWidth=" + (lpw!=null?lpw.GetValue(win).ToString():"?");
