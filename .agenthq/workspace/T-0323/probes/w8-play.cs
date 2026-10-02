var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
System.Func<string> snap = () => { var s=new System.Text.StringBuilder();
  foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(bool)||f.FieldType==typeof(float)||f.FieldType==typeof(double)) s.Append(f.Name).Append("=").Append(f.GetValue(w)).Append(" ");
  return s.ToString(); };
sb.Append("BEFORE ").Append(snap()).Append("\n");
UnityEngine.UIElements.Button pb=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Play") pb=b; }
if (pb!=null) ZClick(pb);
sb.Append("AFTER  ").Append(snap()).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text=="Play"||b.text=="Stop"||b.text.Contains("Play"))) sb.Append("btn now '").Append(b.text).Append("'\n"); }
return sb.ToString();
