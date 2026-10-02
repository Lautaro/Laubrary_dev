// Measure the Add Previewable browser's cell names against the SHIPPED code path: the same Elide the
// grid now calls, at the browser's real cell width (104), over the same list the popup offers.
var pickT = ZType("MirageAssetPicker");
var findAll = pickT.GetMethod("FindAll", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var items = findAll.Invoke(null, null) as System.Collections.IList;
var gridT = ZType("LauAssetGridGUI");
var elide = gridT.GetMethod("Elide", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
if (elide == null) return "no Elide method on LauAssetGridGUI";
var style = UnityEditor.EditorStyles.miniLabel;
float cell = 104f, thumb = 92f;
var sb = new System.Text.StringBuilder();
int overCell = 0, overThumb = 0, elided = 0;
var seen = new System.Collections.Generic.Dictionary<string,string>();
int collisions = 0;
foreach (var it in items)
{
    var f = it.GetType().GetField("asset", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
    var o = (f != null ? f.GetValue(it) : it) as UnityEngine.Object;
    if (o == null) continue;
    string name = o.name;
    float need = style.CalcSize(new GUIContent(name)).x;
    if (need > cell) overCell++;
    if (need > thumb) overThumb++;
    string shown = (string)elide.Invoke(null, new object[]{ name, style, cell });
    if (shown != name) { elided++; sb.Append("  ELIDED '").Append(name).Append("' (").Append(need.ToString("0.#")).Append("px) -> '").Append(shown).Append("'\n"); }
    string prev;
    if (seen.TryGetValue(shown, out prev)) { collisions++; sb.Append("  COLLISION '").Append(shown).Append("' <= '").Append(prev).Append("' AND '").Append(name).Append("'\n"); }
    else seen[shown] = name;
    float after = style.CalcSize(new GUIContent(shown)).x;
    if (after > cell + 0.5f) sb.Append("  STILL OVER '").Append(shown).Append("' ").Append(after.ToString("0.#")).Append("\n");
}
var head = new System.Text.StringBuilder();
head.Append("items=").Append(items.Count).Append(" namesWiderThanOldLabel(92)=").Append(overThumb)
    .Append(" widerThanCell(104)=").Append(overCell).Append(" elidedNow=").Append(elided)
    .Append(" visibleStringCollisions=").Append(collisions).Append("\n");
return head.ToString() + sb.ToString();
