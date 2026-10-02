var win = ZWin("ShaperWindow");
UnityEngine.UIElements.VisualElement scrim = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.name == "zui-popover-scrim") { scrim = e; break; }
if (scrim == null) return "no popover";
var sb = new System.Text.StringBuilder();
var seen = new System.Collections.Generic.Dictionary<string,int>();
foreach (var e in ZAll(scrim)) {
  var te = e as UnityEngine.UIElements.Label; if (te == null || string.IsNullOrEmpty(te.text) || te.text == "✓") continue;
  var row = te.hierarchy.parent;
  string tip = ZTip(row);
  string key = te.text;
  if (!seen.ContainsKey(key)) seen[key] = 0; seen[key]++;
  sb.AppendLine("'" + te.text + "' tip='" + (tip.Length>90?tip.Substring(0,90)+"…":tip) + "'");
}
var dup = new System.Text.StringBuilder();
foreach (var kv in seen) if (kv.Value > 1) dup.Append(kv.Key).Append(" x").Append(kv.Value).Append("; ");
return "DUPLICATED ENTRIES: " + dup + "\n\n" + sb;
