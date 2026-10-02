var w = ZWin("ShaperWindow"); if (w == null) return "no window";
string want = UnityEditor.EditorPrefs.GetString("T334.pressText","Add edge");
var sb=new System.Text.StringBuilder();
int n=0;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null) continue;
  if (b.text != want) continue; n++;
  sb.Append("found drawn=").Append(ZDrawn(b)).Append(" displayed=").Append(ZDisplayed(b)).Append(" rect=").Append(b.worldBound).Append(" | ").Append(ZPath(b)).Append("\n"); }
if (n==0) sb.Append("no button '").Append(want).Append("' anywhere in the tree\n");
sb.Append("-- drawn buttons containing 'edge'/'Edge' --\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||string.IsNullOrEmpty(b.text)) continue;
  if (b.text.IndexOf("dge", System.StringComparison.OrdinalIgnoreCase)>=0) sb.Append("  '").Append(b.text).Append("' drawn=").Append(ZDrawn(b)).Append("\n"); }
return sb.ToString();
