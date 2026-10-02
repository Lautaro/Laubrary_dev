// On a Composite (no Fill section), toggling ANY chip rewrites the shared saved selection — does it
// silently turn OFF a section that merely does not exist for this node kind?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
string key = "ZuiSectionToggleBar.ShaperWindow.userSel";
UnityEditor.EditorPrefs.SetString(key, "Views=1;Canvas=1;Layers=1;Shape=1;Fill=1;Swarm=1;SpriteFX=1;Lights=1;Tags=1");
var sb = new System.Text.StringBuilder();
sb.Append("pref set to    ").Append(UnityEditor.EditorPrefs.GetString(key)).Append("\n");
UnityEngine.UIElements.Button chip = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)||b.text!="SpriteFX") continue;
  bool inBar=false; for (var p=b.hierarchy.parent;p!=null;p=p.hierarchy.parent) if (p.GetType().Name=="ZuiSectionToggleBar") { inBar=true; break; }
  if (inBar) { chip=b; break; } }
if (chip == null) return sb.Append("no SpriteFX chip").ToString();
sb.Append(ZPress(w, chip)).Append("   (a chip that has NOTHING to do with Fill)\n");
sb.Append("pref now       ").Append(UnityEditor.EditorPrefs.GetString(key)).Append("\n");
return sb.ToString();
