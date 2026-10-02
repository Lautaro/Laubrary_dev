var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null; };
var provT = FT("LauTagLibraryProvider");
var lib = provT.GetMethod("Get", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, null);
var libT = lib.GetType();
sb.Append("library=").Append(lib == null ? "NULL" : UnityEditor.AssetDatabase.GetAssetPath(lib as UnityEngine.Object)).Append("\n");
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var getOrCreate = libT.GetMethod("GetOrCreateTag", BFi);
var getIds = libT.GetMethod("GetTagIds", BFi);
var setIds = libT.GetMethod("SetTagIds", BFi);
var del = libT.GetMethod("DeleteTag", BFi);
var tagsP = libT.GetProperty("Tags", BFi);
var tl0 = tagsP.GetValue(lib) as System.Collections.IList;
sb.Append("tags in library before = ").Append(tl0.Count).Append("\n");

string guid = UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Shaper/AuditA24Doc.asset");
var before = getIds.Invoke(lib, new object[] { guid }) as System.Collections.IList;
sb.Append("asset tags before  = ").Append(before == null ? 0 : before.Count).Append("\n");

var tag = getOrCreate.Invoke(lib, new object[] { "A24AuditTag" });
int id = (int)tag.GetType().GetField("id").GetValue(tag);
var list = new System.Collections.Generic.List<int>();
if (before != null) foreach (int i in before) list.Add(i);
list.Add(id);
setIds.Invoke(lib, new object[] { guid, list });
var after = getIds.Invoke(lib, new object[] { guid }) as System.Collections.IList;
sb.Append("ADD  -> asset tags = ").Append(after.Count).Append(" containsNew=").Append(after.Contains(id)).Append("\n");

setIds.Invoke(lib, new object[] { guid, new System.Collections.Generic.List<int>() });
var after2 = getIds.Invoke(lib, new object[] { guid }) as System.Collections.IList;
sb.Append("REMOVE -> asset tags = ").Append(after2.Count).Append("\n");
del.Invoke(lib, new object[] { id });
var tl = tagsP.GetValue(lib) as System.Collections.IList;
bool still = false; foreach (var t in tl) if ((int)t.GetType().GetField("id").GetValue(t) == id) still = true;
sb.Append("tag removed from library = ").Append(!still).Append(" library size now = ").Append(tl.Count).Append(" (was ").Append(tl0.Count).Append(")\n");
return sb.ToString();
