var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo vbF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { vbF = t.GetField("viewBar", BFi); if (vbF != null) break; }
var vb = vbF.GetValue(win);
var capF = vb.GetType().GetField("_capture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var applyF = vb.GetType().GetField("_apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var cap = (System.Delegate)capF.GetValue(vb);
var apl = (System.Delegate)applyF.GetValue(vb);

// CAPTURE with everything as it is
var d = cap.DynamicInvoke() as System.Collections.IDictionary;
sb.Append("captured entries=").Append(d.Count).Append("\n");
foreach (System.Collections.DictionaryEntry e in d)
{
    var k = e.Key.ToString();
    sb.Append("  KEY '").Append(k.Length > 26 ? k.Substring(0, 26) : k).Append("' = ").Append(e.Value).Append("\n");
}
bool hasBackdrop = false, hasBake = false;
foreach (System.Collections.DictionaryEntry e in d)
{ var k = e.Key.ToString(); if (k.StartsWith("Preview backdrop")) hasBackdrop = true; if (k.StartsWith("Bake")) hasBake = true; }
sb.Append("RIGHT PANE captured: Preview backdrop=").Append(hasBackdrop).Append(" Bake=").Append(hasBake).Append("\n");
return sb.ToString();
