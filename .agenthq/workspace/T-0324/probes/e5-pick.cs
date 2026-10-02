// Pick an asset THROUGH the open browser's own onPick callback — the exact delegate a click on a cell fires.
string want = UnityEditor.EditorPrefs.GetString("T324.pick", "");
UnityEditor.EditorWindow pop = null;
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name == "PopupWindow") { pop = w; break; }
if (pop == null) return "NO POPUP";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
object content = null;
for (var t = pop.GetType(); t != null; t = t.BaseType) { var f = t.GetField("m_WindowContent", BFi); if (f != null) { content = f.GetValue(pop); break; } }
if (content == null) return "no content";
var pickF = content.GetType().GetField("_onPick", BFi);
var listF = content.GetType().GetField("_eagerEntries", BFi);
if (pickF == null || listF == null) return "no _onPick/_eagerEntries";
var list = listF.GetValue(content) as System.Collections.IList;
UnityEngine.Object target = null;
foreach (var o in list) { var uo = o as UnityEngine.Object; if (uo != null && uo.name == want) { target = uo; break; } }
if (target == null) return "no '" + want + "' among " + list.Count;
var act = pickF.GetValue(content) as System.Action<UnityEngine.Object>;
if (act == null) return "_onPick is not Action<Object>";
act(target);
pop.Close();
return "picked " + target.name + " through the browser's own onPick";
