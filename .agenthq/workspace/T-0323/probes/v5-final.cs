var sb=new System.Text.StringBuilder();
var w=ZWin("ShaperWindow"); if (w==null) return "no shaper";
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach (var f in w.GetType().GetFields(BFi)) if (f.Name=="playing") { sb.Append("playing=").Append(f.GetValue(w)).Append("\n"); if ((bool)f.GetValue(w)) { foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && b.text.Contains("Pause")) { ZClick(b); sb.Append("paused\n"); break; } } } }
sb.Append("openWindows=");
var names=new System.Collections.Generic.SortedSet<string>();
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null) names.Add(x.GetType().Name);
sb.Append(string.Join(",", System.Linq.Enumerable.ToArray(names))).Append("\n");
return sb.ToString();
