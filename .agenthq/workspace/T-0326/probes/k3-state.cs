var w = ZWin(UnityEditor.EditorPrefs.GetString("T326.walkWin", "ChunkWindow"));
if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var a = af != null ? af.GetValue(w) as UnityEngine.Object : null;
sb.Append("bound=").Append(a == null ? "<null>" : a.name).Append(" path=")
  .Append(a == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(a)).Append("\n");
sb.Append(ZSummary(w.GetType().Name + "-fresh")).Append("\n");
var rep = ZAudit(w, w.GetType().Name + "-fresh");
sb.Append(ZSummary(w.GetType().Name + "-fresh"));
sb.Append("buttons: ");
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && !string.IsNullOrEmpty(b.text)) sb.Append("[").Append(b.text).Append(b.enabledInHierarchy ? "" : " (greyed)").Append("] "); }
sb.Append("\n");
sb.Append("dump=").Append(ZDump("audit-" + w.GetType().Name + "-fresh", rep)).Append("\n");
return sb.ToString();
