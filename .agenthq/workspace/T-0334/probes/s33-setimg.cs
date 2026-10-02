var w = ZWin("ShaperWindow"); if (w == null) return "no window";
const string p = "Assets/Christina Layout Group/Layout System Examples/HeadImage for Layout Group Tutorial.png";
var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(p);
if (sp == null) return "no sprite at " + p;
UnityEditor.UIElements.ObjectField target = null;
foreach (var e in ZAll(w.rootVisualElement)) { var f = e as UnityEditor.UIElements.ObjectField; if (f == null || !ZDrawn(f)) continue;
  if (f.objectType == typeof(UnityEngine.Sprite)) { target = f; break; } }
if (target == null) return "no Sprite ObjectField drawn";
target.value = sp;
w.Repaint();
return "set '" + sp.name + "' (" + sp.name.Length + " chars) on the backdrop Image field — rerun the fit probe";
