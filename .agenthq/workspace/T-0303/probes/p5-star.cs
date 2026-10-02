var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var spec = curP.GetValue(win);
var layers = spec.GetType().GetField("layers", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).GetValue(spec) as System.Collections.IList;
var s = layers[0];
var sfF = s.GetType().GetField("shapeForm");
var formF = s.GetType().GetField("form");
var shapeFormT = sfF.FieldType;
object star = System.Enum.Parse(shapeFormT, "Star");

// render frame 0 before
var rendT = FT("PyreRenderer");
var rm = rendT.GetMethod("RenderFrame", new System.Type[]{ spec.GetType(), typeof(int) });
System.Func<int,int> Lit = f => { var px = rm.Invoke(null, new object[]{ spec, f }) as UnityEngine.Color32[]; int n2 = 0; foreach (var c in px) if (c.a > 0) n2++; return n2; };
sb.Append("RenderFrame found=").Append(rm != null).Append("\n");
int before = rm == null ? -1 : Lit(0);

// the exact call the menu item's Clickable makes
var dirty = pyreT.GetMethod("Dirty", BFi, null, new System.Type[]{ typeof(System.Action) }, null);
var steady = pyreT.GetMethod("SteadyDefaultFillForSolid", BFi);
sb.Append("Dirty found=").Append(dirty != null).Append(" Steady found=").Append(steady != null).Append("\n");
dirty.Invoke(win, new object[]{ (System.Action)(() => { formF.SetValue(s, null); sfF.SetValue(s, star); if (steady != null) steady.Invoke(win, new object[]{ s }); }) });
foreach (var m in new[]{ "RebuildShape", "RebuildSwarm" }) { var mi = pyreT.GetMethod(m, BFi); if (mi != null) mi.Invoke(win, null); }
int after = rm == null ? -1 : Lit(0);
sb.Append("shapeForm now = ").Append(sfF.GetValue(s)).Append("  lit frame0 ").Append(before).Append(" -> ").Append(after).Append("  changed=").Append(before != after).Append("\n");
sb.Append("spec dirty=").Append(UnityEditor.EditorUtility.IsDirty((UnityEngine.Object)spec)).Append("\n");
return sb.ToString();
