var t = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
t.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Invoke(null, null);
return "console cleared";
