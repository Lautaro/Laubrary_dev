var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement stage=null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="ShaperPreviewStage") stage=e;
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var zp = stage.GetType().GetProperty("Zoom", BF2);
sb.Append("zoomProp=").Append(zp!=null).Append("\n");
if (zp!=null) { sb.Append("zoom=").Append(zp.GetValue(stage)).Append(" canWrite=").Append(zp.CanWrite).Append("\n"); if (zp.CanWrite) zp.SetValue(stage, 12); }
foreach (var p in stage.GetType().GetProperties(BF2)) sb.Append("p:").Append(p.Name).Append(" ");
win.Repaint();
UnityEditor.EditorPrefs.SetString("T320.capWin","ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/sh-zoom.png");
return sb.ToString();
