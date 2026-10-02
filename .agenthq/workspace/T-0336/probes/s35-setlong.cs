var w = ZWin("ShaperWindow"); if (w == null) return "no window";
const string p = "Assets/Shaper/AuditT336Long/AuditT336 A Deliberately Very Long Backdrop Sprite Name For Measuring Clipping.png";
var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(p);
if (sp == null) return "no sprite";
int n = 0;
foreach (var e in ZAll(w.rootVisualElement)) { var f = e as UnityEditor.UIElements.ObjectField; if (f == null || !ZDrawn(f)) continue;
  if (f.objectType == typeof(UnityEngine.Sprite)) { f.value = sp; n++; } }
w.Repaint();
return "assigned to " + n + " Sprite field(s) — rerun the fit probe";
