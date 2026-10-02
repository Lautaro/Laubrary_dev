// Add a named SpriteFx modifier to the bound stack the way the window's own picker does — by finding the
// menu item and invoking it — then rebuild. T324.fx names the modifier.
string wn = "SpriteFxStackWindow";
string want = UnityEditor.EditorPrefs.GetString("T324.fx", "Outline");
var win = ZWin(wn); if (win == null) return "no " + wn;
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
object spec = null;
for (var t = win.GetType(); t != null; t = t.BaseType)
{ var f = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (f != null) { spec = f.GetValue(win); break; } }
if (spec == null) return "no bound spec";
// the modifier list field on the spec
System.Reflection.FieldInfo listF = null;
foreach (var f in spec.GetType().GetFields(BFi))
    if (typeof(System.Collections.IList).IsAssignableFrom(f.FieldType) && f.FieldType.IsGenericType) { listF = f; break; }
if (listF == null) return "no list field on " + spec.GetType().Name;
var elemT = listF.FieldType.GetGenericArguments()[0];
System.Type kind = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (!t.IsAbstract && elemT.IsAssignableFrom(t) && (t.Name == want || t.Name == want + "Modifier")) { kind = t; break; }
  if (kind != null) break; }
if (kind == null) return "no modifier type named " + want + " (element type " + elemT.Name + ")";
var list = listF.GetValue(spec) as System.Collections.IList;
UnityEditor.Undo.RecordObject((UnityEngine.Object)spec, "Add " + want);
list.Add(System.Activator.CreateInstance(kind));
UnityEditor.EditorUtility.SetDirty((UnityEngine.Object)spec);
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb != null) break; }
if (rb != null) rb.Invoke(win, null);
win.Repaint();
return "added " + kind.Name + " to " + listF.Name + " (now " + list.Count + ")";
