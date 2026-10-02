var t = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
int e=0,w=0,l=0;
var m = t.GetMethod("GetCountsByType", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public);
var args = new object[]{ e, w, l };
m.Invoke(null, args);
return "errors=" + args[0] + " warnings=" + args[1] + " logs=" + args[2];
