// screen geometry + put the Shaper window somewhere a screen grab can see all of it
var sb = new System.Text.StringBuilder();
sb.AppendLine("screen=" + Screen.currentResolution.width + "x" + Screen.currentResolution.height);
var mres = typeof(EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
sb.AppendLine("pixelsPerPoint=" + (mres!=null? mres.GetValue(null).ToString() : "?"));
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.AppendLine("doc=" + (doc!=null?doc.name:"NULL"));
var win = ZWin("ShaperWindow");
sb.AppendLine("shaper=" + (win!=null));
if (win != null) { win.titleContent = new GUIContent("T320Tag"); win.position = new Rect(0, 40, 1400, 900); win.Focus(); win.Repaint(); }
return sb.ToString();
