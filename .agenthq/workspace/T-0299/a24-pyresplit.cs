var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) w0.Close();
foreach (var m in pyreT.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
    if (m.GetCustomAttributes(typeof(UnityEditor.MenuItem), false).Length > 0 && m.GetParameters().Length == 0) m.Invoke(null, null);
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;

System.Reflection.MethodInfo setAsset = null, rebuild = null;
for (var t = pyreT; t != null; t = t.BaseType)
{ if (setAsset == null) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
  if (rebuild == null) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly); }
// a DUPLICATE, never a user asset
var dup = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/AuditA24Pyre2.asset");
setAsset.Invoke(win, new object[] { dup });
sb.Append("minSize=").Append(win.minSize.x).Append("x").Append(win.minSize.y).Append("\n");

var lpwF = pyreT.GetField("leftPaneWidth", BFi);
var leftPaneF = pyreT.GetField("leftPane", BFi);

// the repro: a divider legitimately dragged wide at a big window, then the window shrunk
win.position = new UnityEngine.Rect(40, 40, 1900, 1100);
lpwF.SetValue(win, 1458f);
rebuild.Invoke(win, null);
sb.Append("wide window: leftPaneWidth=").Append(lpwF.GetValue(win)).Append("\n");

win.position = new UnityEngine.Rect(40, 40, win.minSize.x, win.minSize.y);
rebuild.Invoke(win, null);
UnityEditor.SessionState.SetString("A24.pyresplit", "armed");
sb.Append("shrunk to minimum and rebuilt; leftPaneWidth field=").Append(lpwF.GetValue(win)).Append("\n");
return sb.ToString();
