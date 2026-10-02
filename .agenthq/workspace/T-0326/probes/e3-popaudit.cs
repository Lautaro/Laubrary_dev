// Audit the open LauAssetBrowser popup: how many rows, do their thumbnails resolve, and does any name
// need more width than the 92px label it is drawn into?
UnityEditor.EditorWindow pop = null;
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name == "PopupWindow") { pop = w; break; }
if (pop == null) return "NO POPUP";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
object content = null;
for (var t = pop.GetType(); t != null; t = t.BaseType)
{ var f = t.GetField("m_WindowContent", BFi); if (f != null) { content = f.GetValue(pop); break; } }
if (content == null)
{ foreach (var f in pop.GetType().GetFields(BFi)) if (f.FieldType.Name.Contains("PopupWindowContent")) { content = f.GetValue(pop); break; } }
if (content == null) return "no content on " + pop.GetType().FullName + " fields=" + string.Join(",", System.Linq.Enumerable.Select(pop.GetType().GetFields(BFi), f => f.Name));
var ct = content.GetType();
var sb = new System.Text.StringBuilder();
sb.AppendLine("content=" + ct.FullName + " popupSize=" + pop.position);
System.Collections.IList items = null;
foreach (var f in ct.GetFields(BFi))
{
    var v = f.GetValue(content);
    if (v is System.Collections.IList l && f.FieldType.IsGenericType && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType.GetGenericArguments()[0]))
    { sb.AppendLine("list field '" + f.Name + "' count=" + l.Count); if (items == null || l.Count > items.Count) items = l; }
    else sb.AppendLine("field " + f.Name + " = " + (v == null ? "null" : v.ToString()));
}
if (items == null) return sb.ToString();
var style = UnityEditor.EditorStyles.miniLabel;
int over = 0, blank = 0; var overs = new System.Collections.Generic.List<string>();
foreach (var o in items)
{
    var uo = o as UnityEngine.Object; if (uo == null) continue;
    float need = style.CalcSize(new GUIContent(uo.name)).x;
    if (need > 92f) { over++; overs.Add(uo.name + " (" + need.ToString("0.#") + ")"); }
    var tex = UnityEditor.AssetPreview.GetAssetPreview(uo);
    if (tex == null) blank++;
}
sb.AppendLine("rows=" + items.Count + "  names wider than the 92px label: " + over + "  (AssetPreview null for " + blank + ", not the browser's own path)");
foreach (var s in overs) sb.AppendLine("   OVER " + s);
return ZDump("popup-audit", sb.ToString()) + "\n" + sb.ToString();
