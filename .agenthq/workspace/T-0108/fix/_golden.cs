System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x != null) { t = x; break; } }
return (string)t.GetMethod("WriteGoldens").Invoke(null, null);
