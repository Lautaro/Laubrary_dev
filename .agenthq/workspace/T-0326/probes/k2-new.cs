// COLD WALK step 2: press the window's own New, then type into the row it opens and press Create.
string name = UnityEditor.EditorPrefs.GetString("T326.newName", "AuditT326ChunkA");
var w = ZWin(UnityEditor.EditorPrefs.GetString("T326.walkWin", "ChunkWindow"));
if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.Button createBtn = null, newBtn = null;
foreach (var e in ZAll(w.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
    if (b.text == "New") newBtn = b;
    if (b.text == "Create") createBtn = b;
}
if (createBtn == null)
{
    if (newBtn == null) return "no New button";
    sb.Append("pressed New -> ").Append(ZClick(newBtn)).Append("\n");
    return sb.Append("(row opens; run again to type + Create)").ToString();
}
UnityEngine.UIElements.TextField field = null;
foreach (var e in ZAll(w.rootVisualElement)) { var t = e as UnityEngine.UIElements.TextField; if (t != null && ZDrawn(t)) { field = t; break; } }
if (field == null) return "no name field";
field.value = name;
sb.Append("typed '").Append(field.value).Append("' tip=").Append(ZTip(field)).Append("\n");
sb.Append("pressed Create -> ").Append(ZClick(createBtn)).Append("\n");
return sb.ToString();
