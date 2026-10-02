UnityEditor.EditorPrefs.DeleteKey("T334.rigOut");
var sb = new System.Text.StringBuilder();
var w = ZWin("MirageWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) { if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
var view = af != null ? af.GetValue(w) : null;
sb.Append("bound=").Append(view == null ? "<null>" : view.ToString()).Append("\n");
if (view == null) return sb.ToString();

// a Zoe with real declared visuals — the one round 14 used
var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/PreviewDemo/PreviewShooterZoe.asset");
sb.Append("zoe=").Append(zoe == null ? "<null>" : zoe.name).Append("\n");
System.Reflection.MethodInfo add = null;
foreach (var m in w.GetType().GetMethods(BFi)) if (m.Name == "AddEntry" && m.GetParameters().Length == 2) add = m;
if (add == null) return sb.Append("no AddEntry").ToString();
add.Invoke(w, new object[] { view, zoe });
System.Reflection.MethodInfo rb = null;
foreach (var m in w.GetType().GetMethods(BFi)) if (m.Name == "RebuildBody" && m.GetParameters().Length == 0) rb = m;
if (rb != null) rb.Invoke(w, null);

var mav = ZType("MirageActiveView");
if (mav != null) { var set = mav.GetMethod("Set", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic); if (set != null) set.Invoke(null, new object[]{ view }); }
sb.Append("entriesNow=");
var ef = view.GetType().GetField("entries", BFi);
var lst = ef != null ? ef.GetValue(view) as System.Collections.IList : null;
sb.Append(lst == null ? -1 : lst.Count).Append("\n");
return sb.ToString();
