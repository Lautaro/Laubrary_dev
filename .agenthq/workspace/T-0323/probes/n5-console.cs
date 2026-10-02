var sb=new System.Text.StringBuilder();
var t = ZType("LogEntries"); 
var mCount = t.GetMethod("GetCount", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
sb.Append("logCount=").Append(mCount!=null? mCount.Invoke(null,null): null).Append("\n");
var start = t.GetMethod("StartGettingEntries", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var end = t.GetMethod("EndGettingEntries", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var getEntry = t.GetMethod("GetEntryInternal", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var entryT = ZType("LogEntry");
if (start!=null && getEntry!=null && entryT!=null) {
  int n=(int)start.Invoke(null,null);
  var e=System.Activator.CreateInstance(entryT);
  var msgF = entryT.GetField("message") ?? entryT.GetField("condition");
  for (int i=Mathf.Max(0,n-25); i<n; i++) { getEntry.Invoke(null, new object[]{i, e}); sb.Append("  ").Append((msgF.GetValue(e) as string ?? "").Split('\n')[0]).Append("\n"); }
  end.Invoke(null,null);
}
return sb.ToString();
