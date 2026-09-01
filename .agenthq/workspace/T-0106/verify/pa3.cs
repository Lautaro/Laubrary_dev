System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
var sb=new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("FT12_DeclaredEqualsRead").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT13_EverySampleWritten").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT14_RangesHold").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT15_DegenerateInputs").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT16_NoInputMutation").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT17_AddDoesNotRaiseAlpha").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT18_HeightIgnoresComposite").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT19_EdgeDistancePolarity").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT21_ExclusivityIsConservative").Invoke(null,null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\newaudit2.txt", sb.ToString());
return "wrote " + sb.Length + " chars";
