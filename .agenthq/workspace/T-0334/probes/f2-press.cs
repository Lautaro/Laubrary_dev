var w = ZWin("MirageWindow"); if (w == null) return "no window";
UnityEngine.UIElements.Button add = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && b.text == "Add Previewable" && ZDrawn(b)) add = b; }
if (add == null) return "no Add Previewable button drawn";
int before = 0; foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) before++;
bool ok = ZClick(add);
int after = 0; var names = new System.Text.StringBuilder();
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) { after++; names.Append(x.GetType().Name).Append(" "); }
return "btn=" + add.worldBound + " clicked=" + ok + " windows " + before + " -> " + after + "\n" + names;
