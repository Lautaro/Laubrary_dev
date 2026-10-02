var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null; };
var libT = FT("LauTagLibrary");
var BFs = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
var getOrCreate = libT.GetMethod("GetOrCreateTag", BFs);
var getIds = libT.GetMethod("GetTagIds", BFs);
var setIds = libT.GetMethod("SetTagIds", BFs);
var del = libT.GetMethod("DeleteTag", BFs);
var tagsP = libT.GetProperty("Tags", BFs);

string path = "Assets/Shaper/AuditA24Doc.asset";
string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
sb.Append("guid=").Append(guid).Append("\n");
var before = getIds.Invoke(null, new object[] { guid }) as System.Collections.IList;
sb.Append("tags before        = ").Append(before == null ? -1 : before.Count).Append("\n");

// ADD a tag
var tag = getOrCreate.Invoke(null, new object[] { "A24AuditTag" });
var idF = tag.GetType().GetField("id");
int id = (int)idF.GetValue(tag);
sb.Append("created tag id=").Append(id).Append("\n");
var list = new System.Collections.Generic.List<int>();
if (before != null) foreach (int i in before) list.Add(i);
list.Add(id);
setIds.Invoke(null, new object[] { guid, list });
var after = getIds.Invoke(null, new object[] { guid }) as System.Collections.IList;
sb.Append("tags after ADD     = ").Append(after.Count).Append(" containsNew=").Append(after.Contains(id)).Append("\n");

// does the BROWSER offer any way to filter by it?
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int filterish = 0;
foreach (var v in all)
{
    string tip = v.tooltip ?? "";
    string txt = v is UnityEngine.UIElements.Button b ? (b.text ?? "") : "";
    if (tip.IndexOf("filter", System.StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("filter", System.StringComparison.OrdinalIgnoreCase) >= 0
     || tip.IndexOf("tag", System.StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("tag", System.StringComparison.OrdinalIgnoreCase) >= 0)
    { filterish++; sb.Append("  MENTIONS TAG/FILTER: ").Append(v.GetType().Name).Append(" text='").Append(txt).Append("' tip='").Append(tip).Append("'\n"); }
}
sb.Append("controls in the window mentioning tag/filter = ").Append(filterish).Append("\n");

// REMOVE the tag again and delete it, leaving the library as found
setIds.Invoke(null, new object[] { guid, new System.Collections.Generic.List<int>() });
var after2 = getIds.Invoke(null, new object[] { guid }) as System.Collections.IList;
sb.Append("tags after REMOVE  = ").Append(after2.Count).Append("\n");
del.Invoke(null, new object[] { id });
var tl = tagsP.GetValue(null) as System.Collections.IList;
bool still = false;
foreach (var t in tl) if ((int)t.GetType().GetField("id").GetValue(t) == id) still = true;
sb.Append("tag deleted from library = ").Append(!still).Append(" library size now=").Append(tl.Count).Append("\n");
return sb.ToString();
