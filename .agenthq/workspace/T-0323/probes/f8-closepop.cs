var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.pressWin","LatheWindow");
var w=ZWin(wn); if (w==null) return "no window";
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover") && e.worldBound.width>600) { ZClick(e); sb.Append("clicked scrim\n"); }
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover")) sb.Append("still open ").Append(e.worldBound).Append("\n");
// transport state
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(bool)) sb.Append("fld ").Append(f.Name).Append("=").Append(f.GetValue(w)).Append(" ");
sb.Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text.Contains("Play")||b.text.Contains("Pause"))) sb.Append("transport btn '").Append(b.text).Append("'\n"); }
return sb.ToString();
