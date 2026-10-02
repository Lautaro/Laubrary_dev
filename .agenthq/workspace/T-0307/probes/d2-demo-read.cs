// Reads the 10 s play samples, stops playback through the transport's own button, and reports every console
// entry logged since d1 cleared it.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("SAMPLES: ").Append(UnityEditor.SessionState.GetString("T0307.play", "<none>")).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
int e = 0, w = 0, l = 0;
var args = new object[] { e, w, l };
lt.GetMethod("GetCountsByType", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, args);
sb.Append("console since clear: errors=").Append(args[0]).Append(" warnings=").Append(args[1]).Append(" logs=").Append(args[2]).Append("\n");

int total = (int)lt.GetMethod("StartGettingEntries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
var entryT = System.Type.GetType("UnityEditor.LogEntry,UnityEditor");
var entry = System.Activator.CreateInstance(entryT);
var getEntry = lt.GetMethod("GetEntryInternal", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
for (int i = 0; i < total && i < 40; i++)
{
    getEntry.Invoke(null, new object[] { i, entry });
    var msg = entryT.GetField("message", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(entry) as string;
    if (msg == null) continue;
    sb.Append("LOG[").Append(i).Append("] ").Append(msg.Length > 160 ? msg.Substring(0, 160).Replace("\n", " / ") : msg.Replace("\n", " / ")).Append("\n");
}
lt.GetMethod("EndGettingEntries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);

// stop through the transport's own button
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (el, into) => { if (el == null) return; into.Add(el); for (int i = 0; i < el.hierarchy.childCount; i++) Walk(el.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
foreach (var v in all)
    if (v is UnityEngine.UIElements.Button b && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause")))
    { sb.Append("transport reads '").Append(b.text).Append("' while playing\n");
      using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } break; }
sb.Append("playing after stop=").Append(WT.GetField("playing", BFi).GetValue(win)).Append("\n");

var demo = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("demo dirty after 10 s of play=").Append(demo != null && UnityEditor.EditorUtility.IsDirty(demo)).Append("\n");
return sb.ToString();
