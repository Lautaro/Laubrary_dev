var t = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
var te = System.Type.GetType("UnityEditor.LogEntry,UnityEditor");
var BF = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
int count = (int)t.GetMethod("StartGettingEntries", BF).Invoke(null, null);
var entry = System.Activator.CreateInstance(te);
var get = t.GetMethod("GetEntryInternal", BF);
var msgF = te.GetField("message", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var sb = new System.Text.StringBuilder(); int unknown = 0;
for (int i=0;i<count;i++) {
  get.Invoke(null, new object[]{ i, entry });
  string m = (string)msgF.GetValue(entry);
  if (m == null) continue;
  if (m.Contains("Unknown style") || m.Contains("USS") || m.Contains("style property") || m.Contains("stylesheet")) { unknown++; if (unknown < 25) sb.AppendLine("[" + i + "] " + m.Split('\n')[0]); }
}
t.GetMethod("EndGettingEntries", BF).Invoke(null, null);
return "console entries=" + count + " style-related=" + unknown + "\n" + sb;
