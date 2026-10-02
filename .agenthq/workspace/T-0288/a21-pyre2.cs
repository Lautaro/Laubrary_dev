var sb = new System.Text.StringBuilder();
System.Type FindT(string n) { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { try { foreach (var t in a.GetTypes()) if (t.Name==n) return t; } catch {} } return null; }
var pw = FindT("PyreWindow");
var win = UnityEditor.EditorWindow.GetWindow(pw);
win.position = new UnityEngine.Rect(120, 40, 1000, 1100);
System.Action<UnityEngine.UIElements.VisualElement,System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> W = null;
W = (e,l) => { l.Add(e); for (int i=0;i<e.hierarchy.childCount;i++) W(e.hierarchy[i], l); };
System.Func<UnityEngine.UIElements.VisualElement,string> TextOf = v => { var p=v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public); if (p!=null && p.PropertyType==typeof(string)) { try { return (string)p.GetValue(v); } catch {} } return null; };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); W(win.rootVisualElement, all);
System.Func<UnityEngine.UIElements.VisualElement,string> Deep = null;
Deep = v => { var t=TextOf(v); if (!string.IsNullOrEmpty(t)) return t; for (int i=0;i<v.hierarchy.childCount;i++) { var s=Deep(v.hierarchy[i]); if (!string.IsNullOrEmpty(s)) return s; } return ""; };
int hits=0;
foreach (var e in all) {
  if (e.resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None) continue;
  var b=e.worldBound; if (b.width<=0||b.height<=0) continue;
  float maxB=b.yMax; int n=0;
  for (int i=0;i<e.hierarchy.childCount;i++) { var c=e.hierarchy[i]; if (c.resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None) continue; var cb=c.worldBound; if (cb.height<=0) continue; n++; if (cb.yMax>maxB) maxB=cb.yMax; }
  if (n>0 && maxB>b.yMax+0.6f) { hits++; sb.Append("OVERFLOW ").Append(e.GetType().Name).Append(" [").Append(Deep(e)).Append("] by ").Append((maxB-b.yMax).ToString("0.0")).Append("px cls=").Append(string.Join(",",e.GetClasses())).Append('\n'); }
}
sb.Append("scanned=").Append(all.Count).Append(" hits=").Append(hits).Append('\n');
foreach (var e in all) { var t = TextOf(e); if (t=="Flame type"||t=="Jet type"||t=="Lash") sb.Append("  \"").Append(t).Append("\" at ").Append((int)e.worldBound.x).Append(',').Append((int)e.worldBound.y).Append(" ").Append((int)e.worldBound.width).Append('x').Append((int)e.worldBound.height).Append('\n'); }
return sb.ToString();
