var sb=new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
UnityEngine.UIElements.Button tb=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && (b.text.Contains("Play")||b.text.Contains("Pause"))) tb=b; }
sb.Append("transport='").Append(tb!=null?tb.text:"NONE").Append("'\n");
// readout label
foreach (var e in ZAll(w.rootVisualElement)) { var l=e as UnityEngine.UIElements.Label; if (l!=null && ZDrawn(l) && l.text!=null && (l.text.StartsWith("frame ")||l.text=="gap")) sb.Append("readout='").Append(l.text).Append("'\n"); }
if (tb!=null && tb.text.Contains("Play")) { ZClick(tb); sb.Append("pressed Play\n"); }
UnityEditor.EditorPrefs.SetFloat("T322.t0", (float)UnityEditor.EditorApplication.timeSinceStartup);
return sb.ToString();
