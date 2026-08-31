System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperProgram"); if (x != null) { t = x; break; } }
bool hasNew = t != null && t.GetField("supportSpread") != null;
System.Type ow = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperFillOwner"); if (x != null) { ow = x; break; } }
bool hasEnd = ow != null && ow.GetField("borderSubtreeEnd") != null;
return "compiling=" + UnityEditor.EditorApplication.isCompiling
     + " failed=" + UnityEditor.EditorUtility.scriptCompilationFailed
     + " supportSpread=" + hasNew + " borderSubtreeEnd=" + hasEnd;
