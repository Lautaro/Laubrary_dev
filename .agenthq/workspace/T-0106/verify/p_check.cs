var sb=new System.Text.StringBuilder();
sb.AppendLine("compileFailed = " + UnityEditor.EditorUtility.scriptCompilationFailed);
System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.ShaperFillCompiler");
var m = t.GetMethod("Clamp01OrZero", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
sb.AppendLine("Clamp01OrZero present = " + (m!=null));
if(m!=null) sb.AppendLine("  Clamp01OrZero(NaN) = " + m.Invoke(null, new object[]{float.NaN}));
return sb.ToString();
