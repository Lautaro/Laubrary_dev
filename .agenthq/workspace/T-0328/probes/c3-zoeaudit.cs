UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ZoePreviewWindow") win = w;
var txt = ZAudit(win, "zoeprev-protoguy");
var p = ZDump("zoeprev-protoguy.txt", txt);
var sb = new System.Text.StringBuilder();
sb.Append(ZSummary("zoeprev-protoguy")).Append("dump=").Append(p).Append("\n");
// what the clip picker offers, through the window's own helper
var m = win.GetType().GetMethod("ClipOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var opts = m.Invoke(win, null) as System.Collections.Generic.List<string>;
sb.Append("clipOptions(").Append(opts.Count).Append(")=").Append(string.Join(", ", opts)).Append("\n");
foreach (var e in ZAll(win.rootVisualElement))
  if (ZDrawn(e) && e is UnityEngine.UIElements.DropdownField df)
    sb.Append("dropdown value='").Append(df.value).Append("' choices=").Append(df.choices.Count)
      .Append(" enabled=").Append(df.enabledInHierarchy).Append(" w=").Append(df.worldBound.width.ToString("F1")).Append("\n");
foreach (var e in ZAll(win.rootVisualElement))
  if (ZDrawn(e) && e is UnityEngine.UIElements.TextField)
    sb.Append("!! TEXT FIELD STILL PRESENT: ").Append(ZCaption(e)).Append("\n");
return sb.ToString();
