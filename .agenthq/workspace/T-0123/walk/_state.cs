var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var rec = t.GetField("recipe", bf).GetValue(win);
var caps = (System.Collections.IEnumerable)rec.GetType().GetField("capabilities", bf | System.Reflection.BindingFlags.Public).GetValue(rec);
var sb = new System.Text.StringBuilder();
sb.AppendLine("recipe=" + rec.GetType().GetField("name", bf | System.Reflection.BindingFlags.Public).GetValue(rec));
foreach (var c in caps)
{
    var ct = c.GetType();
    var baseT = ct.BaseType;
    sb.AppendLine("  " + ct.Name
        + " enabled=" + baseT.GetField("Enabled").GetValue(c)
        + " delay=" + baseT.GetField("delay").GetValue(c)
        + " duration=" + baseT.GetField("duration").GetValue(c)
        + " timed=" + baseT.GetProperty("Timed").GetValue(c));
}
var tracks = t.GetField("tracks", bf).GetValue(win);
sb.AppendLine("tracks=" + (tracks == null ? "null" : "present"));
if (tracks != null)
{
    var tt = tracks.GetType();
    var lanes = (System.Collections.ICollection)tt.GetField("lanes", bf).GetValue(tracks);
    sb.AppendLine("  lanes=" + lanes.Count
        + " total=" + tt.GetField("total", bf).GetValue(tracks)
        + " seconds=" + tt.GetField("seconds", bf).GetValue(tracks));
}
return sb.ToString();
